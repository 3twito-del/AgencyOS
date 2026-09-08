// AgencyOS API host.
//
// Governing documents:
//   docs/02_ARCHITECTURE.md           - modular monolith, HTTPS/OpenAPI boundary
//   docs/06_FORCED_UPDATE_PROTOCOL.md - version identity and release enforcement
//   docs/07_SECURITY_AND_AUDIT.md     - server-side enforcement, audit, identity
//
// Milestone M1 adds identity, organizations, memberships, permission-based
// authorization, an append-only audit trail and server-side release enforcement.

using AgencyOS.Api.Authentication;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Endpoints;
using AgencyOS.Api.Health;
using AgencyOS.Api.Http;
using AgencyOS.Api.Middleware;
using AgencyOS.Api.Observability;
using AgencyOS.Api.Provisioning;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Ai;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Companies;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Idempotency;
using AgencyOS.Application.Intelligence;
using AgencyOS.Application.Interactions;
using AgencyOS.Application.People;
using AgencyOS.Application.Relationships;
using AgencyOS.Api.Workers;
using AgencyOS.Application.Communications;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Documents;
using AgencyOS.Application.Finance;
using AgencyOS.Application.Legal;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Application.Representations;
using AgencyOS.Application.SavedViews;
using AgencyOS.Application.Search;
using AgencyOS.Application.Sync;
using AgencyOS.Application.Tasks;
using AgencyOS.Application.Memberships;
using AgencyOS.Application.Organizations;
using AgencyOS.Application.Provisioning;
using AgencyOS.Application.Releases;
using AgencyOS.Contracts;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Releases;
using AgencyOS.Infrastructure.Communications;
using AgencyOS.Infrastructure.DependencyInjection;
using AgencyOS.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.AddAgencyOSStructuredLogging();

// ---------------------------------------------------------------------------
// Release ring safety
//
// The development authentication scheme trusts a header. It is acceptable only
// on rings that forbid real data (config/release-channels.yaml). Refusing to
// start is the correct response: a host that silently accepted header identity
// against canonical data would be the single worst defect this system could
// ship, and it would be invisible.
// ---------------------------------------------------------------------------
string authenticationMode = builder.Configuration["AgencyOS:Authentication:Mode"] ?? "Development";
bool developmentAuthentication = string.Equals(authenticationMode, "Development", StringComparison.OrdinalIgnoreCase);

bool ringAllowsRealData =
    ReleaseRingNames.TryParse(BuildInfo.Channel, out ReleaseRing buildRing)
    && ReleaseRingNames.AllowsRealData(buildRing);

if (developmentAuthentication && ringAllowsRealData)
{
    throw new InvalidOperationException(
        $"Development authentication cannot run on ring '{BuildInfo.Channel}', which permits real data. "
            + "Configure AgencyOS:Authentication:Mode for a real identity provider.");
}

// ---------------------------------------------------------------------------
// Persistence
//
// A ring that permits real data must be told explicitly where canonical data
// lives. A convenient default is only acceptable where the data is disposable.
// ---------------------------------------------------------------------------
string? connectionString =
    builder.Configuration.GetConnectionString("AgencyOS")
    ?? Environment.GetEnvironmentVariable("AGENCYOS_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    if (ringAllowsRealData)
    {
        throw new InvalidOperationException(
            $"No AgencyOS connection string is configured and ring '{BuildInfo.Channel}' permits real data.");
    }

    connectionString = "Host=localhost;Port=5432;Database=agencyos_dev;Username=postgres;Password=postgres";
}

builder.Services.AddAgencyOSInfrastructure(connectionString);

// Where stored bytes and protection keys live. Beside the application by default,
// which is right for a single-server ALPHA deployment and wrong for anything
// larger; ADR-0024 says so rather than leaving it to be discovered.
builder.Services.AddAgencyOSContentStorage(
    builder.Configuration["AgencyOS:BlobStore:RootPath"],
    builder.Configuration["AgencyOS:DataProtection:KeyPath"]);

// Microsoft Graph is offered only when an operator has registered an application.
// Without one the adapter would refuse every call, and offering a mailbox provider
// that cannot connect is worse than not offering it (ADR-0027). No secret is
// committed: these are configuration, absent by default.
builder.Services.AddMicrosoftGraphProvider(new GraphOptions
{
    ClientId = builder.Configuration["AgencyOS:Graph:ClientId"],
    ClientSecret = builder.Configuration["AgencyOS:Graph:ClientSecret"],
    TenantId = builder.Configuration["AgencyOS:Graph:TenantId"] ?? "common",
});

