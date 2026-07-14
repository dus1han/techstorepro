using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Finance.Queries;

// Note on every SUM in this file: the balance is written as `Amount * ExchangeRate`, never as
// `AmountBase`. AmountBase is a C# expression on the entity and is Ignore()d by EF, so a query that used
// it would compile, run, and quietly evaluate on the *client* after dragging every row of the table over
// the wire. Same trap as the computed totals in SalesQueries.
//
// And note what is *not* here: a balance column to read instead. There is none, by design — see
// FinancialAccount. Every figure below is summed from account_transactions, which is why there is no
// variance to report, unlike the receivables and payables reports of slice 1.

public record AccountDto(
    Guid Id,
    string Name,
    FinancialAccountKind Kind,
    string CurrencyCode,
    Guid? BranchId,
    string? BranchName,
    string? BankName,
    string? AccountNumber,
    bool AllowsOverdraft,
    bool IsActive,

    // Returned so the edit screen can send it back. Without it the form would post a blank Notes on every
    // save — an unconditional write of a field the client was never given — and the note somebody wrote
    // when the account was opened would vanish the first time anybody renamed it.
    string? Notes,
    decimal Balance,
    decimal BalanceBase);

[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.View)]
public record GetAccountsQuery(bool IncludeInactive = false) : IRequest<IReadOnlyCollection<AccountDto>>;

public class GetAccountsQueryHandler : IRequestHandler<GetAccountsQuery, IReadOnlyCollection<AccountDto>>
{
    private readonly IApplicationDbContext _db;

    public GetAccountsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<AccountDto>> Handle(
        GetAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var accounts = _db.FinancialAccounts.AsNoTracking();

        if (!request.IncludeInactive)
        {
            accounts = accounts.Where(a => a.IsActive);
        }

        return await accounts
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Name)
            .Select(a => new AccountDto(
                a.Id,
                a.Name,
                a.Kind,
                a.CurrencyCode,
                a.BranchId,
                a.Branch != null ? a.Branch.Name : null,
                a.BankName,
                a.AccountNumber,
                a.AllowsOverdraft,
                a.IsActive,
                a.Notes,
                _db.AccountTransactions
                    .Where(t => t.FinancialAccountId == a.Id)
                    .Sum(t => (decimal?)t.Amount) ?? 0m,
                _db.AccountTransactions
                    .Where(t => t.FinancialAccountId == a.Id)
                    .Sum(t => (decimal?)(t.Amount * t.ExchangeRate)) ?? 0m))
            .ToListAsync(cancellationToken);
    }
}

// ================================================================================================
// The statement — what moved, and what it left behind
// ================================================================================================

public record AccountStatementRowDto(
    DateTimeOffset OccurredAt,
    AccountTransactionSource Source,
    string? SourceNumber,
    string Description,
    string? Reference,
    decimal In,
    decimal Out,
    decimal RunningBalance);

public record AccountStatementDto(
    Guid AccountId,
    string AccountName,
    FinancialAccountKind Kind,
    string CurrencyCode,
    DateTimeOffset? From,
    DateTimeOffset To,
    decimal OpeningBalance,
    IReadOnlyCollection<AccountStatementRowDto> Rows,
    decimal ClosingBalance);

/// <summary>
/// One account's history: what it held, what moved it, what it holds.
///
/// The opening balance is <b>everything before the window</b>, not the account's
/// <see cref="AccountTransactionSource.OpeningBalance"/> row — a statement for June opens with what was
/// there on the 1st of June, which includes every movement since the account was created. Reading the
/// literal opening-balance row instead would show a statement that starts from the day the shop was
/// founded, whatever dates were asked for.
/// </summary>
[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.View)]
public record GetAccountStatementQuery(
    Guid AccountId,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<AccountStatementDto>;

public class GetAccountStatementQueryHandler : IRequestHandler<GetAccountStatementQuery, AccountStatementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetAccountStatementQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AccountStatementDto> Handle(
        GetAccountStatementQuery request,
        CancellationToken cancellationToken)
    {
        var account = await _db.FinancialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);

        var to = request.To ?? _clock.UtcNow;

        var opening = request.From is { } from
            ? await _db.AccountTransactions
                .Where(t => t.FinancialAccountId == account.Id && t.OccurredAt < from)
                .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m
            : 0m;

        var movements = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.FinancialAccountId == account.Id && t.OccurredAt <= to)
            .Where(t => request.From == null || t.OccurredAt >= request.From)
            .OrderBy(t => t.OccurredAt)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.OccurredAt,
                t.Source,
                t.SourceNumber,
                t.Description,
                t.Reference,
                t.Amount
            })
            .ToListAsync(cancellationToken);

        var running = opening;
        var rows = new List<AccountStatementRowDto>(movements.Count);

        foreach (var m in movements)
        {
            running += m.Amount;

            // Split into two columns only here, at the edge, because that is how a finance person reads a
            // statement. The stored row stays signed, which is what makes the balance a SUM.
            rows.Add(new AccountStatementRowDto(
                m.OccurredAt,
                m.Source,
                m.SourceNumber,
                m.Description,
                m.Reference,
                In: m.Amount > 0 ? m.Amount : 0m,
                Out: m.Amount < 0 ? -m.Amount : 0m,
                RunningBalance: running));
        }

        return new AccountStatementDto(
            account.Id,
            account.Name,
            account.Kind,
            account.CurrencyCode,
            request.From,
            to,
            opening,
            rows,
            running);
    }
}

