using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Documents;
using AgencyOS.Application.Intelligence;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Application.Ai;
using AgencyOS.Application.Ai.Tools;
using AgencyOS.Infrastructure.Ai;
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
using AgencyOS.Infrastructure.Communications;
using AgencyOS.Infrastructure.Persistence.Queries;
using AgencyOS.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
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

        // ---- Documents and communications (M10) ----

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IBlobRepository, BlobRepository>();
        services.AddScoped<IDocumentEventRepository, DocumentEventRepository>();
        services.AddScoped<IDocumentQueries, DocumentQueries>();
        services.AddScoped<IDocumentLinkValidator, LinkLabels>();

        services.AddScoped<ICommunicationAccountRepository, CommunicationAccountRepository>();
        services.AddScoped<ICommunicationMessageRepository, CommunicationMessageRepository>();
        services.AddScoped<ICommunicationThreadRepository, CommunicationThreadRepository>();
        services.AddScoped<IOutboundDispatchRepository, OutboundDispatchRepository>();
        services.AddScoped<ICommunicationEventRepository, CommunicationEventRepository>();
        services.AddScoped<ICommunicationQueries, CommunicationQueries>();

        // The content store and the extractor are stateless and hold no connection,
        // so one instance serves every request.
        services.AddSingleton<IBlobStore, FileSystemBlobStore>();
        services.AddSingleton<IDocumentTextExtractor, PlainTextDocumentExtractor>();

        // Encryption at rest for stored mailbox credentials. The key ring is
        // file-backed in ALPHA, which protects a leaked database and not a
        // compromised server - a limitation stated rather than glossed over
        // (ADR-0027).
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // The fake provider is always registered. It is not a test-only stub: CI has
        // no Microsoft tenant, so it is what proves the send protocol behaves when a
        // provider loses an acknowledgement, which no real mailbox will do on
        // request (ADR-0026, ADR-0028).
        services.AddSingleton<ICommunicationProvider, FakeCommunicationProvider>();
        services.AddSingleton<ICommunicationProviderRegistry, CommunicationProviderRegistry>();

        // ---- Intelligence (M11) ----

        services.AddScoped<IIntelligenceSourceRepository, IntelligenceSourceRepository>();
        services.AddScoped<ISignalRepository, SignalRepository>();
        services.AddScoped<IThesisRepository, ThesisRepository>();
        services.AddScoped<IPredictionRepository, PredictionRepository>();
        services.AddScoped<IWatchlistRepository, WatchlistRepository>();
        services.AddScoped<ITalentRadarRepository, TalentRadarRepository>();
        services.AddScoped<IResearchCaseRepository, ResearchCaseRepository>();
        services.AddScoped<IIntelligenceEventRepository, IntelligenceEventRepository>();
        services.AddScoped<IIntelligenceQueries, IntelligenceQueries>();
        services.AddScoped<IIntelligenceSubjectValidator, IntelligenceSubjectLabels>();

        // ---- AI runtime (M12) ----

        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAiToolRequestRepository, AiToolRequestRepository>();
        services.AddScoped<IAiApprovalRepository, AiApprovalRepository>();
        services.AddScoped<IAiProviderPolicyRepository, AiProviderPolicyRepository>();

        services.AddScoped<IAiQueries, AiQueries>();
        services.AddScoped<IAiContextAssembler, AiContextAssembler>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IModelGateway, ModelGateway>();

        // Registered explicitly, one line each. There is no scanning and no
        // attribute discovery: a tool exists because somebody wrote it down here,
        // and this list is what a reviewer reads to know what a model can ask for
        // (§8, §32).
        services.AddScoped<IAiTool, PersonGetTool>();
        services.AddScoped<IAiTool, CompanyGetTool>();
        services.AddScoped<IAiTool, RelationshipIntelligenceTool>();
        services.AddScoped<IAiTool, SignalsSearchTool>();
        services.AddScoped<IAiTool, AgencySearchTool>();
        services.AddScoped<IAiTool, ResearchCaseGetTool>();
        services.AddScoped<IAiTool, DealGetTool>();
        services.AddScoped<IAiTool, ContractGetTool>();
        services.AddScoped<IAiTool, ReceivablesListTool>();
        services.AddScoped<IAiTool, TaskCreateTool>();

        // The deterministic provider is registered in every environment, not only
        // in tests. A build whose composition differs between CI and production is
        // a build whose tests exercise something else (§74).
        services.AddSingleton<FakeModelProvider>();
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<FakeModelProvider>());

        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>
    /// Configures where stored bytes and protection keys live.
    /// </summary>
    /// <remarks>
    /// Separate from the main registration because a host that never uploads
    /// anything - a migration run, a contract generation - should not have to
    /// create directories to start. The defaults are beside the application, which
    /// is right for a single-server ALPHA deployment and wrong for anything larger;
    /// ADR-0024 says so rather than leaving it to be discovered.
    /// </remarks>
    public static IServiceCollection AddAgencyOSContentStorage(
        this IServiceCollection services,
        string? blobRootPath = null,
        string? protectionKeyPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        BlobStoreOptions options = new();

        if (!string.IsNullOrWhiteSpace(blobRootPath))
        {
            options.RootPath = blobRootPath;
        }

        services.AddSingleton(options);

        IDataProtectionBuilder protection = services
            .AddDataProtection()
            .SetApplicationName("AgencyOS");

        if (!string.IsNullOrWhiteSpace(protectionKeyPath))
        {
            Directory.CreateDirectory(protectionKeyPath);
            protection.PersistKeysToFileSystem(new DirectoryInfo(protectionKeyPath));
        }

        return services;
    }

    /// <summary>
    /// Registers the Microsoft Graph adapter, when an operator has configured one.
    /// </summary>
    /// <remarks>
    /// Absent by default. Without an application registration the adapter would
    /// refuse every call, and offering a mailbox provider that cannot connect is
    /// worse than not offering it (ADR-0027).
    /// </remarks>
    public static IServiceCollection AddMicrosoftGraphProvider(
        this IServiceCollection services,
        GraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            return services;
        }

        services.AddSingleton(options);

        services.AddHttpClient<GraphCommunicationProvider>();

        services.AddSingleton<ICommunicationProvider>(
            provider => provider.GetRequiredService<GraphCommunicationProvider>());

        return services;
    }
}
