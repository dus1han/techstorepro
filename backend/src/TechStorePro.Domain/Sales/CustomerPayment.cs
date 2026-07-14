using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

/// <summary>
/// Money received from a customer (requirements §23).
///
/// <b>A payment is a header, its method lines, and its allocations — three tables, and every one of them
/// is load-bearing.</b>
///
/// <list type="bullet">
/// <item><b>Method lines</b> exist because one sale is settled by cash <em>and</em> card: the customer
///   pays 500 in notes and puts the rest on a card, and requirements §23 asks for exactly that. A single
///   <c>payment_method_id</c> on the header cannot express it, and the workaround — two payments — would
///   make one sale look like two.</item>
/// <item><b>Allocations</b> exist because one payment settles three invoices, and one invoice is settled
///   by two instalments. A single <c>invoice_id</c> on the payment cannot express either.</item>
/// </list>
///
/// The money is always in the company's base currency (§45 D8), so there is no exchange rate here and no
/// FX gain or loss — unlike <c>SupplierPayment</c>, where the shop genuinely owes dollars.
/// </summary>
public class CustomerPayment : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>The card slip number, the transfer reference. Without it, a bank statement cannot be reconciled.</summary>
    public string? Reference { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset PaidAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<CustomerPaymentMethod> Methods { get; set; } = [];
    public ICollection<CustomerPaymentAllocation> Allocations { get; set; } = [];

    /// <summary>
    /// What was actually received — the sum of the tender, not a number typed on the header. There is no
    /// settable total here on purpose: a header amount that disagreed with its method lines would be a
    /// till that does not balance, and nobody could say which figure was the truth.
    /// </summary>
    public decimal Amount => Methods.Sum(m => m.Amount);

    public decimal AllocatedAmount => Allocations.Sum(a => a.Amount);

    /// <summary>
    /// Received but not yet pointed at an invoice — a deposit, or money taken before the invoice was
    /// raised. A real state, not an error: it is a credit the shop owes the customer, and it is what
    /// makes their balance go negative.
    /// </summary>
    public decimal UnallocatedAmount => Amount - AllocatedAmount;

    public void Validate()
    {
        if (Methods.Count == 0)
        {
            throw new DomainException("A payment with no tender is not a payment.");
        }

        if (Amount <= 0)
        {
            throw new DomainException("A payment of nothing is not a payment.");
        }

        if (Methods.Any(m => m.Amount <= 0))
        {
            throw new DomainException("A tender line of nothing tenders nothing.");
        }

        if (Allocations.Any(a => a.Amount <= 0))
        {
            throw new DomainException("An allocation of nothing allocates nothing.");
        }

        if (AllocatedAmount > Amount)
        {
            // Otherwise the shop would appear to have collected more debt than money it actually holds,
            // and the customer's balance would drift quietly in the customer's favour.
            throw new DomainException(
                $"This payment allocates {AllocatedAmount:0.##} but only {Amount:0.##} was received.");
        }
    }
}

/// <summary>
/// One way the customer paid. Cash and card on the same sale are two of these, on one payment.
/// </summary>
public class CustomerPaymentMethod : TenantEntity
{
    public Guid CustomerPaymentId { get; set; }
    public CustomerPayment CustomerPayment { get; set; } = null!;

    public Guid PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>Required when the method says so — <c>PaymentMethod.RequiresReference</c>.</summary>
    public string? Reference { get; set; }
}

/// <summary>
/// A slice of one payment applied to one invoice. This is what marks an invoice paid, and what makes
/// "which invoices did this bank transfer settle?" a question with an answer.
/// </summary>
public class CustomerPaymentAllocation : TenantEntity
{
    public Guid CustomerPaymentId { get; set; }
    public CustomerPayment CustomerPayment { get; set; } = null!;

    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public decimal Amount { get; set; }
}
