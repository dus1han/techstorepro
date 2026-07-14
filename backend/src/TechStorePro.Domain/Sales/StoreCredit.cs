using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Sales;

/// <summary>
/// One movement of store credit — issued when a return is settled that way, redeemed when the customer
/// spends it (requirements §24, "store credit / future usage").
///
/// <b>It is a ledger, not a number on the customer.</b> A single <c>store_credit_balance</c> column would
/// be a figure nobody could explain: "why do I have 240 credit?" has an answer only if every issue and
/// every redemption is a row. The balance is the sum of these entries — the same reasoning that makes
/// <c>stock_balances</c> a cache of <c>stock_movements</c> and not the truth itself.
/// </summary>
public class StoreCreditEntry : TenantEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>The return that issued this credit. Null for a redemption, or a goodwill credit.</summary>
    public Guid? CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    /// <summary>The payment that spent it. Null when this entry is an issue rather than a redemption.</summary>
    public Guid? CustomerPaymentId { get; set; }
    public CustomerPayment? CustomerPayment { get; set; }

    /// <summary>
    /// Positive when credit is issued, negative when it is spent. Signed rather than split into two
    /// columns so that the balance is a SUM and cannot be got wrong by adding the wrong pair.
    /// </summary>
    public decimal Amount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Reason { get; set; } = null!;

    public void Validate()
    {
        if (Amount == 0)
        {
            throw new DomainException("A store credit entry of nothing moves nothing.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            throw new DomainException("A store credit entry needs a reason — it is money.");
        }
    }
}
