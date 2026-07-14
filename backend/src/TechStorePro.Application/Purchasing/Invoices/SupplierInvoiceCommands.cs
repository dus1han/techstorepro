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

namespace TechStorePro.Application.Purchasing.Invoices;

/// <param name="ProductId">
/// Null for a line that is not a product at all — a delivery charge the supplier put on the invoice
/// itself. Those are real and they must be billable, or the invoice would not add up to what the
/// supplier is actually asking for.
/// </param>
public record SupplierInvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid? ProductId = null,
    decimal DiscountPercent = 0m,
    decimal TaxPercent = 0m);

/// <summary>
/// Record what the supplier is asking to be paid (requirements §25).
///
/// <b>This does not touch stock.</b> The goods receipt already did that, and an invoice that moved stock
/// as well would double it. The two documents are deliberately separate because they answer different
/// questions and they genuinely disagree: the goods arrive in March and the invoice in April; three of
/// the ten units were damaged and short-invoiced; the price billed is not the price ordered. One document
/// would force those disagreements to be resolved silently in favour of whichever arrived last.
///
/// <see cref="GoodsReceiptId"/> is therefore optional — the invoice may arrive before the goods, or cover
/// several receipts at once.
/// </summary>
[RequiresPermission(FeatureCatalog.SupplierInvoices, PermissionAction.Create)]
public record RecordSupplierInvoiceCommand(
    Guid SupplierId,
    Guid BranchId,
    string SupplierReference,
    IReadOnlyCollection<SupplierInvoiceLineInput> Lines,
    Guid? GoodsReceiptId = null,
    string CurrencyCode = "AED",
    decimal ExchangeRate = 1m,
    DateTimeOffset? InvoicedAt = null,
    DateTimeOffset? DueAt = null,

    /// <summary>
    /// Post it immediately, which is what puts the debt on the supplier's balance. A draft is a bill
    /// somebody is still checking against the receipt; it owes nothing until it is posted.
    /// </summary>
    bool Post = true,
    string? Notes = null) : IRequest<Guid>;

public class RecordSupplierInvoiceCommandValidator : AbstractValidator<RecordSupplierInvoiceCommand>
{
    public RecordSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);

        RuleFor(x => x.SupplierReference)
            .NotEmpty()
            .WithMessage(
                "A supplier invoice needs the supplier's own reference — without it nobody can match "
                + "this row to the piece of paper the supplier will chase you with.");

        RuleFor(x => x.Lines).NotEmpty().WithMessage("A supplier invoice with no lines bills nothing.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.TaxPercent).InclusiveBetween(0, 100);
        });
    }
}

public class RecordSupplierInvoiceCommandHandler : IRequestHandler<RecordSupplierInvoiceCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public RecordSupplierInvoiceCommandHandler(
        IApplicationDbContext db,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(RecordSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        if (request.GoodsReceiptId is { } receiptId)
        {
            var receipt = await _db.GoodsReceipts
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
                ?? throw new NotFoundException("Goods receipt", receiptId);

            if (receipt.SupplierId != request.SupplierId)
            {
                // The debt would be booked against one supplier and the goods credited to another: the
                // wrong party gets paid, and the right one keeps chasing.
                throw new DomainException("That goods receipt belongs to a different supplier.");
            }
        }

        var invoice = new SupplierInvoice
        {
            Number = await _numbers.NextAsync(DocumentType.SupplierInvoice, request.BranchId, cancellationToken),
            SupplierReference = request.SupplierReference.Trim(),
            SupplierId = supplier.Id,
            BranchId = request.BranchId,
            GoodsReceiptId = request.GoodsReceiptId,
            Status = SupplierInvoiceStatus.Draft,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            InvoicedAt = request.InvoicedAt ?? _clock.UtcNow,
            DueAt = request.DueAt,
            Notes = request.Notes
        };

        _db.SupplierInvoices.Add(invoice);

        foreach (var line in request.Lines)
        {
            if (line.ProductId is { } productId
                && !await _db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
            {
                throw new NotFoundException("Product", productId);
            }

            // Through the DbSet, and ONLY the DbSet — EF's fixup adds it to invoice.Lines itself, and
            // Total is a SUM over that collection. Adding it twice would double the debt.
            _db.SupplierInvoiceLines.Add(new SupplierInvoiceLine
            {
                SupplierInvoiceId = invoice.Id,
                ProductId = line.ProductId,
                Description = line.Description.Trim(),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                TaxPercent = line.TaxPercent
            });
        }

        if (request.Post)
        {
            // Post() validates and flips the status. Do it before touching the balance: an invoice the
            // entity rejects must not have moved money first.
            invoice.Post();

            // The debt, in the company's own money, fixed at the invoice-date rate. This is the number
            // the FX gain or loss is later measured against — see PaySupplierCommand, which must remove
            // exactly this much when the invoice is settled, no matter what rate the money goes out at.
            supplier.Balance += invoice.TotalBase;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return invoice.Id;
    }
}

// --- Post a draft -------------------------------------------------------------------------------

/// <summary>Posting is what puts the debt on the supplier's balance. A draft owes nothing.</summary>
[RequiresPermission(FeatureCatalog.SupplierInvoices, PermissionAction.Approve)]
public record PostSupplierInvoiceCommand(Guid SupplierInvoiceId) : IRequest;

public class PostSupplierInvoiceCommandHandler : IRequestHandler<PostSupplierInvoiceCommand>
{
    private readonly IApplicationDbContext _db;

    public PostSupplierInvoiceCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(PostSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var invoice = await _db.SupplierInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.SupplierInvoiceId, cancellationToken)
            ?? throw new NotFoundException("Supplier invoice", request.SupplierInvoiceId);

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", invoice.SupplierId);

        invoice.Post();

        supplier.Balance += invoice.TotalBase;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

// --- Cancel -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.SupplierInvoices, PermissionAction.Edit)]
public record CancelSupplierInvoiceCommand(Guid SupplierInvoiceId, string Reason) : IRequest;

public class CancelSupplierInvoiceCommandValidator : AbstractValidator<CancelSupplierInvoiceCommand>
{
    public CancelSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.SupplierInvoiceId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Cancelling an invoice needs a reason.");
    }
}

public class CancelSupplierInvoiceCommandHandler : IRequestHandler<CancelSupplierInvoiceCommand>
{
    private readonly IApplicationDbContext _db;

    public CancelSupplierInvoiceCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(CancelSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var invoice = await _db.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstOrDefaultAsync(i => i.Id == request.SupplierInvoiceId, cancellationToken)
            ?? throw new NotFoundException("Supplier invoice", request.SupplierInvoiceId);

        var wasPosted = invoice.Status is not SupplierInvoiceStatus.Draft;

        // The entity refuses to cancel an invoice that has been paid against: the payment would be left
        // pointing at nothing.
        invoice.Cancel();

        if (wasPosted)
        {
            // The debt was on the balance; cancelling takes it back off. An unposted draft never added
            // it, so removing it here would push the supplier's balance negative for a bill nobody owed.
            var supplier = await _db.Suppliers
                .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken)
                ?? throw new NotFoundException("Supplier", invoice.SupplierId);

            supplier.Balance -= invoice.TotalBase;
        }

        invoice.Notes = string.IsNullOrWhiteSpace(invoice.Notes)
            ? $"Cancelled: {request.Reason}"
            : $"{invoice.Notes}\nCancelled: {request.Reason}";

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
