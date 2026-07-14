using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Purchasing;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.Payments;

/// <summary>Which supplier invoice this money settles, and how much of it — in the invoice's currency.</summary>
public record SupplierAllocationLine(Guid SupplierInvoiceId, decimal Amount);

/// <summary>
/// Pay a supplier (requirements §25).
///
/// <b>A payment is a header plus allocations, not a column on an invoice.</b> One transfer settles three
/// invoices; one invoice is settled by two instalments. A shop that pays its supplier monthly does both
/// constantly, and a single <c>invoice_id</c> can express neither.
///
/// <see cref="Allocations"/> may be empty. Money paid before the invoice arrives is an advance, and that
/// is a real state, not an error: it sits as an unallocated credit the supplier owes back.
/// </summary>
[RequiresPermission(FeatureCatalog.SupplierPayments, PermissionAction.Create)]
public record PaySupplierCommand(
    Guid SupplierId,
    Guid BranchId,
    Guid PaymentMethodId,
    decimal Amount,
    IReadOnlyCollection<SupplierAllocationLine>? Allocations = null,
    string CurrencyCode = "AED",

    /// <summary>
    /// The rate on the day the money actually leaves the bank — <em>not</em> the rate the invoice was
    /// booked at. The gap between the two is the realised FX gain or loss, and it is the whole reason
    /// this field exists rather than being looked up.
    /// </summary>
    decimal ExchangeRate = 1m,
    DateTimeOffset? PaidAt = null,
    string? Reference = null,
    string? Notes = null) : IRequest<Guid>;

public class PaySupplierCommandValidator : AbstractValidator<PaySupplierCommand>
{
    public PaySupplierCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("A payment of nothing is not a payment.");
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);

        RuleForEach(x => x.Allocations).ChildRules(allocation =>
        {
            allocation.RuleFor(a => a.SupplierInvoiceId).NotEmpty();
            allocation.RuleFor(a => a.Amount).GreaterThan(0);
        }).When(x => x.Allocations is not null);
    }
}

