using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Inventory;

/// <summary>
/// The one implementation of <see cref="IStockLedger"/>. Read that interface first — it says what the
/// guarantees are and why nothing else may write these tables. This class is how they are kept.
///
/// The shape of every method is the same, and it is the shape that matters:
///
/// <code>
/// 1. refuse to run outside the caller's transaction
/// 2. materialise the balance row and lock it   (INSERT … ON CONFLICT DO NOTHING, then SELECT … FOR UPDATE)
/// 3. validate against the locked state         (availability, serial status, warehouse access)
/// 4. mutate the balance, append the movement   (same transaction, always)
/// </code>
///
/// Step 2 is doubled on purpose. <c>SELECT … FOR UPDATE</c> locks rows that exist; it locks nothing at
/// all when the product has never been in this warehouse, and two concurrent first-receipts would then
/// both insert. The upsert makes the row exist so that the lock has something to take hold of.
/// </summary>
public class StockLedger : IStockLedger
{
    private readonly ApplicationDbContextAccessor _accessor;
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;
    private readonly ICostingStrategy _costing;

    public StockLedger(
        ApplicationDbContextAccessor accessor,
        IApplicationDbContext db,
        ITenantContext tenant,
        IDateTime clock,
        ICostingStrategy costing)
    {
        _accessor = accessor;
        _db = db;
        _tenant = tenant;
        _clock = clock;
        _costing = costing;
    }

