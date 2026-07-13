using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Purchasing;

/// <summary>
/// Money paid to a supplier (requirements §25).
///
/// <b>A payment is a header plus allocations, not a column on an invoice.</b> One transfer settles three
/// invoices; one invoice is settled by two instalments. A single <c>invoice_id</c> on a payment cannot
/// express either, and a shop that pays its supplier monthly does both constantly.
/// </summary>
public class SupplierPayment : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;

    /// <summary>The cheque number, the transfer reference. Without it a bank statement cannot be reconciled.</summary>
    public string? Reference { get; set; }

    /// <summary>What actually left the bank, in the currency it left in.</summary>
    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The rate on the day the money moved — which is <em>not</em> the rate on the invoice. The gap
    /// between the two is the whole of <see cref="SupplierPaymentAllocation.ExchangeGainOrLoss"/>.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public DateTimeOffset PaidAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = [];

    /// <summary>What left the bank, in the company's own money.</summary>
    public decimal AmountBase => Amount * ExchangeRate;

    public decimal AllocatedAmount => Allocations.Sum(a => a.Amount);

    /// <summary>
    /// Paid but not yet pointed at an invoice — an advance to the supplier, or a payment made before
    /// the invoice arrived. A real state, not an error: it becomes a credit the supplier owes back.
    /// </summary>
    public decimal UnallocatedAmount => Amount - AllocatedAmount;

    /// <summary>
    /// The FX gain or loss the whole payment realised. Positive is a gain — the dirham strengthened
    /// between the invoice and the payment, so the debt cost less than it was booked at.
    /// </summary>
    public decimal ExchangeGainOrLoss => Allocations.Sum(a => a.ExchangeGainOrLoss);

    public void Validate()
    {
        if (Amount <= 0)
        {
            throw new DomainException("A payment of nothing is not a payment.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }

        if (AllocatedAmount > Amount)
        {
            // Otherwise the shop would appear to have settled more debt than it actually paid, and the
            // supplier's balance would drift quietly in the shop's favour.
            throw new DomainException(
                $"This payment allocates {AllocatedAmount} but only {Amount} was paid.");
        }

        if (Allocations.Any(a => a.Amount <= 0))
        {
            throw new DomainException("An allocation of nothing allocates nothing.");
        }
    }
}

/// <summary>
/// A slice of one payment applied to one invoice.
///
/// This is also where a foreign-currency purchase finally settles up with reality. The invoice fixed
/// the debt in base currency at the invoice-date rate; the money left the bank at the payment-date rate;
/// and the difference is a genuine gain or loss that the business made by owing money in a currency it
/// does not hold (requirements §26).
/// </summary>
public class SupplierPaymentAllocation : TenantEntity
{
    public Guid SupplierPaymentId { get; set; }
    public SupplierPayment SupplierPayment { get; set; } = null!;

    public Guid SupplierInvoiceId { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;

    /// <summary>How much of the invoice this settles, in the invoice's currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// The rate this slice of debt was <em>booked</em> at, copied from the invoice when the allocation is
    /// made.
    ///
    /// <b>Snapshotted, not looked up later.</b> The invoice's rate is a fact about the day it was
    /// raised; re-reading it years afterwards would give the same answer only until somebody corrected
    /// a historical FX rate, at which point every past gain and loss in the system would silently
    /// change. General Rule 3: history does not move.
    /// </summary>
    public decimal InvoiceExchangeRate { get; set; } = 1m;

    /// <summary>The rate the money actually went out at.</summary>
    public decimal PaymentExchangeRate { get; set; } = 1m;

    /// <summary>
    /// The realised FX gain (positive) or loss (negative), in base currency.
    ///
    /// Worked through: a USD 1,000 invoice booked at 3.67 is a debt of AED 3,670. Pay it when the rate
    /// is 3.60 and only AED 3,600 leaves the bank — the company is AED 70 better off, and that 70 is a
    /// gain it made by owing dollars rather than by selling anything. It belongs in the P&amp;L, not in
    /// the cost of the stock, which is why it is <b>not</b> folded back into the moving average: the
    /// laptops did not become cheaper to buy, the currency moved.
    /// </summary>
    public decimal ExchangeGainOrLoss => Amount * (InvoiceExchangeRate - PaymentExchangeRate);
}
