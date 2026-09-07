using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Infrastructure.Authorization;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyOS.Infrastructure.DependencyInjection;

/// <summary>
/// Composition seam for infrastructure services.
/// </summary>
/// <remarks>
/// Registers persistence, repositories, authorization evaluation and the clock.
/// Application handlers are composed by the host, so this assembly stays a
/// provider of capabilities rather than an arbiter of which ones the host uses.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers AgencyOS infrastructure services against canonical PostgreSQL.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgencyOSInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<AuditAppendOnlyInterceptor>();

        services.AddDbContext<AgencyOsDbContext>((provider, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AgencyOsDbContext).Assembly.FullName));

            options.AddInterceptors(provider.GetRequiredService<AuditAppendOnlyInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AgencyOsDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IReleasePolicyRepository, ReleasePolicyRepository>();
        services.AddScoped<ISystemInitializationRepository, SystemInitializationRepository>();

        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
