using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Catalog.Reference;

// ================================================================================================
// Categories
// ================================================================================================

public record CategoryDto(Guid Id, string Name, Guid? ParentId, string? ParentName, bool IsActive, int ProductCount);

[RequiresPermission(FeatureCatalog.Categories, PermissionAction.View)]
public record GetCategoriesQuery : IRequest<IReadOnlyCollection<CategoryDto>>;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, IReadOnlyCollection<CategoryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCategoriesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken) =>
        await _db.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.Name,
                c.ParentId,
                c.Parent != null ? c.Parent.Name : null,
                c.IsActive,
                _db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);
}

[RequiresPermission(FeatureCatalog.Categories, PermissionAction.Create)]
public record CreateCategoryCommand(string Name, Guid? ParentId) : IRequest<Guid>;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateCategoryCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        if (request.ParentId is { } parent
            && !await _db.ProductCategories.AnyAsync(c => c.Id == parent, cancellationToken))
        {
            throw new NotFoundException("Category", parent);
        }

        var category = new ProductCategory
        {
            Name = request.Name.Trim(),
            ParentId = request.ParentId,
            IsActive = true
        };

        _db.ProductCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return category.Id;
    }
}

[RequiresPermission(FeatureCatalog.Categories, PermissionAction.Edit)]
public record UpdateCategoryCommand(Guid Id, string Name, Guid? ParentId, bool IsActive) : IRequest;

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        // The obvious cycle. The deeper ones (A → B → A) are caught in the handler, which can walk
        // the chain; a validator cannot reach the database.
        RuleFor(x => x)
            .Must(x => x.ParentId != x.Id)
            .WithMessage("A category cannot be its own parent.");
    }
}

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateCategoryCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _db.ProductCategories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Category", request.Id);

        if (request.ParentId is { } parentId)
        {
            await GuardAgainstCycleAsync(request.Id, parentId, cancellationToken);
        }

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        category.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Walks up the proposed parent chain looking for the category being edited. Without this, a
    /// cycle (Laptops → Gaming → Laptops) is trivially creatable and every subsequent tree render
    /// hangs in an infinite loop.
    /// </summary>
    private async Task GuardAgainstCycleAsync(Guid categoryId, Guid proposedParentId, CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        Guid? current = proposedParentId;

        while (current is { } id)
        {
            if (id == categoryId)
            {
                throw new ConflictException(
                    "That parent is a descendant of this category — the change would create a cycle.");
            }

            // Belt and braces: if the data already contains a cycle, do not spin on it.
            if (!seen.Add(id))
            {
                break;
            }

            current = await _db.ProductCategories
                .Where(c => c.Id == id)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}

[RequiresPermission(FeatureCatalog.Categories, PermissionAction.Delete)]
public record DeleteCategoryCommand(Guid Id, string Reason) : IRequest;

public class DeleteCategoryCommandValidator : AbstractValidator<DeleteCategoryCommand>
{
    public DeleteCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteCategoryCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _db.ProductCategories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Category", request.Id);

        // Retiring a category out from under live products would leave them uncategorised with no
        // warning — the product list would quietly lose a filter's worth of rows.
        if (await _db.Products.AnyAsync(p => p.CategoryId == request.Id, cancellationToken))
        {
            throw new ConflictException(
                "This category still has products in it. Move them first, or retire the products.");
        }

        if (await _db.ProductCategories.AnyAsync(c => c.ParentId == request.Id, cancellationToken))
        {
            throw new ConflictException("This category still has sub-categories. Remove them first.");
        }

        category.DeletedReason = request.Reason.Trim();

        _db.ProductCategories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ================================================================================================
// Brands
// ================================================================================================

public record BrandDto(Guid Id, string Name, bool IsActive, int ProductCount);

[RequiresPermission(FeatureCatalog.Brands, PermissionAction.View)]
public record GetBrandsQuery : IRequest<IReadOnlyCollection<BrandDto>>;

public class GetBrandsQueryHandler : IRequestHandler<GetBrandsQuery, IReadOnlyCollection<BrandDto>>
{
    private readonly IApplicationDbContext _db;

    public GetBrandsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<BrandDto>> Handle(GetBrandsQuery request, CancellationToken cancellationToken) =>
        await _db.Brands
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.IsActive, _db.Products.Count(p => p.BrandId == b.Id)))
            .ToListAsync(cancellationToken);
}

