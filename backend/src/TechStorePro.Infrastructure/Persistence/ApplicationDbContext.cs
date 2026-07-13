using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Auditing;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TechStorePro.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the whole system.
///
/// Multi-tenancy is enforced here rather than in each query: every entity implementing
/// <see cref="ITenantScoped"/> gets a global query filter pinned to the current company,
/// and gets its CompanyId stamped on insert. Feature code therefore cannot accidentally
/// read or write another company's rows.
///
/// The audit trail is written here too, from the change tracker, for the same reason: a handler
/// that forgets to audit is a handler that silently loses the "who changed this" answer.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _dateTime;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IDateTime dateTime) : base(options)
    {
        _tenant = tenant;
        _currentUser = currentUser;
        _dateTime = dateTime;
    }

    // --- Identity and tenancy (P1) -----------------------------------------------------------
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<BranchWarehouse> BranchWarehouses => Set<BranchWarehouse>();
    public DbSet<User> Users => Set<User>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
    public DbSet<CompanyUserBranch> CompanyUserBranches => Set<CompanyUserBranch>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();
    public DbSet<Feature> Features => Set<Feature>();

    // --- Configuration (P1) ------------------------------------------------------------------
    public DbSet<SettingDefinition> SettingDefinitions => Set<SettingDefinition>();
    public DbSet<SettingValue> SettingValues => Set<SettingValue>();
    public DbSet<DocumentNumberSequence> DocumentNumberSequences => Set<DocumentNumberSequence>();

    // --- Auditing (P1) -----------------------------------------------------------------------
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // --- Master data (P2) --------------------------------------------------------------------
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<PriceTier> PriceTiers => Set<PriceTier>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<PriceHistory> PriceHistory => Set<PriceHistory>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<FxRate> FxRates => Set<FxRate>();

    // Business module DbSets are added here as modules are built.

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public IQueryable<TEntity> IgnoringTenantFilter<TEntity>() where TEntity : class =>
        Set<TEntity>().IgnoreQueryFilters();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("techstorepro");

        // Picks up every IEntityTypeConfiguration in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (!isTenantScoped && !isSoftDeletable)
            {
                continue;
            }

            // Builds: e => (!isDeleted || e.IsDeleted == false) && (!scoped || e.CompanyId == currentCompanyId)
            var parameter = Expression.Parameter(clrType, "e");
            Expression? filter = null;

            if (isSoftDeletable)
            {
                filter = Expression.Equal(
                    Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)),
                    Expression.Constant(false));
            }

            if (isTenantScoped)
            {
                // The tenant id is read through a closure over `this`, so the filter re-evaluates
                // per DbContext instance instead of being baked into the compiled query.
                Expression<Func<Guid?>> currentCompanyId = () => _tenant.CompanyId;

                var tenantFilter = Expression.OrElse(
                    // Platform-admin / migration contexts have no tenant: do not filter.
                    Expression.Not(Expression.Property(
                        Expression.Invoke(currentCompanyId),
                        nameof(Nullable<Guid>.HasValue))),
                    Expression.Equal(
                        Expression.Convert(
                            Expression.Property(parameter, nameof(ITenantScoped.CompanyId)),
                            typeof(Guid?)),
                        Expression.Invoke(currentCompanyId)));

                filter = filter is null ? tenantFilter : Expression.AndAlso(filter, tenantFilter);
            }

            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(filter!, parameter));
        }

        // Last, so that it also renames anything the configurations above introduced.
        // database-design.md §2: snake_case everywhere.
        modelBuilder.ApplySnakeCaseNames();

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantRules();
        var auditRows = CaptureAuditTrail();

        var written = await base.SaveChangesAsync(cancellationToken);

        // Audit rows are written in a second pass because a newly-inserted entity has no database
        // identity until the first save completes — auditing "the row with id 00000000-…" would be
        // useless. Both passes are inside the caller's transaction where one exists.
        if (auditRows.Count > 0)
        {
            AuditLogs.AddRange(auditRows);
            await base.SaveChangesAsync(cancellationToken);
        }

        return written;
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantRules();
        var auditRows = CaptureAuditTrail();

        var written = base.SaveChanges();

        if (auditRows.Count > 0)
        {
            AuditLogs.AddRange(auditRows);
            base.SaveChanges();
        }

        return written;
    }

    private void ApplyAuditAndTenantRules()
    {
        var now = _dateTime.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantScoped tenantEntity
                && entry.State == EntityState.Added
                && tenantEntity.CompanyId == Guid.Empty
                && _tenant.CompanyId is { } companyId)
            {
                tenantEntity.CompanyId = companyId;
            }

            if (entry.Entity is AuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        auditable.CreatedBy = userId;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedAt = now;
                        auditable.UpdatedBy = userId;
                        break;
                }
            }

            // Deletes on auditable records are retired, not removed. The reason is set by the
            // handler before it calls Remove(); it survives because the delete becomes an update.
            if (entry.Entity is ISoftDeletable deletable && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                deletable.IsDeleted = true;
                deletable.DeletedAt = now;
                deletable.DeletedBy = userId;
            }
        }
    }

    /// <summary>
    /// Turns the pending changes into audit rows (requirements §9). Runs before SaveChanges, while
    /// the original values are still available — afterwards, "old value" is gone forever.
    /// </summary>
    private List<AuditLog> CaptureAuditTrail()
    {
        var companyId = _tenant.CompanyId;

        // Nothing to attribute a change to. Registration (no tenant yet) and migrations land here;
        // both are logged elsewhere, and an audit row with no company would be unreadable anyway.
        if (companyId is null)
        {
            return [];
        }

        var now = _dateTime.UtcNow;
        var rows = new List<AuditLog>();

        foreach (var entry in ChangeTracker.Entries())
        {
            // The audit log does not audit itself, and neither does the noise: a login row and a
            // rotated refresh token are already their own records.
            //
            // LoginHistory is fully qualified because the DbSet property of the same name would
            // otherwise shadow the type in this pattern.
            if (entry.Entity is AuditLog or Domain.Identity.LoginHistory or RefreshToken)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var isSoftDelete = entry.Entity is ISoftDeletable { IsDeleted: true }
                               && entry.State == EntityState.Modified
                               && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified;

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Deleted => AuditAction.Delete,
                _ when isSoftDelete => AuditAction.Delete,
                _ => AuditAction.Update
            };

            var oldValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();

            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;

                if (IgnoredColumns.Contains(name))
                {
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        newValues[name] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        oldValues[name] = property.OriginalValue;
                        break;

                    case EntityState.Modified when property.IsModified:
                        // Only the columns that actually changed. An audit row echoing all 40
                        // unchanged columns is one nobody reads.
                        oldValues[name] = property.OriginalValue;
                        newValues[name] = property.CurrentValue;
                        break;
                }
            }

            if (oldValues.Count == 0 && newValues.Count == 0)
            {
                continue;
            }

            var entity = entry.Entity as BaseEntity;

            rows.Add(new AuditLog
            {
                CompanyId = companyId.Value,
                UserId = _currentUser.UserId,
                UserEmail = _currentUser.Email,
                EntityType = entry.Entity.GetType().Name,
                EntityId = entity?.Id,
                Action = action,
                OldValues = oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
                NewValues = newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null,
                Summary = isSoftDelete
                    ? (entry.Entity as ISoftDeletable)?.DeletedReason
                    : null,
                IpAddress = _currentUser.IpAddress,
                At = now
            });
        }

        return rows;
    }

    /// <summary>
    /// Never recorded. The password hash is the important one: an audit trail that faithfully
    /// records every hash a user has ever had is a credential history, not an audit log.
    /// </summary>
    private static readonly HashSet<string> IgnoredColumns =
    [
        nameof(User.PasswordHash),
        nameof(AuditableEntity.CreatedAt),
        nameof(AuditableEntity.CreatedBy),
        nameof(AuditableEntity.UpdatedAt),
        nameof(AuditableEntity.UpdatedBy)
    ];
}
