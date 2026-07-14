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

[RequiresPermission(FeatureCatalog.Brands, PermissionAction.Edit)]
public record UpdateBrandCommand(Guid Id, string Name, bool IsActive) : IRequest;

public class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateBrandCommandHandler : IRequestHandler<UpdateBrandCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateBrandCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Brand", request.Id);

        var name = request.Name.Trim();

        if (await _db.Brands.AnyAsync(b => b.Name == name && b.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"The brand '{name}' already exists.");
        }

        brand.Name = name;
        brand.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Brands, PermissionAction.Delete)]
public record DeleteBrandCommand(Guid Id, string Reason) : IRequest;

public class DeleteBrandCommandValidator : AbstractValidator<DeleteBrandCommand>
{
    public DeleteBrandCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteBrandCommandHandler : IRequestHandler<DeleteBrandCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteBrandCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Brand", request.Id);

        // Retiring a brand out from under live products would leave them branded by a row nothing can
        // show — the product list would quietly lose a filter's worth of rows, exactly as with categories.
        if (await _db.Products.AnyAsync(p => p.BrandId == request.Id, cancellationToken))
        {
            throw new ConflictException(
                "This brand still has products under it. Rebrand them first, or retire the products.");
        }

        brand.DeletedReason = request.Reason.Trim();

        _db.Brands.Remove(brand);
        await _db.SaveChangesAsync(cancellationToken);
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

/// <summary>
/// Corrects a tax rate's <em>label</em> — not its percentage.
///
/// <b>The percent is deliberately not editable here.</b> A tax rate is effective-dated: an invoice
/// raised in April was 5% and a July change to 9% must not restate it (General Rule 3, and the P2 test
/// <c>Changing_the_rate_does_not_change_what_applied_in_the_past</c>). Editing the percent in place
/// would rewrite what was in force in April, silently changing the tax on documents that have already
/// been issued to customers and filed with the authority. Use <see cref="SupersedeTaxRateCommand"/>,
/// which closes the old rate and opens a new one — so history stays true and the future gets the new
/// number.
/// </summary>
[RequiresPermission(FeatureCatalog.TaxRates, PermissionAction.Edit)]
public record UpdateTaxRateCommand(Guid Id, string Name, bool IsDefault, bool IsActive) : IRequest;

public class UpdateTaxRateCommandValidator : AbstractValidator<UpdateTaxRateCommand>
{
    public UpdateTaxRateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdateTaxRateCommandHandler : IRequestHandler<UpdateTaxRateCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateTaxRateCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateTaxRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await _db.TaxRates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Tax rate", request.Id);

        rate.Name = request.Name.Trim();
        rate.IsActive = request.IsActive;

        // Exactly one default, as on create: two would make "the rate for a product with none set"
        // depend on row order.
        if (request.IsDefault && !rate.IsDefault)
        {
            var existingDefaults = await _db.TaxRates
                .Where(t => t.IsDefault && t.Id != request.Id)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        rate.IsDefault = request.IsDefault;

        rate.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The tax went up. Closes the current rate at the moment the new one takes effect, and opens a
/// successor — two rows, not one edited row, so April still says 5% and August says 9%.
/// </summary>
[RequiresPermission(FeatureCatalog.TaxRates, PermissionAction.Edit)]
public record SupersedeTaxRateCommand(Guid Id, decimal Percent, DateTimeOffset? EffectiveFrom) : IRequest<Guid>;

public class SupersedeTaxRateCommandValidator : AbstractValidator<SupersedeTaxRateCommand>
{
    public SupersedeTaxRateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Percent).InclusiveBetween(0, 100);
    }
}

public class SupersedeTaxRateCommandHandler : IRequestHandler<SupersedeTaxRateCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public SupersedeTaxRateCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(SupersedeTaxRateCommand request, CancellationToken cancellationToken)
    {
        var current = await _db.TaxRates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Tax rate", request.Id);

        var effectiveFrom = request.EffectiveFrom ?? _clock.UtcNow;

        if (current.ValidTo is not null)
        {
            throw new ConflictException(
                $"'{current.Name}' has already been superseded and is closed. Supersede the rate that "
                + "is currently in force, or create a new one.");
        }

        if (effectiveFrom <= current.ValidFrom)
        {
            // The successor would begin before its predecessor did, and "what was the rate in April?"
            // would have two answers.
            throw new ConflictException(
                "The new rate must take effect after the one it replaces began.");
        }

        var successor = new TaxRate
        {
            Name = current.Name,
            Percent = request.Percent,
            IsDefault = current.IsDefault,
            ValidFrom = effectiveFrom,
            ValidTo = null,
            IsActive = true
        };

        successor.Validate();

        // The old rate stops exactly when the new one starts: no gap in which a document could find no
        // rate at all, and no overlap in which it could find two.
        current.ValidTo = effectiveFrom;
        current.IsDefault = false;

        _db.TaxRates.Add(successor);
        await _db.SaveChangesAsync(cancellationToken);

        return successor.Id;
    }
}

