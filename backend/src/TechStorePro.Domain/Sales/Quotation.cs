using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

public enum QuotationStatus : short
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Rejected = 4,

    /// <summary>Past its validity date. Set by the caller, not inferred, so the reason is in the record.</summary>
    Expired = 5,

    /// <summary>Turned into a sales order. Terminal — a quotation cannot be converted twice.</summary>
    Converted = 6
}

/// <summary>
/// A price, promised for a while (requirements §22).
///
/// <b>It does not touch stock and it does not reserve any.</b> A quotation is an offer, not a claim on
/// the shelf: quoting ten laptops the shop does not have is legitimate, and holding stock for every
/// speculative quote would empty the warehouse on paper while it sat full. Stock is promised at
/// <see cref="SalesOrder"/> confirmation, which is where the customer commits.
/// </summary>
public class Quotation : TenantEntity
{
    public string Number { get; set; } = null!;

    /// <summary>Null for a walk-in enquiry — someone who wants a price but is not on the books yet.</summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    /// <summary>Always the company's base currency — requirements §45 <b>D8</b>.</summary>
    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset QuotedAt { get; set; }

    /// <summary>
    /// After this, the price is no longer promised. Null means the quote does not expire, which is a
    /// choice the shop can make and live with.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; set; }

    public string? Notes { get; set; }

    public ICollection<QuotationLine> Lines { get; set; } = [];

    public decimal NetTotal => Lines.Sum(l => l.NetTotal);
    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);
    public decimal Total => NetTotal + TaxTotal;

    public bool IsExpiredAt(DateTimeOffset at) => ValidUntil is { } until && until <= at;

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A quotation with no lines quotes nothing.");
        }

        if (ValidUntil is { } until && until <= QuotedAt)
        {
            throw new DomainException("A quotation cannot expire before it was raised.");
        }
    }

    public void Send()
    {
        if (Status != QuotationStatus.Draft)
        {
            throw new DomainException($"A quotation that is {Status} cannot be sent.");
        }

        Validate();
        Status = QuotationStatus.Sent;
    }

    public void Accept(DateTimeOffset at)
    {
        if (Status is not (QuotationStatus.Draft or QuotationStatus.Sent))
        {
            throw new DomainException($"A quotation that is {Status} cannot be accepted.");
        }

        if (IsExpiredAt(at))
        {
            // The price was promised until a date, and that date has passed. Accepting it now would
            // honour a price the shop withdrew — possibly below today's cost.
            throw new DomainException(
                "This quotation has expired. Re-quote it at today's prices rather than accepting a "
                + "price the shop is no longer offering.");
        }

        Status = QuotationStatus.Accepted;
    }

    public void Reject()
    {
        if (Status is QuotationStatus.Converted)
        {
            throw new DomainException("A quotation that became an order cannot be rejected.");
        }

        Status = QuotationStatus.Rejected;
    }

    /// <summary>Called when a sales order is raised from this quotation.</summary>
    public void MarkConverted()
    {
        if (Status == QuotationStatus.Converted)
        {
            throw new DomainException(
                "This quotation has already become an order. Converting it again would sell the same "
                + "goods twice at the same promised price.");
        }

        if (Status is QuotationStatus.Rejected or QuotationStatus.Expired)
        {
            throw new DomainException($"A quotation that is {Status} cannot become an order.");
        }

        Status = QuotationStatus.Converted;
    }
}

public class QuotationLine : TenantEntity
{
    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// The rate as it stood when the document was raised — <b>not</b> a foreign key to
    /// <see cref="TaxRate"/>. Changing the rate next year must not restate a quote given this year.
    /// </summary>
    public decimal TaxPercent { get; set; }

    /// <summary>
    /// Which price list this price came from, as <c>IPriceResolver</c> reported it. Stored so the shop
    /// can answer "why is this customer being charged that?" a month later without replaying the
    /// pricing rules as they stand today.
    /// </summary>
    public string? PriceSource { get; set; }

    public decimal NetTotal => SalesMath.Net(Quantity, UnitPrice, DiscountPercent, DiscountAmount);
    public decimal TaxAmount => SalesMath.Tax(NetTotal, TaxPercent);
    public decimal LineTotal => NetTotal + TaxAmount;
}
