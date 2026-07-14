using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Invoices;

/// <summary>
/// Something billed that is not goods — a delivery charge, a callout fee, labour. It consumes no stock,
/// so it carries no cost and no serial.
/// </summary>
public record ServiceLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxPercent = 0m,
    decimal DiscountPercent = 0m,
    decimal DiscountAmount = 0m);

/// <summary>
/// Bill the customer for goods that have been delivered (requirements §22).
///
/// <b>The invoice does not move stock</b> — the delivery already did, and an invoice that moved it as
/// well would issue the same laptop twice. What the invoice does is turn the delivery into money: it
/// prices the delivered lines, snapshots the tax rate and the COGS the ledger came back with, and raises
/// the customer's balance by the total. That balance is the receivable P7 will report on.
///
/// It binds each delivered serial to the invoice line that sold it (<c>Serial.SoldInvoiceLineId</c>).
/// <b>That link is what makes a warranty claim answerable two years later</b> — P6 walks it backwards
/// from a machine on the counter to the sale that put it in the customer's hands.
/// </summary>
/// <summary>
/// What the salesperson actually charged for one picked line, when it is not simply what the price list
/// says — the haggle at the counter (requirements §22, "Discount").
///
/// It exists because a counter sale has no order to carry the agreed price on. An ordered line already
/// has one, and its price wins; see the handler.
/// </summary>
public record LinePrice(
    Guid DeliveryLineId,
    decimal? UnitPrice = null,
    decimal DiscountPercent = 0m,
    decimal DiscountAmount = 0m);

[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.Create)]
public record RaiseInvoiceCommand(
    Guid DeliveryId,
    DateTimeOffset? InvoicedAt = null,
    DateTimeOffset? DueAt = null,
    IReadOnlyCollection<ServiceLine>? ServiceLines = null,
    IReadOnlyCollection<LinePrice>? LinePrices = null,
    string? CurrencyCode = null,
    Guid? DiscountApprovedBy = null,
    string? Notes = null) : IRequest<Guid>;

public class RaiseInvoiceCommandValidator : AbstractValidator<RaiseInvoiceCommand>
{
    public RaiseInvoiceCommandValidator()
    {
        RuleFor(x => x.DeliveryId).NotEmpty();

        RuleForEach(x => x.ServiceLines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty().MaximumLength(500);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.TaxPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0);
        }).When(x => x.ServiceLines is not null);
    }
}

