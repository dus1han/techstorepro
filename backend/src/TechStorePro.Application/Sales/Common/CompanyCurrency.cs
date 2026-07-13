using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Common;

/// <summary>
/// Sales are raised in the company's base currency, and only in it (requirements §45 <b>D8</b>).
///
/// This is enforced in one place rather than validated per command, because the rule is not about the
/// shape of a request — it is about what the business has decided it will do. Purchases may be in any
/// currency (an overseas supplier invoices in USD, and P4 books the FX gain when the payment goes out at
/// a different rate); <b>sales may not</b>. Invoicing a customer in a foreign currency creates an FX
/// exposure on the receivable, and the module that measures it does not exist. Accepting the currency
/// while quietly ignoring the exposure would be the worst of both.
///
/// When the business decides otherwise, this is the one guard to remove — and the compiler will list
/// every document it protected.
/// </summary>
public static class CompanyCurrency
{
    public static async Task<string> ResolveAsync(
        IApplicationDbContext db,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId
            ?? throw new DomainException("A sales document cannot be raised without a company.");

        return await db.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.BaseCurrency)
            .FirstAsync(cancellationToken);
    }

    /// <summary>Returns the base currency, having checked the request did not ask for another one.</summary>
    public static async Task<string> EnsureAsync(
        IApplicationDbContext db,
        ITenantContext tenant,
        string? requested,
        CancellationToken cancellationToken)
    {
        var baseCurrency = await ResolveAsync(db, tenant, cancellationToken);

        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Sales are raised in {baseCurrency}, and this document asks for {requested.ToUpperInvariant()}. "
                + "Invoicing a customer in a foreign currency would create an exchange exposure on the "
                + "receivable that this system does not yet measure (§45 D8).");
        }

        return baseCurrency;
    }
}
