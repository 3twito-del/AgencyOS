using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>
/// Supplies a context to the EF Core command-line tools.
/// </summary>
/// <remarks>
/// Lets migrations be authored and scripted without starting the API and without
/// a reachable database. The connection string is only used when a command
/// actually contacts a server; <c>migrations add</c> and <c>migrations script</c>
/// do not.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgencyOsDbContext>
{
    private const string DesignTimeConnectionVariable = "AGENCYOS_DESIGN_CONNECTION";

    public AgencyOsDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(DesignTimeConnectionVariable)
            ?? "Host=localhost;Port=5432;Database=agencyos_design;Username=postgres;Password=postgres";

        DbContextOptions<AgencyOsDbContext> options =
            new DbContextOptionsBuilder<AgencyOsDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AgencyOsDbContext).Assembly.FullName))
                .Options;

        return new AgencyOsDbContext(options);
    }
}