public class RaiseInvoiceCommandHandler : IRequestHandler<RaiseInvoiceCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISalesLinePricer _pricer;
    private readonly IDiscountAuthorizer _discounts;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public RaiseInvoiceCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        ISalesLinePricer pricer,
        IDiscountAuthorizer discounts,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _pricer = pricer;
        _discounts = discounts;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(RaiseInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var invoiceId = await PostAsync(request, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return invoiceId;
    }

    /// <summary>
    /// The invoice itself, without owning the transaction — so the counter sale can raise it in the same
    /// transaction that moved the goods and took the money. See <c>SellAtCounterCommand</c>.
    /// </summary>
    internal async Task<Guid> PostAsync(RaiseInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoicedAt = request.InvoicedAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.EnsureAsync(_db, _tenant, request.CurrencyCode, cancellationToken);

        var delivery = await _db.Deliveries
            .Include(d => d.Lines).ThenInclude(l => l.Serials)
            .FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        if (delivery.Status == DeliveryStatus.Invoiced)
        {
            // Billing the same delivery twice would double the customer's debt and double the revenue,
            // and the second invoice would look exactly as legitimate as the first.
            throw new DomainException(
                $"Delivery {delivery.Number} has already been invoiced. Bill it again and the customer "
                + "owes for the same goods twice.");
        }

        if (delivery.Status == DeliveryStatus.Cancelled)
        {
            throw new DomainException("That delivery was cancelled. There are no goods to bill for.");
        }

        var customer = await _db.Customers.FirstAsync(c => c.Id == delivery.CustomerId, cancellationToken);

        var orderLines = delivery.SalesOrderId is { } orderId
            ? await _db.SalesOrderLines.Where(l => l.SalesOrderId == orderId).ToListAsync(cancellationToken)
            : [];

        var invoice = new SalesInvoice
        {
            Number = await _numbers.NextAsync(DocumentType.Invoice, delivery.BranchId, cancellationToken),
            CustomerId = customer.Id,
            BranchId = delivery.BranchId,
            SalesOrderId = delivery.SalesOrderId,
            DeliveryId = delivery.Id,
            Status = SalesInvoiceStatus.Draft,
            CurrencyCode = currency,
            InvoicedAt = invoicedAt,

            // Due on the customer's terms. Zero days means due on receipt, and null says so.
            DueAt = request.DueAt
                ?? (customer.PaymentTermDays > 0
                    ? invoicedAt.AddDays(customer.PaymentTermDays)
                    : null),
            Notes = request.Notes
        };

        _db.SalesInvoices.Add(invoice);

        foreach (var deliveryLine in delivery.Lines)
        {
            var orderLine = deliveryLine.SalesOrderLineId is { } orderLineId
                ? orderLines.FirstOrDefault(l => l.Id == orderLineId)
                : null;

            var over = request.LinePrices?.FirstOrDefault(p => p.DeliveryLineId == deliveryLine.Id);

            // An ordered line is billed at the price it was ordered at. Re-resolving it here would mean a
            // price list published between the order and the delivery silently changed what the customer
            // agreed to pay.
            var priced = orderLine is not null
                ? new PricedLine(
                    orderLine.UnitPrice,
                    orderLine.DiscountPercent,
                    orderLine.DiscountAmount,
                    orderLine.TaxPercent,
                    orderLine.PriceSource ?? "Sales order",
                    NetTotal: 0m,
                    MinimumPrice: null,
                    RequiresApproval: false)

                // No order behind it — a counter sale. Price it from the list, with whatever the
                // salesperson agreed at the till applied on top and checked against the floor.
                : await _pricer.PriceAsync(
                    deliveryLine.ProductId,
                    customer.Id,
                    deliveryLine.Quantity,
                    unitPriceOverride: over?.UnitPrice,
                    discountPercent: over?.DiscountPercent ?? 0m,
                    discountAmount: over?.DiscountAmount ?? 0m,
                    asOf: invoicedAt,
                    cancellationToken: cancellationToken);

            var product = await _db.Products.FirstAsync(p => p.Id == deliveryLine.ProductId, cancellationToken);

            // Below the floor on its price list. Someone with (Sales, Approve) has to own that — and who
            // they were is stamped onto the line below, because the question anyone asks later is not
            // "was this approved?" but "who approved this?".
            Guid? approvedBy = priced.RequiresApproval
                ? await _discounts.AuthoriseAsync(
                    product.Name,
                    priced.MinimumPrice ?? 0m,
                    request.DiscountApprovedBy,
                    cancellationToken)
                : null;

            var invoiceLine = new SalesInvoiceLine
            {
                SalesInvoiceId = invoice.Id,
                DeliveryLineId = deliveryLine.Id,
                ProductId = deliveryLine.ProductId,
                Description = orderLine?.Description ?? product.Name,
                Quantity = deliveryLine.Quantity,
                UnitPrice = priced.UnitPrice,
                DiscountPercent = priced.DiscountPercent,
                DiscountAmount = priced.DiscountAmount,
                TaxPercent = priced.TaxPercent,
                PriceSource = priced.PriceSource,

                // Carried from the delivery, where the ledger valued it. Not recomputed: by now the
                // average may have moved, and the margin on this sale would be restated by accident.
                UnitCost = deliveryLine.UnitCost,

                DiscountApprovedBy = approvedBy
            };

            _db.SalesInvoiceLines.Add(invoiceLine);

            // Bind each machine to the line that sold it. P6's warranty claim starts from a serial on the
            // counter and has to find its way here.
            foreach (var delivered in deliveryLine.Serials)
            {
                var serial = await _db.Serials.FirstAsync(s => s.Id == delivered.SerialId, cancellationToken);

                serial.SoldInvoiceLineId = invoiceLine.Id;
            }
        }

        foreach (var service in request.ServiceLines ?? [])
        {
            _db.SalesInvoiceLines.Add(new SalesInvoiceLine
            {
                SalesInvoiceId = invoice.Id,
                ProductId = null,
                Description = service.Description,
                Quantity = service.Quantity,
                UnitPrice = service.UnitPrice,
                DiscountPercent = service.DiscountPercent,
                DiscountAmount = service.DiscountAmount,
                TaxPercent = service.TaxPercent,
                PriceSource = "Service line",
                UnitCost = 0m   // no stock consumed, so no cost of goods
            });
        }

        invoice.Post();

        // The debt, recorded. Posting the bill and recording what the customer owes are one act — an
        // invoice that did not raise the balance would be a receivable nobody chases.
        customer.Balance += invoice.Total;

        delivery.Status = DeliveryStatus.Invoiced;

        return invoice.Id;
    }
}

/// <summary>
/// Cancel an unpaid invoice. Refused once money has been received against it — see
/// <see cref="SalesInvoice.Cancel"/>. The stock is <em>not</em> returned: the goods left at the
/// delivery, and getting them back is a credit note, not a cancellation.
/// </summary>
[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.Delete)]
public record CancelInvoiceCommand(Guid InvoiceId, string Reason) : IRequest<Unit>;

public class CancelInvoiceCommandValidator : AbstractValidator<CancelInvoiceCommand>
{
    public CancelInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class CancelInvoiceCommandHandler : IRequestHandler<CancelInvoiceCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public CancelInvoiceCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(CancelInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var invoice = await _db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        var wasPosted = invoice.Status == SalesInvoiceStatus.Posted;

        invoice.Cancel();

        if (wasPosted)
        {
            // Take the debt back off the customer. It was put there by posting; cancelling has to undo
            // exactly that and nothing else.
            invoice.Customer.Balance -= invoice.Total;
        }

        invoice.Notes = string.IsNullOrWhiteSpace(invoice.Notes)
            ? $"Cancelled: {request.Reason}"
            : $"{invoice.Notes}\nCancelled: {request.Reason}";

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}
