using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Catalog.Products;

public record ProductDto(
    Guid Id,
    string ItemCode,
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    string? Model,
    ProductKind Kind,
    ProductCondition Condition,
    TrackingMode TrackingMode,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal? MarginPercent,
    Guid? TaxRateId,
    string? TaxRateName,
    int WarrantyMonths,
    decimal ReorderLevel,
    bool IsActive);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Products, PermissionAction.View)]
public record GetProductsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    ProductKind? Kind = null,
    bool? IsActive = null,
    string? SortBy = null,
    string? SortDir = null) : IRequest<PagedResult<ProductDto>>;

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IApplicationDbContext _db;

    public GetProductsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // The counter searches by fragments — half a model number, part of a name — so this is a
            // Contains, not a prefix match. It also matches the barcode and SKU exactly, because a
            // scanner types the whole code and expects one hit.
            var term = request.Search.Trim().ToLower();

            query = query.Where(p =>
                p.Name.ToLower().Contains(term)
                || p.Sku.ToLower().Contains(term)
                || p.ItemCode.ToLower().Contains(term)
                || (p.Barcode != null && p.Barcode.ToLower().Contains(term))
                || (p.Model != null && p.Model.ToLower().Contains(term)));
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (request.BrandId is { } brandId)
        {
            query = query.Where(p => p.BrandId == brandId);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(p => p.Kind == kind);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(p => p.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);

        query = Sort(query, request.SortBy, request.SortDir);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(
                p.Id, p.ItemCode, p.Sku, p.Barcode, p.Name, p.Description,
                p.CategoryId, p.Category != null ? p.Category.Name : null,
                p.BrandId, p.Brand != null ? p.Brand.Name : null,
                p.Model, p.Kind, p.Condition, p.TrackingMode, p.Unit,
                p.PurchasePrice, p.SellingPrice,
                p.SellingPrice == 0 ? null : (p.SellingPrice - p.PurchasePrice) / p.SellingPrice * 100m,
                p.TaxRateId, p.TaxRate != null ? p.TaxRate.Name : null,
                p.WarrantyMonths, p.ReorderLevel, p.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, total, page, pageSize);
    }

    /// <summary>
    /// Whitelisted sort columns. Never an interpolated column name: that is SQL injection through
    /// the back door of an ORM, and "sortBy=name; drop table products" should not even be expressible.
    /// </summary>
    private static IQueryable<Product> Sort(IQueryable<Product> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return (sortBy?.ToLowerInvariant()) switch
        {
            "sku" => descending ? query.OrderByDescending(p => p.Sku) : query.OrderBy(p => p.Sku),
            "sellingprice" => descending ? query.OrderByDescending(p => p.SellingPrice) : query.OrderBy(p => p.SellingPrice),
            "purchaseprice" => descending ? query.OrderByDescending(p => p.PurchasePrice) : query.OrderBy(p => p.PurchasePrice),
            "createdat" => descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name)
        };
    }
}

// --- Get one ------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Products, PermissionAction.View)]
public record GetProductQuery(Guid Id) : IRequest<ProductDto>;

public class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductDto>
{
    private readonly IApplicationDbContext _db;

    public GetProductQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ProductDto> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.TaxRate)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        return new ProductDto(
            product.Id, product.ItemCode, product.Sku, product.Barcode, product.Name, product.Description,
            product.CategoryId, product.Category?.Name,
            product.BrandId, product.Brand?.Name,
            product.Model, product.Kind, product.Condition, product.TrackingMode, product.Unit,
            product.PurchasePrice, product.SellingPrice, product.DefaultMarginPercent,
            product.TaxRateId, product.TaxRate?.Name,
            product.WarrantyMonths, product.ReorderLevel, product.IsActive);
    }
}

// --- Create -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Products, PermissionAction.Create)]
public record CreateProductCommand(
    string ItemCode,
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid? BrandId,
    string? Model,
    string? Specifications,
    ProductKind Kind,
    ProductCondition Condition,
    TrackingMode TrackingMode,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    Guid? TaxRateId,
    int WarrantyMonths,
    decimal ReorderLevel) : IRequest<Guid>;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Barcode).MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(0, 120);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateProductCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        var itemCode = request.ItemCode.Trim().ToUpperInvariant();

        if (await _db.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }

        if (await _db.Products.AnyAsync(p => p.ItemCode == itemCode, cancellationToken))
        {
            throw new ConflictException($"A product with item code '{itemCode}' already exists.");
        }

        await EnsureReferencesExistAsync(request.CategoryId, request.BrandId, request.TaxRateId, cancellationToken);

        var product = new Product
        {
            ItemCode = itemCode,
            Sku = sku,
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Model = request.Model,
            Specifications = request.Specifications,
            Kind = request.Kind,
            Condition = request.Condition,
            TrackingMode = request.TrackingMode,
            Unit = request.Unit.Trim(),
            PurchasePrice = request.PurchasePrice,
            SellingPrice = request.SellingPrice,
            TaxRateId = request.TaxRateId,
            WarrantyMonths = request.WarrantyMonths,
            ReorderLevel = request.ReorderLevel,
            IsActive = true
        };

        // The domain rule — a service cannot be serial-tracked — lives on the entity, so no code path
        // can create one, not just this handler.
        product.Validate();

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        return product.Id;
    }

    private async Task EnsureReferencesExistAsync(
        Guid? categoryId,
        Guid? brandId,
        Guid? taxRateId,
        CancellationToken cancellationToken)
    {
        // Tenant-filtered, so passing another company's category id gives a 404 rather than quietly
        // linking across companies.
        if (categoryId is { } category && !await _db.ProductCategories.AnyAsync(c => c.Id == category, cancellationToken))
        {
            throw new NotFoundException("Category", category);
        }

        if (brandId is { } brand && !await _db.Brands.AnyAsync(b => b.Id == brand, cancellationToken))
        {
            throw new NotFoundException("Brand", brand);
        }

        if (taxRateId is { } tax && !await _db.TaxRates.AnyAsync(t => t.Id == tax, cancellationToken))
        {
            throw new NotFoundException("Tax rate", tax);
        }
    }
}

