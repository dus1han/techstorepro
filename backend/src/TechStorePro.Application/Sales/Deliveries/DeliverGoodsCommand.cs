using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Deliveries;

/// <param name="SerialNumbers">
/// Which machines are going out — one per unit, for a serial-tracked product. These must already exist
/// and be in this warehouse: they were captured at the goods receipt (P4), and a serial that appears for
/// the first time at the door is a serial nobody bought.
/// </param>
public record DeliverLine(
    Guid ProductId,
    decimal Quantity,
    Guid? SalesOrderLineId = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    string? Notes = null);

/// <summary>
/// The goods leave (requirements §22). <b>This is the only thing in sales that moves stock.</b>
///
/// <see cref="SalesOrderId"/> is optional, deliberately: a counter sale hands the goods over on the
/// spot, and there is no order to raise first. Requiring one would produce orders written after the
/// fact — the same fiction that requiring a PO for every goods receipt would produce (§25).
/// </summary>
[RequiresPermission(FeatureCatalog.Deliveries, PermissionAction.Create)]
public record DeliverGoodsCommand(
    Guid BranchId,
    Guid WarehouseId,
    IReadOnlyCollection<DeliverLine> Lines,
    Guid? CustomerId = null,
    Guid? SalesOrderId = null,
    DateTimeOffset? DeliveredAt = null,
    string? DeliveredTo = null,
    string? Notes = null) : IRequest<Guid>;

/// <summary>What a delivery posting produced — the document, and its lines in the order they were asked for.</summary>
internal record DeliveryPosting(Delivery Delivery, IReadOnlyList<DeliveryLine> Lines);

public class DeliverGoodsCommandValidator : AbstractValidator<DeliverGoodsCommand>
{
    public DeliverGoodsCommandValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A delivery must deliver at least one line.");

        RuleFor(x => x)
            .Must(x => x.CustomerId is not null || x.SalesOrderId is not null)
            .WithMessage("A delivery needs a customer, or an order to take one from.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

public class DeliverGoodsCommandHandler : IRequestHandler<DeliverGoodsCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public DeliverGoodsCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(DeliverGoodsCommand request, CancellationToken cancellationToken)
    {
        // One transaction around the document, its number and every movement it makes. A half-delivered
        // delivery — three lines off the shelf and the fourth refused — would leave stock gone with no
        // document accounting for it.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var delivered = await PostAsync(request, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return delivered.Delivery.Id;
    }

    /// <summary>
    /// The delivery itself, without owning the transaction — so the counter sale can move the goods, raise
    /// the bill and take the money inside <em>one</em> transaction. A declined card must leave the laptop
    /// on the shelf, and that is only true if all three share a rollback.
    /// </summary>
    /// <remarks>
    /// The lines come back <b>in the order they were requested</b>, and not through
    /// <c>delivery.Lines</c>: that collection is populated by EF's fixup, whose order is an implementation
    /// detail. The counter sale zips these against its own lines to carry each one's discount onto the
    /// invoice, and zipping against an arbitrary order would put one product's discount on another's.
    /// </remarks>
    internal async Task<DeliveryPosting> PostAsync(
        DeliverGoodsCommand request,
        CancellationToken cancellationToken)
    {
        var deliveredAt = request.DeliveredAt ?? _clock.UtcNow;

        var order = await LoadOrderAsync(request, cancellationToken);

        var customerId = order?.CustomerId
            ?? request.CustomerId
            ?? throw new DomainException("A delivery needs a customer.");

        if (!await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            throw new NotFoundException("Customer", customerId);
        }

        var warehouseId = order?.WarehouseId ?? request.WarehouseId;

        var delivery = new Delivery
        {
            Number = await _numbers.NextAsync(DocumentType.DeliveryNote, request.BranchId, cancellationToken),
            CustomerId = customerId,
            BranchId = request.BranchId,
            WarehouseId = warehouseId,
            SalesOrderId = order?.Id,
            Status = DeliveryStatus.Delivered,
            DeliveredAt = deliveredAt,
            DeliveredTo = request.DeliveredTo,
            Notes = request.Notes
        };

        _db.Deliveries.Add(delivery);

        var documentLines = new List<DeliveryLine>();

        foreach (var line in request.Lines)
        {
            var orderLine = ResolveOrderLine(order, line);

            var documentLine = new DeliveryLine
            {
                DeliveryId = delivery.Id,
                SalesOrderLineId = orderLine?.Id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                Notes = line.Notes,
                UnitCost = 0m   // the ledger decides this, below
            };

            // Through the DbSet, and only the DbSet. See ReceiveGoodsCommand — adding to the parent's
            // collection as well double-counts every total computed from it.
            _db.DeliveryLines.Add(documentLine);
            documentLines.Add(documentLine);

            var result = await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: warehouseId,
                    BranchId: request.BranchId,
                    ProductId: line.ProductId,
                    Type: MovementType.Sale,
                    Quantity: line.Quantity,
                    ReferenceType: StockReferenceType.Delivery,
                    ReferenceId: delivery.Id,
                    ReferenceNumber: delivery.Number,

                    // No UnitCost: an issue is valued at the warehouse's moving average, and the ledger
                    // is the only thing that knows what that is right now. A cost passed in here would be
                    // the caller inventing COGS.
                    UnitCost: null,
                    SerialNumbers: line.SerialNumbers,

                    // Hand the promise back, so that picking the goods consumes the reservation instead of
                    // competing with it. Without this, delivering the two units the order reserved would
                    // fail its own availability check against the reservation it made.
                    ReservationId: orderLine?.StockReservationId,
                    OccurredAt: deliveredAt,
                    Notes: line.Notes),
                cancellationToken);

            // COGS, snapshotted at the moment the stock moved (§45 D1). Recomputing it later would
            // restate the margin on every sale the shop has made, because the average keeps moving.
            documentLine.UnitCost = result.UnitCost;

            // Which machines went out of the door — one row per unit. This is what a warranty claim will
            // follow back in P6.
            foreach (var movement in result.Movements.Where(m => m.SerialId is not null))
            {
                var serial = await _db.Serials.FirstAsync(s => s.Id == movement.SerialId, cancellationToken);

                _db.DeliverySerials.Add(new DeliverySerial
                {
                    DeliveryLineId = documentLine.Id,
                    SerialNumber = serial.SerialNumber,
                    SerialId = serial.Id
                });
            }

            if (orderLine is not null)
            {
                orderLine.DeliveredQuantity += line.Quantity;
            }
        }