// ---------------------------------------------------------------------------
// First-run initialization
//
// The gate exists only when a token is configured. Constructing it validates the
// token's strength, so a weak bootstrap credential fails startup rather than
// sitting quietly in a deployment.
// ---------------------------------------------------------------------------
string? bootstrapToken =
    builder.Configuration["AgencyOS:Bootstrap:Token"]
    ?? Environment.GetEnvironmentVariable("AGENCYOS_BOOTSTRAP_TOKEN");

BootstrapTokenGate? bootstrapGate =
    string.IsNullOrWhiteSpace(bootstrapToken) ? null : new BootstrapTokenGate(bootstrapToken);

// Application composition. The host decides which capabilities it uses; the
// infrastructure assembly only provides them.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IExecutionContext, HttpExecutionContext>();
builder.Services.AddScoped<AuditRecorder>();
builder.Services.AddScoped<CreateOrganizationHandler>();
builder.Services.AddScoped<GrantMembershipHandler>();
builder.Services.AddSingleton<IApiContractPolicy, ServerApiContractPolicy>();
builder.Services.AddScoped<BootstrapSystemHandler>();
builder.Services.AddScoped<ClientCompatibilityService>();

// People vertical slice (M2). TenantGuard is the one place a command establishes
// who is acting and whether they may; the query service applies the same check to
// reads so an endpoint cannot forget it.
builder.Services.AddScoped<TenantGuard>();
builder.Services.AddScoped<PeopleSliceQueryService>();
builder.Services.AddScoped<CreatePersonHandler>();
builder.Services.AddScoped<UpdatePersonHandler>();
builder.Services.AddScoped<CreateCompanyHandler>();
builder.Services.AddScoped<UpdateCompanyHandler>();
builder.Services.AddScoped<CreateRelationshipHandler>();
builder.Services.AddScoped<EndRelationshipHandler>();
builder.Services.AddScoped<RecordInteractionHandler>();
builder.Services.AddScoped<CreateTaskHandler>();
builder.Services.AddScoped<CompleteTaskHandler>();
builder.Services.AddScoped<ReopenTaskHandler>();

// Search, saved views and synchronization (M3). Each service applies its own
// tenant-scoped authorization, so no endpoint can read across a tenant by
// forgetting a check.
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<SavedViewService>();
builder.Services.AddScoped<SyncService>();

// The server half of the offline write queue. A key the client alone checks is a
// client that can be wrong twice (ADR-0014).
builder.Services.AddScoped<IdempotencyCoordinator>();

// Talent and representation (M4). SensitiveNotes holds the one implementation of
// note redaction; every read path that can return positioning or strategy notes
// goes through it, including saved views (ADR-0017).
builder.Services.AddScoped<SensitiveNotes>();
builder.Services.AddScoped<RepresentationQueryService>();

// Projects and packaging (M5).
builder.Services.AddScoped<ProjectQueryService>();
builder.Services.AddScoped<CreateProjectHandler>();
builder.Services.AddScoped<UpdateProjectHandler>();
builder.Services.AddScoped<ChangeProjectLifecycleHandler>();
builder.Services.AddScoped<ProjectRoleHandler>();
builder.Services.AddScoped<AttachmentHandler>();
builder.Services.AddScoped<ProjectCompanyHandler>();
builder.Services.AddScoped<SourcePropertyHandler>();
builder.Services.AddScoped<ProjectLinkHandler>();
builder.Services.AddScoped<PackageHandler>();

// Opportunities and submissions (M6).
builder.Services.AddScoped<OpportunityQueryService>();
builder.Services.AddScoped<OpportunityHandler>();
builder.Services.AddScoped<OpportunityTargetHandler>();
builder.Services.AddScoped<RecordSubmissionHandler>();
builder.Services.AddScoped<RecordPitchHandler>();

// Deals and offers (M7). DealRedaction holds the one implementation of what a
// caller may see of a negotiation - the strategy and the economics - so every
// read path applies the identical rule (ADR-0021).
builder.Services.AddScoped<DealRedaction>();
builder.Services.AddScoped<DealQueryService>();
builder.Services.AddScoped<DealHandler>();
builder.Services.AddScoped<OfferHandler>();

// Contracts, rights, options and obligations (M8). ContractRedaction holds the
// one implementation of what a caller may see of an instrument - its terms, the
// economics inside them, and content a person classified as privileged - so every
// read path applies the identical rule (ADR-0021, ADR-0022).
builder.Services.AddScoped<ContractRedaction>();
builder.Services.AddScoped<ContractQueryService>();
builder.Services.AddScoped<ContractHandler>();
builder.Services.AddScoped<ContractVersionHandler>();
builder.Services.AddScoped<RightsHandler>();
builder.Services.AddScoped<OptionHandler>();
builder.Services.AddScoped<ObligationHandler>();

