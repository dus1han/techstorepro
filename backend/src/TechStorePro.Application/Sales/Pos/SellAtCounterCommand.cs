using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Application.Sales.Payments;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Pos;

public record CounterSaleLine(
    Guid ProductId,
    decimal Quantity,
    IReadOnlyCollection<string>? SerialNumbers = null,
    decimal? UnitPrice = null,
    decimal DiscountPercent = 0m,
    decimal DiscountAmount = 0m);

public record CounterSaleResult(
    Guid DeliveryId,
    Guid InvoiceId,
    Guid PaymentId,
    string InvoiceNumber,
    decimal Total,
    decimal Paid,
    decimal Change);

/// <summary>
/// The till (requirements §22, "POS sales"). <b>One call, one transaction, three documents.</b>
///
/// At the counter, handing over the goods, raising the bill and taking the money are a single act — the
/// customer who has walked out with a laptop has not "maybe" paid for it. So the delivery, the invoice
/// and the payment are written inside one transaction: if the card is declined, the laptop is still in
/// stock and there is no invoice chasing anybody for it.
///
/// It is deliberately <em>not</em> a fourth kind of sale. It composes the same handlers the documented
/// flow uses — the same delivery that binds the serial, the same invoice that snapshots COGS, the same
/// payment that allocates. A separate POS path with its own stock logic would be the second place stock
/// moves, and there is exactly one (architecture.md §4.5).
/// </summary>
[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.Create)]
public record SellAtCounterCommand(
    Guid CustomerId,
    Guid BranchId,
    Guid WarehouseId,
    IReadOnlyCollection<CounterSaleLine> Lines,
    IReadOnlyCollection<TenderLine> Methods,
    DateTimeOffset? SoldAt = null,

    /// <summary>The manager called over to authorise a price below the floor (§32).</summary>
    Guid? DiscountApprovedBy = null,
    string? Notes = null) : IRequest<CounterSaleResult>;

public class SellAtCounterCommandValidator : AbstractValidator<SellAtCounterCommand>
{
    public SellAtCounterCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A sale must sell at least one line.");
        RuleFor(x => x.Methods).NotEmpty().WithMessage("A counter sale is paid for at the counter.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0);
        });

        RuleForEach(x => x.Methods).ChildRules(method =>
        {
            method.RuleFor(m => m.PaymentMethodId).NotEmpty();
            method.RuleFor(m => m.Amount).GreaterThan(0);
        });
    }
}

public class SellAtCounterCommandHandler : IRequestHandler<SellAtCounterCommand, CounterSaleResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IStockLedger _ledger;
    private readonly IAccountLedger _accounts;
    private readonly ISalesLinePricer _pricer;
    private readonly IDiscountAuthorizer _discounts;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public SellAtCounterCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        IStockLedger ledger,
        IAccountLedger accounts,
        ISalesLinePricer pricer,
        IDiscountAuthorizer discounts,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _ledger = ledger;
        _accounts = accounts;
        _pricer = pricer;
        _discounts = discounts;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<CounterSaleResult> Handle(
        SellAtCounterCommand request,
        CancellationToken cancellationToken)
    {
        // One transaction around all three documents. A declined card must leave the laptop on the shelf.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var soldAt = request.SoldAt ?? _clock.UtcNow;

        var delivered = await new DeliverGoodsCommandHandler(_db, _ledger, _numbers, _clock)
            .PostAsync(
                new DeliverGoodsCommand(
                    BranchId: request.BranchId,
                    WarehouseId: request.WarehouseId,
                    Lines: request.Lines
                        .Select(l => new DeliverLine(l.ProductId, l.Quantity, SerialNumbers: l.SerialNumbers))
                        .ToList(),
                    CustomerId: request.CustomerId,
                    DeliveredAt: soldAt,
                    Notes: request.Notes),
                cancellationToken);

        // What the salesperson agreed at the till, carried onto the bill. The delivery lines come back in
        // the order they were asked for, so this zip cannot put one product's discount on another's.
        var prices = request.Lines
            .Zip(delivered.Lines, (asked, line) => new LinePrice(
                line.Id,
                asked.UnitPrice,
                asked.DiscountPercent,
                asked.DiscountAmount))
            .ToList();

        // The invoice reads the delivery back out of the database, so the delivery has to be in it. This
        // is not the end of the transaction — nothing is committed until the money is in.
        await _db.SaveChangesAsync(cancellationToken);

        var invoiceId = await new RaiseInvoiceCommandHandler(
                _db, _tenant, _pricer, _discounts, _numbers, _clock)
            .PostAsync(
                new RaiseInvoiceCommand(
                    delivered.Delivery.Id,
                    InvoicedAt: soldAt,
                    DueAt: soldAt,
                    LinePrices: prices,
                    DiscountApprovedBy: request.DiscountApprovedBy),
                cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var invoice = await _db.SalesInvoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == invoiceId, cancellationToken);

        var tendered = request.Methods.Sum(m => m.Amount);

        if (tendered < invoice.Total)
        {
            // Part-paying at the till is not a counter sale — it is a credit sale, and it needs an order,
            // a credit limit and somebody to chase. Silently letting the customer leave under-paid would
            // create a receivable nobody knows exists.
            throw new DomainException(
                $"The sale comes to {invoice.Total:0.##} and {tendered:0.##} was tendered. Take the "
                + "balance, or raise it as a credit sale against the customer's account.");
        }

        // Cash is over-tendered constantly — the customer hands over 200 for a 168 sale. The change is
        // handed back, so only what the sale is worth is allocated to it.
        //
        // Settle() below is therefore also what keeps the till honest (P7): the money banked into the cash
        // account is the sale, not the notes that crossed the counter. Bank the 200 and the drawer would
        // claim 32 dirhams that walked out in the customer's pocket.
        var change = tendered - invoice.Total;

        var paymentId = await new RecordPaymentCommandHandler(_db, _tenant, _numbers, _accounts, _clock)
            .PostAsync(
                new RecordPaymentCommand(
                    CustomerId: request.CustomerId,
                    BranchId: request.BranchId,
                    Methods: Settle(request.Methods, invoice.Total),
                    Allocations: [new AllocationLine(invoiceId, invoice.Total)],
                    PaidAt: soldAt,
                    Notes: request.Notes),
                cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CounterSaleResult(
            delivered.Delivery.Id,
            invoiceId,
            paymentId,
            invoice.Number,
            invoice.Total,
            tendered,
            change);
    }

    /// <summary>
    /// What the till actually keeps. Change comes out of the cash drawer, so the last tender is reduced by
    /// it — recording the full 200 the customer handed over would leave the shop holding money it gave
    /// straight back, and the customer's balance in credit for it.
    /// </summary>
    private static List<TenderLine> Settle(IReadOnlyCollection<TenderLine> methods, decimal total)
    {
        var settled = new List<TenderLine>();
        var remaining = total;

        foreach (var method in methods)
        {
            if (remaining <= 0)
            {
                break;   // fully covered by earlier tender; this one was change in the making
            }

            var taken = Math.Min(method.Amount, remaining);

            settled.Add(method with { Amount = taken });
            remaining -= taken;
        }

        return settled;
    }
}
