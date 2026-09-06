using Microsoft.Extensions.DependencyInjection;

namespace AgencyOS.Infrastructure.DependencyInjection;

/// <summary>
/// Composition seam for infrastructure services.
/// </summary>
/// <remarks>
/// M0 registers nothing: this is the single place where persistence, outbox
/// dispatch and external adapters attach from M1 onward, so hosts never wire
/// infrastructure types directly.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers AgencyOS infrastructure services.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgencyOSInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
