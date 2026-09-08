using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Releases;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.SavedViews;
using AgencyOS.Domain.Sync;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>
/// The canonical PostgreSQL context.
/// </summary>
/// <remarks>
/// <c>docs/02_ARCHITECTURE.md</c>: PostgreSQL is canonical. Nothing else holds
/// truth, and the Windows client never sees this schema - it talks to versioned
/// contracts only.
/// </remarks>
public sealed class AgencyOsDbContext : DbContext, IUnitOfWork
{
    private readonly IClock _clock;

    public AgencyOsDbContext(DbContextOptions<AgencyOsDbContext> options, IClock clock)
        : base(options) => _clock = clock;

    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>
    /// The audit trail. Append-only: see <see cref="AuditAppendOnlyInterceptor"/>
    /// and the database trigger installed by the initial migration.
    /// </summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ReleasePolicy> ReleasePolicies => Set<ReleasePolicy>();

    /// <summary>
    /// The one-time initialization record. A singleton enforced by the database,
    /// not by convention.
    /// </summary>
    public DbSet<SystemInitialization> SystemInitializations => Set<SystemInitialization>();

    // ---- People vertical slice (M2) ----

    public DbSet<Person> People => Set<Person>();

    /// <summary>External bodies the agency holds records about, never tenants (ADR-0010).</summary>
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<ProfessionalRelationship> Relationships => Set<ProfessionalRelationship>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<InteractionParticipant> InteractionParticipants => Set<InteractionParticipant>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    // ---- Search, saved views and synchronization (M3) ----

    /// <summary>Per-user named queries. Private to their owner (ADR-0013).</summary>
    public DbSet<SavedView> SavedViews => Set<SavedView>();

    /// <summary>
    /// The per-tenant change feed. Written by <see cref="ChangeFeedRecorder"/> in
    /// the same transaction as the change it describes.
    /// </summary>
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    /// <summary>The counter that hands out commit-ordered feed positions.</summary>
    public DbSet<ChangeSequence> ChangeSequences => Set<ChangeSequence>();

    /// <summary>
    /// Recorded idempotency keys, which make a retried offline command safe
    /// (ADR-0014).
    /// </summary>
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    // ---- Talent and representation (M4) ----

    /// <summary>The agency's representation metadata about a person (ADR-0017).</summary>
    public DbSet<TalentProfile> TalentProfiles => Set<TalentProfile>();

    public DbSet<TalentDiscipline> TalentDisciplines => Set<TalentDiscipline>();

    /// <summary>People the agency is pursuing. A pursuit, not a relationship.</summary>
    public DbSet<Prospect> Prospects => Set<Prospect>();

    public DbSet<ProspectEvent> ProspectEvents => Set<ProspectEvent>();

    /// <summary>Representation relationships. Being a client derives from these.</summary>
    public DbSet<Representation> Representations => Set<Representation>();

    public DbSet<RepresentationEvent> RepresentationEvents => Set<RepresentationEvent>();

    public DbSet<RepresentationScope> RepresentationScopes => Set<RepresentationScope>();

    public DbSet<RepresentationTeamMember> RepresentationTeamMembers => Set<RepresentationTeamMember>();

    public DbSet<Credit> Credits => Set<Credit>();

    public DbSet<Material> Materials => Set<Material>();

    // ---- Projects and packaging (M5) ----

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectEvent> ProjectEvents => Set<ProjectEvent>();

    public DbSet<ProjectRole> ProjectRoles => Set<ProjectRole>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<AttachmentEvent> AttachmentEvents => Set<AttachmentEvent>();

    public DbSet<ProjectCompanyParticipation> ProjectCompanyParticipations =>
        Set<ProjectCompanyParticipation>();

    public DbSet<SourceProperty> SourceProperties => Set<SourceProperty>();

    public DbSet<ProjectSourceProperty> ProjectSourceProperties => Set<ProjectSourceProperty>();