public class PaySupplierCommandHandler : IRequestHandler<PaySupplierCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public PaySupplierCommandHandler(
        IApplicationDbContext db,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(PaySupplierCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var paidAt = request.PaidAt ?? _clock.UtcNow;

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        var method = await _db.PaymentMethods
            .FirstOrDefaultAsync(m => m.Id == request.PaymentMethodId, cancellationToken)
            ?? throw new NotFoundException("Payment method", request.PaymentMethodId);

        if (method.RequiresReference && string.IsNullOrWhiteSpace(request.Reference))
        {
            // A transfer or a cheque with no reference cannot be matched against the bank statement, and
            // the money becomes unreconcilable the moment it leaves.
            throw new DomainException(
                $"{method.Name} needs a reference — without it this payment cannot be reconciled "
                + "against the bank.");
        }

        var payment = new SupplierPayment
        {
            Number = await _numbers.NextAsync(DocumentType.SupplierPayment, request.BranchId, cancellationToken),
            SupplierId = supplier.Id,
            BranchId = request.BranchId,
            PaymentMethodId = method.Id,
            Reference = request.Reference,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            PaidAt = paidAt,
            Notes = request.Notes
        };

        _db.SupplierPayments.Add(payment);

        // What the settled debt was booked at, in base currency — accumulated as the allocations are
        // made. It is NOT what left the bank: see the balance arithmetic at the end, which is the whole
        // point of this handler.
        var settledDebtBase = 0m;

        foreach (var allocation in request.Allocations ?? [])
        {
            var invoice = await _db.SupplierInvoices
                .Include(i => i.Lines)
                .Include(i => i.Allocations)
                .FirstOrDefaultAsync(i => i.Id == allocation.SupplierInvoiceId, cancellationToken)
                ?? throw new NotFoundException("Supplier invoice", allocation.SupplierInvoiceId);

            if (invoice.SupplierId != supplier.Id)
            {
                // One supplier's money settling another's bill would leave both balances wrong and the
                // error invisible: each invoice would look paid, and the wrong party would keep chasing.
                throw new DomainException("That invoice belongs to a different supplier.");
            }

            if (invoice.Status is SupplierInvoiceStatus.Draft or SupplierInvoiceStatus.Cancelled)
            {
                throw new DomainException($"An invoice that is {invoice.Status} cannot be paid.");
            }

            if (invoice.CurrencyCode != payment.CurrencyCode)
            {
                // The allocation's Amount is in the invoice's currency and the payment's Amount is in the
                // payment's. Allowing them to differ would make the two numbers incomparable, and
                // Validate()'s "you allocated more than you paid" check — the one thing standing between
                // the shop and a supplier balance that drifts quietly in its favour — would be comparing
                // dollars against dirhams and passing.
                throw new DomainException(
                    $"Invoice {invoice.Number} is in {invoice.CurrencyCode} and this payment is in "
                    + $"{payment.CurrencyCode}. Pay it in the currency it was billed in.");
            }

            if (allocation.Amount > invoice.OutstandingAmount)
            {
                // Over-paying is not refused outright — it is refused *against that invoice*. The extra
                // money is real and belongs on the account as an advance, not hidden inside a document
                // that would then show as more than settled.
                throw new DomainException(
                    $"Invoice {invoice.Number} has {invoice.OutstandingAmount:0.##} outstanding and this "
                    + $"allocates {allocation.Amount:0.##}. Allocate the difference to another invoice, "
                    + "or leave it unallocated as an advance.");
            }

            var line = new SupplierPaymentAllocation
            {
                SupplierPaymentId = payment.Id,
                SupplierInvoiceId = invoice.Id,
                Amount = allocation.Amount,

                // Snapshotted from the invoice, not looked up later. The invoice's rate is a fact about
                // the day it was raised; re-reading it years afterwards would give the same answer only
                // until somebody corrected a historical FX rate — at which point every past gain and loss
                // in the system would silently change. History does not move.
                InvoiceExchangeRate = invoice.ExchangeRate,
                PaymentExchangeRate = payment.ExchangeRate
            };

            // Through the DbSet, and ONLY the DbSet. EF's fixup puts it into invoice.Allocations itself —
            // which is what RefreshPaymentStatus reads on the next line. Adding it by hand as well would
            // count the money twice and mark a half-paid invoice settled.
            _db.SupplierPaymentAllocations.Add(line);

            invoice.RefreshPaymentStatus();

            settledDebtBase += allocation.Amount * invoice.ExchangeRate;
        }

        payment.Validate();

        // The balance, and the one piece of arithmetic in this handler that is easy to get wrong.
        //
        // The debt was booked in base currency at the INVOICE's rate; the money left the bank at the
        // PAYMENT's rate. Subtracting only what left the bank would leave the difference sitting on the
        // supplier's balance forever — a fully-settled USD invoice would still show AED 70 owing, and no
        // amount of paying would ever clear it, because the residue is not a debt at all. It is an FX
        // gain.
        //
        // So a settled invoice has exactly what it added taken back off (Amount × InvoiceRate), and the
        // gain or loss falls out as the difference. Worked through: a USD 1,000 invoice booked at 3.67
        // added AED 3,670. Paid at 3.60, only AED 3,600 leaves the bank. Take 3,670 off the balance and
        // it reaches zero, exactly as it should; the AED 70 the shop kept is the realised gain, which is
        // P&L, not a reduction in the cost of the stock (D1 — the laptops did not become cheaper to buy).
        //
        // Unallocated money settles no invoice, so it has no invoice rate to be measured against: it comes
        // off at what it actually cost.
        var unallocatedBase = payment.UnallocatedAmount * payment.ExchangeRate;

        supplier.Balance -= settledDebtBase + unallocatedBase;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return payment.Id;
    }
}
