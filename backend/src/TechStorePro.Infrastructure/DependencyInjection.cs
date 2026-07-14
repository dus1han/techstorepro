using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Application.Inventory.Barcodes;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Repairs.Services;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Inventory;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Identity;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using TechStorePro.Infrastructure.Repairs;
using TechStorePro.Infrastructure.Sales;
using TechStorePro.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TechStorePro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro");

                // EnableRetryOnFailure is deliberately NOT set. A retrying execution strategy
                // refuses user-initiated transactions — and this system depends on them: document
                // numbering holds a SELECT … FOR UPDATE across statements, and the stock ledger will
                // do the same. Retrying a transaction the strategy cannot see would also replay half
                // a stock movement, which is worse than a transient failure surfacing to the caller.
                //
                // If transient-fault retry is wanted later, it must be reintroduced by wrapping each
                // operation in an IExecutionStrategy.ExecuteAsync delegate, not by flipping this on.
            }));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ApplicationDbContextAccessor>();
        services.AddSingleton<IDateTime, SystemDateTime>();

        // Permission grants are cached per (user, company) for a couple of minutes; see
        // PermissionService for why they are not in the token.
        services.AddMemoryCache();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthSessionFactory, AuthSessionFactory>();

        services.AddScoped<ISettingsProvider, SettingsProvider>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();

        // P2. Sales calls this and snapshots the result onto the document line.
        services.AddScoped<IPriceResolver, PriceResolver>();

        // P5. Tax is resolved, never assumed: no jurisdiction and no rate is hardcoded anywhere in this
        // codebase (§45 D7). A company that configures no tax rate legitimately sells at zero.
        services.AddScoped<ITaxResolver, TaxResolver>();

        // One place where price, tax and the discount floor meet. A quotation, an order and an invoice
        // must not price the same line three different ways.
        services.AddScoped<ISalesLinePricer, SalesLinePricer>();

        // Selling below the price list's floor needs (Sales, Approve) — §32's manager approval, expressed
        // as a permission rather than a queue. There is a customer standing at the counter.
        services.AddScoped<IDiscountAuthorizer, DiscountAuthorizer>();

        // P3 — the stock ledger. Registered once, injected everywhere stock moves, and the only thing
        // in the system permitted to write stock_movements, stock_balances or a serial's status
        // (architecture.md §4.5).
        //
        // Weighted average is the only costing strategy shipped (§45 D1). The interface exists so FIFO
        // stays a second implementation rather than a rewrite; nothing else is planned.
        services.AddSingleton<ICostingStrategy, WeightedAverageCosting>();
        services.AddScoped<IStockLedger, StockLedger>();
        services.AddSingleton<ILabelRenderer, LabelRenderer>();

        // The cache must be able to prove itself. One implementation, shared by the /balance-audit
        // endpoint and the nightly job — a job with its own copy of the arithmetic could pass while
        // the endpoint failed, and "the nightly job is green" would then mean nothing.
        services.AddScoped<IBalanceAuditor, BalanceAuditor>();

        // P6 — the warranty question, answered at the workshop door: is this repair free, and who is
        // paying? It reads two sources (the shop's own warranty, derived by P5 at the moment of sale; and a
        // manufacturer's or supplier's, registered by hand), because the system knows about the two in
        // entirely different ways. See IWarrantyLookup.
        services.AddScoped<IWarrantyLookup, WarrantyLookup>();

        // Sweeps expired reservations and proves the balances against the ledger, per company, forever.
        // Without it, requirements §20's "release reservation" has no answer for the reservations that
        // nobody releases, and the ledger-vs-cache guarantee is asserted but never checked.
        services.Configure<InventoryMaintenanceOptions>(
            configuration.GetSection(InventoryMaintenanceOptions.Section));
        services.AddHostedService<InventoryMaintenanceService>();

        // QuestPDF's Community licence: free for companies below the revenue threshold, which every
        // shop this product targets is. It must be set before the first document is rendered, and the
        // library throws a licence exception rather than watermarking if it is not.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        services.AddScoped<ReferenceDataSeeder>();

        return services;
    }
}
