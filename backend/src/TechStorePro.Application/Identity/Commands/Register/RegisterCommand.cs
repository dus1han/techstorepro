using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.Register;

/// <summary>Company registration (requirements §3): creates the tenant and its first owner.</summary>
[AllowAnonymousRequest]
public record RegisterCommand(
    string CompanyName,
    string ContactPerson,
    string Email,
    string Password,
    string? Phone,
    string? Address,
    string? Country,
    string? TaxNumber,
    string BaseCurrency,
    string TimeZone) : IRequest<AuthResult>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactPerson).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.BaseCurrency).NotEmpty().Length(3);
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(64);

        // The password policy proper is configurable per company (requirements §11) — but a company
        // that does not exist yet has no settings to read, so registration enforces the floor.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTime _clock;
    private readonly IAuthSessionFactory _sessions;

    public RegisterCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        IDateTime clock,
        IAuthSessionFactory sessions)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _sessions = sessions;
    }

    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Users are global, not tenant-scoped, so this address may already belong to someone who is
        // a member of a different company. Registering a *new* company under an existing login is a
        // legitimate flow — but it is not this one, and silently attaching to an existing account
        // from an anonymous endpoint would let an attacker who guessed an email create a company
        // that user appears to own.
        var emailTaken = await _db.IgnoringTenantFilter<User>()
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException(
                "An account already exists for this email address. Sign in and create the company from there.");
        }

        var now = _clock.UtcNow;

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var company = new Company
        {
            Name = request.CompanyName.Trim(),
            LegalName = request.CompanyName.Trim(),
            Email = email,
            Phone = request.Phone,
            Address = request.Address,
            Country = request.Country,
            TaxNumber = request.TaxNumber,
            BaseCurrency = request.BaseCurrency.ToUpperInvariant(),
            TimeZone = request.TimeZone,
            IsActive = true
        };
        _db.Companies.Add(company);

        var user = new User
        {
            Email = email,
            FullName = request.ContactPerson.Trim(),
            Phone = request.Phone,
            PasswordHash = _hasher.Hash(request.Password),
            IsActive = true
        };
        _db.Users.Add(user);

        // The founder owns the company. An owner implicitly holds every permission, so no grants are
        // written here — see PermissionService. Without this, a brand-new company would have nobody
        // able to grant anyone anything, and would be bricked on arrival.
        var membership = new CompanyUser
        {
            CompanyId = company.Id,
            UserId = user.Id,
            IsOwner = true,
            IsDefault = true,
            IsActive = true
        };
        _db.CompanyUsers.Add(membership);

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
        // order. Insert them first with the branch's default left null, then set it — two statements,
        // one transaction, so the company is still never half-created.
        await _db.SaveChangesAsync(cancellationToken);

        branch.DefaultWarehouseId = warehouse.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await _sessions.IssueAsync(user.Id, company.Id, cancellationToken);
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
            [DocumentType.CreditNote] = "CN",
            [DocumentType.DebitNote] = "DN",
            [DocumentType.Payment] = "PAY",
            [DocumentType.PurchaseOrder] = "PO",
            [DocumentType.GoodsReceipt] = "GRN",
            [DocumentType.SupplierPayment] = "SPY",
            [DocumentType.StockTransfer] = "TRF",
            [DocumentType.StockAdjustment] = "ADJ",
            [DocumentType.StockCount] = "CNT",
            [DocumentType.RepairTicket] = "REP",
            [DocumentType.Expense] = "EXP",
            [DocumentType.ImportShipment] = "IMP"
        };

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
