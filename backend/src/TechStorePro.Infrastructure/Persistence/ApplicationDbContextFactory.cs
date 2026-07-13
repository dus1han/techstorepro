using TechStorePro.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechStorePro.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. It builds a context with no tenant and no
/// user so that migrations are generated against the unfiltered schema.
/// The connection string comes from the ConnectionStrings__DefaultConnection environment
/// variable, falling back to the local Docker Postgres in docker-compose.yml.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string LocalDevConnection =
        "Host=localhost;Port=5433;Database=techstorepro;Username=techstorepro;Password=techstorepro_dev_password";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? LocalDevConnection;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(
            options,
            new DesignTimeTenantContext(),
            new DesignTimeCurrentUser(),
            new DesignTimeClock());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool HasTenant => false;
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Email => null;
        public bool IsAuthenticated => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class DesignTimeClock : IDateTime
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
