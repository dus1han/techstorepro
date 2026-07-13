using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Platform;

public record CompanySummaryDto(
    Guid Id,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    int UserCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// Every company on the platform. <b>The only place in the system that reads across tenants</b>, and it
/// is reachable exclusively by a <see cref="PlatformAdmin"/> — see the <c>Platform</c> authorization
/// policy, which demands the platform claim rather than merely tolerating the absence of a company one.
/// </summary>
[RequiresPlatformAdmin]
public record GetCompaniesQuery(int Page = 1, int PageSize = 25, string? Search = null)
    : IRequest<PagedResult<CompanySummaryDto>>;

public class GetCompaniesQueryHandler : IRequestHandler<GetCompaniesQuery, PagedResult<CompanySummaryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCompaniesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<CompanySummaryDto>> Handle(
        GetCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Companies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Code.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CompanySummaryDto(
                c.Id,
                c.Code,
                c.Name,
                c.BaseCurrency,
                c.IsActive,
                c.Users.Count(u => !u.IsDeleted),
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CompanySummaryDto>(items, total, page, pageSize);
    }
}

// --- Onboarding ---------------------------------------------------------------------------------

public record CompanyCreatedDto(Guid CompanyId, string CompanyCode, Guid OwnerUserId, string OwnerLogin);

/// <summary>
/// Creates a company and its first user, in one transaction.
///
/// <b>This replaces self-service registration.</b> There is no longer any anonymous endpoint that can
/// bring a tenant into existence: companies are onboarded by TechStorePro, which is what requirements
/// §2 describes and what resolves the self-service half of open question Q7.
///
/// The owner it creates is the bootstrap. An owner implicitly holds every permission (see
/// <c>PermissionService</c>), so no grants are written here — and without one, a brand-new company
/// would contain nobody able to grant anybody anything, and would be bricked on arrival.
/// </summary>
/// <param name="OwnerUsername">
/// The first login's name — <c>admin</c>, or whatever the shop asks for. Only unique within this
/// company, so it does not matter that another company already has an "admin".
/// </param>
/// <param name="OwnerPassword">
/// Handed to the customer out of band. They are forced to change it on first use
/// (<see cref="User.MustChangePassword"/>).
/// </param>
[RequiresPlatformAdmin]
public record CreateCompanyCommand(
    string Name,
    string Code,
    string OwnerFullName,
    string OwnerUsername,
    string OwnerPassword,
    string BaseCurrency = "AED",
    string TimeZone = "Asia/Dubai",
    string? OwnerEmail = null,
    string? Phone = null,
    string? Address = null,
    string? Country = null,
    string? TaxNumber = null) : IRequest<CompanyCreatedDto>;

public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$")
            .WithMessage(
                "A company code may contain only letters, numbers and hyphens — it is the half of a "
                + "login that comes after the '@', and it has to be typeable.");

        RuleFor(x => x.OwnerFullName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.OwnerUsername)
            .NotEmpty()
            .MaximumLength(100)
            .Must(u => !u.Contains('@'))
            .WithMessage("A username cannot contain '@' — the login is 'username@COMPANY'.");

        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.BaseCurrency).NotEmpty().Length(3);
        RuleFor(x => x.TimeZone).NotEmpty();
        RuleFor(x => x.OwnerEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.OwnerEmail));
    }
}

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CompanyCreatedDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTime _clock;

    public CreateCompanyCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        IDateTime clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<CompanyCreatedDto> Handle(
        CreateCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var code = Company.NormaliseCode(request.Code);
        var username = User.NormaliseUsername(request.OwnerUsername);

        // Unique across the platform, and checked including retired companies: the code is half of
        // every login this company's staff will type, and handing a dead tenant's code to a live one
        // would silently repoint 'ahmed@GULF01' at a different Ahmed.
        var codeTaken = await _db.IgnoringTenantFilter<Company>()
            .AnyAsync(c => c.Code == code, cancellationToken);

        if (codeTaken)
        {
            throw new ConflictException($"The company code '{code}' is already in use.");
        }

        var now = _clock.UtcNow;

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var company = new Company
        {
            Name = request.Name.Trim(),
            Code = code,
            LegalName = request.Name.Trim(),
            Email = request.OwnerEmail?.Trim().ToLowerInvariant(),
            Phone = request.Phone,
            Address = request.Address,
            Country = request.Country,
            TaxNumber = request.TaxNumber,
            BaseCurrency = request.BaseCurrency.ToUpperInvariant(),
            TimeZone = request.TimeZone,
            IsActive = true
        };

        _db.Companies.Add(company);

        var owner = new User
        {
            CompanyId = company.Id,
            Username = username,
            Email = request.OwnerEmail?.Trim().ToLowerInvariant(),
            FullName = request.OwnerFullName.Trim(),
            Phone = request.Phone,
            PasswordHash = _hasher.Hash(request.OwnerPassword),
            IsOwner = true,
            IsActive = true,

            // The platform admin knows this password — they chose it and read it out to the customer.
            // It must not stay usable by them.
            MustChangePassword = true
        };

        _db.Users.Add(owner);

        // A company with no branch and no warehouse cannot transact, and every later module assumes
        // both exist. Defaults now beat a half-configured tenant later.
        var branch = new Branch
        {
            CompanyId = company.Id,
            Name = "Main Branch",
            Code = "MAIN",
            Address = request.Address,
            Phone = request.Phone,
            IsDefault = true,
            IsActive = true
        };

        _db.Branches.Add(branch);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            Name = "Main Warehouse",
            Code = "MAIN",
            Type = WarehouseType.Main,
            BranchId = branch.Id,   // branch-owned by default; shared warehouses are opt-in
            IsActive = true
        };

        _db.Warehouses.Add(warehouse);

        SeedDocumentNumbering(company.Id, branch.Id, now.Year);

        // Branch and Warehouse reference each other (a warehouse belongs to a branch; a branch names
        // its default warehouse), so inserting both in one batch is a circular dependency EF cannot
        // order. Insert them with the branch's default left null, then set it — two statements, one
        // transaction, so the company is still never half-created.
        await _db.SaveChangesAsync(cancellationToken);

        branch.DefaultWarehouseId = warehouse.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new CompanyCreatedDto(company.Id, company.Code, owner.Id, $"{owner.Username}@{company.Code}");
    }

    /// <summary>
    /// Every document type gets a sequence up front, so the first invoice of a new company does not
    /// race to create its own counter.
    /// </summary>
    private void SeedDocumentNumbering(Guid companyId, Guid branchId, int year)
    {
        var prefixes = new Dictionary<DocumentType, string>
        {
            [DocumentType.Quotation] = "QT",
            [DocumentType.SalesOrder] = "SO",
            [DocumentType.Invoice] = "INV",
            [DocumentType.DeliveryNote] = "DLV",
            [DocumentType.CreditNote] = "CN",
            [DocumentType.DebitNote] = "DN",
            [DocumentType.Payment] = "PAY",
            [DocumentType.PurchaseOrder] = "PO",
            [DocumentType.GoodsReceipt] = "GRN",
            [DocumentType.SupplierInvoice] = "SINV",
            [DocumentType.SupplierPayment] = "SPY",
            [DocumentType.StockTransfer] = "TRF",
            [DocumentType.StockAdjustment] = "ADJ",
            [DocumentType.StockCount] = "CNT",
            [DocumentType.RepairTicket] = "REP",
            [DocumentType.Expense] = "EXP",
            [DocumentType.ImportShipment] = "IMP"
        };

        // A document type with no prefix here is not fatal — DocumentNumberGenerator would invent one
        // from the enum name — and that is exactly why it is worth failing on. P4 added SupplierInvoice
        // and forgot this dictionary, so supplier invoices would have numbered themselves "SUP-…":
        // nothing broke, nothing complained, and the shop would have discovered its own numbering
        // convention had been chosen for it by a substring. Adding a document type now breaks here, at
        // the one moment someone is in a position to choose the prefix deliberately.
        var missing = Enum.GetValues<DocumentType>().Where(t => !prefixes.ContainsKey(t)).ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"No document number prefix is defined for: {string.Join(", ", missing)}. "
                + "Add one to SeedDocumentNumbering — a company must not be provisioned with a "
                + "numbering convention nobody chose.");
        }

        foreach (var (type, prefix) in prefixes)
        {
            _db.DocumentNumberSequences.Add(new DocumentNumberSequence
            {
                CompanyId = companyId,
                BranchId = branchId,
                DocumentType = type,
                Prefix = prefix,
                Year = year,
                NextNumber = 1,
                Padding = 5,
                ResetsAnnually = true
            });
        }
    }
}

// --- Suspend / restore --------------------------------------------------------------------------

/// <summary>
/// Suspends or restores a company. A suspended company's staff cannot log in — the check is in
/// <c>LoginCommandHandler</c> — which is the enforcement point for a tenant that has stopped paying.
/// </summary>
[RequiresPlatformAdmin]
public record SetCompanyActiveCommand(Guid CompanyId, bool IsActive) : IRequest;

public class SetCompanyActiveCommandHandler : IRequestHandler<SetCompanyActiveCommand>
{
    private readonly IApplicationDbContext _db;

    public SetCompanyActiveCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(SetCompanyActiveCommand request, CancellationToken cancellationToken)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            ?? throw new NotFoundException("Company", request.CompanyId);

        company.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
