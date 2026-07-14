using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Returns;

/// <param name="SerialNumbers">
/// Which machines came back. They must be the ones this invoice line actually sold — a serial that was
/// never sold on this line is either a different sale or somebody else's laptop.
/// </param>
/// <param name="Restock">
/// False when the goods did not come back to the shelf: they were faulty and written off, or nothing
/// physical returned at all (a pricing correction). The money still goes back; the stock does not come
/// in. Booking a broken laptop back into sellable stock would sell it to the next customer.
/// </param>
public record ReturnLine(
    Guid SalesInvoiceLineId,
    decimal Quantity,
    IReadOnlyCollection<string>? SerialNumbers = null,
    bool Restock = true);

/// <summary>
/// The customer brought it back (requirements §24).
///
/// <b>This is the only thing in sales that puts stock back</b> — a <c>MovementType.SaleReturn</c> through
/// the ledger, which is also the only thing that can. The returned serials go to <c>Returned</c> and not
/// to <c>InStock</c>: a machine that came back is inspected before it is sold to somebody else, and the
/// state machine will not let it skip that.
///
/// The refund is priced from the <em>invoice</em>, never re-resolved: the customer gets back what they
/// were charged, not what the price list happens to say today.
///
/// <see cref="Refund"/> decides where the money goes. The default the shop should reach for is
/// <see cref="RefundMethod.OffsetAgainstBalance"/> when the invoice is unpaid — handing back cash for
/// money never received would give the customer the shop's own money.
/// </summary>
[RequiresPermission(FeatureCatalog.CreditNotes, PermissionAction.Create)]
/// <param name="RefundFromAccountId">
/// The till or bank account the money is handed back out of (P7). Required for
/// <see cref="RefundMethod.CashRefund"/> and <see cref="RefundMethod.BankRefund"/>, and meaningless for the
/// other two — an offset moves no money, and a store credit is a promise rather than a payment.
/// </param>
public record IssueCreditNoteCommand(
    Guid SalesInvoiceId,
    IReadOnlyCollection<ReturnLine> Lines,
    RefundMethod Refund,
    string Reason,
    Guid? WarehouseId = null,
    Guid? RefundFromAccountId = null,
    DateTimeOffset? IssuedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class IssueCreditNoteCommandValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteCommandValidator()
    {
        RuleFor(x => x.SalesInvoiceId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A credit note must credit at least one line.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("A credit note needs a reason — money is going back, and somebody will ask why.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.SalesInvoiceLineId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });

        // Money that physically leaves has to leave from somewhere. Without this the notes would come out
        // of the drawer and the drawer would not know.
        RuleFor(x => x.RefundFromAccountId)
            .NotEmpty()
            .When(x => x.Refund is RefundMethod.CashRefund or RefundMethod.BankRefund)
            .WithMessage("A refund must say which account the money is handed back out of.");
    }
}

public class IssueCreditNoteCommandHandler : IRequestHandler<IssueCreditNoteCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IStockLedger _ledger;
    private readonly IAccountLedger _accounts;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public IssueCreditNoteCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        IStockLedger ledger,
        IAccountLedger accounts,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _ledger = ledger;
        _accounts = accounts;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(IssueCreditNoteCommand request, CancellationToken cancellationToken)
    {
        // The goods coming back and the money going back are one act. If the stock cannot be returned —
        // a serial that was never sold on this line — the refund must not happen either.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var issuedAt = request.IssuedAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.ResolveAsync(_db, _tenant, cancellationToken);

        var invoice = await _db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .Include(i => i.Delivery)
            .FirstOrDefaultAsync(i => i.Id == request.SalesInvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.SalesInvoiceId);

        if (invoice.Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Cancelled)
        {
            throw new DomainException(
                $"An invoice that is {invoice.Status} has nothing to credit. Nothing was ever billed.");
        }

        var customer = await _db.Customers.FirstAsync(c => c.Id == invoice.CustomerId, cancellationToken);

        var warehouseId = request.WarehouseId
            ?? invoice.Delivery?.WarehouseId
            ?? throw new DomainException(
                "Say which warehouse the goods came back to. The invoice has no delivery to infer it from.");

        var alreadyCredited = await _db.CreditNoteLines
            .Where(l => l.CreditNote.SalesInvoiceId == invoice.Id
                        && l.CreditNote.Status == CreditNoteStatus.Issued)
            .GroupBy(l => l.SalesInvoiceLineId)
            .Select(g => new { LineId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, cancellationToken);

        var creditNote = new CreditNote
        {
            Number = await _numbers.NextAsync(DocumentType.CreditNote, invoice.BranchId, cancellationToken),
            CustomerId = customer.Id,
            BranchId = invoice.BranchId,
            SalesInvoiceId = invoice.Id,
            WarehouseId = warehouseId,
            Status = CreditNoteStatus.Issued,
            RefundMethod = request.Refund,
            CurrencyCode = currency,
            IssuedAt = issuedAt,
            Reason = request.Reason,
            Notes = request.Notes
        };

        _db.CreditNotes.Add(creditNote);

        foreach (var line in request.Lines)
        {
            var invoiceLine = invoice.Lines.FirstOrDefault(l => l.Id == line.SalesInvoiceLineId)
                ?? throw new NotFoundException("Invoice line", line.SalesInvoiceLineId);

            var credited = alreadyCredited.GetValueOrDefault(invoiceLine.Id);
            var creditable = invoiceLine.Quantity - credited;

            if (line.Quantity > creditable)
            {
                // Crediting more than was sold would refund goods the customer never bought — and, if it
                // restocked, conjure inventory out of a refund.
                throw new DomainException(
                    $"That line sold {invoiceLine.Quantity:0.##} and {credited:0.##} has already been "
                    + $"credited. Crediting {line.Quantity:0.##} more would refund goods nobody bought.");
            }

            // Priced from the invoice, never re-resolved. The customer gets back exactly what they paid —
            // including the tax they were charged, at the rate they were charged it.
            var documentLine = new CreditNoteLine
            {
                CreditNoteId = creditNote.Id,
                SalesInvoiceLineId = invoiceLine.Id,
                ProductId = invoiceLine.ProductId,
                Description = invoiceLine.Description,
                Quantity = line.Quantity,
                UnitPrice = invoiceLine.UnitPrice,
                DiscountPercent = invoiceLine.DiscountPercent,
                DiscountAmount = invoiceLine.DiscountAmount,
                TaxPercent = invoiceLine.TaxPercent,
                UnitCost = invoiceLine.UnitCost,
                RestockedToShelf = line.Restock
            };

            // Through the DbSet, and only the DbSet — see ReceiveGoodsCommand.
            _db.CreditNoteLines.Add(documentLine);

            if (!line.Restock || invoiceLine.ProductId is not { } productId)
            {
                // Faulty goods written off, or a service line that was never stock. The money still goes
                // back; nothing comes onto the shelf. Booking a broken laptop back into sellable stock
                // would simply sell it to the next customer.
                continue;
            }

            await GuardSerialsAsync(invoiceLine, line, cancellationToken);

            await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: warehouseId,
                    BranchId: invoice.BranchId,
                    ProductId: productId,
                    Type: MovementType.SaleReturn,
                    Quantity: line.Quantity,
                    ReferenceType: StockReferenceType.CreditNote,
                    ReferenceId: creditNote.Id,
                    ReferenceNumber: creditNote.Number,

                    // The cost it left at, not today's average. Goods coming back at a cost they never had
                    // would move the average by an amount the shop never spent.
                    UnitCost: invoiceLine.UnitCost,
                    SerialNumbers: line.SerialNumbers,
                    OccurredAt: issuedAt,
                    Notes: request.Reason),
                cancellationToken);
        }

        creditNote.Validate();

        await SettleAsync(creditNote, invoice, customer, request.RefundFromAccountId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return creditNote.Id;
    }

    /// <summary>
    /// The serials coming back must be the ones this line sold. Accepting any serial would let a customer
    /// return a machine they bought elsewhere — or one they never bought at all — and be refunded this
    /// invoice's price for it.
    /// </summary>
    private async Task GuardSerialsAsync(
        SalesInvoiceLine invoiceLine,
        ReturnLine line,
        CancellationToken cancellationToken)
    {
        if (line.SerialNumbers is not { Count: > 0 } serials)
        {
            return;
        }

        var sold = await _db.Serials
            .Where(s => serials.Contains(s.SerialNumber))
            .ToListAsync(cancellationToken);

        foreach (var number in serials)
        {
            var serial = sold.FirstOrDefault(s => s.SerialNumber == number)
                ?? throw new DomainException($"Serial {number} is not one this shop has ever held.");

            if (serial.SoldInvoiceLineId != invoiceLine.Id)
            {
                throw new DomainException(
                    $"Serial {number} was not sold on this invoice line. Credit it against the invoice "
                    + "that actually sold it — refunding it here would pay out this line's price for "
                    + "somebody else's machine.");
            }
        }
    }

    /// <summary>
    /// Where the money goes (requirements §24).
    ///
    /// <b>A credit note is a negative invoice, and a refund is a negative payment.</b> Holding both halves
    /// in mind is the only way this arithmetic comes out right:
    ///
    /// <list type="bullet">
    /// <item>the credit note itself takes <c>Total</c> <b>off</b> the customer's balance — they owe less,
    ///   exactly as an invoice made them owe more;</item>
    /// <item>then the refund <b>pays that credit back out</b> — in cash, or as store credit — which puts
    ///   the same amount back on. Net zero.</item>
    /// </list>
    ///
    /// So only <see cref="RefundMethod.OffsetAgainstBalance"/> actually moves the balance, and that is
    /// correct: it is the one method where nothing is handed back. Get this wrong in the obvious way —
    /// deduct on every method — and a customer refunded 100 in cash walks out with the money <em>and</em> a
    /// 100 credit on their account, which the shop discovers the next time they shop.
    /// </summary>
    private async Task SettleAsync(
        CreditNote creditNote,
        SalesInvoice invoice,
        Domain.Catalog.Customer customer,
        Guid? refundFromAccountId,
        CancellationToken cancellationToken)
    {
        // The credit note, as a document: the customer owes this much less.
        customer.Balance -= creditNote.Total;

        switch (creditNote.RefundMethod)
        {
            case RefundMethod.OffsetAgainstBalance:
                // Nothing is handed back. The debt simply shrinks, and it stays shrunk.
                break;

            case RefundMethod.StoreCredit:
                // The shop keeps the money and owes the customer goods instead. The credit is paid out of
                // the balance and into a ledger that can explain itself later — "why do I have 240 credit?"
                // has an answer only if every issue is a row.
                customer.Balance += creditNote.Total;

                var entry = new StoreCreditEntry
                {
                    CustomerId = customer.Id,
                    CreditNoteId = creditNote.Id,
                    Amount = creditNote.Total,
                    OccurredAt = creditNote.IssuedAt,
                    Reason = $"Credit note {creditNote.Number}: {creditNote.Reason}"
                };

                entry.Validate();
                _db.StoreCreditEntries.Add(entry);
                break;

            case RefundMethod.CashRefund:
            case RefundMethod.BankRefund:
                // Money physically leaves the business, which cancels the credit it was paying out.
                //
                // Refusing to refund an invoice that was never paid is the important part: the shop would
                // be handing the customer its own money, and the customer would still owe for the goods
                // they had kept.
                if (invoice.PaidAmount < creditNote.Total)
                {
                    throw new DomainException(
                        $"Invoice {invoice.Number} has only {invoice.PaidAmount:0.##} paid against it, and "
                        + $"this refunds {creditNote.Total:0.##}. Refunding money that never arrived would "
                        + "hand the customer the shop's own money. Offset it against their balance "
                        + "instead.");
                }

                customer.Balance += creditNote.Total;

                // And it leaves an account (P7). Notes that come out of a drawer the system does not know
                // about are notes the cash position still thinks are there. The ledger refuses if the
                // drawer cannot stand it — a shop cannot refund 500 in cash out of a till holding 300,
                // however good the customer's claim is.
                //
                // Checked here and not only in the validator: handlers in this codebase are composed
                // directly as well as dispatched (the till does it), and a rule that lived only in the
                // FluentValidation pipeline would be a rule the composed path skips.
                var refundAccount = refundFromAccountId
                    ?? throw new DomainException(
                        "A refund must say which account the money is handed back out of.");

                await _accounts.PostAsync(
                    new AccountPosting(
                        refundAccount,
                        -creditNote.Total,   // negative: money out
                        AccountTransactionSource.CustomerRefund,
                        $"{customer.Name} — refund on credit note {creditNote.Number}",
                        BranchId: creditNote.BranchId,
                        SourceId: creditNote.Id,
                        SourceNumber: creditNote.Number,
                        OccurredAt: creditNote.IssuedAt),
                    cancellationToken);
                break;

            default:
                throw new DomainException($"Unknown refund method: {creditNote.RefundMethod}.");
        }

        invoice.RefreshPaymentStatus();
    }
}
