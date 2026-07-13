using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.Imports;

/// <param name="Absorbed">What actually reached inventory and raised the moving average.</param>
/// <param name="Unabsorbed">
/// What could not: the stock it belonged to had already been sold by the time the charge arrived. Real
/// money, with nowhere in inventory to live. Reported rather than hidden.
/// </param>
public record ApportionmentResultDto(
    Guid ShipmentId,
    decimal TotalCharges,
    decimal Absorbed,
    decimal Unabsorbed,
    IReadOnlyCollection<ApportionedLineDto> Lines);

public record ApportionedLineDto(
    Guid GoodsReceiptLineId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal LineValueBase,
    decimal ApportionedCost,
    decimal LandedUnitCost,
    decimal QuantityStillInStock,
    decimal AbsorbedCost);

/// <summary>
/// Folds a container's freight, duty and clearing into the cost of the goods it carried
/// (requirements §26, decision D6 — <b>by value</b>).
///
/// <b>This is the sharpest edge in the system.</b> Costing is weighted average (D1), so the number this
/// command produces does not merely price one container: it feeds the moving average of every product
/// it touches, and spreads to units that arrived years ago. An error here is not localised and it never
/// washes out. That is why the arithmetic lives in <see cref="LandedCostApportionment"/> as a pure
/// function tested against the business's own worked example, and why this handler does nothing but
/// gather the inputs and post the results.
///
/// <para><b>Why it revalues instead of receiving at the right cost.</b> Goods and their true cost do not
/// arrive together: the container is unpacked in March and the clearing agent invoices in April. The
/// receipt has already posted — refusing to book stock the shop can see on the shelf would be absurd —
/// so the charges arrive as a <see cref="MovementType.Revaluation"/>: money folded into the stock
/// without inventing a single unit.</para>
///
/// <para><b>And what happens when the stock has gone.</b> If the container sold out before the clearing
/// invoice landed, there is nothing left for the cost to attach to. It is not silently dropped (that
/// would overstate margin) and not smeared over whatever else is on the shelf (that would charge one
/// container's freight to another's goods). It is recorded as
/// <see cref="ImportShipment.UnabsorbedCost"/> — visible, attributable, and P7's problem to expense.</para>
/// </summary>
[RequiresPermission(FeatureCatalog.ImportShipments, PermissionAction.Approve)]
public record ApportionLandedCostCommand(
    Guid ShipmentId,
    ApportionmentBasis Basis = ApportionmentBasis.ByValue) : IRequest<ApportionmentResultDto>;

public class ApportionLandedCostCommandHandler
    : IRequestHandler<ApportionLandedCostCommand, ApportionmentResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public ApportionLandedCostCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ApportionmentResultDto> Handle(
        ApportionLandedCostCommand request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;

        var shipment = await _db.ImportShipments
            .Include(s => s.Charges)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId, cancellationToken)
            ?? throw new NotFoundException("Import shipment", request.ShipmentId);

        // Every line of every receipt that came in this container. They are what the charges divide
        // over — and they are why this cannot run before the goods arrive.
        var lines = await _db.GoodsReceiptLines
            .Include(l => l.GoodsReceipt)
            .Include(l => l.Product)
            .Where(l => l.GoodsReceipt.ImportShipmentId == shipment.Id)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new DomainException(
                "No goods have been received against this shipment, so there is nothing for its "
                + "charges to attach to.");
        }

        var totalCharges = shipment.TotalChargesBase;

        // The apportionment itself: pure, and tested against the worked example the business agreed.
        // Note the line value is in BASE currency — a container whose goods are billed in USD and whose
        // duty is billed in AED cannot be divided up in either one alone.
        var apportionable = lines
            .Select(l => new ApportionableLine(
                l.Id,
                l.Quantity,
                l.LineTotal * l.GoodsReceipt.ExchangeRate))
            .ToList();

        var shares = LandedCostApportionment
            .Apportion(apportionable, totalCharges, request.Basis)
            .ToDictionary(s => s.LineId, s => s.Amount);

        var results = new List<ApportionedLineDto>();
        var absorbed = 0m;
        var unabsorbed = 0m;

        foreach (var line in lines)
        {
            var share = shares[line.Id];

            // Recorded on the line whether or not it can be absorbed, so that "what did this container
            // actually cost us per unit?" is answerable from the document even when the stock has gone.
            line.ApportionedCost = share;

            var balance = await _db.StockBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    b => b.ProductId == line.ProductId
                        && b.WarehouseId == line.GoodsReceipt.WarehouseId,
                    cancellationToken);

            var onHand = balance?.Quantity ?? 0m;

            // The charge belongs to the units that came in this container. If some have already been
            // sold, only the survivors can carry their share of it — the rest is money the shop spent
            // on goods it no longer owns.
            var carryable = Math.Min(onHand, line.Quantity);

            var absorbable = line.Quantity == 0
                ? 0m
                : Math.Round(share * carryable / line.Quantity, 4, MidpointRounding.AwayFromZero);

            if (absorbable > 0)
            {
                await _ledger.RevalueAsync(
                    warehouseId: line.GoodsReceipt.WarehouseId,
                    branchId: line.GoodsReceipt.BranchId,
                    productId: line.ProductId,
                    valueAdjustment: absorbable,
                    referenceType: StockReferenceType.GoodsReceipt,
                    referenceId: shipment.Id,
                    referenceNumber: shipment.Number,
                    occurredAt: now,
                    notes: $"Landed cost apportioned from shipment {shipment.Number}",
                    cancellationToken: cancellationToken);

                absorbed += absorbable;
            }

            unabsorbed += share - absorbable;

            results.Add(new ApportionedLineDto(
                line.Id,
                line.ProductId,
                line.Product.Name,
                line.Quantity,
                line.LineTotal * line.GoodsReceipt.ExchangeRate,
                share,
                line.LandedUnitCost,
                carryable,
                absorbable));
        }

        // Refuses to be marked costed unless the goods actually arrived, and refuses to be costed
        // twice — which would double the freight inside the moving average, where it would never wash
        // back out.
        shipment.MarkCosted(_currentUser.UserId, now, unabsorbed);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ApportionmentResultDto(shipment.Id, totalCharges, absorbed, unabsorbed, results);
    }
}
