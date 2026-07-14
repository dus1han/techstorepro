using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Purchasing;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.GoodsReceipts;

/// <param name="SerialNumbers">
/// One per unit, for a serial-tracked product. Captured at the door rather than at the sale, because
/// that is what makes a warranty claim answerable two years later: the serial ties the laptop on the
/// counter back to the container it arrived in and what it actually cost.
/// </param>
public record ReceiveLine(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    Guid? PurchaseOrderLineId = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    string? Notes = null);

/// <summary>
/// Goods have arrived (requirements §27). <b>This is what puts them on the shelf.</b>
///
/// <see cref="PurchaseOrderId"/> is optional — requirements §25 says so outright, and the direct
/// purchase (supplier → GRN → stock) is a first-class flow, not a workaround for a missing order.
///
/// <see cref="ImportShipmentId"/> is optional too. When it is set, the goods came in a container whose
/// freight, duty and clearing may not have been invoiced yet: the receipt posts at the goods price now,
/// and the landed cost is folded in later by <c>ApportionLandedCostCommand</c>. Waiting for the clearing
/// agent before booking stock the shop can physically see would be absurd.
/// </summary>
[RequiresPermission(FeatureCatalog.GoodsReceipts, PermissionAction.Create)]
public record ReceiveGoodsCommand(
    Guid SupplierId,
    Guid BranchId,
    Guid WarehouseId,
    IReadOnlyCollection<ReceiveLine> Lines,
    Guid? PurchaseOrderId = null,
    Guid? ImportShipmentId = null,
    string CurrencyCode = "AED",
    decimal ExchangeRate = 1m,
    string? SupplierReference = null,
    DateTimeOffset? ReceivedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class ReceiveGoodsCommandValidator : AbstractValidator<ReceiveGoodsCommand>
{
    public ReceiveGoodsCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A goods receipt must receive at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
        });
    }
}

