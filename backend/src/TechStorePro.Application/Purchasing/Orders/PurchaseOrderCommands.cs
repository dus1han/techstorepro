using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Purchasing;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.Orders;

public record PurchaseOrderLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    string? Notes = null);

/// <summary>
/// Commit to buying something before it exists (requirements §25).
///
/// The order is <b>optional</b> — <see cref="GoodsReceipt.PurchaseOrderId"/> is nullable and the direct
/// purchase is a first-class path. This command is for the case where the order earns its keep: agreeing
/// a price and a quantity up front, so that what turns up can be checked against what was agreed.
///
/// It is raised as a <b>draft</b>. Nothing is committed and nothing is reserved until it is approved,
/// which is a separate permission precisely because approving one is what commits the company's money.
/// </summary>
[RequiresPermission(FeatureCatalog.PurchaseOrders, PermissionAction.Create)]
public record CreatePurchaseOrderCommand(
    Guid SupplierId,
    Guid BranchId,
    Guid WarehouseId,
    IReadOnlyCollection<PurchaseOrderLineInput> Lines,
    string CurrencyCode = "AED",
    decimal ExchangeRate = 1m,
    DateTimeOffset? OrderedAt = null,
    DateTimeOffset? ExpectedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A purchase order with no lines orders nothing.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
        });
    }
}

public class CreatePurchaseOrderCommandHandler : IRequestHandler<CreatePurchaseOrderCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public CreatePurchaseOrderCommandHandler(
        IApplicationDbContext db,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        // The document number is drawn inside the transaction, so a rejected order returns its number
        // instead of burning it and leaving a gap the auditor will ask about.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId, cancellationToken))
        {
            throw new NotFoundException("Supplier", request.SupplierId);
        }

        var order = new PurchaseOrder
        {
            Number = await _numbers.NextAsync(DocumentType.PurchaseOrder, request.BranchId, cancellationToken),
            SupplierId = request.SupplierId,
            BranchId = request.BranchId,
            WarehouseId = request.WarehouseId,
            Status = PurchaseOrderStatus.Draft,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            OrderedAt = request.OrderedAt ?? _clock.UtcNow,
            ExpectedAt = request.ExpectedAt,
            Notes = request.Notes
        };

        _db.PurchaseOrders.Add(order);

        foreach (var line in request.Lines)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == line.ProductId, cancellationToken))
            {
                throw new NotFoundException("Product", line.ProductId);
            }

            // Through the DbSet, and ONLY the DbSet. EF's relationship fixup puts the line into
            // order.Lines by itself; adding it there by hand as well would put the same instance in the
            // collection twice and every total computed from it — including Total — would be double.
            // This is the P3 bug that fell out of P4 (adjustments and counts), and it is not going to be
            // reintroduced here.
            _db.PurchaseOrderLines.Add(new PurchaseOrderLine
            {
                PurchaseOrderId = order.Id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                ReceivedQuantity = 0m,
                Notes = line.Notes
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return order.Id;
    }
}

// --- Approve ------------------------------------------------------------------------------------

/// <summary>
/// Approving is what commits the company to spending the money — so it is its own permission, and the
/// person who chose the supplier need not be the person who signs for it (Feature.cs, "Purchasing. Note
/// where Approve sits, because that is where the money is").
///
/// It is also the gate on receiving: <c>ReceiveGoodsCommand</c> refuses to post stock against a draft
/// order.
/// </summary>
[RequiresPermission(FeatureCatalog.PurchaseOrders, PermissionAction.Approve)]
public record ApprovePurchaseOrderCommand(Guid PurchaseOrderId) : IRequest;

public class ApprovePurchaseOrderCommandHandler : IRequestHandler<ApprovePurchaseOrderCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public ApprovePurchaseOrderCommandHandler(
        IApplicationDbContext db,
        ICurrentUser user,
        IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task Handle(ApprovePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new NotFoundException("Purchase order", request.PurchaseOrderId);

        // Who approved it is stamped by the entity, not by this handler — the rule about which states
        // may be approved lives on PurchaseOrder, where no code path can dodge it.
        order.Approve(_user.UserId, _clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --- Cancel -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.PurchaseOrders, PermissionAction.Edit)]
public record CancelPurchaseOrderCommand(Guid PurchaseOrderId, string Reason) : IRequest;

public class CancelPurchaseOrderCommandValidator : AbstractValidator<CancelPurchaseOrderCommand>
{
    public CancelPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Cancelling an order needs a reason.");
    }
}

public class CancelPurchaseOrderCommandHandler : IRequestHandler<CancelPurchaseOrderCommand>
{
    private readonly IApplicationDbContext _db;

    public CancelPurchaseOrderCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new NotFoundException("Purchase order", request.PurchaseOrderId);

        // The entity refuses this once goods have arrived against the order: the stock is on the shelf
        // and the supplier will invoice for it, so an order claiming it never happened would leave a
        // receipt pointing at nothing.
        order.Cancel();

        order.Notes = string.IsNullOrWhiteSpace(order.Notes)
            ? $"Cancelled: {request.Reason}"
            : $"{order.Notes}\nCancelled: {request.Reason}";

        await _db.SaveChangesAsync(cancellationToken);
    }
}
