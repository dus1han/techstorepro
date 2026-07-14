using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Finance;

/// <summary>
/// What kind of expense this is — rent, transport, the customs broker's fee (requirements §34).
///
/// Reference data the shop owns, not an enum, because §34's list ends in "other expenses" and a fixed
/// enum would make every shop's fifth category "Other" for ever. A shop that wants "Utilities" adds it.
/// </summary>
public class ExpenseCategory : TenantEntity
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new DomainException("An expense category needs a name.");
        }
    }
}

public enum ExpenseStatus : short
{
    Recorded = 1,
    Cancelled = 2
}

/// <summary>
/// Money the shop spent that bought no stock (requirements §34) — the rent, the courier, the clearing
/// agent's fee, the electricity.
///
/// <b>An expense is recorded and paid in one act.</b> There is no draft and no accrual, because there is
/// no general ledger to accrue into (§45 D3): the shop pays the rent and the money leaves the bank. So
/// recording one writes an outbound <see cref="AccountTransaction"/> against
/// <see cref="FinancialAccountId"/>, and an expense that named no account would be money that vanished.
///
/// <b>It is recorded in the currency of the account it is paid from.</b> You cannot spend dollars out of a
/// dirham account — the bank converts, and what left the dirham account is dirhams. Allowing the two to
/// differ would put a number on this document that disagrees with the money that actually moved, and there
/// would be no way to say which of the two was the truth.
///
/// <b>A mistake is cancelled, never edited.</b> The amount, the account and the date of a paid expense are
/// facts about money that has gone; changing them in place would silently restate a bank balance that has
/// already been reconciled. <see cref="Cancel"/> writes a reversing movement instead, and the wrong
/// expense stays visible next to the right one — which is what an auditor is looking for.
/// </summary>
public class Expense : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>The account the money left. Not nullable: an expense that paid out of nowhere is not one.</summary>
    public Guid FinancialAccountId { get; set; }
    public FinancialAccount FinancialAccount { get; set; } = null!;

    /// <summary>
    /// The landlord, the courier, the clearing agent — when they are somebody the shop already knows.
    /// Null for the ones it does not: a parking fee has no supplier.
    ///
    /// It does <b>not</b> put anything on the supplier's balance. This is money already paid, not a bill
    /// outstanding — a payable is a <c>SupplierInvoice</c>, and confusing the two would double-count the
    /// debt (once as an unpaid invoice, once as a paid expense).
    /// </summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string Description { get; set; } = null!;

    /// <summary>In <see cref="CurrencyCode"/>, which is the account's currency. Always positive.</summary>
    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>The account's currency into base, on the day of the expense. For reporting; see <see cref="AccountTransaction.ExchangeRate"/>.</summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>What it cost the shop in its own money. This is the figure the P&amp;L subtracts.</summary>
    public decimal AmountBase => Amount * ExchangeRate;

    public DateTimeOffset ExpenseDate { get; set; }

    /// <summary>The receipt or invoice number the shop was given. What the bank statement is matched on.</summary>
    public string? Reference { get; set; }

    public ExpenseStatus Status { get; set; } = ExpenseStatus.Recorded;

    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }

    public string? Notes { get; set; }

    public void Validate()
    {
        if (Amount <= 0)
        {
            // A negative expense would be income, and income does not arrive through this door.
            throw new DomainException("An expense of nothing is not an expense.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new DomainException("An expense must say what the money was spent on.");
        }
    }

    /// <summary>
    /// Undo an expense that should not have been recorded. The money comes back into the account it left,
    /// as a movement of its own — the original expense and its reversal both stay on the statement.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status == ExpenseStatus.Cancelled)
        {
            throw new DomainException($"Expense {Number} is already cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancelling an expense needs a reason — it is money.");
        }

        Status = ExpenseStatus.Cancelled;
        CancelledAt = at;
        CancelledReason = reason;
    }
}
