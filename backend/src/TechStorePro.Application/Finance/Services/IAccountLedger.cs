using TechStorePro.Domain.Finance;

namespace TechStorePro.Application.Finance.Services;

/// <summary>
/// One movement of money, as the caller describes it. The caller says what happened and how much; the
/// ledger decides whether the account can stand it, what rate to carry, and what the statement will say.
/// </summary>
/// <param name="Amount">
/// Signed: positive is money into the account, negative is money out — and in the <em>account's</em>
/// currency, never the document's (see <see cref="AccountTransaction"/>).
///
/// Unlike <c>StockPosting</c> — where the direction is implied by the movement type, because a caller that
/// could pass a negative quantity could invert a warehouse — money genuinely moves both ways through the
/// same door, and the source type does not settle which: a cancelled expense is an <c>Expense</c>-shaped
/// event that puts money back.
/// </param>
/// <param name="OccurredAt">
/// When the money actually moved, which is not always now — an expense can be recorded on Friday for a
/// receipt dated Tuesday. Defaults to the current time.
/// </param>
public record AccountPosting(
    Guid FinancialAccountId,
    decimal Amount,
    AccountTransactionSource Source,
    string Description,
    Guid? BranchId = null,
    Guid? SourceId = null,
    string? SourceNumber = null,
    string? Reference = null,
    DateTimeOffset? OccurredAt = null);

/// <summary>
/// <b>The single door into the money tables.</b> Nothing else in the system may write
/// <c>account_transactions</c> — not sales, not purchasing, not expenses. The same rule, and the same
/// reasons, as <c>IStockLedger</c> and <c>stock_movements</c>.
///
/// Two invariants, and they only hold if there is exactly one place that enforces them:
///
/// <list type="bullet">
/// <item><b>A till cannot be overdrawn.</b> The account row is locked (<c>SELECT … FOR UPDATE</c>) before
///   its balance is summed, so two people paying out the last 500 in the drawer cannot both pass the
///   check. This is the cash equivalent of "prevent overselling", and it fails in exactly the same way
///   without the lock: both reads see 500, both succeed, and the drawer owes 500 it does not have.</item>
/// <item><b>The movement and the document that caused it are written in the same transaction.</b> Every
///   method here <b>requires an ambient transaction and throws without one</b> — money never moves on its
///   own: a payment also settles invoices, an expense also gets a number. A ledger that quietly committed
///   by itself would leave the bank debited and the expense that debited it rolled back.</item>
/// </list>
///
/// Note what is <em>not</em> here: no balance to maintain. The account carries no cached total (see
/// <see cref="FinancialAccount"/>), so there is nothing for this ledger to keep in step and nothing that
/// can drift out of it. The lock exists to serialise the <em>check</em>, not to protect a cache.
/// </summary>
public interface IAccountLedger
{
    /// <summary>
    /// Move money into or out of an account. Refuses an overdraw the account does not permit, and refuses
    /// to run outside a transaction.
    /// </summary>
    Task<AccountTransaction> PostAsync(AccountPosting posting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Move money between two accounts — the till banked at the end of the day, a float taken out to the
    /// second shop.
    ///
    /// <b>Two movements, not one.</b> The same reasoning as a stock transfer: one row with a "from" and a
    /// "to" column would mean each account's statement had to know about the other's, and the money would
    /// belong to both ends at once. Two rows means each account's balance is the sum of its own history and
    /// nothing else.
    ///
    /// Where the two accounts hold different currencies, <paramref name="amountIn"/> is what the receiving
    /// account actually received — the shop banked USD 1,000 and AED 3,600 landed. Guessing it from a rate
    /// would produce a bank balance that disagrees with the bank.
    /// </summary>
    Task<(AccountTransaction Out, AccountTransaction In)> TransferAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amountOut,
        decimal amountIn,
        string description,
        string? reference = null,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the account holds, in its own currency: the sum of its movements, computed in the database.
    /// There is no cache to read instead.
    /// </summary>
    Task<decimal> BalanceAsync(
        Guid financialAccountId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}