[RequiresPermission(FeatureCatalog.Brands, PermissionAction.Create)]
public record CreateBrandCommand(string Name) : IRequest<Guid>;

public class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateBrandCommandHandler : IRequestHandler<CreateBrandCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateBrandCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await _db.Brands.AnyAsync(b => b.Name == name, cancellationToken))
        {
            throw new ConflictException($"The brand '{name}' already exists.");
        }

        var brand = new Brand { Name = name, IsActive = true };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(cancellationToken);

        return brand.Id;
    }
}

// ================================================================================================
// Tax rates (requirements §11 — effective-dated)
// ================================================================================================

public record TaxRateDto(
    Guid Id,
    string Name,
    decimal Percent,
    bool IsDefault,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    bool IsInForce);

[RequiresPermission(FeatureCatalog.TaxRates, PermissionAction.View)]
public record GetTaxRatesQuery : IRequest<IReadOnlyCollection<TaxRateDto>>;

public class GetTaxRatesQueryHandler : IRequestHandler<GetTaxRatesQuery, IReadOnlyCollection<TaxRateDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetTaxRatesQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<TaxRateDto>> Handle(GetTaxRatesQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var rates = await _db.TaxRates
            .AsNoTracking()
            .OrderByDescending(t => t.ValidFrom)
            .ToListAsync(cancellationToken);

        return rates
            .Select(t => new TaxRateDto(
                t.Id, t.Name, t.Percent, t.IsDefault, t.ValidFrom, t.ValidTo, t.IsActive, t.IsInForceAt(now)))
            .ToList();
    }
}

[RequiresPermission(FeatureCatalog.TaxRates, PermissionAction.Create)]
public record CreateTaxRateCommand(
    string Name,
    decimal Percent,
    bool IsDefault,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo) : IRequest<Guid>;

public class CreateTaxRateCommandValidator : AbstractValidator<CreateTaxRateCommand>
{
    public CreateTaxRateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Percent).InclusiveBetween(0, 100);
    }
}

public class CreateTaxRateCommandHandler : IRequestHandler<CreateTaxRateCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CreateTaxRateCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateTaxRateCommand request, CancellationToken cancellationToken)
    {
        var rate = new TaxRate
        {
            Name = request.Name.Trim(),
            Percent = request.Percent,
            IsDefault = request.IsDefault,
            ValidFrom = request.ValidFrom ?? _clock.UtcNow,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        rate.Validate();

        // Exactly one default. Two would make "the tax rate for a product with none set" ambiguous,
        // and the answer would depend on row order.
        if (rate.IsDefault)
        {
            var existingDefaults = await _db.TaxRates.Where(t => t.IsDefault).ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        _db.TaxRates.Add(rate);
        await _db.SaveChangesAsync(cancellationToken);

        return rate.Id;
    }
}

// ================================================================================================
// Price tiers (requirements §31)
// ================================================================================================

public record PriceTierDto(Guid Id, string Name, bool IsDefault, bool IsActive, int CustomerCount);

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.View)]
public record GetPriceTiersQuery : IRequest<IReadOnlyCollection<PriceTierDto>>;

public class GetPriceTiersQueryHandler : IRequestHandler<GetPriceTiersQuery, IReadOnlyCollection<PriceTierDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPriceTiersQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<PriceTierDto>> Handle(GetPriceTiersQuery request, CancellationToken cancellationToken) =>
        await _db.PriceTiers
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new PriceTierDto(
                t.Id, t.Name, t.IsDefault, t.IsActive,
                _db.Customers.Count(c => c.PriceTierId == t.Id)))
            .ToListAsync(cancellationToken);
}

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.Create)]
public record CreatePriceTierCommand(string Name, bool IsDefault) : IRequest<Guid>;

