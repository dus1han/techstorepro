namespace TechStorePro.Application.Reports;

/// <summary>
/// How old a debt is, in the only terms a shop actually argues about: how long past its due date.
///
/// The buckets are days <em>overdue</em>, not days since the invoice. An invoice raised ninety days ago on
/// sixty-day terms is thirty days late, not ninety, and a report that called it ninety would have the shop
/// chasing a customer who is behaving exactly as agreed.
/// </summary>
public enum AgeingBucket : short
{
    /// <summary>Not yet due. Money the shop is owed but has no right to chase.</summary>
    Current = 1,

    Days1To30 = 2,
    Days31To60 = 3,
    Days61To90 = 4,
    Days90Plus = 5
}

public static class Ageing
{
    /// <summary>
    /// The date a document falls due. <b>Null means due on receipt, not "never due".</b>
    ///
    /// Both <c>SalesInvoice.DueAt</c> and <c>SupplierInvoice.DueAt</c> are nullable, and null is what a
    /// counter sale and a cash purchase carry — no terms were given, so the money was owed the moment the
    /// document was raised. Treating null as "no due date" would drop every walk-in sale out of every
    /// bucket, and the ageing would quietly under-report the debt by exactly the invoices most likely to
    /// go unpaid.
    /// </summary>
    public static DateTimeOffset DueDate(DateTimeOffset? dueAt, DateTimeOffset documentDate) =>
        dueAt ?? documentDate;

    /// <summary>Whole days late at <paramref name="asOf"/>. Zero or negative means not yet due.</summary>
    public static int DaysOverdue(DateTimeOffset dueAt, DateTimeOffset asOf) =>
        (asOf.UtcDateTime.Date - dueAt.UtcDateTime.Date).Days;

    public static AgeingBucket Bucket(DateTimeOffset dueAt, DateTimeOffset asOf) =>
        DaysOverdue(dueAt, asOf) switch
        {
            <= 0 => AgeingBucket.Current,
            <= 30 => AgeingBucket.Days1To30,
            <= 60 => AgeingBucket.Days31To60,
            <= 90 => AgeingBucket.Days61To90,
            _ => AgeingBucket.Days90Plus
        };
}

/// <summary>
/// The five columns of an ageing report, and the arithmetic that fills them.
///
/// <b>Only positive debt is bucketed.</b> A document can end up with a negative balance — an invoice
/// credited for more than was left owing on it, say — and that is not an aged debt at all; it is money the
/// shop owes back. Bucketing it would net a credit against a genuine ninety-day debt and make the oldest,
/// most dangerous column look smaller than it is. It belongs in <c>Credits</c>, which is where the caller
/// puts it.
/// </summary>
public sealed class AgeingColumns
{
    public decimal Current { get; private set; }
    public decimal Days1To30 { get; private set; }
    public decimal Days31To60 { get; private set; }
    public decimal Days61To90 { get; private set; }
    public decimal Days90Plus { get; private set; }

    public decimal TotalDue => Current + Days1To30 + Days31To60 + Days61To90 + Days90Plus;

    public void Add(AgeingBucket bucket, decimal amount)
    {
        switch (bucket)
        {
            case AgeingBucket.Current: Current += amount; break;
            case AgeingBucket.Days1To30: Days1To30 += amount; break;
            case AgeingBucket.Days31To60: Days31To60 += amount; break;
            case AgeingBucket.Days61To90: Days61To90 += amount; break;
            case AgeingBucket.Days90Plus: Days90Plus += amount; break;
        }
    }

    public void Add(AgeingColumns other)
    {
        Current += other.Current;
        Days1To30 += other.Days1To30;
        Days31To60 += other.Days31To60;
        Days61To90 += other.Days61To90;
        Days90Plus += other.Days90Plus;
    }
}