public class ReceiveGoodsCommandHandler : IRequestHandler<ReceiveGoodsCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public ReceiveGoodsCommandHandler(
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

    public async Task<Guid> Handle(ReceiveGoodsCommand request, CancellationToken cancellationToken)
    {
        // One transaction around the document, its number and every movement it makes. If any line
        // fails — an unknown product, a serial that already belongs to another machine — the whole
        // receipt rolls back, the number is given back rather than burnt, and no stock moved. A
        // half-received delivery is worse than a rejected one.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var receivedAt = request.ReceivedAt ?? _clock.UtcNow;

        var order = await LoadOrderAsync(request, cancellationToken);
        var shipment = await LoadShipmentAsync(request, cancellationToken);

        var receipt = new GoodsReceipt
        {
            Number = await _numbers.NextAsync(DocumentType.GoodsReceipt, request.BranchId, cancellationToken),
            SupplierId = request.SupplierId,
            BranchId = request.BranchId,
            WarehouseId = request.WarehouseId,
            PurchaseOrderId = order?.Id,
            ImportShipmentId = shipment?.Id,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            SupplierReference = request.SupplierReference,
            ReceivedAt = receivedAt,
            Notes = request.Notes
        };

        _db.GoodsReceipts.Add(receipt);

        foreach (var line in request.Lines)
        {
            var documentLine = new GoodsReceiptLine
            {
                GoodsReceiptId = receipt.Id,
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                Notes = line.Notes,

                // Zero until the container's charges are known. For a local purchase it stays zero,
                // because nothing was shipped and so nothing was apportioned.
                ApportionedCost = 0m
            };

            // Through the DbSet, and ONLY the DbSet.
            //
            // It has to be the DbSet: PostAsync below saves inside this transaction, so `receipt` is
            // Unchanged by the time these lines are attached, and EF would take a child with an id
            // already set (BaseEntity assigns one) for an existing row and emit an UPDATE that matches
            // nothing.
            //
            // And it has to be *only* the DbSet: EF's relationship fixup adds it to receipt.Lines by
            // itself, so adding it there by hand as well would put the same instance in the collection
            // twice — and every total computed from Lines would be double.
            _db.GoodsReceiptLines.Add(documentLine);

            // The cost the ledger books, in base currency. It is the goods price only: the freight has
            // not been invoiced yet and inventing a figure for it would feed a guess into the moving
            // average, where — because the average is moving — it would never wash out.
            var unitCostBase = documentLine.LineTotal * receipt.ExchangeRate / line.Quantity;

            var result = await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: request.WarehouseId,
                    BranchId: request.BranchId,
                    ProductId: line.ProductId,
                    Type: MovementType.Receipt,
                    Quantity: line.Quantity,
                    ReferenceType: StockReferenceType.GoodsReceipt,
                    ReferenceId: receipt.Id,
                    ReferenceNumber: receipt.Number,
                    UnitCost: unitCostBase,
                    SerialNumbers: line.SerialNumbers,
                    OccurredAt: receivedAt,
                    Notes: line.Notes),
                cancellationToken);

            // Keep the serials the ledger created, so the receipt can answer "which machines came in
            // this box?" without replaying the ledger.
            foreach (var movement in result.Movements.Where(m => m.SerialId is not null))
            {
                var serial = await _db.Serials.FirstAsync(s => s.Id == movement.SerialId, cancellationToken);

                _db.GoodsReceiptSerials.Add(new GoodsReceiptSerial
                {
                    GoodsReceiptLineId = documentLine.Id,
                    SerialNumber = serial.SerialNumber,
                    SerialId = serial.Id
                });
            }

            // Tick the order off, if there was one.
            if (ResolveOrderLine(order, line) is { } orderLine)
            {
                orderLine.ReceivedQuantity += line.Quantity;

                // Keep the link on the document too. Without it the receipt could not say which line of
                // the order it fulfilled, and the next receipt against the same order would have to
                // guess all over again.
                documentLine.PurchaseOrderLineId = orderLine.Id;
            }
        }

        receipt.Validate();

        order?.RefreshReceiptStatus();

        // The goods are here, so the shipment has landed — even if its charges have not.
        if (shipment is not null && shipment.Status == ImportShipmentStatus.InTransit)
        {
            shipment.Arrive(receivedAt);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return receipt.Id;
    }

    /// <summary>
    /// Which line of the order this receipt line fulfils, if any.
    ///
    /// <b>The caller does not have to name it.</b> It used to have to, and the failure was silent and
    /// expensive: a receipt that named the order but not its lines posted the stock, captured the
    /// serials, and left the order sitting at <c>Approved</c> for ever — fully delivered, still showing
    /// as outstanding, and chased. Nothing errored, so nobody found out until someone asked why the
    /// supplier kept sending goods that had already arrived.
    ///
    /// So an unnamed line is matched on its product instead. That is unambiguous for the order this
    /// system raises — one line per product — and where it is <em>not</em> unambiguous, the ambiguity is
    /// reported rather than guessed at: crediting the wrong line would leave one half of the order
    /// permanently over-received and the other half permanently open.
    /// </summary>
    private static PurchaseOrderLine? ResolveOrderLine(PurchaseOrder? order, ReceiveLine line)
    {
        if (order is null)
        {
            return null;   // A direct purchase. There is no order to tick off.
        }

        if (line.PurchaseOrderLineId is { } explicitId)
        {
            return order.Lines.FirstOrDefault(l => l.Id == explicitId)
                ?? throw new NotFoundException("Purchase order line", explicitId);
        }

        var candidates = order.Lines.Where(l => l.ProductId == line.ProductId).ToList();

        if (candidates.Count > 1)
        {
            throw new DomainException(
                $"This order has {candidates.Count} lines for the same product, so it cannot be told "
                + "which one these goods fulfil. Name the purchase order line explicitly.");
        }

        // None is legitimate: goods can arrive that were never on the order. They are received — the box
        // is real and it is standing in the doorway — they simply tick nothing off.
        return candidates.SingleOrDefault();
    }

    private async Task<PurchaseOrder?> LoadOrderAsync(
        ReceiveGoodsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PurchaseOrderId is not { } id)
        {
            return null;   // A direct purchase. Requirements §25 — this is normal, not an omission.
        }

        var order = await _db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Purchase order", id);

        if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
        {
            throw new DomainException(
                $"Goods cannot be received against a {order.Status} purchase order. Approve it first.");
        }

        if (order.SupplierId != request.SupplierId)
        {
            // The goods would be credited to one supplier and the order ticked off against another.
            throw new DomainException("That purchase order belongs to a different supplier.");
        }

        return order;
    }

    private async Task<ImportShipment?> LoadShipmentAsync(
        ReceiveGoodsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ImportShipmentId is not { } id)
        {
            return null;
        }

        var shipment = await _db.ImportShipments
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("Import shipment", id);

        if (shipment.Status == ImportShipmentStatus.Costed)
        {
            // The container's charges have already been spread over the goods that were in it. Adding
            // more goods now would mean those units carry none of the freight, and the ones already
            // received carry all of it — the same container, costed two different ways.
            throw new DomainException(
                "This shipment has already been costed. Goods received now would carry none of its "
                + "freight. Receive them against a new shipment.");
        }

        if (shipment.Status == ImportShipmentStatus.Cancelled)
        {
            throw new DomainException("That shipment is cancelled.");
        }

        return shipment;
    }
}