public class CreatePriceTierCommandValidator : AbstractValidator<CreatePriceTierCommand>
{
    public CreatePriceTierCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreatePriceTierCommandHandler : IRequestHandler<CreatePriceTierCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreatePriceTierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreatePriceTierCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await _db.PriceTiers.AnyAsync(t => t.Name == name, cancellationToken))
        {
            throw new ConflictException($"The price tier '{name}' already exists.");
        }

        var tier = new PriceTier { Name = name, IsDefault = request.IsDefault, IsActive = true };

        if (tier.IsDefault)
        {
            foreach (var existing in await _db.PriceTiers.Where(t => t.IsDefault).ToListAsync(cancellationToken))
            {
                existing.IsDefault = false;
            }
        }

        _db.PriceTiers.Add(tier);
        await _db.SaveChangesAsync(cancellationToken);

        return tier.Id;
    }
}

// ================================================================================================
// Payment methods (requirements §23)
// ================================================================================================

public record PaymentMethodDto(
    Guid Id,
    string Name,
    PaymentMethodKind Kind,
    bool RequiresReference,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    bool IsInForce);

[RequiresPermission(FeatureCatalog.PaymentMethods, PermissionAction.View)]
public record GetPaymentMethodsQuery : IRequest<IReadOnlyCollection<PaymentMethodDto>>;

public class GetPaymentMethodsQueryHandler
    : IRequestHandler<GetPaymentMethodsQuery, IReadOnlyCollection<PaymentMethodDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetPaymentMethodsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<PaymentMethodDto>> Handle(
        GetPaymentMethodsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var methods = await _db.PaymentMethods.AsNoTracking().OrderBy(m => m.Name).ToListAsync(cancellationToken);

        return methods
            .Select(m => new PaymentMethodDto(
                m.Id, m.Name, m.Kind, m.RequiresReference, m.ValidFrom, m.ValidTo, m.IsActive, m.IsInForceAt(now)))
            .ToList();
    }
}

[RequiresPermission(FeatureCatalog.PaymentMethods, PermissionAction.Create)]
public record CreatePaymentMethodCommand(
    string Name,
    PaymentMethodKind Kind,
    bool RequiresReference,
    DateTimeOffset? ValidFrom) : IRequest<Guid>;

public class CreatePaymentMethodCommandValidator : AbstractValidator<CreatePaymentMethodCommand>
{
    public CreatePaymentMethodCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreatePaymentMethodCommandHandler : IRequestHandler<CreatePaymentMethodCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CreatePaymentMethodCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreatePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await _db.PaymentMethods.AnyAsync(m => m.Name == name, cancellationToken))
        {
            throw new ConflictException($"The payment method '{name}' already exists.");
        }

        var method = new PaymentMethod
        {
            Name = name,
            Kind = request.Kind,
            RequiresReference = request.RequiresReference,
            ValidFrom = request.ValidFrom ?? _clock.UtcNow,
            IsActive = true
        };

        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);

        return method.Id;
    }
}

// ================================================================================================
// Currencies and FX (requirements §26)
// ================================================================================================

public record CurrencyDto(string Code, string Name, string? Symbol, int DecimalPlaces);

[RequiresPermission(FeatureCatalog.Currencies, PermissionAction.View)]
public record GetCurrenciesQuery : IRequest<IReadOnlyCollection<CurrencyDto>>;

public class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, IReadOnlyCollection<CurrencyDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCurrenciesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken) =>
        await _db.Currencies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Code, c.Name, c.Symbol, c.DecimalPlaces))
            .ToListAsync(cancellationToken);
}

public record FxRateDto(Guid Id, string CurrencyCode, decimal RateToBase, DateOnly RateDate);

[RequiresPermission(FeatureCatalog.Currencies, PermissionAction.View)]
public record GetFxRatesQuery(string? CurrencyCode = null) : IRequest<IReadOnlyCollection<FxRateDto>>;

public class GetFxRatesQueryHandler : IRequestHandler<GetFxRatesQuery, IReadOnlyCollection<FxRateDto>>
{
    private readonly IApplicationDbContext _db;

    public GetFxRatesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<FxRateDto>> Handle(GetFxRatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.FxRates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            var code = request.CurrencyCode.Trim().ToUpperInvariant();
            query = query.Where(r => r.CurrencyCode == code);
        }

        return await query
            .OrderByDescending(r => r.RateDate)
            .Take(200)
            .Select(r => new FxRateDto(r.Id, r.CurrencyCode, r.RateToBase, r.RateDate))
            .ToListAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Currencies, PermissionAction.Create)]
