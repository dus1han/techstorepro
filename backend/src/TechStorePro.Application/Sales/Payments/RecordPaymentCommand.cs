using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Payments;

/// <summary>
/// One way the customer paid. Cash and card on the same sale are two of these.
///
/// <see cref="FinancialAccountId"/> says which drawer the notes went into (P7). It is optional and
/// normally omitted — the payment method already knows, and the till screen does not want to ask. It
/// exists for the shop with two branches and two cash drawers, where "Cash" is one method and the money
/// is physically in one of two places: naming the account is the only way to say which.
/// </summary>
public record TenderLine(
    Guid PaymentMethodId,
    decimal Amount,
    string? Reference = null,
    Guid? FinancialAccountId = null);

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
    private readonly IAccountLedger _accounts;
    private readonly IDateTime _clock;

    public RecordPaymentCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        IDocumentNumberGenerator numbers,
        IAccountLedger accounts,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _numbers = numbers;
        _accounts = accounts;
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

            if (method.Kind == PaymentMethodKind.StoreCredit)
            {
                await RedeemStoreCreditAsync(payment, customer, tender.Amount, paidAt, cancellationToken);
            }
            else
            {
                // The money arrives somewhere (P7). Store credit deliberately does not come through here:
                // no cash moves when a customer spends a voucher — the shop took that money when the goods
                // came back, and it is in the drawer already. Writing an account transaction for it would
                // add the same notes to the till twice, and the till would come up over by exactly the
                // credit the shop had issued.
                await BankAsync(payment, customer, method, tender, paidAt, cancellationToken);
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

    /// <summary>
    /// The money, into the account that now holds it (P7, requirements §33).
    ///
    /// The account comes from the tender line if it named one — the shop with two tills — and otherwise
    /// from the payment method, which is where a single-branch shop configures it once and forgets it.
    ///
    /// <b>If neither names an account the payment is refused, not silently banked nowhere.</b> That is the
    /// whole point of the check: a payment that wrote no account transaction would still settle the
    /// invoice and still reduce the customer's balance, so nothing downstream would look wrong — the money
    /// would simply not be in the shop's cash position, and it would never be missed, because there is
    /// nothing to miss it against.
    /// </summary>
    private async Task BankAsync(
        CustomerPayment payment,
        Domain.Catalog.Customer customer,
        PaymentMethod method,
        TenderLine tender,
        DateTimeOffset paidAt,
        CancellationToken cancellationToken)
    {
        var accountId = tender.FinancialAccountId ?? method.FinancialAccountId
            ?? throw new DomainException(
                $"'{method.Name}' has no cash or bank account behind it, so this money would arrive "
                + "nowhere. Point the payment method at an account, or name one on the tender.");

        await _accounts.PostAsync(
            new AccountPosting(
                accountId,
                tender.Amount,   // positive: money in
                AccountTransactionSource.CustomerPayment,
                $"{customer.Name} — payment {payment.Number}",
                BranchId: payment.BranchId,
                SourceId: payment.Id,
                SourceNumber: payment.Number,
                Reference: tender.Reference ?? payment.Reference,
                OccurredAt: paidAt),
            cancellationToken);
    }

    /// <summary>
    /// Spending credit the customer already holds (requirements §24, "future usage").
    ///
    /// It is tender rather than a discount because that is what it is: the shop has already had this
    /// money, from the goods that came back. Drawing it down is a negative entry on the store-credit
    /// ledger, so the balance is always the sum of its history and never a number nobody can explain.
    /// </summary>
    private async Task RedeemStoreCreditAsync(
        CustomerPayment payment,
        Domain.Catalog.Customer customer,
        decimal amount,
        DateTimeOffset paidAt,
        CancellationToken cancellationToken)
    {
        var held = await _db.StoreCreditEntries
            .Where(e => e.CustomerId == customer.Id)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        if (amount > held)
        {
            // Spending credit they do not have would be the shop giving away goods for nothing, and the
            // ledger would go negative with no record of where the money came from.
            throw new DomainException(
                $"{customer.Name} holds {held:0.##} in store credit and this tenders {amount:0.##}. "
                + "Take the difference by another method.");
        }

        var entry = new StoreCreditEntry
        {
            CustomerId = customer.Id,
            CustomerPaymentId = payment.Id,
            Amount = -amount,   // signed, so the balance is a SUM and cannot be got wrong
            OccurredAt = paidAt,
            Reason = $"Redeemed against payment {payment.Number}"
        };

        entry.Validate();

        _db.StoreCreditEntries.Add(entry);
    }
}