[RequiresPermission(FeatureCatalog.TaxRates, PermissionAction.Delete)]
public record DeleteTaxRateCommand(Guid Id, string Reason) : IRequest;

public class DeleteTaxRateCommandValidator : AbstractValidator<DeleteTaxRateCommand>
{
    public DeleteTaxRateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteTaxRateCommandHandler : IRequestHandler<DeleteTaxRateCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteTaxRateCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteTaxRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await _db.TaxRates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Tax rate", request.Id);

        if (await _db.Products.AnyAsync(p => p.TaxRateId == request.Id, cancellationToken))
        {
            throw new ConflictException(
                "This tax rate is still set on products. Move them to another rate first.");
        }

        rate.DeletedReason = request.Reason.Trim();

        _db.TaxRates.Remove(rate);
        await _db.SaveChangesAsync(cancellationToken);
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

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.Edit)]
public record UpdatePriceTierCommand(Guid Id, string Name, bool IsDefault, bool IsActive) : IRequest;

public class UpdatePriceTierCommandValidator : AbstractValidator<UpdatePriceTierCommand>
{
    public UpdatePriceTierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdatePriceTierCommandHandler : IRequestHandler<UpdatePriceTierCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdatePriceTierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdatePriceTierCommand request, CancellationToken cancellationToken)
    {
        var tier = await _db.PriceTiers.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Price tier", request.Id);

        var name = request.Name.Trim();

        if (await _db.PriceTiers.AnyAsync(t => t.Name == name && t.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"The price tier '{name}' already exists.");
        }

        // Exactly one default tier: it is what a customer with no tier of their own falls back to, and
        // two of them would make that fallback depend on row order — the same customer could be quoted
        // two different prices on two different days for no reason anyone could explain.
        if (request.IsDefault && !tier.IsDefault)
        {
            var existingDefaults = await _db.PriceTiers
                .Where(t => t.IsDefault && t.Id != request.Id)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        if (!request.IsDefault && tier.IsDefault)
        {
            throw new ConflictException(
                "This is the default price tier. Make another tier the default instead — a company with "
                + "no default has no price for a customer who has not been given a tier.");
        }

        tier.Name = name;
        tier.IsDefault = request.IsDefault;
        tier.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Pricing, PermissionAction.Delete)]
public record DeletePriceTierCommand(Guid Id, string Reason) : IRequest;

public class DeletePriceTierCommandValidator : AbstractValidator<DeletePriceTierCommand>
{
    public DeletePriceTierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeletePriceTierCommandHandler : IRequestHandler<DeletePriceTierCommand>
{
    private readonly IApplicationDbContext _db;

    public DeletePriceTierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeletePriceTierCommand request, CancellationToken cancellationToken)
    {
        var tier = await _db.PriceTiers.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Price tier", request.Id);

        if (tier.IsDefault)
        {
            throw new ConflictException(
                "The default price tier cannot be retired: every customer without a tier of their own "
                + "resolves through it. Make another tier the default first.");
        }

        // Retiring a tier that customers are still on would leave them with no tier, and the price
        // resolver would silently fall back to the default — quietly re-pricing a wholesale customer
        // at retail on their next order.
        if (await _db.Customers.AnyAsync(c => c.PriceTierId == request.Id, cancellationToken))
        {
            throw new ConflictException(
                "Customers are still on this price tier. Move them to another tier first, or they "
                + "would silently be re-priced at the default.");
        }

        if (await _db.PriceLists.AnyAsync(l => l.PriceTierId == request.Id, cancellationToken))
        {
            throw new ConflictException("This tier still has price lists. Retire them first.");
        }

        tier.DeletedReason = request.Reason.Trim();

        _db.PriceTiers.Remove(tier);
        await _db.SaveChangesAsync(cancellationToken);
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
    Guid? FinancialAccountId,
    string? FinancialAccountName,
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

        // The account is joined in by hand: PaymentMethod deliberately has no navigation property to it
        // (master data does not need to know what a finance module is), so there is nothing for EF to
        // Include.
        var methods = await _db.PaymentMethods
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                Method = m,
                AccountName = _db.FinancialAccounts
                    .Where(a => a.Id == m.FinancialAccountId)
                    .Select(a => a.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return methods
            .Select(x => new PaymentMethodDto(
                x.Method.Id,
                x.Method.Name,
                x.Method.Kind,
                x.Method.RequiresReference,
                x.Method.FinancialAccountId,
                x.AccountName,
                x.Method.ValidFrom,
                x.Method.ValidTo,
                x.Method.IsActive,
                x.Method.IsInForceAt(now)))
            .ToList();
    }
}

/// <param name="FinancialAccountId">
/// Where money tendered this way lands (P7). Required in practice for every method that moves money — a
/// payment through a method with no account behind it is refused, because the alternative is money that
/// arrived nowhere and is missed by nobody. It must be null for store credit, which moves no money.
/// </param>
[RequiresPermission(FeatureCatalog.PaymentMethods, PermissionAction.Create)]
public record CreatePaymentMethodCommand(
    string Name,
    PaymentMethodKind Kind,
    bool RequiresReference,
    DateTimeOffset? ValidFrom,
    Guid? FinancialAccountId = null) : IRequest<Guid>;

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

