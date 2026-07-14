using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Finance;

/// <summary>
/// What put money into an account, or took it out. The transaction points back at the document that
/// caused it, so every figure in the cash position can be walked back to the act that produced it.
/// </summary>
public enum AccountTransactionSource : short
{
    /// <summary>What was in the drawer on the day the shop started using the system.</summary>
    OpeningBalance = 1,

    CustomerPayment = 2,

    /// <summary>A return settled in cash or by bank transfer — money physically handed back.</summary>
    CustomerRefund = 3,

    SupplierPayment = 4,
    Expense = 5,

    /// <summary>The reversal of a cancelled expense. Never a delete — see <c>Expense.Cancel</c>.</summary>
    ExpenseCancellation = 6,

    /// <summary>The leg that leaves. Depositing the till into the bank writes one of these and a <see cref="TransferIn"/>.</summary>
    TransferOut = 7,
    TransferIn = 8
}

/// <summary>
/// One movement of money in or out of a <see cref="FinancialAccount"/> (requirements §33).
///
/// <b>Signed, not split into two columns</b> — positive is money in, negative is money out — so the balance
/// is a SUM and cannot be got wrong by adding the wrong pair. Same reasoning as <c>StoreCreditEntry</c>.
///
/// <b>The amount is what the account itself lost or gained, in its own money.</b> That sentence is doing
/// more work than it looks, and it is the one thing in this table that is easy to get wrong:
///
/// A USD 1,000 supplier invoice booked at 3.67 is a debt of AED 3,670. Paid when the rate is 3.60, the
/// bank hands over <b>AED 3,600</b> — and AED 3,600 is what this row records. The AED 70 difference is a
/// realised FX gain (P4); it belongs in the P&amp;L. It is <em>not</em> money, and it never entered or left
/// any account. Book the invoice-rate value here instead and the shop's bank account would disagree with
/// the actual bank by 70 dirhams, for ever, on every foreign bill it ever paid — a discrepancy that grows
/// and that nothing in the system could explain.
/// </summary>
public class AccountTransaction : TenantEntity
{
    public Guid FinancialAccountId { get; set; }
    public FinancialAccount FinancialAccount { get; set; } = null!;

    /// <summary>Which shop the money moved in. Null on a movement against a company-wide bank account.</summary>
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public AccountTransactionSource Source { get; set; }

    /// <summary>The document that caused it — the payment, the expense, the transfer.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>
    /// The document's number, snapshotted. A statement has to stay readable when the document behind a
    /// row is long gone, and a join that resolves eight different source tables to find a string is a
    /// join nobody should write.
    /// </summary>
    public string? SourceNumber { get; set; }

    /// <summary>Positive in, negative out — in the account's currency, never the document's.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// The account's currency into the company's base, on the day the money moved. It is 1 for an account
    /// already held in base currency, which is nearly all of them.
    ///
    /// It exists only so the cash position can be totalled across a dirham till and a dollar bank account.
    /// It has nothing to do with the rate on the document that caused the movement — see the class remarks.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>What the movement was worth in the company's own money, for reporting only.</summary>
    public decimal AmountBase => Amount * ExchangeRate;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The cheque number, the transfer reference — what the bank statement is matched on.</summary>
    public string? Reference { get; set; }

    /// <summary>What a person reading the statement needs to see. Written by the ledger, not by the caller.</summary>
    public string Description { get; set; } = null!;

    public void Validate()
    {
        if (Amount == 0)
        {
            throw new DomainException("A movement of nothing moves nothing.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new DomainException("A movement of money must say what it was.");
        }
    }
}