        delivery.Validate();

        order?.RefreshDeliveryStatus();

        return new DeliveryPosting(delivery, documentLines);
    }

    private async Task<SalesOrder?> LoadOrderAsync(
        DeliverGoodsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SalesOrderId is not { } id)
        {
            return null;   // A counter sale. Normal, not an omission.
        }

        var order = await _db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Sales order", id);

        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyDelivered))
        {
            // A draft order has reserved nothing, so delivering against it would take stock the shop
            // never promised — and quietly, since the order would still read as unconfirmed afterwards.
            throw new DomainException(
                $"Goods cannot be delivered against a {order.Status} order. Confirm it first — that is "
                + "what reserves the stock.");
        }

        if (request.CustomerId is { } requested && requested != order.CustomerId)
        {
            throw new DomainException("That order belongs to a different customer.");
        }

        return order;
    }

    /// <summary>
    /// Ties a delivery line back to the order line it fulfils, and refuses to over-deliver it.
    /// </summary>
    private static SalesOrderLine? ResolveOrderLine(SalesOrder? order, DeliverLine line)
    {
        if (order is null)
        {
            return null;
        }

        var orderLine = line.SalesOrderLineId is { } lineId
            ? order.Lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new NotFoundException("Sales order line", lineId)

            // The caller did not say which line this fulfils, so match on the product. Ambiguity is
            // resolved by refusing, not by guessing: two lines for the same product on one order is a
            // legitimate thing to do (different prices, different discounts), and picking one at random
            // would tick off the wrong one.
            : SingleLineFor(order, line.ProductId);

        if (orderLine is null)
        {
            return null;
        }

        if (line.Quantity > orderLine.OutstandingQuantity)
        {
            throw new DomainException(
                $"That line orders {orderLine.Quantity:0.##} and has already had "
                + $"{orderLine.DeliveredQuantity:0.##} delivered. Delivering {line.Quantity:0.##} more "
                + "would send out goods nobody ordered — and the reservation only covers what was.");
        }

        return orderLine;
    }

    private static SalesOrderLine? SingleLineFor(SalesOrder order, Guid productId)
    {
        var matches = order.Lines.Where(l => l.ProductId == productId && l.OutstandingQuantity > 0).ToList();

        return matches.Count switch
        {
            0 => throw new DomainException(
                "That product is not outstanding on this order. Deliver it as a counter sale, or add it "
                + "to the order first."),
            1 => matches[0],
            _ => throw new DomainException(
                "This order has more than one outstanding line for that product. Say which line is being "
                + "delivered — guessing would tick off the wrong price.")
        };
    }
}
