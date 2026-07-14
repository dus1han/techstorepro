using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Finance;

/// <summary>
/// The one implementation of <see cref="IAccountLedger"/>. Read that interface first — it says what the
/// guarantees are and why nothing else may write <c>account_transactions</c>. This class is how they are
/// kept, and the shape of every write is the same:
///
/// <code>
/// 1. refuse to run outside the caller's transaction
/// 2. lock the account row                      (SELECT … FOR UPDATE)
/// 3. validate against the locked state         (active, right currency, can it stand the withdrawal)
/// 4. append the movement                       (same transaction, always)
/// </code>
///
/// Step 2 needs no upsert, unlike <c>StockLedger</c>: an account is created deliberately by a human before
/// any money can move through it, so the row always exists. A stock balance is created by the first receipt
/// that happens to land, which is why that ledger has to materialise the row before it can lock it.
/// </summary>
public class AccountLedger : IAccountLedger
{
    private readonly ApplicationDbContextAccessor _accessor;
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;

    public AccountLedger(
        ApplicationDbContextAccessor accessor,
        IApplicationDbContext db,
        ITenantContext tenant,
        IDateTime clock)
    {
        _accessor = accessor;
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<AccountTransaction> PostAsync(
        AccountPosting posting,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireTenantAndTransaction();

        var account = await LockAccountAsync(companyId, posting.FinancialAccountId, cancellationToken);

        if (!account.IsActive)
        {
            throw new DomainException(
                $"'{account.Name}' is closed. Money cannot move through an account the shop has retired.");
        }

        // Read *after* the lock, never before: the whole point of the lock is that this number cannot
        // change between here and the commit.
        if (posting.Amount < 0)
        {
            await RefuseOverdrawAsync(account, posting.Amount, cancellationToken);
        }

        var transaction = new AccountTransaction
        {
            FinancialAccountId = account.Id,
            BranchId = posting.BranchId ?? account.BranchId,
            Source = posting.Source,
            SourceId = posting.SourceId,
            SourceNumber = posting.SourceNumber,
            Amount = posting.Amount,
            ExchangeRate = await RateToBaseAsync(account, cancellationToken),
            OccurredAt = posting.OccurredAt ?? _clock.UtcNow,
            Reference = posting.Reference,
            Description = posting.Description
        };

        transaction.Validate();

        // Through the DbSet, and only the DbSet. Adding to account.Transactions as well would have EF's
        // fixup count it twice — the trap P4 wrote down and P6 finally explained.
        _db.AccountTransactions.Add(transaction);

        return transaction;
    }

    public async Task<(AccountTransaction Out, AccountTransaction In)> TransferAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amountOut,
        decimal amountIn,
        string description,
        string? reference = null,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        RequireTenantAndTransaction();

        if (fromAccountId == toAccountId)
        {
            throw new DomainException("An account cannot be transferred to itself.");
        }

        if (amountOut <= 0 || amountIn <= 0)
        {
            throw new DomainException("A transfer of nothing moves nothing.");
        }

        var at = occurredAt ?? _clock.UtcNow;

        // Locked in id order, always. Two clerks banking two tills into the same account at the same
        // moment would otherwise take the two locks in opposite orders and deadlock — and Postgres would
        // resolve it by killing one of them, which the clerk sees as a payment that failed for no reason.
        var first = fromAccountId.CompareTo(toAccountId) < 0 ? fromAccountId : toAccountId;
        var second = first == fromAccountId ? toAccountId : fromAccountId;
        var companyId = _tenant.CompanyId!.Value;

        await LockAccountAsync(companyId, first, cancellationToken);
        await LockAccountAsync(companyId, second, cancellationToken);

        var out_ = await PostAsync(
            new AccountPosting(
                fromAccountId,
                -amountOut,
                AccountTransactionSource.TransferOut,
                description,
                Reference: reference,
                OccurredAt: at),
            cancellationToken);

        var in_ = await PostAsync(
            new AccountPosting(
                toAccountId,
                amountIn,
                AccountTransactionSource.TransferIn,
                description,
                Reference: reference,
                OccurredAt: at),
            cancellationToken);

        // Each leg points at the other, so a statement row can answer "where did this money come from?"
        // without a join through a transfer table that does not exist.
        out_.SourceId = in_.Id;
        in_.SourceId = out_.Id;

        return (out_, in_);
    }