// ================================================================================================
// The cash position (§33, §36) — what the shop actually holds, right now
// ================================================================================================

public record CashPositionLineDto(
    Guid AccountId,
    string Name,
    FinancialAccountKind Kind,
    string CurrencyCode,
    string? BranchName,
    decimal Balance,
    decimal BalanceBase);

public record CashPositionDto(
    DateTimeOffset AsOf,
    string BaseCurrency,
    IReadOnlyCollection<CashPositionLineDto> Accounts,
    decimal CashTotalBase,
    decimal BankTotalBase,
    decimal TotalBase);

/// <summary>
/// Everything the shop holds, in one number and in the numbers behind it (requirements §33).
///
/// It totals in the <b>base currency</b>, because a shop with a dirham till and a dollar account cannot
/// add them up otherwise — and the §36 dashboard's "cash" widget is exactly one figure. The per-account
/// rows carry both, so the total can always be taken apart into money somebody can physically point at.
/// </summary>
[RequiresPermission(FeatureCatalog.Accounts, PermissionAction.View)]
public record GetCashPositionQuery(DateTimeOffset? AsOf = null) : IRequest<CashPositionDto>;

public class GetCashPositionQueryHandler : IRequestHandler<GetCashPositionQuery, CashPositionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;

    public GetCashPositionQueryHandler(IApplicationDbContext db, ITenantContext tenant, IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<CashPositionDto> Handle(GetCashPositionQuery request, CancellationToken cancellationToken)
    {
        var asOf = request.AsOf ?? _clock.UtcNow;

        var baseCurrency = await _db.Companies
            .Where(c => c.Id == _tenant.CompanyId)
            .Select(c => c.BaseCurrency)
            .FirstOrDefaultAsync(cancellationToken) ?? "AED";

        var lines = await _db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Name)
            .Select(a => new CashPositionLineDto(
                a.Id,
                a.Name,
                a.Kind,
                a.CurrencyCode,
                a.Branch != null ? a.Branch.Name : null,
                _db.AccountTransactions
                    .Where(t => t.FinancialAccountId == a.Id && t.OccurredAt <= asOf)
                    .Sum(t => (decimal?)t.Amount) ?? 0m,
                _db.AccountTransactions
                    .Where(t => t.FinancialAccountId == a.Id && t.OccurredAt <= asOf)
                    .Sum(t => (decimal?)(t.Amount * t.ExchangeRate)) ?? 0m))
            .ToListAsync(cancellationToken);

        var cash = lines.Where(l => l.Kind == FinancialAccountKind.Cash).Sum(l => l.BalanceBase);
        var bank = lines.Where(l => l.Kind == FinancialAccountKind.Bank).Sum(l => l.BalanceBase);

        return new CashPositionDto(asOf, baseCurrency, lines, cash, bank, cash + bank);
    }
}

// ================================================================================================
// Expenses (§34)
// ================================================================================================

public record ExpenseDto(
    Guid Id,
    string Number,
    Guid ExpenseCategoryId,
    string CategoryName,
    Guid BranchId,
    string BranchName,
    Guid FinancialAccountId,
    string AccountName,
    Guid? SupplierId,
    string? SupplierName,
    string Description,
    decimal Amount,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal AmountBase,
    DateTimeOffset ExpenseDate,
    string? Reference,
    ExpenseStatus Status,
    string? CancelledReason,
    string? Notes);

