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
    Custom = 6
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

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);
}