// --- Update -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Products, PermissionAction.Edit)]
public record UpdateProductCommand(
    Guid Id,
    string Name,
    string? Description,
    string? Barcode,
    Guid? CategoryId,
    Guid? BrandId,
    string? Model,
    string? Specifications,
    ProductCondition Condition,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    Guid? TaxRateId,
    int WarrantyMonths,
    decimal ReorderLevel,
    bool IsActive) : IRequest;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(0, 120);
    }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public UpdateProductCommandHandler(IApplicationDbContext db, IDateTime clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        // SKU and tracking mode are deliberately NOT editable.
        //
        // Changing a SKU orphans every barcode label already printed and stuck to a box. Changing the
        // tracking mode is worse: switching a serial-tracked product to untracked would strand the
        // serials already sold under it, and a warranty claim two years from now would have nothing
        // to match against. Both are "create a new product" operations, not edits.

        await EnsureReferencesExistAsync(request.CategoryId, request.BrandId, request.TaxRateId, cancellationToken);

        RecordPriceChange(product, PriceKind.Purchase, product.PurchasePrice, request.PurchasePrice);
        RecordPriceChange(product, PriceKind.Selling, product.SellingPrice, request.SellingPrice);

        product.Name = request.Name.Trim();
        product.Description = request.Description;
        product.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.Model = request.Model;
        product.Specifications = request.Specifications;
        product.Condition = request.Condition;
        product.Unit = request.Unit.Trim();
        product.PurchasePrice = request.PurchasePrice;
        product.SellingPrice = request.SellingPrice;
        product.TaxRateId = request.TaxRateId;
        product.WarrantyMonths = request.WarrantyMonths;
        product.ReorderLevel = request.ReorderLevel;
        product.IsActive = request.IsActive;

        product.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Requirements §31 asks for price history. Writing it here — rather than leaving it to the audit
    /// log — means "show me this product's price over the last year" is a plain query the business can
    /// run, not a forensic dig through change records.
    /// </summary>
    private void RecordPriceChange(Product product, PriceKind kind, decimal oldPrice, decimal newPrice)
    {
        if (oldPrice == newPrice)
        {
            return;
        }

        _db.PriceHistory.Add(new PriceHistory
        {
            ProductId = product.Id,
            Kind = kind,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            ChangedAt = _clock.UtcNow,
            ChangedBy = _currentUser.UserId
        });
    }

    private async Task EnsureReferencesExistAsync(
        Guid? categoryId,
        Guid? brandId,
        Guid? taxRateId,
        CancellationToken cancellationToken)
    {
        if (categoryId is { } category && !await _db.ProductCategories.AnyAsync(c => c.Id == category, cancellationToken))
        {
            throw new NotFoundException("Category", category);
        }

        if (brandId is { } brand && !await _db.Brands.AnyAsync(b => b.Id == brand, cancellationToken))
        {
            throw new NotFoundException("Brand", brand);
        }

        if (taxRateId is { } tax && !await _db.TaxRates.AnyAsync(t => t.Id == tax, cancellationToken))
        {
            throw new NotFoundException("Tax rate", tax);
        }
    }
}

// --- Delete -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Products, PermissionAction.Delete)]
public record DeleteProductCommand(Guid Id, string Reason) : IRequest;

public class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when deleting a product.")
            .MaximumLength(500);
    }
}

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteProductCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        // From P3 this must also refuse to retire a product that still has stock on hand — you cannot
        // delete something the shelf disagrees about. The stock ledger does not exist yet, so the
        // check cannot be written honestly; it is listed against P3 rather than faked here.
        product.DeletedReason = request.Reason.Trim();

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --- Price history ------------------------------------------------------------------------------

public record PriceHistoryDto(PriceKind Kind, decimal OldPrice, decimal NewPrice, DateTimeOffset ChangedAt);

[RequiresPermission(FeatureCatalog.Products, PermissionAction.View)]
public record GetPriceHistoryQuery(Guid ProductId) : IRequest<IReadOnlyCollection<PriceHistoryDto>>;

public class GetPriceHistoryQueryHandler
    : IRequestHandler<GetPriceHistoryQuery, IReadOnlyCollection<PriceHistoryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPriceHistoryQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<PriceHistoryDto>> Handle(
        GetPriceHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
        {
            throw new NotFoundException("Product", request.ProductId);
        }

        return await _db.PriceHistory
            .AsNoTracking()
            .Where(h => h.ProductId == request.ProductId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new PriceHistoryDto(h.Kind, h.OldPrice, h.NewPrice, h.ChangedAt))
            .ToListAsync(cancellationToken);
    }
}
