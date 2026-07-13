using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Orders;

public record OrderLine(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice = null,
    decimal DiscountPercent = 0m,
    decimal DiscountAmount = 0m,
    string? Description = null);

/// <summary>
/// Raise a sales order (requirements §22). It is created as a <b>draft</b> and reserves nothing —
/// see <see cref="ConfirmSalesOrderCommand"/>, which is where the shelf is committed.
/// </summary>
[RequiresPermission(FeatureCatalog.SalesOrders, PermissionAction.Create)]
public record CreateSalesOrderCommand(
    Guid CustomerId,
    Guid BranchId,
    Guid WarehouseId,
    IReadOnlyCollection<OrderLine> Lines,
    DateTimeOffset? OrderedAt = null,
    DateTimeOffset? ExpectedAt = null,
    string? CurrencyCode = null,
    string? Notes = null) : IRequest<Guid>;

public class CreateSalesOrderCommandValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A sales order must order at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).When(l => l.UnitPrice is not null);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateSalesOrderCommandHandler : IRequestHandler<CreateSalesOrderCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISalesLinePricer _pricer;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public CreateSalesOrderCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        ISalesLinePricer pricer,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _pricer = pricer;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var orderedAt = request.OrderedAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.EnsureAsync(_db, _tenant, request.CurrencyCode, cancellationToken);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var order = new SalesOrder
        {
            Number = await _numbers.NextAsync(DocumentType.SalesOrder, request.BranchId, cancellationToken),
            CustomerId = customer.Id,
            BranchId = request.BranchId,
            WarehouseId = request.WarehouseId,
            Status = SalesOrderStatus.Draft,
            CurrencyCode = currency,
            OrderedAt = orderedAt,
            ExpectedAt = request.ExpectedAt,
            Notes = request.Notes
        };

        _db.SalesOrders.Add(order);

        foreach (var line in request.Lines)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, cancellationToken)
                ?? throw new NotFoundException("Product", line.ProductId);

            var priced = await _pricer.PriceAsync(
                line.ProductId,
                customer.Id,
                line.Quantity,
                line.UnitPrice,
                line.DiscountPercent,
                line.DiscountAmount,
                orderedAt,
                cancellationToken);

            if (priced.RequiresApproval)
            {
                // The discount workflow lands in slice 3. Until it does, refusing is the honest answer:
                // silently accepting a line below its floor would be the giveaway the floor exists to
                // stop, and nobody would ever know it happened.
                throw new DomainException(
                    $"{product.Name} is priced below its floor of {priced.MinimumPrice:0.##} and needs "
                    + "a manager's approval (§32).");
            }

            _db.SalesOrderLines.Add(new SalesOrderLine
            {
                SalesOrderId = order.Id,
                ProductId = line.ProductId,
                Description = line.Description ?? product.Name,
                Quantity = line.Quantity,
                DeliveredQuantity = 0m,
                UnitPrice = priced.UnitPrice,
                DiscountPercent = priced.DiscountPercent,
                DiscountAmount = priced.DiscountAmount,
                TaxPercent = priced.TaxPercent,
                PriceSource = priced.PriceSource
            });
        }

        order.Validate();

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return order.Id;
    }
}

/// <summary>
/// The customer has committed. <b>This is where the stock is promised</b> — one reservation per line,
/// through the ledger, under its lock.
///
/// Two salespeople cannot both promise the last laptop: the second reservation fails its availability
/// check against a balance the first already holds. That check is the whole of "prevent overselling",
/// and it lives in the ledger rather than here precisely so that no caller can forget it.
/// </summary>
[RequiresPermission(FeatureCatalog.SalesOrders, PermissionAction.Edit)]
public record ConfirmSalesOrderCommand(Guid SalesOrderId) : IRequest<Unit>;

public class ConfirmSalesOrderCommandHandler : IRequestHandler<ConfirmSalesOrderCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;

    public ConfirmSalesOrderCommandHandler(IApplicationDbContext db, IStockLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<Unit> Handle(ConfirmSalesOrderCommand request, CancellationToken cancellationToken)
    {
        // Reservations are ledger writes, and the ledger throws without an ambient transaction: if the
        // fourth line of an order cannot be reserved, the three already reserved must be given back, not
        // left holding stock for an order that was refused.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var order = await _db.SalesOrders
            .Include(o => o.Lines)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new NotFoundException("Sales order", request.SalesOrderId);

        order.Confirm();

        // The credit limit is checked here and not at invoicing, because this is the moment the shop
        // commits goods to someone who has not paid. Discovering at delivery that the customer is over
        // their limit is discovering it too late — the laptop is already in their car.
        if (order.Customer.WouldExceedCreditLimit(order.Total))
        {
            throw new DomainException(
                $"This order would take {order.Customer.Name} to "
                + $"{order.Customer.Balance + order.Total:0.##} against a credit limit of "
                + $"{order.Customer.CreditLimit:0.##}. Take a payment, or raise the limit.");
        }

        foreach (var line in order.Lines)
        {
            var reservation = await _ledger.ReserveAsync(
                warehouseId: order.WarehouseId,
                productId: line.ProductId,
                quantity: line.Quantity,
                referenceType: StockReferenceType.Delivery,
                referenceId: order.Id,
                referenceNumber: order.Number,
                expiresAt: null,   // an order does not expire; a quotation does, and it reserves nothing
                cancellationToken: cancellationToken);

            // The delivery hands this back to the ledger so that picking the goods consumes the promise
            // rather than competing with it.
            line.StockReservationId = reservation.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// Cancel an order and give the shelf back. Refused once anything has been delivered — see
/// <see cref="SalesOrder.Cancel"/>.
/// </summary>
[RequiresPermission(FeatureCatalog.SalesOrders, PermissionAction.Delete)]
public record CancelSalesOrderCommand(Guid SalesOrderId, string Reason) : IRequest<Unit>;

public class CancelSalesOrderCommandValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderCommandValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("Cancelling an order needs a reason — the stock it was holding is about to be "
                         + "released, and somebody will ask why.");
    }
}

public class CancelSalesOrderCommandHandler : IRequestHandler<CancelSalesOrderCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;

    public CancelSalesOrderCommandHandler(IApplicationDbContext db, IStockLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<Unit> Handle(CancelSalesOrderCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var order = await _db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new NotFoundException("Sales order", request.SalesOrderId);

        order.Cancel();

        foreach (var line in order.Lines.Where(l => l.StockReservationId is not null))
        {
            await _ledger.ReleaseAsync(line.StockReservationId!.Value, cancellationToken: cancellationToken);
            line.StockReservationId = null;
        }

        order.Notes = string.IsNullOrWhiteSpace(order.Notes)
            ? $"Cancelled: {request.Reason}"
            : $"{order.Notes}\nCancelled: {request.Reason}";

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}
