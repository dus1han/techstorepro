using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Configuration;

/// <summary>
/// One state-changing request the API has already seen (api-design.md §5).
///
/// <b>This is what stops a double-clicked "Take payment" taking the money twice.</b> The caller sends an
/// <c>Idempotency-Key</c>; the first request to arrive with that key claims it, does the work, and stores
/// what it answered. A repeat — the impatient second click, the mobile client retrying a request whose
/// response was lost on a flaky connection — gets the <em>stored</em> answer back rather than executing
/// again.
///
/// The key is claimed <b>before</b> the work runs, not after, and the unique index on (company, key) is
/// what makes the claim atomic. Writing the record afterwards would leave the window this exists to close
/// wide open: two clicks 50ms apart would both find no record, and both would sell the laptop.
///
/// Scoped to the company like everything else — two shops may coincidentally generate the same key, and
/// one must not be handed the other's response.
/// </summary>
/// <remarks>
/// <b>Not a <see cref="TenantEntity"/>, and that is load-bearing.</b> Everything else in the system is
/// soft-deleted, because sales, stock and repair history must stay auditable. This must not be: releasing
/// a key after a failed request has to <em>actually remove the row</em>, and a soft delete would leave it
/// in the table where the unique index still sees it — so the caller could never retry with that key, and
/// the very trap this class exists to close would be replaced by a worse one. It is tenant-scoped, and
/// nothing more.
/// </remarks>
public class IdempotencyRecord : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public string Key { get; set; } = null!;

    /// <summary>e.g. <c>POST /api/v1/customer-payments</c>. Part of what "the same request" means.</summary>
    public string Endpoint { get; set; } = null!;

    /// <summary>
    /// A hash of the request body. The same key with a <em>different</em> body is not a retry — it is a
    /// bug in the caller or an attempt to reuse a key, and replaying the first response would hide it.
    /// </summary>
    public string RequestHash { get; set; } = null!;

    /// <summary>Null until the request finishes. A record with no status is a request still in flight.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The response body, verbatim, so a replay is indistinguishable from the original.</summary>
    public string? ResponseBody { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsComplete => StatusCode is not null;
}
