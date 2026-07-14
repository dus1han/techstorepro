namespace TechStorePro.Application.Sales.Services;

/// <summary>
/// Decides whether a line discounted below its floor may be sold, and by whom (requirements §32).
///
/// <b>Approval is a permission, not a workflow.</b> There is no "pending approval" queue, and that is a
/// deliberate choice: a customer is standing at the counter. A sale that parked itself in a queue until a
/// manager logged in would not be a control — it would be a lost sale, and the shop would work around it
/// by giving every salesperson the permission, which is the same as having no control at all.
///
/// So the manager is called over and they authorise it <em>in the moment</em>, by being the one who has
/// <c>(Sales, Approve)</c>. Who they were is stamped onto the line
/// (<c>SalesInvoiceLine.DiscountApprovedBy</c>), which is the question anyone actually asks later: not
/// "was this approved?" but "who approved this?".
/// </summary>
public interface IDiscountAuthorizer
{
    /// <summary>
    /// Returns the id of whoever authorised the below-floor price, or throws if the caller may not.
    /// </summary>
    /// <param name="approvedBy">
    /// The manager who authorised it, when that is not the person operating the till. Null means the
    /// caller is authorising it themselves, and they had better hold the permission.
    /// </param>
    Task<Guid?> AuthoriseAsync(
        string description,
        decimal floor,
        Guid? approvedBy = null,
        CancellationToken cancellationToken = default);
}
