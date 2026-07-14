using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Finance.Accounts;

// ================================================================================================
// Opening an account
// ================================================================================================

/// <summary>
/// Open a till or a bank account (requirements §33).
///
/// <see cref="OpeningBalance"/> is what was already in it on the day the shop started using the system.
/// It is <b>a movement, not a column</b> — the first row on the account's statement, sourced as
/// <see cref="AccountTransactionSource.OpeningBalance"/>. A number typed onto the account itself would be
/// a figure with no date, no author and no explanation, sitting outside the ledger that every other figure
/// in this module is derived from.
/// </summary>
[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.Create)]
public record OpenAccountCommand(
    string Name,
    FinancialAccountKind Kind,
    Guid? BranchId = null,
    string? CurrencyCode = null,
    string? BankName = null,
    string? AccountNumber = null,
    bool AllowsOverdraft = false,
    decimal OpeningBalance = 0m,
    DateTimeOffset? OpenedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class OpenAccountCommandValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BankName).MaximumLength(200);
        RuleFor(x => x.AccountNumber).MaximumLength(64);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0)
            .WithMessage("An account cannot be opened already overdrawn.");
    }
}

public class OpenAccountCommandHandler : IRequestHandler<OpenAccountCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAccountLedger _ledger;
    private readonly IDateTime _clock;

    public OpenAccountCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        IAccountLedger ledger,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<Guid> Handle(OpenAccountCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var currency = await CompanyCurrency.EnsureAsync(_db, _tenant, request.CurrencyCode, cancellationToken);

        if (request.BranchId is { } branchId
            && !await _db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var account = new FinancialAccount
        {
            Name = request.Name,
            Kind = request.Kind,
            CurrencyCode = currency,
            BranchId = request.BranchId,
            BankName = request.BankName,
            AccountNumber = request.AccountNumber,
            AllowsOverdraft = request.AllowsOverdraft,
            Notes = request.Notes
        };

        account.Validate();

        _db.FinancialAccounts.Add(account);

        // Saved before the opening balance is posted: the ledger locks the account row with a raw
        // SELECT … FOR UPDATE, and a row that only exists in the change tracker is not there to be locked.
        await _db.SaveChangesAsync(cancellationToken);

        if (request.OpeningBalance > 0)
        {
            await _ledger.PostAsync(
                new AccountPosting(
                    account.Id,
                    request.OpeningBalance,
                    AccountTransactionSource.OpeningBalance,
                    $"Opening balance — {account.Name}",
                    BranchId: request.BranchId,
                    OccurredAt: request.OpenedAt ?? _clock.UtcNow),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return account.Id;
    }
}

// ================================================================================================
// Editing and closing
// ================================================================================================

/// <summary>
/// Rename an account, correct its bank details, grant or withdraw an overdraft.
///
/// <b>Its currency is not editable, and neither is its kind.</b> Both would restate every movement it has
/// ever carried: an AED till holding 4,300 does not become a USD till holding 4,300 because somebody
/// changed a dropdown — the money in the drawer did not change, only the label on it, and now the cash
/// position is wrong by a factor of 3.67.
/// </summary>
[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.Edit)]
public record UpdateAccountCommand(
    Guid Id,
    string Name,
    string? BankName = null,
    string? AccountNumber = null,
    bool AllowsOverdraft = false,
    bool IsActive = true,
    string? Notes = null) : IRequest;

public class UpdateAccountCommandValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BankName).MaximumLength(200);
        RuleFor(x => x.AccountNumber).MaximumLength(64);
    }
}

public class UpdateAccountCommandHandler : IRequestHandler<UpdateAccountCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountLedger _ledger;

    public UpdateAccountCommandHandler(IApplicationDbContext db, IAccountLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task Handle(UpdateAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);

        if (!request.IsActive && account.IsActive)
        {
            var balance = await _ledger.BalanceAsync(account.Id, cancellationToken: cancellationToken);

            if (balance != 0)
            {
                // Closing an account with money in it would hide that money: it would drop out of the cash
                // position while still being, physically, in the drawer. Bank it or spend it first — the
                // same reasoning that stops P2 retiring a customer who still owes.
                throw new DomainException(
                    $"'{account.Name}' still holds {balance:0.##} {account.CurrencyCode}. Transfer it out "
                    + "before closing the account, or the money will vanish from the cash position while "
                    + "still being in the drawer.");
            }
        }

        account.Name = request.Name;
        account.BankName = request.BankName;
        account.AccountNumber = request.AccountNumber;
        account.AllowsOverdraft = request.AllowsOverdraft;
        account.IsActive = request.IsActive;
        account.Notes = request.Notes;

        account.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ================================================================================================
// Moving money between accounts
// ================================================================================================

/// <summary>
/// Bank the till, take a float out to the second shop (requirements §33).
///
/// <see cref="AmountIn"/> is optional and defaults to <see cref="AmountOut"/>, which is the whole story
/// whenever both accounts hold the same currency — which is nearly always. It differs only when they do
/// not: USD 1,000 leaves the dollar account and AED 3,670 lands in the dirham one, and <b>the shop types
/// what the bank actually credited</b> rather than the system inferring it from a rate. A rate would be
/// right to six decimal places and wrong by whatever the bank charged for the conversion — and the bank
/// statement, not the FX table, is what this account has to reconcile against.
/// </summary>
[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.Approve)]
public record TransferBetweenAccountsCommand(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal AmountOut,
    decimal? AmountIn = null,
    string? Description = null,
    string? Reference = null,
    DateTimeOffset? OccurredAt = null) : IRequest;

public class TransferBetweenAccountsCommandValidator : AbstractValidator<TransferBetweenAccountsCommand>
{
    public TransferBetweenAccountsCommandValidator()
    {
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.ToAccountId).NotEmpty()
            .NotEqual(x => x.FromAccountId)
            .WithMessage("An account cannot be transferred to itself.");

        RuleFor(x => x.AmountOut).GreaterThan(0);
        RuleFor(x => x.AmountIn).GreaterThan(0).When(x => x.AmountIn is not null);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class TransferBetweenAccountsCommandHandler : IRequestHandler<TransferBetweenAccountsCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountLedger _ledger;

    public TransferBetweenAccountsCommandHandler(IApplicationDbContext db, IAccountLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task Handle(TransferBetweenAccountsCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var from = await _db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == request.FromAccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.FromAccountId);

        var to = await _db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == request.ToAccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.ToAccountId);

        var amountIn = request.AmountIn ?? request.AmountOut;

        if (request.AmountIn is null && from.CurrencyCode != to.CurrencyCode)
        {
            // Defaulting the in-leg to the out-leg across a currency boundary would credit AED 1,000 for
            // USD 1,000 — off by a factor of 3.67, silently, on a screen that showed neither number.
            throw new DomainException(
                $"'{from.Name}' holds {from.CurrencyCode} and '{to.Name}' holds {to.CurrencyCode}. Say what "
                + $"actually landed in '{to.Name}' — the bank's figure, not a converted one.");
        }

        await _ledger.TransferAsync(
            from.Id,
            to.Id,
            request.AmountOut,
            amountIn,
            request.Description ?? $"Transfer — {from.Name} to {to.Name}",
            request.Reference,
            request.OccurredAt,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
