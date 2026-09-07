using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Deals;

/// <summary>Opaque, immutable identifier for a <see cref="Deal"/>.</summary>
public readonly record struct DealId(Guid Value)
{
    public static DealId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of commercial transaction is being negotiated.
/// </summary>
/// <remarks>
/// Broad enough for film and television representation, and deliberately not a
/// rights ontology. Nothing here says what is granted, in which territories, for
/// how long, or on what conditions - those are legal concepts and they are M8's
/// (ADR-0021).
/// </remarks>
public enum DealKind
{
    /// <summary>Employment of a performer or other on-camera client.</summary>
    TalentEmployment = 1,

    /// <summary>Writing services.</summary>
    Writing = 2,

    /// <summary>Directing services.</summary>
    Directing = 3,

    /// <summary>Producing services.</summary>
    Producing = 4,

    /// <summary>Outright sale of a project or property.</summary>
    ProjectSale = 5,

    /// <summary>Licensing of a project rather than a sale of it.</summary>
    ProjectLicense = 6,

    /// <summary>A packaged group taken as one commercial transaction.</summary>
    Package = 7,

    /// <summary>Other contracted services.</summary>
    Services = 8,

    /// <summary>A working arrangement between companies.</summary>
    Partnership = 9,

    Other = 99,
}

/// <summary>
/// Where a deal stands as a negotiation.
/// </summary>
/// <remarks>
/// <para>
/// The values match the F# rules kernel's own codes; a test walks both
/// vocabularies in each direction.
/// </para>
/// <para>
/// <see cref="TermsAgreed"/> is the furthest this milestone goes. It means the
/// commercial terms are settled, and it means nothing whatever about a contract:
/// nothing is drafted, signed, executed or payable. There is deliberately no
/// Signed, Executed, Paid or Commissioned value, because M7 has no way to
/// substantiate any of them and a status that claimed one would be read as the
/// agency saying so (ADR-0021).
/// </para>
/// </remarks>
public enum DealStatus
{
    /// <summary>Opened, with nothing on the table yet.</summary>
    Draft = 1,

    /// <summary>At least one offer has been recorded.</summary>
    Negotiating = 2,

    /// <summary>An offer was accepted. Commercial terms agreed; nothing executed.</summary>
    TermsAgreed = 3,

    /// <summary>Talks ended without agreement.</summary>
    NoDeal = 4,

    /// <summary>Called off. Terminal.</summary>
    Cancelled = 5,
}

/// <summary>
/// What causes a deal to change status.
/// </summary>
/// <remarks>
/// Statuses are never set directly. Two of these are consequences of offer acts
/// rather than requests a caller may make, and routing every change through a
/// named cause is what makes that enforceable rather than conventional.
/// </remarks>
public enum DealTransition
{
    /// <summary>An offer was recorded against the deal.</summary>
    OfferRecorded = 1,

    /// <summary>An offer was accepted.</summary>
    OfferAccepted = 2,

    /// <summary>Negotiation was explicitly reopened.</summary>
    NegotiationReopened = 3,

    /// <summary>Talks were closed without agreement.</summary>
    ClosedNoDeal = 4,

    /// <summary>The deal was called off.</summary>
    Cancelled = 5,
}

/// <summary>What kind of thing a deal event records.</summary>
public enum DealEventKind
{
    /// <summary>The deal was opened.</summary>
    Opened = 1,

    /// <summary>The status changed.</summary>
    StatusChanged = 2,

    /// <summary>Descriptive fields were edited.</summary>
    MetadataUpdated = 3,
}

/// <summary>
/// A recorded change to a deal.
/// </summary>
/// <remarks>
/// Append-only, and distinct from the audit trail: the audit log answers who did
/// what under which permission, and this answers what the negotiation did. A
/// timeline built from audit rows would be a security artefact shown to agents
/// (ADR-0012).
/// </remarks>
public sealed class DealEvent
{
    private DealEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DealId DealId { get; private set; }

    public DealEventKind Kind { get; private set; }

    public DealStatus? FromStatus { get; private set; }

    public DealStatus ToStatus { get; private set; }

    /// <summary>What caused it, when a status changed.</summary>
    public DealTransition? Transition { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static DealEvent Record(
        OrganizationId organizationId,
        DealId dealId,
        DealEventKind kind,
        DealStatus? fromStatus,
        DealStatus toStatus,
        DealTransition? transition,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new DealEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DealId = dealId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Transition = transition,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// One commercial negotiation, anchored to the market conversation that produced it.
/// </summary>
/// <remarks>
/// <para>
/// A deal is not an opportunity and not a target. The opportunity is what the
/// agency is pursuing, the target is who it is pursuing it with, and the deal is
/// the commercial transaction that conversation turned into. It requires both, so
/// the lineage from client or project through pursuit and target to agreed terms
/// is never broken.
/// </para>
/// <para>
/// The counterparty is not stored here. It is whoever the target names, read by
/// join, because copying it would create a second answer that can disagree with
/// the first the moment somebody corrects the target (ADR-0021).
/// </para>
/// <para>
/// Neither is the accepted offer. Which offer is the agreement is answered by the
/// offer that says it is, guaranteed unique by a partial index; a column here
/// would be the same fact written twice.
/// </para>
/// </remarks>
public sealed class Deal
{
    /// <summary>Statuses in which the negotiation is over.</summary>
    public static IReadOnlySet<DealStatus> TerminalStatuses { get; } =
        new HashSet<DealStatus> { DealStatus.Cancelled };

    /// <summary>
    /// The target stages from which a negotiation may be opened.
    /// </summary>
    /// <remarks>
    /// M6 put <see cref="OpportunityTargetStage.Advanced"/> in the vocabulary as
    /// the boundary with this milestone, and <c>Interested</c> immediately
    /// precedes it. Anything earlier means nobody has said yes to anything yet,
    /// and a deal opened there would be a negotiation with a party who does not
    /// know they are in one.
    /// </remarks>
    public static IReadOnlySet<OpportunityTargetStage> OpenableFromStages { get; } =
        new HashSet<OpportunityTargetStage>
        {
            OpportunityTargetStage.Interested,
            OpportunityTargetStage.Advanced,
        };

    /// <summary>
    /// Statuses in which a live deal blocks another for the same target and kind.
    /// </summary>
    /// <remarks>
    /// Mirrored by a partial unique index. Two colleagues opening a negotiation
    /// with the same buyer for the same kind of transaction is the duplicate this
    /// prevents; a genuinely different transaction with that buyer is a different
    /// kind and is allowed.
    /// </remarks>
    public static IReadOnlySet<DealStatus> LiveStatuses { get; } =
        new HashSet<DealStatus>
        {
            DealStatus.Draft,
            DealStatus.Negotiating,
            DealStatus.TermsAgreed,
        };

    private readonly List<DealEvent> _events = [];

    private Deal()
    {
    }

    public DealId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The pursuit this negotiation came out of.</summary>
    public OpportunityId OpportunityId { get; private set; }

    /// <summary>The market conversation this negotiation came out of.</summary>
    public OpportunityTargetId OpportunityTargetId { get; private set; }

    /// <summary>What the deal is called. Searchable; carries no sensitive content.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>An internal handle, when the agency uses one.</summary>
    public string? Reference { get; private set; }

    public DealKind Kind { get; private set; }

    public DealStatus Status { get; private set; }

    /// <summary>The internal owner. A negotiation nobody owns is one nobody runs.</summary>
    public UserId OwnerUserId { get; private set; }

    public DateOnly OpenedOn { get; private set; }

    /// <summary>When the negotiation ended, set with a terminal status.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Factual description of what is being negotiated.</summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// Internal negotiating strategy. Requires <c>deals.strategy.read</c>.
    /// </summary>
    /// <remarks>
    /// Judgment, kept apart from fact by design. "They offered 500,000 USD" is a
    /// term; "we think they can reach 750,000 and should trade backend for
    /// guarantee" is this. Sharing a field would make it impossible to show the
    /// first without the second (ADR-0021).
    /// </remarks>
    public string? StrategyNotes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<DealEvent> Events => _events;

    /// <summary>Gets a value indicating whether the negotiation can no longer move.</summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>Gets a value indicating whether the deal blocks another for its target and kind.</summary>
    public bool IsLive => LiveStatuses.Contains(Status);

    /// <summary>Gets a value indicating whether offers may be recorded now.</summary>
    public bool AcceptsOfferActivity => DealRules.DealAcceptsOfferActivity((int)Status);

    /// <summary>Gets a value indicating whether commercial terms have been agreed.</summary>
    public bool HasAgreedTerms => Status == DealStatus.TermsAgreed;

    /// <summary>The statuses reachable from where the deal is now.</summary>
    /// <remarks>Derived from the rules kernel, so the published table is the enforced one.</remarks>
    public static IReadOnlySet<DealStatus> ReachableFrom(DealStatus status) =>
        new HashSet<DealStatus>(
            DealRules.ReachableDealStates((int)status).Select(code => (DealStatus)code));

    /// <summary>Whether a transition is legal from a status.</summary>
    public static bool Permits(DealStatus status, DealTransition transition) =>
        DealRules.DealPermits((int)status, (int)transition);

    /// <summary>
    /// Opens a negotiation against a target that has reached commercial discussion.
    /// </summary>
    /// <remarks>
    /// The target's stage is checked by the caller, which holds it; this refuses
    /// only what it can see. Opening is always an explicit act - nothing creates a
    /// deal because a target became interested, because "we are negotiating" is a
    /// claim about the world that a state change in a pipeline cannot make.
    /// </remarks>
    public static Deal Open(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunityTargetId opportunityTargetId,
        string name,
        DealKind kind,
        UserId ownerUserId,
        DateOnly openedOn,
        UserId createdBy,
        DateTimeOffset now,
        string? reference = null,
        string? summary = null,
        string? strategyNotes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown deal kind '{kind}'.");
        }

        Deal deal = new()
        {
            Id = DealId.New(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            OpportunityTargetId = opportunityTargetId,
            Name = Ensure.NotBlankMax(name, nameof(name), 300),
            Reference = Ensure.OptionalMax(reference, nameof(reference), 100),
            Kind = kind,
            Status = DealStatus.Draft,
            OwnerUserId = ownerUserId,
            OpenedOn = openedOn,
            Summary = Ensure.OptionalMax(summary, nameof(summary), 4000),
            StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        deal._events.Add(DealEvent.Record(
            organizationId,
            deal.Id,
            DealEventKind.Opened,
            null,
            DealStatus.Draft,
            transition: null,
            now,
            createdBy,
            reason: null));

        return deal;
    }

    /// <summary>Edits the descriptive fields. Never the status.</summary>
    public void UpdateMetadata(
        string name,
        DealKind kind,
        UserId ownerUserId,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reference = null,
        string? summary = null,
        string? strategyNotes = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown deal kind '{kind}'.");
        }

        // The kind selects which terms are meaningful, so changing it once terms
        // exist would retroactively invalidate them. Terms are frozen on recorded
        // offers, so there would be no way to correct them either.
        if (kind != Kind && Status != DealStatus.Draft)
        {
            throw new DomainException(
                "A deal's kind can only be changed while it is a draft, "
                + "because recorded offers carry terms chosen for the kind it was.");
        }

        Name = Ensure.NotBlankMax(name, nameof(name), 300);
        Reference = Ensure.OptionalMax(reference, nameof(reference), 100);
        Kind = kind;
        OwnerUserId = ownerUserId;
        Summary = Ensure.OptionalMax(summary, nameof(summary), 4000);
        StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000);

        _events.Add(DealEvent.Record(
            OrganizationId,
            Id,
            DealEventKind.MetadataUpdated,
            Status,
            Status,
            transition: null,
            now,
            actor,
            reason: null));

        Touch(now);
    }

    /// <summary>
    /// Moves the deal into negotiation because an offer was recorded.
    /// </summary>
    /// <remarks>
    /// Not a status a caller may set. A deal is negotiating because something is
    /// on the table, and the only way to put something on the table is to record
    /// it.
    /// </remarks>
    public void NoteOfferRecorded(DateTimeOffset now, UserId actor) =>
        Advance(DealTransition.OfferRecorded, now, actor, reason: null, closedOn: null);

    /// <summary>
    /// Agrees the deal's terms because an offer was accepted.
    /// </summary>
    /// <remarks>
    /// The only route to <see cref="DealStatus.TermsAgreed"/>. There is no command
    /// that sets it, so a deal cannot claim agreed terms with no accepted offer
    /// behind it.
    /// </remarks>
    public void NoteOfferAccepted(DateTimeOffset now, UserId actor) =>
        Advance(DealTransition.OfferAccepted, now, actor, reason: null, closedOn: null);

    /// <summary>
    /// Reopens the negotiation, unwinding an acceptance or a closure.
    /// </summary>
    /// <remarks>
    /// Explicit and consequential. When it unwinds an acceptance the caller
    /// supersedes the accepted offer in the same transaction, so what was agreed
    /// on the day survives as a record rather than being edited into something
    /// else.
    /// </remarks>
    public void Reopen(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(DealTransition.NegotiationReopened, now, actor, reason, closedOn: null);
    }

    /// <summary>Closes the negotiation without agreement.</summary>
    public void CloseWithoutAgreement(
        DateOnly closedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(DealTransition.ClosedNoDeal, now, actor, reason, closedOn);
    }

    /// <summary>Calls the negotiation off.</summary>
    public void Cancel(
        DateOnly closedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(DealTransition.Cancelled, now, actor, reason, closedOn);
    }

    /// <summary>Applies a transition through the rules kernel, or refuses it.</summary>
    private void Advance(
        DealTransition transition,
        DateTimeOffset now,
        UserId actor,
        string? reason,
        DateOnly? closedOn)
    {
        int next = DealRules.NextDealState((int)Status, (int)transition);

        if (next == 0)
        {
            throw new DomainException($"A {Status} deal cannot record '{transition}'.");
        }

        DealStatus from = Status;
        DealStatus to = (DealStatus)next;

        // Recording another offer in an already-negotiating deal is legal and
        // changes nothing; writing an event for it would fill the timeline with
        // rows saying the status stayed the same.
        if (from == to && transition == DealTransition.OfferRecorded)
        {
            Touch(now);
            return;
        }

        if (closedOn is { } closed)
        {
            if (closed < OpenedOn)
            {
                throw new DomainException("A deal cannot end before it opened.");
            }

            ClosedOn = closed;
        }
        else if (to is DealStatus.Negotiating or DealStatus.Draft)
        {
            // Reopening a closed negotiation makes it live again, so the end date
            // stops being true.
            ClosedOn = null;
        }

        Status = to;

        _events.Add(DealEvent.Record(
            OrganizationId,
            Id,
            DealEventKind.StatusChanged,
            from,
            to,
            transition,
            now,
            actor,
            reason));

        Touch(now);
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Deal), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