/// <summary>
/// What the shop has spent. Cancelled expenses are returned rather than hidden — the point of cancelling
/// rather than deleting is that the mistake stays visible next to its correction.
/// </summary>
[RequiresPermission(FeatureCatalog.Expenses, PermissionAction.View)]
public record GetExpensesQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ExpenseCategoryId = null,
    Guid? BranchId = null,
    Guid? FinancialAccountId = null,
    ExpenseStatus? Status = null) : IRequest<IReadOnlyCollection<ExpenseDto>>;

public class GetExpensesQueryHandler : IRequestHandler<GetExpensesQuery, IReadOnlyCollection<ExpenseDto>>
{
    private readonly IApplicationDbContext _db;

    public GetExpensesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<ExpenseDto>> Handle(
        GetExpensesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Expenses.AsNoTracking();

        if (request.From is { } from)
        {
            query = query.Where(e => e.ExpenseDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(e => e.ExpenseDate <= to);
        }

        if (request.ExpenseCategoryId is { } category)
        {
            query = query.Where(e => e.ExpenseCategoryId == category);
        }

        if (request.BranchId is { } branch)
        {
            query = query.Where(e => e.BranchId == branch);
        }

        if (request.FinancialAccountId is { } account)
        {
            query = query.Where(e => e.FinancialAccountId == account);
        }

        if (request.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        return await query
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => new ExpenseDto(
                e.Id,
                e.Number,
                e.ExpenseCategoryId,
                e.ExpenseCategory.Name,
                e.BranchId,
                e.Branch.Name,
                e.FinancialAccountId,
                e.FinancialAccount.Name,
                e.SupplierId,
                e.Supplier != null ? e.Supplier.Name : null,
                e.Description,
                e.Amount,
                e.CurrencyCode,
                e.ExchangeRate,
                e.Amount * e.ExchangeRate,
                e.ExpenseDate,
                e.Reference,
                e.Status,
                e.CancelledReason,
                e.Notes))
            .ToListAsync(cancellationToken);
    }
}

public record ExpenseSummaryLineDto(Guid CategoryId, string CategoryName, int Count, decimal TotalBase);

public record ExpenseSummaryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<ExpenseSummaryLineDto> Categories,
    decimal TotalBase);

/// <summary>
/// Expenses grouped by category over a period — §34's report, and the figure the computed P&amp;L
/// subtracts from gross profit when the §35 report set lands.
///
/// <b>Cancelled expenses are excluded</b>, and that is the one thing here that has to be right: a
/// cancellation reverses the money in the account ledger, so counting the expense as well would charge the
/// shop for a payment that was taken back.
/// </summary>
[RequiresPermission(FeatureCatalog.Expenses, PermissionAction.View)]
public record GetExpenseSummaryQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? BranchId = null) : IRequest<ExpenseSummaryDto>;

public class GetExpenseSummaryQueryHandler : IRequestHandler<GetExpenseSummaryQuery, ExpenseSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetExpenseSummaryQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ExpenseSummaryDto> Handle(
        GetExpenseSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var to = request.To ?? _clock.UtcNow;
        var from = request.From ?? to.AddMonths(-1);

        var query = _db.Expenses
            .AsNoTracking()
            .Where(e => e.Status == ExpenseStatus.Recorded)
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to);

        if (request.BranchId is { } branch)
        {
            query = query.Where(e => e.BranchId == branch);
        }

        // Grouped and summed in the database; sorted afterwards. EF cannot translate an ORDER BY over a
        // property of a projected DTO, and there is one row per expense category — the shop has a dozen,
        // not a million — so sorting them in memory costs nothing.
        var categories = await query
            .GroupBy(e => new { e.ExpenseCategoryId, e.ExpenseCategory.Name })
            .Select(g => new ExpenseSummaryLineDto(
                g.Key.ExpenseCategoryId,
                g.Key.Name,
                g.Count(),
                g.Sum(e => e.Amount * e.ExchangeRate)))
            .ToListAsync(cancellationToken);

        // Biggest first: this is a screen somebody opens to find out where the money went.
        var ordered = categories.OrderByDescending(c => c.TotalBase).ToList();

        return new ExpenseSummaryDto(from, to, ordered, ordered.Sum(c => c.TotalBase));
    }
}