    public DbSet<ProjectMaterialLink> ProjectMaterialLinks => Set<ProjectMaterialLink>();

    public DbSet<Package> Packages => Set<Package>();

    public DbSet<PackageEvent> PackageEvents => Set<PackageEvent>();

    public DbSet<PackageElement> PackageElements => Set<PackageElement>();

    // ---- Opportunities and submissions (M6) ----

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<OpportunityEvent> OpportunityEvents => Set<OpportunityEvent>();

    public DbSet<OpportunitySubject> OpportunitySubjects => Set<OpportunitySubject>();

    public DbSet<OpportunityTarget> OpportunityTargets => Set<OpportunityTarget>();

    public DbSet<OpportunityTargetEvent> OpportunityTargetEvents => Set<OpportunityTargetEvent>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<SubmissionMaterial> SubmissionMaterials => Set<SubmissionMaterial>();

    public DbSet<OpportunityPitch> OpportunityPitches => Set<OpportunityPitch>();

    public DbSet<PitchMaterial> PitchMaterials => Set<PitchMaterial>();

    public DbSet<OpportunityTaskLink> OpportunityTaskLinks => Set<OpportunityTaskLink>();

    // ---- Deals and offers (M7) ----

    public DbSet<Deal> Deals => Set<Deal>();

    public DbSet<DealEvent> DealEvents => Set<DealEvent>();

    public DbSet<Offer> Offers => Set<Offer>();

    public DbSet<OfferTerm> OfferTerms => Set<OfferTerm>();

    public DbSet<OfferEvent> OfferEvents => Set<OfferEvent>();

    public DbSet<DealTaskLink> DealTaskLinks => Set<DealTaskLink>();

    // ---- Contracts, rights, options and obligations (M8) ----

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<ContractEvent> ContractEvents => Set<ContractEvent>();

    public DbSet<ContractParty> ContractParties => Set<ContractParty>();

    public DbSet<ContractSignature> ContractSignatures => Set<ContractSignature>();

    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();

    public DbSet<ContractTerm> ContractTerms => Set<ContractTerm>();

    public DbSet<RightsGrant> RightsGrants => Set<RightsGrant>();

    public DbSet<ContractOption> ContractOptions => Set<ContractOption>();

    public DbSet<OptionEvent> OptionEvents => Set<OptionEvent>();

    public DbSet<Obligation> Obligations => Set<Obligation>();

    public DbSet<ObligationEvent> ObligationEvents => Set<ObligationEvent>();

    public DbSet<NoticeRequirement> NoticeRequirements => Set<NoticeRequirement>();

    public DbSet<NoticeRecord> NoticeRecords => Set<NoticeRecord>();

    public DbSet<ContractRelationship> ContractRelationships => Set<ContractRelationship>();

    public DbSet<ContractTaskLink> ContractTaskLinks => Set<ContractTaskLink>();

    // ---- Finance (M9) ----

    public DbSet<MonetaryObligation> MonetaryObligations => Set<MonetaryObligation>();

    public DbSet<Receivable> Receivables => Set<Receivable>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<PaymentAdjustment> PaymentAdjustments => Set<PaymentAdjustment>();

    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();

    public DbSet<CommissionEntitlement> CommissionEntitlements => Set<CommissionEntitlement>();

    public DbSet<CommissionAdjustment> CommissionAdjustments => Set<CommissionAdjustment>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<FinanceEvent> FinanceEvents => Set<FinanceEvent>();

    public DbSet<FinanceTaskLink> FinanceTaskLinks => Set<FinanceTaskLink>();

    // ---- Documents and communications (M10) ----

    public DbSet<BlobObject> BlobObjects => Set<BlobObject>();

    public DbSet<BlobIngestion> BlobIngestions => Set<BlobIngestion>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();

    public DbSet<DocumentEvent> DocumentEvents => Set<DocumentEvent>();

