using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Finance;

public enum FinancialAccountKind : short
{
    /// <summary>A till, a petty-cash box, a safe. Money you can hold.</summary>
    Cash = 1,

    /// <summary>An account at a bank. Money somebody else holds for you.</summary>
    Bank = 2
}

/// <summary>
/// A place money sits — a till, a petty-cash box, a bank account (requirements §33).
///
/// <b>There is no balance column here, and that is the whole design.</b> The balance is the SUM of
/// <see cref="AccountTransaction"/>, exactly as store credit is the sum of its entries and
/// <c>stock_balances</c> is a cache of <c>stock_movements</c>. P7 slice 1 spent its entire budget proving
/// that two hand-maintained caches (<c>Customer.Balance</c>, <c>Supplier.Balance</c>) still agreed with the
/// documents beneath them. A cash balance needs no such proof because there is nothing to disagree with:
/// "why does the till say 4,300?" is answered by reading the rows that put it there.
///
/// Stock is cached and this is not, and the difference is not inconsistency. A stock balance carries the
/// weighted average, which is genuinely derived state that has to be recomputed under a lock as each
/// movement lands. A cash balance is a plain sum, and Postgres can add up a column.
/// </summary>
public class FinancialAccount : TenantEntity
{
    public string Name { get; set; } = null!;

    public FinancialAccountKind Kind { get; set; } = FinancialAccountKind.Cash;

    /// <summary>
    /// The money this account holds. Every transaction against it is denominated in this, because an
    /// account holds one currency — a dirham bank account does not contain dollars, whatever the invoice
    /// that drained it was written in.
    /// </summary>
    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The branch whose account this is. <b>Null means company-wide</b> — the bank account every branch
    /// pays into. A till is branch-owned, because the notes are physically in one shop, and two branches
    /// sharing one cash account would make "how much is in the drawer?" unanswerable.
    /// </summary>
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string? BankName { get; set; }

    /// <summary>The IBAN or account number. It is what a payment is reconciled against.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>
    /// May this account go below zero? A bank may grant an overdraft; a drawer may not. See
    /// <see cref="Validate"/> — a cash account that allowed it would be a promise to hand over notes that
    /// are not there.
    /// </summary>
    public bool AllowsOverdraft { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public ICollection<AccountTransaction> Transactions { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new DomainException("An account needs a name.");
        }

        if (Kind == FinancialAccountKind.Cash && AllowsOverdraft)
        {
            // An overdrawn till is not a debt, it is a counting error. The bank can lend the shop money;
            // the cash drawer cannot, and a system that let it go negative would be reporting notes that
            // nobody could physically produce.
            throw new DomainException(
                "A cash account cannot be overdrawn — there is no such thing as negative notes in a "
                + "drawer. Only a bank account can carry an overdraft.");
        }
    }
}