// Finance (M9). LedgerPosting holds the one implementation of which accounts an
// event touches, so a payment cannot be posted one way here and another way by a
// future import - and the client-funds rule lives in exactly one place
// (ADR-0023).
builder.Services.AddScoped<LedgerPosting>();
builder.Services.AddScoped<FinanceQueryService>();
builder.Services.AddScoped<MonetaryObligationHandler>();
builder.Services.AddScoped<ReceivableHandler>();
builder.Services.AddScoped<InvoiceHandler>();
builder.Services.AddScoped<PaymentHandler>();
builder.Services.AddScoped<CommissionHandler>();
builder.Services.AddScoped<LedgerHandler>();
// Documents and communications (M10). DocumentIngestion holds the one
// implementation of the bytes-first, rows-second ordering, so no future upload
// path can invent a different one and leave a document nobody can open
// (ADR-0024). OutboundSendProcessor holds the send protocol for the same reason,
// and it is the only thing in AgencyOS that causes an irreversible external act
// (ADR-0028).
builder.Services.AddScoped<DocumentAuthorization>();
builder.Services.AddScoped<DocumentLinkValidator>();
builder.Services.AddScoped<DocumentIngestion>();
builder.Services.AddScoped<DocumentHandler>();
builder.Services.AddScoped<DocumentQueryService>();

builder.Services.AddScoped<CommunicationAuthorization>();
builder.Services.AddScoped<CommunicationQueryService>();
builder.Services.AddScoped<CommunicationAccountHandler>();
builder.Services.AddScoped<CommunicationMessageHandler>();
builder.Services.AddScoped<OutboundDispatchHandler>();
builder.Services.AddScoped<MailboxSynchronizer>();
builder.Services.AddScoped<OutboundSendProcessor>();

// The background loop. Canonical state lives in PostgreSQL and every claim takes
// an expiring lease, so two instances running at once is ordinary rather than a
// bug, and a restart loses nothing about a send that may already have happened
// (ADR-0029).
builder.Services.AddSingleton(new CommunicationWorkerOptions
{
    Enabled = builder.Configuration.GetValue("AgencyOS:Worker:Enabled", true),
});
builder.Services.AddHostedService<CommunicationWorker>();

// Intelligence (M11). The conceptual chain is kept apart in the type system, not
// merged into one "intelligence note": a source is evidence, a signal is a claim
// with provenance, a thesis is a view somebody holds, and a prediction is a
// falsifiable statement with a date. Nothing here summarizes, extracts or scores
// anything on its own (ADR-0030).
builder.Services.AddScoped<IntelligenceAuthorization>();
builder.Services.AddScoped<IntelligenceQueryService>();
builder.Services.AddScoped<IntelligenceSourceHandler>();
builder.Services.AddScoped<SignalHandler>();
builder.Services.AddScoped<ThesisHandler>();
builder.Services.AddScoped<PredictionHandler>();
builder.Services.AddScoped<WatchlistHandler>();
builder.Services.AddScoped<TalentRadarHandler>();
builder.Services.AddScoped<ResearchCaseHandler>();

// AI runtime (M12). The model is untrusted input rather than a trusted
// component: context assembly decides what it may see, the tool registry decides
// what it may ask for, and an approval decides what actually happens. It
// influences none of the three (ADR-0031).
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
builder.Services.AddScoped<ModelDataPolicy>();
builder.Services.AddScoped<AgentRuntime>();
builder.Services.AddScoped<AiQueryService>();
builder.Services.AddScoped<AgentRunHandler>();
builder.Services.AddScoped<AiApprovalHandler>();
builder.Services.AddScoped<AiProviderPolicyHandler>();

builder.Services.AddScoped<CreateTalentProfileHandler>();
builder.Services.AddScoped<UpdateTalentProfileHandler>();
builder.Services.AddScoped<ChangeTalentDisciplineHandler>();
builder.Services.AddScoped<CreateProspectHandler>();
builder.Services.AddScoped<AdvanceProspectHandler>();
builder.Services.AddScoped<ConvertProspectHandler>();
builder.Services.AddScoped<CreateRepresentationHandler>();
builder.Services.AddScoped<TransitionRepresentationHandler>();
builder.Services.AddScoped<ChangeRepresentationScopeHandler>();
builder.Services.AddScoped<AssignRepresentationTeamMemberHandler>();
builder.Services.AddScoped<RemoveRepresentationTeamMemberHandler>();
builder.Services.AddScoped<AddCreditHandler>();
builder.Services.AddScoped<UpdateCreditHandler>();
builder.Services.AddScoped<AddMaterialHandler>();
builder.Services.AddScoped<UpdateMaterialHandler>();
builder.Services.AddSingleton<AgencyOS.Api.Endpoints.IClockAccessor, AgencyOS.Api.Http.SystemClockAccessor>();

