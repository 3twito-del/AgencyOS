using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Application.Authorization;
using AgencyOS.Infrastructure.Authorization;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Idempotency;
using AgencyOS.Application.Finance;
using AgencyOS.Application.Legal;
using AgencyOS.Application.SavedViews;
using AgencyOS.Application.Representations;
using AgencyOS.Application.Search;
using AgencyOS.Application.Sync;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Infrastructure.Persistence.Queries;
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

        // People vertical slice (M2).
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IRelationshipRepository, RelationshipRepository>();
        services.AddScoped<IInteractionRepository, InteractionRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IPeopleSliceQueries, PeopleSliceQueries>();

        // Search, saved views and synchronization (M3).
        services.AddScoped<ISearchQueries, SearchQueries>();
        services.AddScoped<ISyncQueries, SyncQueries>();
        services.AddScoped<ISavedViewRepository, SavedViewRepository>();
        services.AddScoped<ISavedViewResultQueries, SavedViewResultQueries>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        // Talent and representation (M4).
        services.AddScoped<ITalentProfileRepository, TalentProfileRepository>();
        services.AddScoped<IProspectRepository, ProspectRepository>();
        services.AddScoped<IRepresentationRepository, RepresentationRepository>();
        services.AddScoped<ICreditRepository, CreditRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IRepresentationQueries, RepresentationQueries>();

        // Projects and packaging (M5).
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ISourcePropertyRepository, SourcePropertyRepository>();
        services.AddScoped<IPackageRepository, PackageRepository>();
        services.AddScoped<IPackageElementTargets, PackageElementTargets>();
        services.AddScoped<IProjectQueries, ProjectQueries>();

        // Opportunities and submissions (M6).
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IOpportunityTargetRepository, OpportunityTargetRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IOpportunityPitchRepository, OpportunityPitchRepository>();
        services.AddScoped<IOpportunitySubjectTargets, OpportunitySubjectTargets>();
        services.AddScoped<IOpportunityQueries, OpportunityQueries>();

        // Deals and offers (M7).
        services.AddScoped<IDealRepository, DealRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IDealTaskLinkRepository, DealTaskLinkRepository>();
        services.AddScoped<IDealQueries, DealQueries>();

        // Contracts, rights, options and obligations (M8).
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IContractVersionRepository, ContractVersionRepository>();
        services.AddScoped<IRightsGrantRepository, RightsGrantRepository>();
        services.AddScoped<IContractOptionRepository, ContractOptionRepository>();
        services.AddScoped<IObligationRepository, ObligationRepository>();
        services.AddScoped<INoticeRepository, NoticeRepository>();
        services.AddScoped<IContractTaskLinkRepository, ContractTaskLinkRepository>();
        services.AddScoped<IContractQueries, ContractQueries>();

        // Finance (M9).
        services.AddScoped<IMonetaryObligationRepository, MonetaryObligationRepository>();
        services.AddScoped<IReceivableRepository, ReceivableRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICommissionRepository, CommissionRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IFinanceEventRepository, FinanceEventRepository>();
        services.AddScoped<IFinanceTaskLinkRepository, FinanceTaskLinkRepository>();
        services.AddScoped<IFinanceQueries, FinanceQueries>();

        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
