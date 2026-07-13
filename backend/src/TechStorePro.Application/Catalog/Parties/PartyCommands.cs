using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Catalog.Parties;

// ================================================================================================
// Customers (requirements §14)
// ================================================================================================

public record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    CustomerType Type,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    decimal CreditLimit,
    int PaymentTermDays,
    Guid? PriceTierId,
    string? PriceTierName,
    decimal Balance,
    bool IsActive);

[RequiresPermission(FeatureCatalog.Customers, PermissionAction.View)]
public record GetCustomersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    CustomerType? Type = null,
    bool? IsActive = null) : IRequest<PagedResult<CustomerDto>>;

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCustomersQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            // Phone is in the search because at a counter, "the customer who called from this
            // number" is how a repair job is actually found.
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || c.Code.ToLower().Contains(term)
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.CompanyName != null && c.CompanyName.ToLower().Contains(term)));
        }

        if (request.Type is { } type)
        {
            query = query.Where(c => c.Type == type);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDto(
                c.Id, c.Code, c.Name, c.Type, c.CompanyName, c.Email, c.Phone, c.Address,
                c.TaxNumber, c.CreditLimit, c.PaymentTermDays,
                c.PriceTierId, c.PriceTier != null ? c.PriceTier.Name : null,
                c.Balance, c.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerDto>(items, total, page, pageSize);
    }
}

[RequiresPermission(FeatureCatalog.Customers, PermissionAction.Create)]
public record CreateCustomerCommand(
    string Code,
    string Name,
    CustomerType Type,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    decimal CreditLimit,
    int PaymentTermDays,
    Guid? PriceTierId) : IRequest<Guid>;

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaymentTermDays).InclusiveBetween(0, 365);

        // A walk-in has no account to run a balance against — they pay and leave. Extending credit to
        // an anonymous counter customer is not a business decision anyone would defend, so it is
        // refused rather than merely discouraged.
        RuleFor(x => x)
            .Must(x => x.Type != CustomerType.WalkIn || x.CreditLimit == 0)
            .WithMessage("A walk-in customer cannot be given a credit limit — there is no account to bill.");

        RuleFor(x => x.CompanyName)
            .NotEmpty()
            .When(x => x.Type == CustomerType.Corporate)
            .WithMessage("A corporate customer needs a company name.");
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateCustomerCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"A customer with code '{code}' already exists.");
        }

        if (request.PriceTierId is { } tier && !await _db.PriceTiers.AnyAsync(t => t.Id == tier, cancellationToken))
        {
            throw new NotFoundException("Price tier", tier);
        }

        var customer = new Customer
        {
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            CompanyName = request.CompanyName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            TaxNumber = request.TaxNumber,
            CreditLimit = request.CreditLimit,
            PaymentTermDays = request.PaymentTermDays,
            PriceTierId = request.PriceTierId,
            Balance = 0,
            IsActive = true
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);

        return customer.Id;
    }
}

[RequiresPermission(FeatureCatalog.Customers, PermissionAction.Edit)]
public record UpdateCustomerCommand(
    Guid Id,
    string Name,
    CustomerType Type,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    decimal CreditLimit,
    int PaymentTermDays,
    Guid? PriceTierId,
    bool IsActive) : IRequest;

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaymentTermDays).InclusiveBetween(0, 365);
    }
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateCustomerCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Customer", request.Id);

        if (request.PriceTierId is { } tier && !await _db.PriceTiers.AnyAsync(t => t.Id == tier, cancellationToken))
        {
            throw new NotFoundException("Price tier", tier);
        }

        // Balance is not settable. It is the sum of what they owe, maintained by sales and payments
        // in P5 — letting an admin type over it would let anyone erase a debt with no trace in the
        // ledger that produced it.
        customer.Name = request.Name.Trim();
        customer.Type = request.Type;
        customer.CompanyName = request.CompanyName;
        customer.Email = request.Email;
        customer.Phone = request.Phone;
        customer.Address = request.Address;
        customer.TaxNumber = request.TaxNumber;
        customer.CreditLimit = request.CreditLimit;
        customer.PaymentTermDays = request.PaymentTermDays;
        customer.PriceTierId = request.PriceTierId;
        customer.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Customers, PermissionAction.Delete)]
public record DeleteCustomerCommand(Guid Id, string Reason) : IRequest;

public class DeleteCustomerCommandValidator : AbstractValidator<DeleteCustomerCommand>
{
    public DeleteCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteCustomerCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Customer", request.Id);

        // Retiring a customer who still owes money would hide the debt from the receivables report.
        // The balance is authoritative even before P5 fills it in, so the guard is written now
        // rather than left as a comment for later.
        if (customer.Balance != 0)
        {
            throw new ConflictException(
                $"{customer.Name} has an outstanding balance of {customer.Balance:N2}. "
                + "Settle it before retiring the customer.");
        }

