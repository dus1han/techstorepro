using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Application.Inventory.Barcodes;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Inventory;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Identity;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
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

        // P2. Sales (P5) will call this and snapshot the result onto the invoice line.
        services.AddScoped<IPriceResolver, PriceResolver>();

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
