using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Catalog;

/// <summary>
/// ISO 4217 reference data. Deliberately <b>not</b> tenant-scoped: the dirham is the dirham for
/// everyone, and a per-company copy of the currency list would be a hundred rows saying the
/// same thing.
/// </summary>
public class Currency
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Symbol { get; set; }

    /// <summary>
    /// Minor units. Two for most currencies, zero for the yen — and getting this wrong means
    /// rounding a ¥1,000 sale to ¥1,000.00 and then to ¥10.00 somewhere downstream.
    /// </summary>
    public int DecimalPlaces { get; set; } = 2;
}

/// <summary>
/// The rate from a foreign currency into the company's base currency, on a given date.
///
/// Rates are per-company and per-day, and a transaction stores the rate it used. Re-reading "today's
/// rate" when reprinting last March's purchase invoice would silently restate its cost — the same
/// trap as tax rates, and the same answer: snapshot it.
/// </summary>
public class FxRate : TenantEntity
{
    public string CurrencyCode { get; set; } = null!;
    public Currency? Currency { get; set; }

    /// <summary>Multiply a foreign amount by this to get the base-currency amount.</summary>
    public decimal RateToBase { get; set; }

    /// <summary>The date the rate applies to. One rate per currency per day.</summary>
    public DateOnly RateDate { get; set; }

    public void Validate()
    {
        if (RateToBase <= 0)
        {
            throw new DomainException("An FX rate must be greater than zero.");
        }
    }

    /// <summary>Converts a foreign amount into the base currency, rounded to 4 places.</summary>
    public decimal ToBase(decimal foreignAmount) => Math.Round(foreignAmount * RateToBase, 4);
}

/// <summary>How money moves (requirements §23). Configurable, and effective-dated like every rule.</summary>
public enum PaymentMethodKind : short
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Cheque = 4,
    Online = 5,
    Custom = 6,

    /// <summary>
    /// Credit the customer already holds, from a return settled that way (requirements §24).
    ///
    /// It is a payment method rather than a discount because that is what it is: the shop has already had
    /// the money. Tendering it draws down <c>store_credit_entries</c>, and a customer cannot spend credit
    /// they do not have — checked when the payment is taken.
    /// </summary>
    StoreCredit = 7
}

public class PaymentMethod : TenantEntity
{
    public string Name { get; set; } = null!;

    public PaymentMethodKind Kind { get; set; } = PaymentMethodKind.Cash;

    /// <summary>
    /// Does this method need a reference (a cheque number, a transaction id)? A bank transfer with
    /// no reference cannot be reconciled against the statement, so the rule is per-method rather
    /// than a blanket requirement that would annoy every cash sale.
    /// </summary>
    public bool RequiresReference { get; set; }

    /// <summary>
    /// Where money tendered this way lands (P7): "Cash" goes into the till, "Bank transfer" into the bank
    /// account. Without it, a payment would be money that arrived nowhere, and the shop's cash position
    /// would be short by every sale it ever took.
    ///
    /// <b>Nullable, and null means two entirely different things depending on the kind.</b> For
    /// <see cref="PaymentMethodKind.StoreCredit"/> it is <em>correct</em> and required to be null: no money
    /// moves when a customer spends credit they already hold — the shop had that money when the goods came
    /// back, and it is sitting in the till already. An account transaction there would invent cash that is
    /// not in the drawer. For every other kind, null means the method is <em>not yet configured</em>, and a
    /// payment tendered through it is refused rather than silently losing the money (see
    /// <c>RecordPaymentCommand</c>). It is nullable rather than required because the column had to land on
    /// a table that already had rows in it, and a shop's own tender that no account can hold is a
    /// contradiction the shop has to resolve, not one a migration can guess at.
    /// </summary>
    public Guid? FinancialAccountId { get; set; }

    /// <summary>True when money actually moves — everything except store credit.</summary>
    public bool MovesMoney => Kind != PaymentMethodKind.StoreCredit;

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);

    public void Validate()
    {
        if (!MovesMoney && FinancialAccountId is not null)
        {
            // Store credit is money the shop already has. Pointing it at a cash account would add the
            // amount to the drawer a second time, every time a customer spent a voucher — and the till
            // would come up over by exactly the credit the shop had issued.
            throw new DomainException(
                "Store credit is not money moving: the shop already holds it. It cannot pay into an "
                + "account.");
        }
    }
}
