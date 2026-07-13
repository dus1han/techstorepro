namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// Binds a scope to a company when there is no request to read one from.
///
/// <b>This exists for background work and nothing else.</b> A nightly job has no token, so
/// <see cref="ITenantContext"/> would resolve to null — and a null tenant switches the DbContext query
/// filters off entirely, which is right for a migration and catastrophic for a job that writes. The
/// reconciler would then sweep every company's reservations through one company's ledger.
///
/// So a job creates a scope <em>per company</em> and names the company explicitly. It cannot be reached
/// from a request path: <see cref="TechStorePro.Application.Common.Interfaces.ITenantContext"/> is what
/// handlers inject, and the HTTP implementation of it takes the company from the token claim only —
/// there is no code path where an incoming request can call this and change which company it is.
/// </summary>
public interface ITenantSetter
{
    /// <summary>
    /// Pins this scope to <paramref name="companyId"/>. Throws if the scope already has a company from
    /// a token: silently overriding a request's tenant is the one thing this must never enable.
    /// </summary>
    void UseCompany(Guid companyId);
}