    public DbSet<CommunicationAccount> CommunicationAccounts => Set<CommunicationAccount>();

    public DbSet<CommunicationThread> CommunicationThreads => Set<CommunicationThread>();

    public DbSet<CommunicationMessage> CommunicationMessages => Set<CommunicationMessage>();

    public DbSet<CommunicationParticipant> CommunicationParticipants =>
        Set<CommunicationParticipant>();

    public DbSet<CommunicationAttachment> CommunicationAttachments =>
        Set<CommunicationAttachment>();

    public DbSet<CommunicationLink> CommunicationLinks => Set<CommunicationLink>();

    public DbSet<OutboundDispatch> OutboundDispatches => Set<OutboundDispatch>();

    public DbSet<OutboundRecipient> OutboundRecipients => Set<OutboundRecipient>();

    public DbSet<OutboundAttachment> OutboundAttachments => Set<OutboundAttachment>();

    public DbSet<CommunicationEvent> CommunicationEvents => Set<CommunicationEvent>();

    // ---- Intelligence (M11) ----

    public DbSet<IntelligenceSource> IntelligenceSources => Set<IntelligenceSource>();

    public DbSet<Signal> Signals => Set<Signal>();

    public DbSet<SignalEvidence> SignalEvidence => Set<SignalEvidence>();

    /// <summary>
    /// Every intelligence subject, of every owner, in one table.
    /// </summary>
    /// <remarks>
    /// Table-per-hierarchy. The question people actually ask - what does the agency
    /// know about this person - wants one scan rather than five (ADR-0030).
    /// </remarks>
    public DbSet<IntelligenceSubject> IntelligenceSubjects => Set<IntelligenceSubject>();

    public DbSet<Thesis> Theses => Set<Thesis>();

    public DbSet<ThesisRevision> ThesisRevisions => Set<ThesisRevision>();

    public DbSet<ThesisEvidence> ThesisEvidence => Set<ThesisEvidence>();

    public DbSet<Prediction> Predictions => Set<Prediction>();

    public DbSet<PredictionRevision> PredictionRevisions => Set<PredictionRevision>();

    public DbSet<PredictionEvidence> PredictionEvidence => Set<PredictionEvidence>();

    public DbSet<Watchlist> Watchlists => Set<Watchlist>();

    public DbSet<TalentRadarEntry> TalentRadarEntries => Set<TalentRadarEntry>();

    public DbSet<ResearchCase> ResearchCases => Set<ResearchCase>();

    public DbSet<ResearchCaseLink> ResearchCaseLinks => Set<ResearchCaseLink>();

    public DbSet<IntelligenceEvent> IntelligenceEvents => Set<IntelligenceEvent>();

    /// <summary>
    /// Saves, recording a change-feed entry for every cached record that moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The feed entries and the business change commit together or not at all. A
    /// change without its entry would be invisible to every client until something
    /// else touched the record; an entry without its change would send clients to
    /// fetch a state that does not exist.
    /// </para>
    /// <para>
    /// A transaction is opened when the caller has not already opened one, because
    /// the position allocation must hold the tenant's counter row until the same
    /// commit. When the caller owns a transaction, that one is used and committed
    /// by them.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // The exclusive-arc columns are filled here rather than at each call site.
        // A link written without them would still satisfy every C# type, and would
        // then fail a check constraint or - worse - carry a stale identifier from a
        // previous target. One place, applied to every added link (ADR-0025).
        LinkArcSynchronizer.Apply(ChangeTracker);

        IReadOnlyList<PendingChange> pending = ChangeFeedRecorder.Collect(ChangeTracker);

        if (pending.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Database.CurrentTransaction is not null)
        {
            await ChangeFeedRecorder
                .RecordAsync(this, pending, _clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await using IDbContextTransaction transaction =
            await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ChangeFeedRecorder
            .RecordAsync(this, pending, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        int written = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgencyOsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
