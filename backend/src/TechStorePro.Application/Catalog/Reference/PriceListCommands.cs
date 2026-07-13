using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Catalog.Reference;

/// <summary>
/// Price lists give price tiers their meaning. Without them, a "Wholesale" tier is a label that
/// changes nothing — every customer falls back to the product's own selling price.
/// </summary>
public record PriceListDto(
    Guid Id,
    string Name,
    Guid PriceTierId,
    string PriceTierName,
    string CurrencyCode,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    bool IsInForce,
    int ItemCount);

public record PriceListItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    decimal UnitPrice,
    decimal? MinimumPrice);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.View)]
public record GetPriceListsQuery : IRequest<IReadOnlyCollection<PriceListDto>>;

public class GetPriceListsQueryHandler : IRequestHandler<GetPriceListsQuery, IReadOnlyCollection<PriceListDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetPriceListsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<PriceListDto>> Handle(
        GetPriceListsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var lists = await _db.PriceLists
            .AsNoTracking()
            .Include(l => l.PriceTier)
            .Include(l => l.Items)
            .OrderByDescending(l => l.ValidFrom)
            .ToListAsync(cancellationToken);

        return lists
            .Select(l => new PriceListDto(
                l.Id, l.Name, l.PriceTierId, l.PriceTier.Name, l.CurrencyCode,
                l.ValidFrom, l.ValidTo, l.IsActive, l.IsInForceAt(now), l.Items.Count))
            .ToList();
    }
}

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.View)]
public record GetPriceListItemsQuery(Guid PriceListId) : IRequest<IReadOnlyCollection<PriceListItemDto>>;

public class GetPriceListItemsQueryHandler
    : IRequestHandler<GetPriceListItemsQuery, IReadOnlyCollection<PriceListItemDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPriceListItemsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<PriceListItemDto>> Handle(
        GetPriceListItemsQuery request,
        CancellationToken cancellationToken)
    {
        if (!await _db.PriceLists.AnyAsync(l => l.Id == request.PriceListId, cancellationToken))
        {
            throw new NotFoundException("Price list", request.PriceListId);
        }

        return await _db.PriceListItems
            .AsNoTracking()
            .Where(i => i.PriceListId == request.PriceListId)
            .OrderBy(i => i.Product.Name)
            .Select(i => new PriceListItemDto(
                i.Id, i.ProductId, i.Product.Name, i.Product.Sku, i.UnitPrice, i.MinimumPrice))
            .ToListAsync(cancellationToken);
    }
}

// --- Create -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.Create)]
public record CreatePriceListCommand(
    string Name,
    Guid PriceTierId,
    string CurrencyCode,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo) : IRequest<Guid>;

public class CreatePriceListCommandValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PriceTierId).NotEmpty();
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);

        RuleFor(x => x)
            .Must(x => x.ValidTo is null || x.ValidFrom is null || x.ValidTo > x.ValidFrom)
            .WithMessage("A price list's validity must end after it begins.");
    }
}

public class CreatePriceListCommandHandler : IRequestHandler<CreatePriceListCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CreatePriceListCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreatePriceListCommand request, CancellationToken cancellationToken)
    {
        if (!await _db.PriceTiers.AnyAsync(t => t.Id == request.PriceTierId, cancellationToken))
        {
            throw new NotFoundException("Price tier", request.PriceTierId);
        }

        var currency = request.CurrencyCode.Trim().ToUpperInvariant();

        if (!await _db.Currencies.AnyAsync(c => c.Code == currency, cancellationToken))
        {
            throw new NotFoundException("Currency", currency);
        }

        var list = new PriceList
        {
            Name = request.Name.Trim(),
            PriceTierId = request.PriceTierId,
            CurrencyCode = currency,
            ValidFrom = request.ValidFrom ?? _clock.UtcNow,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        // Two lists in force for one tier at the same instant would make "what does this customer pay
        // today?" have two answers, and the resolver would silently pick one. Refusing the overlap is
        // the only honest option — the alternative is a price that depends on row order.
        var siblings = await _db.PriceLists
            .Where(l => l.PriceTierId == request.PriceTierId && l.IsActive)
            .ToListAsync(cancellationToken);

        var clash = siblings.FirstOrDefault(existing => list.OverlapsWith(existing));

        if (clash is not null)
        {
            throw new ConflictException(
                $"'{clash.Name}' already prices this tier over an overlapping period "
                + $"({clash.ValidFrom:d} – {(clash.ValidTo is null ? "open" : clash.ValidTo.Value.ToString("d"))}). "
                + "Close it first, or choose dates that do not overlap.");
        }

        _db.PriceLists.Add(list);
        await _db.SaveChangesAsync(cancellationToken);

        return list.Id;
    }
}

// --- Set a price on a list ----------------------------------------------------------------------

/// <summary>Upserts the price of one product on one list.</summary>
[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.Edit)]
public record SetPriceListItemCommand(
    Guid PriceListId,
    Guid ProductId,
    decimal UnitPrice,
    decimal? MinimumPrice) : IRequest<Guid>;

public class SetPriceListItemCommandValidator : AbstractValidator<SetPriceListItemCommand>
{
    public SetPriceListItemCommandValidator()
    {
        RuleFor(x => x.PriceListId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);

        // The floor is what a salesperson may not discount below without approval (requirements §32).
        // A floor above the price itself would put every sale into approval on day one.
        RuleFor(x => x)
            .Must(x => x.MinimumPrice is null || x.MinimumPrice <= x.UnitPrice)
            .WithMessage("The minimum price cannot be above the price itself.");
    }
}

public class SetPriceListItemCommandHandler : IRequestHandler<SetPriceListItemCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public SetPriceListItemCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(SetPriceListItemCommand request, CancellationToken cancellationToken)
    {
        if (!await _db.PriceLists.AnyAsync(l => l.Id == request.PriceListId, cancellationToken))
        {
            throw new NotFoundException("Price list", request.PriceListId);
        }

        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
        {
            throw new NotFoundException("Product", request.ProductId);
        }

        var existing = await _db.PriceListItems
            .FirstOrDefaultAsync(
                i => i.PriceListId == request.PriceListId && i.ProductId == request.ProductId,
                cancellationToken);

        if (existing is not null)
        {
            existing.UnitPrice = request.UnitPrice;
            existing.MinimumPrice = request.MinimumPrice;

            await _db.SaveChangesAsync(cancellationToken);

            return existing.Id;
        }

        var item = new PriceListItem
        {
            PriceListId = request.PriceListId,
            ProductId = request.ProductId,
            UnitPrice = request.UnitPrice,
            MinimumPrice = request.MinimumPrice
        };

        _db.PriceListItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return item.Id;
    }
}
