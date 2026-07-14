using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Payments;

/// <summary>One way the customer paid. Cash and card on the same sale are two of these.</summary>
public record TenderLine(Guid PaymentMethodId, decimal Amount, string? Reference = null);

/// <summary>Which invoice this money settles, and how much of it.</summary>
public record AllocationLine(Guid SalesInvoiceId, decimal Amount);

/// <summary>
/// Take money from a customer (requirements §23).
///
/// <see cref="Allocations"/> may be empty: money can legitimately arrive before the invoice does — a
/// deposit on an order, a customer paying down their account. That money is not lost and not guessed at;
/// it sits as an unallocated credit and pushes the customer's balance negative, which is precisely what
/// "the shop owes them" looks like.
/// </summary>
[RequiresPermission(FeatureCatalog.CustomerPayments, PermissionAction.Create)]
public record RecordPaymentCommand(
    Guid CustomerId,
    Guid BranchId,
    IReadOnlyCollection<TenderLine> Methods,
    IReadOnlyCollection<AllocationLine>? Allocations = null,
    DateTimeOffset? PaidAt = null,
    string? Reference = null,
    string? CurrencyCode = null,
    string? Notes = null) : IRequest<Guid>;

public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Methods).NotEmpty().WithMessage("A payment must say how the money arrived.");

        RuleForEach(x => x.Methods).ChildRules(method =>
        {
            method.RuleFor(m => m.PaymentMethodId).NotEmpty();
            method.RuleFor(m => m.Amount).GreaterThan(0);
        });

        RuleForEach(x => x.Allocations).ChildRules(allocation =>
        {
            allocation.RuleFor(a => a.SalesInvoiceId).NotEmpty();
            allocation.RuleFor(a => a.Amount).GreaterThan(0);
        }).When(x => x.Allocations is not null);
    }
}

public class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public RecordPaymentCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var paymentId = await PostAsync(request, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return paymentId;
    }

    /// <summary>
    /// The payment itself, without the transaction — so the counter sale can take the money inside the
    /// same transaction that moved the goods and raised the bill. At the till those are one act: a
    /// customer who has walked out with a laptop has not "maybe" paid for it.
    /// </summary>
    internal async Task<Guid> PostAsync(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        var paidAt = request.PaidAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.EnsureAsync(_db, _tenant, request.CurrencyCode, cancellationToken);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var payment = new CustomerPayment
        {
            Number = await _numbers.NextAsync(DocumentType.Payment, request.BranchId, cancellationToken),
            CustomerId = customer.Id,
            BranchId = request.BranchId,
            Reference = request.Reference,
            CurrencyCode = currency,
            PaidAt = paidAt,
            Notes = request.Notes
        };

        _db.CustomerPayments.Add(payment);

        foreach (var tender in request.Methods)
        {
            var method = await _db.PaymentMethods
                .FirstOrDefaultAsync(m => m.Id == tender.PaymentMethodId, cancellationToken)
                ?? throw new NotFoundException("Payment method", tender.PaymentMethodId);

            if (method.RequiresReference && string.IsNullOrWhiteSpace(tender.Reference))
            {
                // A card or a cheque without its reference cannot be matched to the bank statement, and
                // the money becomes unreconcilable the moment it lands.
                throw new DomainException(
                    $"{method.Name} needs a reference — without it this payment cannot be reconciled "
                    + "against the bank.");
            }

            // Through the DbSet, and only the DbSet. See ReceiveGoodsCommand: adding to the parent's
            // collection as well would count the tender twice, and Amount is computed from it.
            _db.CustomerPaymentMethods.Add(new CustomerPaymentMethod
            {
                CustomerPaymentId = payment.Id,
                PaymentMethodId = method.Id,
                Amount = tender.Amount,
                Reference = tender.Reference
            });
        }

        foreach (var allocation in request.Allocations ?? [])
        {
            var invoice = await _db.SalesInvoices
                .Include(i => i.Lines)
                .Include(i => i.Allocations)
                .FirstOrDefaultAsync(i => i.Id == allocation.SalesInvoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice", allocation.SalesInvoiceId);

            if (invoice.CustomerId != customer.Id)
            {
                // One customer's money settling another's debt would leave both balances wrong and the
                // error invisible: each invoice would look paid, and the wrong person would be chased.
                throw new DomainException("That invoice belongs to a different customer.");
            }

            if (invoice.Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Cancelled)
            {
                throw new DomainException($"An invoice that is {invoice.Status} cannot be paid.");
            }

            if (allocation.Amount > invoice.OutstandingAmount)
            {
                // Over-paying an invoice is not refused outright — it is refused *against that invoice*.
                // The extra money is real; it belongs on the account as a credit, not hidden inside a
                // document that would then show as more than settled.
                throw new DomainException(
                    $"Invoice {invoice.Number} has {invoice.OutstandingAmount:0.##} outstanding and this "
                    + $"allocates {allocation.Amount:0.##}. Allocate the difference to another invoice, "
                    + "or leave it unallocated as a credit on the account.");
            }

            // Through the DbSet, and only the DbSet. EF's fixup puts it in invoice.Allocations itself —
            // which is what RefreshPaymentStatus reads a line below. Adding it there by hand as well
            // would count the money twice and mark a half-paid invoice settled.
            _db.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = payment.Id,
                SalesInvoiceId = invoice.Id,
                Amount = allocation.Amount
            });

            invoice.RefreshPaymentStatus();
        }

        payment.Validate();

        // The debt, reduced. Every dirham received comes off what the customer owes — including money not
        // yet pointed at an invoice, which is what takes the balance negative and makes it a credit.
        customer.Balance -= payment.Amount;

        return payment.Id;
    }
}