        customer.DeletedReason = request.Reason.Trim();

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ================================================================================================
// Suppliers (requirements §15)
// ================================================================================================

public record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    SupplierType Type,
    string? Email,
    string? Phone,
    string? Address,
    string? Country,
    string? TaxNumber,
    string DefaultCurrency,
    int PaymentTermDays,
    int LeadTimeDays,
    decimal Balance,
    bool IsActive);

[RequiresPermission(FeatureCatalog.Suppliers, PermissionAction.View)]
public record GetSuppliersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    SupplierType? Type = null,
    bool? IsActive = null) : IRequest<PagedResult<SupplierDto>>;

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly IApplicationDbContext _db;

    public GetSuppliersQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            query = query.Where(s =>
                s.Name.ToLower().Contains(term)
                || s.Code.ToLower().Contains(term)
                || (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        if (request.Type is { } type)
        {
            query = query.Where(s => s.Type == type);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(s => s.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id, s.Code, s.Name, s.Type, s.Email, s.Phone, s.Address, s.Country,
                s.TaxNumber, s.DefaultCurrency, s.PaymentTermDays, s.LeadTimeDays,
                s.Balance, s.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierDto>(items, total, page, pageSize);
    }
}

[RequiresPermission(FeatureCatalog.Suppliers, PermissionAction.Create)]
public record CreateSupplierCommand(
    string Code,
    string Name,
    SupplierType Type,
    string? Email,
    string? Phone,
    string? Address,
    string? Country,
    string? TaxNumber,
    string DefaultCurrency,
    int PaymentTermDays,
    int LeadTimeDays) : IRequest<Guid>;

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
        RuleFor(x => x.PaymentTermDays).InclusiveBetween(0, 365);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365);

        // An overseas supplier that invoices in the base currency is almost certainly a data-entry
        // mistake, and it would silently skip the whole landed-cost/FX path in P4. Requiring the
        // country makes the mistake visible at the point it is made.
        RuleFor(x => x.Country)
            .NotEmpty()
            .When(x => x.Type == SupplierType.Overseas)
            .WithMessage("An overseas supplier needs a country — it drives customs and landed cost.");
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateSupplierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.Suppliers.AnyAsync(s => s.Code == code, cancellationToken))
        {
            throw new ConflictException($"A supplier with code '{code}' already exists.");
        }

        var currency = request.DefaultCurrency.Trim().ToUpperInvariant();

        if (!await _db.Currencies.AnyAsync(c => c.Code == currency, cancellationToken))
        {
            throw new NotFoundException("Currency", currency);
        }

        var supplier = new Supplier
        {
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            Country = request.Country,
            TaxNumber = request.TaxNumber,
            DefaultCurrency = currency,
            PaymentTermDays = request.PaymentTermDays,
            LeadTimeDays = request.LeadTimeDays,
            Balance = 0,
            IsActive = true
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken);

        return supplier.Id;
    }
}

[RequiresPermission(FeatureCatalog.Suppliers, PermissionAction.Edit)]
public record UpdateSupplierCommand(
    Guid Id,
    string Name,
    SupplierType Type,
    string? Email,
    string? Phone,
    string? Address,
    string? Country,
    string? TaxNumber,
    string DefaultCurrency,
    int PaymentTermDays,
    int LeadTimeDays,
    bool IsActive) : IRequest;

public class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateSupplierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.Id);

        var currency = request.DefaultCurrency.Trim().ToUpperInvariant();

        if (!await _db.Currencies.AnyAsync(c => c.Code == currency, cancellationToken))
        {
            throw new NotFoundException("Currency", currency);
        }

        supplier.Name = request.Name.Trim();
        supplier.Type = request.Type;
        supplier.Email = request.Email;
        supplier.Phone = request.Phone;
        supplier.Address = request.Address;
        supplier.Country = request.Country;
        supplier.TaxNumber = request.TaxNumber;
        supplier.DefaultCurrency = currency;
        supplier.PaymentTermDays = request.PaymentTermDays;
        supplier.LeadTimeDays = request.LeadTimeDays;
        supplier.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(FeatureCatalog.Suppliers, PermissionAction.Delete)]
public record DeleteSupplierCommand(Guid Id, string Reason) : IRequest;

public class DeleteSupplierCommandValidator : AbstractValidator<DeleteSupplierCommand>
{
    public DeleteSupplierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeleteSupplierCommandHandler : IRequestHandler<DeleteSupplierCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteSupplierCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.Id);

        if (supplier.Balance != 0)
        {
            throw new ConflictException(
                $"{supplier.Name} has an outstanding balance of {supplier.Balance:N2}. "
                + "Settle it before retiring the supplier.");
        }

        supplier.DeletedReason = request.Reason.Trim();

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