    public async Task<decimal> BalanceAsync(
        Guid financialAccountId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AccountTransactions
            .Where(t => t.FinancialAccountId == financialAccountId);

        if (asOf is { } cutoff)
        {
            query = query.Where(t => t.OccurredAt <= cutoff);
        }

        return await query.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
    }

    /// <summary>
    /// The withdrawal check, run under the account's lock.
    ///
    /// The balance is the database's sum <b>plus whatever this transaction has already posted and not yet
    /// saved</b>. That second term is not an optimisation — without it, a handler that empties an account
    /// in two movements would check the second against a balance that still contains the first, and a till
    /// holding 500 would pay out 500 twice inside one commit. <c>StockLedger</c> gets this for free,
    /// because it mutates a tracked balance row; a ledger with no cache has to add up its own pending work.
    /// </summary>
    private async Task RefuseOverdrawAsync(
        FinancialAccount account,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (account.AllowsOverdraft)
        {
            return;
        }

        var settled = await BalanceAsync(account.Id, cancellationToken: cancellationToken);

        var pending = _accessor.Context.ChangeTracker.Entries<AccountTransaction>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(t => t.FinancialAccountId == account.Id)
            .Sum(t => t.Amount);

        var available = settled + pending;

        if (available + amount < 0)
        {
            throw new DomainException(
                $"'{account.Name}' holds {available:0.##} {account.CurrencyCode} and this pays out "
                + $"{-amount:0.##}. The money is not there.");
        }
    }

    /// <summary>
    /// The account's currency into the company's base. Almost always 1 — the till holds dirhams and the
    /// company reports in dirhams — and looked up only when it does not.
    /// </summary>
    private async Task<decimal> RateToBaseAsync(FinancialAccount account, CancellationToken cancellationToken)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == account.CompanyId, cancellationToken)
            ?? throw new NotFoundException("Company", account.CompanyId);

        if (string.Equals(account.CurrencyCode, company.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        var rate = await _db.FxRates
            .Where(r => r.CurrencyCode == account.CurrencyCode)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                $"There is no exchange rate for {account.CurrencyCode}, so what '{account.Name}' holds "
                + $"cannot be reported in {company.BaseCurrency}.");

        return rate.RateToBase;
    }

    private async Task<FinancialAccount> LockAccountAsync(
        Guid companyId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await _accessor.Context.FinancialAccounts
            .FromSqlRaw(
                """
                SELECT * FROM techstorepro.financial_accounts
                WHERE id = {0} AND company_id = {1} AND is_deleted = false
                FOR UPDATE
                """,
                accountId, companyId)
            .FirstOrDefaultAsync(cancellationToken);

        return account ?? throw new NotFoundException("Account", accountId);
    }

    private Guid RequireTenantAndTransaction()
    {
        var companyId = _tenant.CompanyId
            ?? throw new DomainException("Money cannot move without a company.");

        if (_accessor.Context.Database.CurrentTransaction is null)
        {
            // Without an ambient transaction the FOR UPDATE lock would be released the moment the SELECT
            // finished, and two clerks paying out the last 500 in the drawer would both pass the check.
            // It is the same failure as overselling the last laptop, in the same place, for the same
            // reason — and the money is easier to lose.
            throw new DomainException(
                "Money must move inside a transaction. Call IApplicationDbContext.BeginTransactionAsync "
                + "first.");
        }

        return companyId;
    }
}