public record SetFxRateCommand(string CurrencyCode, decimal RateToBase, DateOnly RateDate) : IRequest<Guid>;

public class SetFxRateCommandValidator : AbstractValidator<SetFxRateCommand>
{
    public SetFxRateCommandValidator()
    {
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.RateToBase).GreaterThan(0);
    }
}

public class SetFxRateCommandHandler : IRequestHandler<SetFxRateCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public SetFxRateCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(SetFxRateCommand request, CancellationToken cancellationToken)
    {
        var code = request.CurrencyCode.Trim().ToUpperInvariant();

        if (!await _db.Currencies.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new NotFoundException("Currency", code);
        }

        // Upsert. A day has one rate; re-posting it corrects the day rather than creating a second
        // truth that later conversions would pick between arbitrarily.
        var existing = await _db.FxRates
            .FirstOrDefaultAsync(r => r.CurrencyCode == code && r.RateDate == request.RateDate, cancellationToken);

        if (existing is not null)
        {
            existing.RateToBase = request.RateToBase;
            existing.Validate();

            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var rate = new FxRate
        {
            CurrencyCode = code,
            RateToBase = request.RateToBase,
            RateDate = request.RateDate
        };

        rate.Validate();

        _db.FxRates.Add(rate);
        await _db.SaveChangesAsync(cancellationToken);

        return rate.Id;
    }
}

// ================================================================================================
// Discounts (requirements §32)
// ================================================================================================

public record DiscountDto(
    Guid Id,
    string Name,
    Guid? ProductId,
    string? ProductName,
    Guid? CustomerId,
    string? CustomerName,
    DiscountMethod Method,
    decimal Value,
    decimal? MaxValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    bool IsInForce);

[RequiresPermission(FeatureCatalog.Discounts, PermissionAction.View)]
public record GetDiscountsQuery : IRequest<IReadOnlyCollection<DiscountDto>>;

public class GetDiscountsQueryHandler : IRequestHandler<GetDiscountsQuery, IReadOnlyCollection<DiscountDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetDiscountsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<DiscountDto>> Handle(GetDiscountsQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var discounts = await _db.Discounts
            .AsNoTracking()
            .Include(d => d.Product)
            .Include(d => d.Customer)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        return discounts
            .Select(d => new DiscountDto(
                d.Id, d.Name,
                d.ProductId, d.Product?.Name,
                d.CustomerId, d.Customer?.Name,
                d.Method, d.Value, d.MaxValue,
                d.ValidFrom, d.ValidTo, d.IsActive, d.IsInForceAt(now)))
            .ToList();
    }
}

[RequiresPermission(FeatureCatalog.Discounts, PermissionAction.Create)]
public record CreateDiscountCommand(
    string Name,
    Guid? ProductId,
    Guid? CustomerId,
    DiscountMethod Method,
    decimal Value,
    decimal? MaxValue,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo) : IRequest<Guid>;

public class CreateDiscountCommandValidator : AbstractValidator<CreateDiscountCommand>
{
    public CreateDiscountCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0);

        RuleFor(x => x.Value)
            .LessThanOrEqualTo(100)
            .When(x => x.Method == DiscountMethod.Percentage)
            .WithMessage("A percentage discount cannot exceed 100%.");
    }
}

public class CreateDiscountCommandHandler : IRequestHandler<CreateDiscountCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CreateDiscountCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateDiscountCommand request, CancellationToken cancellationToken)
    {
        if (request.ProductId is { } productId
            && !await _db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw new NotFoundException("Product", productId);
        }

        if (request.CustomerId is { } customerId
            && !await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            throw new NotFoundException("Customer", customerId);
        }

        var discount = new Discount
        {
            Name = request.Name.Trim(),
            ProductId = request.ProductId,
            CustomerId = request.CustomerId,
            Method = request.Method,
            Value = request.Value,
            MaxValue = request.MaxValue,
            ValidFrom = request.ValidFrom ?? _clock.UtcNow,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        discount.Validate();

        _db.Discounts.Add(discount);
        await _db.SaveChangesAsync(cancellationToken);

        return discount.Id;
    }
}