// ---------------------------------------------------------------------------
// Authentication and authorization
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(AgencyOsAuthentication.DevelopmentScheme)
    .AddScheme<DevelopmentAuthenticationOptions, DevelopmentAuthenticationHandler>(
        AgencyOsAuthentication.DevelopmentScheme,
        configureOptions: null);

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

AuthorizationBuilder authorization = builder.Services.AddAuthorizationBuilder();

// One policy per known permission, so an endpoint cannot name a permission that
// does not exist in the domain vocabulary.
foreach (string permission in Permission.All)
{
    authorization.AddPolicy(
        PermissionPolicy.Name(permission),
        policy => policy
            .AddAuthenticationSchemes(AgencyOsAuthentication.DevelopmentScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
}

// ---------------------------------------------------------------------------
// Health
//
// Liveness and readiness are separate endpoints with separate meanings. Only the
// readiness endpoint runs checks; see PostgresReadinessCheck for why conflating
// them is harmful.
// ---------------------------------------------------------------------------
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresReadinessCheck>(
        "postgres",
        tags: [HealthResponseWriter.ReadyTag]);

// ---------------------------------------------------------------------------
// API contract
//
// The machine-readable contract required by CLAUDE.md principle 6. No UI is
// registered: the requirement is the document, not a browser experience.
// ---------------------------------------------------------------------------
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "AgencyOS API";
        document.Info.Version = "v1";
        document.Info.Description =
            $"AgencyOS versioned domain API. API contract version {ApiContract.Current}. "
                + "Clients present release identity headers on every request and are refused "
                + "for mutations when revoked or incompatible (docs/06_FORCED_UPDATE_PROTOCOL.md).";

        return Task.CompletedTask;
    });
});

// ---------------------------------------------------------------------------
// Observability
//
// ADR-0004 deferred OpenTelemetry in M0 and named the condition that would change
// the answer: the first cross-process call. ADR-0016 records that M3 is that
// milestone - a queued command can be captured on Monday, submitted on Wednesday
// and replayed idempotently, which no single log line explains.
//
// No collector and no vendor. The exporter is registered only when an OTLP
// endpoint is configured, so an ordinary deployment carries the instrumentation
// and exports nowhere.
// ---------------------------------------------------------------------------
string? otlpEndpoint =
    builder.Configuration["AgencyOS:Telemetry:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("AGENCYOS_OTLP_ENDPOINT");

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "agencyos-api",
        serviceVersion: BuildInfo.Version,
        serviceInstanceId: BuildInfo.BuildId))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(AgencyOsTelemetry.SourceName)

            // Npgsql publishes its own activity source; subscribing to it by name
            // is exactly what its instrumentation helper does, without a package
            // whose AddNpgsql collides with EF Core's.
            .AddSource("Npgsql")
            .AddAspNetCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(AgencyOsTelemetry.SourceName)
            .AddAspNetCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AgencyOsExceptionHandler>();

WebApplication app = builder.Build();

app.UseExceptionHandler();

// Release enforcement is the outermost gate on mutations: a revoked or
// incompatible client is refused before authentication, authorization or any
// handler is consulted.
app.UseMiddleware<ClientCompatibilityMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Liveness. Runs no checks: the process being able to answer is the answer.
// A database outage must not cause an orchestrator to restart healthy instances.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = static _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

// Readiness. Fails when canonical PostgreSQL cannot be reached, so traffic is
// routed away from an instance that cannot serve it.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration => registration.Tags.Contains(HealthResponseWriter.ReadyTag),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapGet("/version", static () => VersionResponse.Current());

app.MapOpenApi();

app.MapAgencyOsApi(bootstrapGate);

app.Logger.LogInformation(
    "AgencyOS API starting. version={Version} channel={Channel} buildId={BuildId} commit={GitCommit} "
        + "apiContract={ApiContractVersion} authentication={AuthenticationMode} bootstrap={BootstrapEnabled} "
        + "telemetryExport={TelemetryExport}",
    BuildInfo.Version,
    BuildInfo.Channel,
    BuildInfo.BuildId,
    BuildInfo.GitCommit,
    ApiContract.Current,
    authenticationMode,
    bootstrapGate is not null,
    string.IsNullOrWhiteSpace(otlpEndpoint) ? "none" : otlpEndpoint);

app.Run();

/// <summary>
/// Entry point of the AgencyOS API host. Declared explicitly so
/// <c>AgencyOS.Tests.Integration</c> can host it with <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