    public async Task<StockPostingResult> PostAsync(
        StockPosting posting,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireTenantAndTransaction();

        var occurredAt = posting.OccurredAt ?? _clock.UtcNow;

        var product = await LoadStockableProductAsync(posting.ProductId, cancellationToken);
        var warehouse = await LoadAccessibleWarehouseAsync(
            posting.WarehouseId,
            posting.BranchId,
            forIssue: posting.Type.IsOutbound(),
            cancellationToken);

        var serialNumbers = NormaliseSerialNumbers(product, posting);

        // Locked from here until the caller's transaction commits. Everything below reads a balance
        // nobody else can be changing underneath it.
        var balance = await LockBalanceAsync(companyId, warehouse.Id, product.Id, cancellationToken);

        var reservation = await LoadReservationAsync(posting, balance, cancellationToken);

        var movements = new List<StockMovement>();
        var unitCost = 0m;

        // A serial-tracked product moves one unit at a time, so the loop below runs once per serial and
        // exactly once for everything else. Writing it as one loop rather than two branches is what
        // keeps the costing and the balance arithmetic identical for both.
        var steps = serialNumbers.Count > 0
            ? serialNumbers.Select(s => (Quantity: 1m, SerialNumber: (string?)s)).ToList()
            : [(Quantity: posting.Quantity, SerialNumber: (string?)null)];

        var reservationAllowance = reservation?.OutstandingQuantity ?? 0m;

        foreach (var (quantity, serialNumber) in steps)
        {
            Serial? serial = null;

            if (serialNumber is not null)
            {
                serial = await MoveSerialAsync(
                    product, warehouse, serialNumber, posting, occurredAt, cancellationToken);
            }

            if (posting.Type.IsInbound())
            {
                unitCost = balance.ApplyInbound(posting.Type, quantity, posting.UnitCost, _costing);
            }
            else
            {
                // The allowance is consumed as the loop advances: reserving two units and delivering
                // three must fail on the third, not quietly borrow the reservation twice.
                var allowance = Math.Min(reservationAllowance, quantity);
                reservationAllowance -= allowance;

                unitCost = balance.ApplyOutbound(posting.Type, quantity, allowance);

                if (allowance > 0)
                {
                    // The promise is being kept, so it stops holding stock off the shelf: the units it
                    // was guarding have now physically left. Fulfilling without releasing would leave
                    // reserved_quantity pointing at stock that is no longer there.
                    reservation!.Fulfil(allowance);
                    balance.ReleaseReservation(allowance);
                }
            }

            var movement = new StockMovement
            {
                CompanyId = companyId,
                WarehouseId = warehouse.Id,
                BranchId = posting.BranchId,
                ProductId = product.Id,
                SerialId = serial?.Id,
                Type = posting.Type,
                Quantity = quantity * posting.Type.Direction(),
                UnitCost = unitCost,
                AverageCostAfter = balance.AverageCost,
                BalanceAfter = balance.Quantity,
                ReferenceType = posting.ReferenceType,
                ReferenceId = posting.ReferenceId,
                ReferenceNumber = posting.ReferenceNumber,
                Notes = posting.Notes,
                OccurredAt = occurredAt
            };

            _db.StockMovements.Add(movement);
            movements.Add(movement);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new StockPostingResult(movements, balance, unitCost);
    }

    public async Task<StockReservation> ReserveAsync(
        Guid warehouseId,
        Guid productId,
        decimal quantity,
        StockReferenceType referenceType,
        Guid? referenceId,
        string? referenceNumber,
        DateTimeOffset? expiresAt,
        Guid? serialId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireTenantAndTransaction();

        var product = await LoadStockableProductAsync(productId, cancellationToken);

        var balance = await LockBalanceAsync(companyId, warehouseId, productId, cancellationToken);

        // Throws InsufficientStockException (422) if the units are already promised to someone else.
        // This check under this lock is the whole of "prevent overselling" (requirements §20).
        balance.Reserve(quantity);

        if (serialId is { } id)
        {
            var serial = await _db.Serials.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("Serial", id);

            serial.TransitionTo(SerialStatus.Reserved, warehouseId);
            AddSerialEvent(serial, SerialEventType.Reserved, referenceType, referenceId, referenceNumber, notes);
        }

        var reservation = new StockReservation
        {
            CompanyId = companyId,
            WarehouseId = warehouseId,
            ProductId = product.Id,
            SerialId = serialId,
            Quantity = quantity,
            Status = ReservationStatus.Active,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            ReservedAt = _clock.UtcNow,
            ExpiresAt = expiresAt,
            Notes = notes
        };

        _db.StockReservations.Add(reservation);
        await _db.SaveChangesAsync(cancellationToken);

        return reservation;
    }

    public async Task ReleaseAsync(
        Guid reservationId,
        bool expired = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireTenantAndTransaction();

        var reservation = await _db.StockReservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("Reservation", reservationId);

        var outstanding = reservation.OutstandingQuantity;

        var balance = await LockBalanceAsync(
            companyId, reservation.WarehouseId, reservation.ProductId, cancellationToken);

        if (outstanding > 0)
        {
            balance.ReleaseReservation(outstanding);
        }

        reservation.Release(_clock.UtcNow, expired);

        if (reservation.SerialId is { } serialId)
        {
            var serial = await _db.Serials.FirstOrDefaultAsync(s => s.Id == serialId, cancellationToken);

            // A reservation whose serial has since been sold is being released *by* that sale. The
            // serial is already where it belongs, so moving it back to InStock would be a lie.
            if (serial is { Status: SerialStatus.Reserved })
            {
                serial.TransitionTo(SerialStatus.InStock, reservation.WarehouseId);
                AddSerialEvent(
                    serial,
                    SerialEventType.ReservationReleased,
                    reservation.ReferenceType,
                    reservation.ReferenceId,
                    reservation.ReferenceNumber,
                    expired ? "Reservation expired" : null);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> AvailableAsync(
        Guid warehouseId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        // No lock: this is a read, and by the time the caller acts on it the number may already be
        // stale. That is fine and unavoidable — the *authoritative* check is the one PostAsync makes
        // under the lock. A caller that trusts this number to decide whether a sale is safe has
        // written a race, and PostAsync will catch it.
        var balance = await _db.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.WarehouseId == warehouseId && b.ProductId == productId, cancellationToken);

        return balance?.AvailableQuantity ?? 0m;
    }

    // --- The lock -----------------------------------------------------------------------------

    /// <summary>
    /// Materialises the balance row if it does not exist, then locks it for the rest of the caller's
    /// transaction. See the class remarks for why both statements are needed.
    /// </summary>
    private async Task<StockBalance> LockBalanceAsync(
        Guid companyId,
        Guid warehouseId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var context = _accessor.Context;

        // The tracked instance, if this transaction already locked this balance — a transfer locks two
        // balances, a multi-line adjustment locks one per product, and re-reading a row we have already
        // modified would silently discard those changes.
        var tracked = context.ChangeTracker.Entries<StockBalance>()
            .Select(e => e.Entity)
            .FirstOrDefault(b => b.WarehouseId == warehouseId && b.ProductId == productId);

        if (tracked is not null)
        {
            return tracked;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO techstorepro.stock_balances
                (id, company_id, warehouse_id, product_id, quantity, reserved_quantity, average_cost, created_at)
            VALUES
                (gen_random_uuid(), {0}, {1}, {2}, 0, 0, 0, now())
            ON CONFLICT (company_id, warehouse_id, product_id) DO NOTHING
            """,
            [companyId, warehouseId, productId],
            cancellationToken);

        var balance = await context.StockBalances
            .FromSqlRaw(
                """
                SELECT * FROM techstorepro.stock_balances
                WHERE company_id = {0} AND warehouse_id = {1} AND product_id = {2}
                FOR UPDATE
                """,
                companyId, warehouseId, productId)
            .FirstOrDefaultAsync(cancellationToken);

        // The upsert above guarantees the row. If it is not here, the tenant filter disagrees with the
        // company id we just wrote — which would mean the ledger is writing across companies.
        return balance ?? throw new DomainException(
            "The stock balance could not be locked. This should be impossible and means the tenant "
            + "context and the ledger disagree about which company is posting.");
    }

    // --- Validation ---------------------------------------------------------------------------

    private Guid RequireTenantAndTransaction()
    {
        var companyId = _tenant.CompanyId
            ?? throw new DomainException("Stock cannot be posted without a company.");

        if (_accessor.Context.Database.CurrentTransaction is null)
        {
            // Without an ambient transaction the FOR UPDATE lock would be released as soon as the
            // SELECT finished, and two concurrent sales of the last unit would both pass their
            // availability check. Failing loudly beats overselling and finding out from the customer.
            throw new DomainException(
                "A stock movement must be posted inside a transaction. "
                + "Call IApplicationDbContext.BeginTransactionAsync first.");
        }

        return companyId;
    }

    private async Task<Product> LoadStockableProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Product", productId);

        if (product.Kind == ProductKind.Service)
        {
            throw new DomainException(
                $"'{product.Name}' is a service and has no stock: a ledger entry for an hour of labour "
                + "would be meaningless.");
        }

        return product;
    }

    private async Task<Warehouse> LoadAccessibleWarehouseAsync(
        Guid warehouseId,
        Guid branchId,
        bool forIssue,
        CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses
            .Include(w => w.AccessibleToBranches)
            .FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", warehouseId);

        if (!warehouse.IsActive)
        {
            throw new DomainException($"Warehouse '{warehouse.Name}' is not active.");
        }

        // "Shared" must never silently mean "any branch may drain it" (requirements §45 D2). The rule
        // lives on the Warehouse entity; this is the one place that asks it.
        if (!warehouse.IsAccessibleTo(branchId, forIssue))
        {
            throw new ForbiddenException(
                $"This branch may not {(forIssue ? "issue from" : "receive into")} warehouse "
                + $"'{warehouse.Name}'.");
        }

        return warehouse;
    }

    /// <summary>
    /// Checks the serial numbers against the product's tracking mode, and returns them trimmed and
    /// upper-cased. A serial-tracked product needs exactly one serial per unit; anything else needs none.
    /// </summary>
    private static List<string> NormaliseSerialNumbers(Product product, StockPosting posting)
    {
        var supplied = posting.SerialNumbers?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .ToList() ?? [];

        if (product.TrackingMode != TrackingMode.Serial)
        {
            if (supplied.Count > 0)
            {
                throw new DomainException(
                    $"'{product.Name}' is not serial-tracked, so serial numbers cannot be recorded "
                    + "against it. Tracking mode is not editable — create a serial-tracked product.");
            }

            return [];
        }

        if (supplied.Count != posting.Quantity)
        {
            throw new DomainException(
                $"'{product.Name}' is serial-tracked: {posting.Quantity} units need {posting.Quantity} "
                + $"serial numbers, but {supplied.Count} were supplied.");
        }

        if (supplied.Distinct().Count() != supplied.Count)
        {
            throw new DomainException("The same serial number was supplied twice in one movement.");
        }

        return supplied;
    }

    /// <summary>
    /// Finds or creates the serial, and moves it to the status this movement implies. The status —
    /// not the quantity — is what stops the same laptop being sold twice.
    /// </summary>
    private async Task<Serial> MoveSerialAsync(
        Product product,
        Warehouse warehouse,
        string serialNumber,
        StockPosting posting,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var serial = await _db.Serials
            .FirstOrDefaultAsync(s => s.SerialNumber == serialNumber, cancellationToken);

        if (serial is null)
        {
            if (posting.Type.IsOutbound())
            {
                throw new NotFoundException("Serial", serialNumber);
            }

            serial = new Serial
            {
                CompanyId = warehouse.CompanyId,
                ProductId = product.Id,
                SerialNumber = serialNumber,
                Status = SerialStatus.InStock,
                WarehouseId = warehouse.Id,
                PurchaseCost = posting.UnitCost ?? 0m,
                WarrantyUntil = product.WarrantyMonths > 0
                    ? occurredAt.AddMonths(product.WarrantyMonths)
                    : null
            };

            _db.Serials.Add(serial);
            AddSerialEvent(serial, SerialEventType.Received, posting.ReferenceType, posting.ReferenceId,
                posting.ReferenceNumber, posting.Notes, occurredAt);

            return serial;
        }

        if (serial.ProductId != product.Id)
        {
            throw new ConflictException(
                $"Serial '{serialNumber}' already belongs to a different product. A serial number "
                + "identifies one physical machine and cannot be reused.");
        }

        if (posting.Type.IsOutbound() && serial.WarehouseId != warehouse.Id)
        {
            throw new DomainException(
                $"Serial '{serialNumber}' is not in warehouse '{warehouse.Name}'.");
        }

        var target = TargetStatus(posting.Type);

        // Throws if the transition is nonsense: selling a scrapped unit, receiving one that is already
        // on the shelf, shipping one that has already left.
        serial.TransitionTo(target, WarehouseAfter(posting.Type, warehouse.Id));

        AddSerialEvent(serial, EventFor(posting.Type), posting.ReferenceType, posting.ReferenceId,
            posting.ReferenceNumber, posting.Notes, occurredAt);

        return serial;
    }

    /// <summary>Where the unit physically is after this movement. Null once it is nobody's stock.</summary>
    private static Guid? WarehouseAfter(MovementType type, Guid warehouseId) => type switch
    {
        MovementType.Sale => null,
        MovementType.PurchaseReturn => null,
        MovementType.TransferOut => null,
        MovementType.AdjustmentOut => null,
        MovementType.CountAdjustmentOut => null,
        _ => warehouseId
    };

    private static SerialStatus TargetStatus(MovementType type) => type switch
    {
        MovementType.OpeningBalance => SerialStatus.InStock,
        MovementType.Receipt => SerialStatus.InStock,
        MovementType.TransferIn => SerialStatus.InStock,
        MovementType.AdjustmentIn => SerialStatus.InStock,
        MovementType.CountAdjustmentIn => SerialStatus.InStock,
        MovementType.RepairReturn => SerialStatus.InStock,

        // Physically back, but not back on the shelf. Somebody has to look at a returned machine and
        // decide it is resaleable — see Serial.TransitionTo.
        MovementType.SaleReturn => SerialStatus.Returned,

        MovementType.Sale => SerialStatus.Sold,
        MovementType.TransferOut => SerialStatus.InTransit,
        MovementType.PurchaseReturn => SerialStatus.ReturnedToSupplier,
        MovementType.RepairConsumption => SerialStatus.InRepair,

        MovementType.AdjustmentOut => SerialStatus.Scrapped,
        MovementType.CountAdjustmentOut => SerialStatus.Scrapped,

        _ => throw new DomainException($"Movement type {type} has no defined serial status.")
    };

    private static SerialEventType EventFor(MovementType type) => type switch
    {
        MovementType.OpeningBalance => SerialEventType.Received,
        MovementType.Receipt => SerialEventType.Received,
        MovementType.Sale => SerialEventType.Sold,
        MovementType.SaleReturn => SerialEventType.Returned,
        MovementType.PurchaseReturn => SerialEventType.Adjusted,
        MovementType.TransferOut => SerialEventType.TransferredOut,
        MovementType.TransferIn => SerialEventType.TransferredIn,
        MovementType.AdjustmentIn => SerialEventType.Adjusted,
        MovementType.AdjustmentOut => SerialEventType.Scrapped,
        MovementType.RepairConsumption => SerialEventType.SentToRepair,
        MovementType.RepairReturn => SerialEventType.ReturnedFromRepair,
        MovementType.CountAdjustmentIn => SerialEventType.Counted,
        MovementType.CountAdjustmentOut => SerialEventType.Counted,
        _ => SerialEventType.Adjusted
    };

    private void AddSerialEvent(
        Serial serial,
        SerialEventType type,
        StockReferenceType? referenceType,
        Guid? referenceId,
        string? referenceNumber,
        string? notes,
        DateTimeOffset? at = null)
    {
        _db.SerialEvents.Add(new SerialEvent
        {
            CompanyId = serial.CompanyId,
            SerialId = serial.Id,
            Type = type,
            Status = serial.Status,
            WarehouseId = serial.WarehouseId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            At = at ?? _clock.UtcNow
        });
    }

    /// <summary>
    /// Loads the reservation a movement is consuming, and refuses one that belongs to a different
    /// product or warehouse — a mis-keyed reservation id would otherwise release someone else's stock.
    /// </summary>
    private async Task<StockReservation?> LoadReservationAsync(
        StockPosting posting,
        StockBalance balance,
        CancellationToken cancellationToken)
    {
        if (posting.ReservationId is not { } reservationId)
        {
            return null;
        }

        var reservation = await _db.StockReservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("Reservation", reservationId);

        if (reservation.ProductId != balance.ProductId || reservation.WarehouseId != balance.WarehouseId)
        {
            throw new DomainException(
                "That reservation is for a different product or warehouse than the movement consuming it.");
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            throw new DomainException($"Reservation is {reservation.Status} and cannot be consumed.");
        }

        return reservation;
    }
}