        await EnsureAccountExistsAsync(_db, request.FinancialAccountId, cancellationToken);

        var method = new PaymentMethod
        {
            Name = name,
            Kind = request.Kind,
            RequiresReference = request.RequiresReference,
            FinancialAccountId = request.FinancialAccountId,
            ValidFrom = request.ValidFrom ?? _clock.UtcNow,
            IsActive = true
        };

        method.Validate();

        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);

        return method.Id;
    }

    internal static async Task EnsureAccountExistsAsync(
        IApplicationDbContext db,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (accountId is { } id && !await db.FinancialAccounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            throw new NotFoundException("Account", id);
        }
    }
}

[RequiresPermission(FeatureCatalog.PaymentMethods, PermissionAction.Edit)]
public record UpdatePaymentMethodCommand(
    Guid Id,
    string Name,
    PaymentMethodKind Kind,
    bool RequiresReference,
    DateTimeOffset? ValidTo,
    bool IsActive,
    Guid? FinancialAccountId = null) : IRequest;

public class UpdatePaymentMethodCommandValidator : AbstractValidator<UpdatePaymentMethodCommand>
{
    public UpdatePaymentMethodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdatePaymentMethodCommandHandler : IRequestHandler<UpdatePaymentMethodCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdatePaymentMethodCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdatePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Payment method", request.Id);

        var name = request.Name.Trim();

        if (await _db.PaymentMethods.AnyAsync(m => m.Name == name && m.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"The payment method '{name}' already exists.");
        }

        if (request.ValidTo is { } end && end <= method.ValidFrom)
        {
            throw new ConflictException("A payment method's validity must end after it begins.");
        }

        await CreatePaymentMethodCommandHandler.EnsureAccountExistsAsync(
            _db, request.FinancialAccountId, cancellationToken);

        method.Name = name;
        method.Kind = request.Kind;
        method.RequiresReference = request.RequiresReference;
        method.FinancialAccountId = request.FinancialAccountId;
        method.ValidTo = request.ValidTo;
        method.IsActive = request.IsActive;

        method.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.PaymentMethods, PermissionAction.Delete)]
public record DeletePaymentMethodCommand(Guid Id, string Reason) : IRequest;

public class DeletePaymentMethodCommandValidator : AbstractValidator<DeletePaymentMethodCommand>
{
    public DeletePaymentMethodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeletePaymentMethodCommandHandler : IRequestHandler<DeletePaymentMethodCommand>
{
    private readonly IApplicationDbContext _db;

    public DeletePaymentMethodCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeletePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Payment method", request.Id);

        // Payments (P5) will reference this. When they exist, retiring a method that has taken money
        // must be refused here — a payment whose method has vanished cannot be reconciled against the
        // bank statement. There is nothing to check against yet, and faking the check would be worse
        // than leaving the note.
        method.DeletedReason = request.Reason.Trim();

        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
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

[RequiresPermission(FeatureCatalog.Discounts, PermissionAction.Edit)]
public record UpdateDiscountCommand(
    Guid Id,
    string Name,
    DiscountMethod Method,
    decimal Value,
    decimal? MaxValue,
    DateTimeOffset? ValidTo,
    bool IsActive) : IRequest;

public class UpdateDiscountCommandValidator : AbstractValidator<UpdateDiscountCommand>
{
    public UpdateDiscountCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0);
    }
}

public class UpdateDiscountCommandHandler : IRequestHandler<UpdateDiscountCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateDiscountCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateDiscountCommand request, CancellationToken cancellationToken)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Discount", request.Id);

        // What the discount applies to — the product, the customer — is deliberately not editable. A
        // discount is a rule about a specific pairing; repointing it at a different customer would
        // rewrite what was agreed with the first one. Retire it and raise the rule you actually mean.
        discount.Name = request.Name.Trim();
        discount.Method = request.Method;
        discount.Value = request.Value;
        discount.MaxValue = request.MaxValue;
        discount.ValidTo = request.ValidTo;
        discount.IsActive = request.IsActive;

        // The entity's own rules: the approval ceiling cannot sit below the discount (every use would
        // stall in approval), and a fixed discount cannot exceed the line it is taken off.
        discount.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Discounts, PermissionAction.Delete)]
public record DeleteDiscountCommand(Guid Id, string Reason) : IRequest;

public class DeleteDiscountCommandValidator : AbstractValidator<DeleteDiscountCommand>
{
    public DeleteDiscountCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteDiscountCommandHandler : IRequestHandler<DeleteDiscountCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteDiscountCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteDiscountCommand request, CancellationToken cancellationToken)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Discount", request.Id);

        // Soft-deleted, so the discount that was applied to a past order can still be explained. The
        // rule stops applying from now on; it does not stop having applied.
        discount.DeletedReason = request.Reason.Trim();

        _db.Discounts.Remove(discount);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
