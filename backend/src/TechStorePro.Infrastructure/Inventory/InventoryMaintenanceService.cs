using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TechStorePro.Infrastructure.Inventory;

public class InventoryMaintenanceOptions
{
    public const string Section = "Inventory:Maintenance";

    /// <summary>Turn the whole job off. Left on by default: an unswept reservation holds stock off the
    /// shelf forever, and an unproven balance cache is exactly the failure the module exists to catch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often expired reservations are swept. Fifteen minutes, not nightly: a quote that expired at
    /// nine in the morning must not keep the last unit off the shelf until two the following night.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The hour (UTC) the balance audit runs. Two in the morning — it reads the whole ledger, and it
    /// should not do that while the shop is trading.
    /// </summary>
    public int ReconcileAtUtcHour { get; set; } = 2;
}

/// <summary>
/// The two pieces of inventory housekeeping that nothing else can do, run per company, forever.
///
/// <list type="number">
/// <item><b>Sweep expired reservations.</b> A quote that reserved the last unit and was then forgotten
///   keeps that unit off the shelf until someone notices. Nobody notices. Requirements §20 asks for
///   reservations to be released; this is what releases the ones nobody released.</item>
/// <item><b>Prove the balance cache against the ledger.</b> <c>stock_balances</c> is a cache of
///   <c>stock_movements</c>, written in the same transaction under a row lock — but "written correctly"
///   is a claim, and a claim that is never checked is a claim that quietly stops being true. This
///   recomputes from the ledger and shouts if the two disagree, in quantity <em>or</em> in cost
///   (development-plan.md P3: "must agree to the cent").</item>
/// </list>
///
/// <para><b>It runs per company, in a scope pinned to that company, and never cross-tenant.</b> A
/// background scope has no token, so the tenant would resolve to null — which switches the DbContext
/// query filters off entirely. Reading every company's rows at once would be bad enough; releasing one
/// company's reservation through another company's ledger would be a tenancy breach committed by our
/// own maintenance job. Hence <see cref="ITenantSetter"/>, and hence the per-company loop.</para>
///
/// <para>A company that throws does not stop the others: the sweep is a housekeeping pass, and one
/// tenant's bad data must not leave every other tenant's stock reserved.</para>
/// </summary>
public class InventoryMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<InventoryMaintenanceOptions> _options;
    private readonly ILogger<InventoryMaintenanceService> _logger;

    private DateOnly _lastReconciledOn = DateOnly.MinValue;

    public InventoryMaintenanceService(
        IServiceScopeFactory scopes,
        IOptions<InventoryMaintenanceOptions> options,
        ILogger<InventoryMaintenanceService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;

        if (!options.Enabled)
        {
            _logger.LogWarning(
                "Inventory maintenance is disabled. Expired reservations will hold stock off the shelf "
                + "indefinitely and the stock balance cache will not be proven against the ledger.");
            return;
        }

        using var timer = new PeriodicTimer(options.SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Tick first, work second: waiting one interval before the first pass keeps the job off the
            // critical path of start-up, where migrations may still be running.
            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A BackgroundService that throws out of ExecuteAsync stops silently for the lifetime of
                // the process. Stock would then quietly stop being reconciled and nothing would say so.
                _logger.LogError(ex, "Inventory maintenance pass failed. It will run again next tick.");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var companies = await CompaniesAsync(cancellationToken);

        var now = Now();
        var reconcileDue =
            _lastReconciledOn < DateOnly.FromDateTime(now.UtcDateTime)
            && now.Hour >= _options.Value.ReconcileAtUtcHour;

        foreach (var companyId in companies)
        {
            await ForCompanyAsync(companyId, "reservation sweep", ExpireReservationsAsync, cancellationToken);

            if (reconcileDue)
            {
                await ForCompanyAsync(companyId, "balance audit", ReconcileAsync, cancellationToken);
            }
        }

        if (reconcileDue)
        {
            _lastReconciledOn = DateOnly.FromDateTime(now.UtcDateTime);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a scope pinned to one company, and swallows its failure so the
    /// next company still runs. The failure is logged, not hidden.
    /// </summary>
    private async Task ForCompanyAsync(
        Guid companyId,
        string what,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();

            scope.ServiceProvider.GetRequiredService<ITenantSetter>().UseCompany(companyId);

            await work(scope.ServiceProvider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inventory {What} failed for company {CompanyId}.", what, companyId);
        }
    }

    private async Task ExpireReservationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IApplicationDbContext>();
        var ledger = services.GetRequiredService<IStockLedger>();
        var clock = services.GetRequiredService<IDateTime>();

        var now = clock.UtcNow;

        var due = await db.StockReservations
            .Where(r => r.Status == ReservationStatus.Active
                && r.ExpiresAt != null
                && r.ExpiresAt <= now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        // One transaction for the batch: releasing a reservation takes the balance row's lock, and the
        // ledger refuses to run outside a transaction precisely so that a half-released sweep cannot
        // leave reserved_quantity pointing at stock nobody is holding.
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        foreach (var id in due)
        {
            await ledger.ReleaseAsync(id, expired: true, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Released {Count} expired stock reservation(s); the stock they were holding is sellable again.",
            due.Count);
    }

    private async Task ReconcileAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var audit = await services.GetRequiredService<IBalanceAuditor>().AuditAsync(cancellationToken);

        if (audit.Agrees)
        {
            _logger.LogInformation(
                "Stock balance audit passed: {Count} balance(s) agree with the ledger.",
                audit.BalancesChecked);
            return;
        }

        // Error, not warning. A balance that disagrees with the ledger means something wrote stock
        // outside IStockLedger — the one thing the module is built to make impossible. Every sale,
        // valuation and reorder report downstream of that row is now wrong.
        _logger.LogError(
            "Stock balance audit FAILED: {Bad} of {Count} balance(s) disagree with the ledger. "
            + "Something has written stock outside IStockLedger.",
            audit.Discrepancies.Count,
            audit.BalancesChecked);

        foreach (var d in audit.Discrepancies)
        {
            _logger.LogError(
                "{Kind}: {Product} in {Warehouse} — cached {CachedQty} @ {CachedCost}, "
                + "ledger {LedgerQty} @ {LedgerCost} (value off by {ValueDifference}).",
                d.Kind,
                d.ProductName,
                d.WarehouseName,
                d.CachedQuantity,
                d.CachedAverageCost,
                d.LedgerQuantity,
                d.LedgerAverageCost,
                d.ValueDifference);
        }
    }

    /// <summary>
    /// Every company, read with no tenant — the one place that is legitimate, because the job's whole
    /// purpose is to visit all of them. Everything it then <em>does</em> happens in a scope pinned to
    /// exactly one.
    /// </summary>
    private async Task<List<Guid>> CompaniesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.Companies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    private DateTimeOffset Now()
    {
        using var scope = _scopes.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IDateTime>().UtcNow;
    }
}
