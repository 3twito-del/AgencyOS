using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for a <see cref="Contract"/>.</summary>
public readonly record struct ContractId(Guid Value)
{
    public static ContractId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of legal instrument this is.
/// </summary>
/// <remarks>
/// One deal routinely produces several: a long-form agreement, a short-form memo
/// signed first, a side letter covering something the main paper does not, and an
/// amendment months later. Forcing one contract per deal would make three of those
/// four unrecordable (ADR-0022).
/// </remarks>
public enum ContractKind
{
    /// <summary>The full agreement.</summary>
    LongForm = 1,

    /// <summary>A short-form or deal memo, often signed before the long form exists.</summary>
    ShortForm = 2,

    /// <summary>A letter covering something the main agreement does not.</summary>
    SideLetter = 3,

    /// <summary>A distinct instrument changing another. Never a version of it.</summary>
    Amendment = 4,

    /// <summary>An attachment carrying additional terms.</summary>
    Rider = 5,

    /// <summary>An agreement with a lending entity rather than the individual.</summary>
    LoanOut = 6,

    /// <summary>A separate services agreement arising from the same transaction.</summary>
    ServicesAgreement = 7,

    Other = 99,
}

/// <summary>
/// Where a contract stands as a legal instrument.
/// </summary>
/// <remarks>
/// <para>
/// Values match the F# rules kernel's own codes; a test walks both vocabularies in
/// each direction.
/// </para>
/// <para>
/// <see cref="PartiallyExecuted"/> and <see cref="Executed"/> are reached by
/// recording signatures, never by a status command, so a contract cannot claim to
/// be executed with nobody's signature behind it. Execution says nothing about
/// effectiveness, which is a separate recorded date (ADR-0022).
/// </para>
/// </remarks>
public enum ContractStatus
{
    /// <summary>Being drafted.</summary>
    Draft = 1,

    /// <summary>Circulated for review.</summary>
    UnderReview = 2,

    /// <summary>Cleared internally; awaiting signatures.</summary>
    ApprovedForExecution = 3,

    /// <summary>Some required signatories have signed; not all.</summary>
    PartiallyExecuted = 4,

    /// <summary>Every required signatory has signed.</summary>
    Executed = 5,

    /// <summary>Drafting stopped without agreement. Terminal.</summary>
    Abandoned = 6,

    /// <summary>Replaced by another instrument. Terminal.</summary>
    Superseded = 7,

    /// <summary>Ended after execution. Terminal.</summary>
    Terminated = 8,
}

/// <summary>What causes a contract to change status.</summary>
public enum ContractTransition
{
    SentForReview = 1,
    ReturnedToDrafting = 2,
    ApprovedForSignature = 3,

    /// <summary>A required signatory signed, and others remain. Not caller-requestable.</summary>
    SignatureRecorded = 4,

    /// <summary>The last required signatory signed. Not caller-requestable.</summary>
    ExecutionCompleted = 5,

    DraftingAbandoned = 6,
    ReplacedByAnother = 7,
    TerminationRecorded = 8,
}

/// <summary>
/// How sensitive a piece of legal content is.
/// </summary>
/// <remarks>
/// <para>
/// Assigned by a person, never inferred. AgencyOS must not decide that something
/// is attorney-client privileged: privilege is a legal status with legal
/// consequences, and a system that guessed would be wrong in both directions -
/// withholding what could be shared, and worse, sharing what could not
/// (ADR-0022).
/// </para>
/// <para>
/// The default is <see cref="Ordinary"/>, so nothing is privileged by accident and
/// marking something is a deliberate act.
/// </para>
/// </remarks>
public enum PrivilegeClass
{
    /// <summary>Ordinary internal content. Visible with contract read access.</summary>
    Ordinary = 1,

    /// <summary>Commercially confidential, but not legal advice.</summary>
    Confidential = 2,

    /// <summary>The agency's own legal strategy.</summary>
    LegalStrategy = 3,

    /// <summary>Communications a person has classified as privileged.</summary>
    AttorneyClientPrivileged = 4,
}

/// <summary>What kind of thing a contract event records.</summary>
public enum ContractEventKind
{
    Opened = 1,
    StatusChanged = 2,
    VersionRecorded = 3,
    SignatureRecorded = 4,
    MetadataUpdated = 5,
    EffectivenessRecorded = 6,
}

/// <summary>
/// A recorded change to a contract.
/// </summary>
/// <remarks>
/// Append-only, and distinct from the audit trail: the audit log answers who did
/// what under which permission, and this answers what happened to the instrument.
/// A legal history rendered from audit rows would be a security artefact shown to
/// a lawyer (ADR-0012).
/// </remarks>
public sealed class ContractEvent
{
    private ContractEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    public ContractEventKind Kind { get; private set; }

    public ContractStatus? FromStatus { get; private set; }

    public ContractStatus ToStatus { get; private set; }

    public ContractTransition? Transition { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Detail { get; private set; }

    public string? Reason { get; private set; }

    internal static ContractEvent Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractEventKind kind,
        ContractStatus? fromStatus,
        ContractStatus toStatus,
        ContractTransition? transition,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? detail = null,
        string? reason = null)
    {
        return new ContractEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractId = contractId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Transition = transition,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Detail = Ensure.OptionalMax(detail, nameof(detail), 1000),
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// A legal agreement arising from a deal.
/// </summary>
/// <remarks>
/// <para>
/// Anchored to the deal and to <em>the offer that was accepted</em>, not to
/// whatever is accepted now. If M7 later reopens the negotiation and that offer
/// becomes superseded, the contract still records the commercial snapshot it was
/// drafted to implement - which is exactly the question a lawyer asks when the
/// paper and the deal have drifted apart (ADR-0022).
/// </para>
/// <para>
/// Fact, analysis and strategy are three fields rather than one Notes box.
/// "This version contains a 12-month exclusivity clause" is a fact about the
/// document; "counsel believes that is overbroad" is analysis; "trade exclusivity
/// for a higher guarantee" is strategy. They have different readerships and
/// different sensitivities, and one field could not serve any of them.
/// </para>
/// </remarks>
public sealed class Contract
{
    /// <summary>Statuses in which the instrument is finished.</summary>
    public static IReadOnlySet<ContractStatus> TerminalStatuses { get; } =
        new HashSet<ContractStatus>
        {
            ContractStatus.Abandoned,
            ContractStatus.Superseded,
            ContractStatus.Terminated,
        };

    private readonly List<ContractEvent> _events = [];
    private readonly List<ContractParty> _parties = [];
    private readonly List<ContractSignature> _signatures = [];

    private Contract()
    {
    }

    public ContractId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The negotiation this instrument implements.</summary>
    public DealId DealId { get; private set; }

    /// <summary>
    /// The commercial snapshot it was drafted from.
    /// </summary>
    /// <remarks>
    /// Fixed at creation. A reopened negotiation does not repoint it, because the
    /// question "what were we papering" has one answer and it is historical.
    /// </remarks>
    public OfferId AcceptedOfferId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>The agency's or counsel's own reference for the instrument.</summary>
    public string? Reference { get; private set; }

    public ContractKind Kind { get; private set; }

    public ContractStatus Status { get; private set; }

    public UserId OwnerUserId { get; private set; }

    /// <summary>Factual description of what the instrument is.</summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// What counsel or the agency thinks of the drafting. Requires
    /// <c>contracts.privileged.read</c> when classified above ordinary.
    /// </summary>
    public string? LegalAnalysis { get; private set; }

    /// <summary>The agency's negotiating strategy on the paper.</summary>
    public string? StrategyNotes { get; private set; }

    /// <summary>How sensitive the analysis and strategy are. Assigned, never inferred.</summary>
    public PrivilegeClass Privilege { get; private set; }

    /// <summary>
    /// When the last required signature was recorded.
    /// </summary>
    /// <remarks>
    /// Derived from the signatures rather than typed in, and deliberately separate
    /// from <see cref="EffectiveOn"/>: a contract can be signed in March and
    /// effective from January.
    /// </remarks>
    public DateOnly? ExecutedOn { get; private set; }

    /// <summary>
    /// When the agreement takes effect, as recorded.
    /// </summary>
    /// <remarks>
    /// Never inferred from execution. Contracts are routinely effective on
    /// signature, retroactively, or on a later event, and assuming the first would
    /// misdate every obligation measured from it.
    /// </remarks>
    public DateOnly? EffectiveOn { get; private set; }

    /// <summary>When the agreement ended, as recorded.</summary>
    public DateOnly? TerminatedOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ContractEvent> Events => _events;

    public IReadOnlyCollection<ContractParty> Parties => _parties;

    public IReadOnlyCollection<ContractSignature> Signatures => _signatures;

    /// <summary>Gets a value indicating whether the instrument can no longer move.</summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>Gets a value indicating whether every required signatory has signed.</summary>
    public bool IsFullyExecuted => DealRules.IsContractFullyExecuted((int)Status);

    /// <summary>Gets a value indicating whether a new drafting version may be recorded.</summary>
    public bool AcceptsNewVersions => DealRules.ContractAcceptsNewVersions((int)Status);

    /// <summary>Gets a value indicating whether signatures may be recorded now.</summary>
    public bool AcceptsSignatures => DealRules.ContractAcceptsSignatures((int)Status);

    /// <summary>The parties whose signature the agreement requires.</summary>
    public IReadOnlyCollection<ContractParty> RequiredSignatories =>
        [.. _parties.Where(party => party.IsRequiredSignatory)];

    /// <summary>Required signatories who have not signed yet.</summary>
    public IReadOnlyCollection<ContractParty> OutstandingSignatories =>
        [.. _parties.Where(party =>
            party.IsRequiredSignatory && _signatures.All(signature => signature.ContractPartyId != party.Id))];

    /// <summary>The statuses reachable from where the contract is now.</summary>
    /// <remarks>Derived from the rules kernel, so the published table is the enforced one.</remarks>
    public static IReadOnlySet<ContractStatus> ReachableFrom(ContractStatus status)
    {
        HashSet<ContractStatus> reachable = [];

        foreach (ContractTransition transition in Enum.GetValues<ContractTransition>())
        {
            int next = DealRules.NextContractState((int)status, (int)transition);

            if (next != 0)
            {
                reachable.Add((ContractStatus)next);
            }
        }

        return reachable;
    }

    /// <summary>Whether a transition is legal from a status.</summary>
    public static bool Permits(ContractStatus status, ContractTransition transition) =>
        DealRules.ContractPermits((int)status, (int)transition);

    /// <summary>Opens a contract against an agreed deal.</summary>
    /// <remarks>
    /// The caller establishes that the deal's terms are agreed and that the offer
    /// is the accepted one; this refuses only what it can see.
    /// </remarks>
    public static Contract Open(
        OrganizationId organizationId,
        DealId dealId,
        OfferId acceptedOfferId,
        string title,
        ContractKind kind,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? reference = null,
        string? summary = null,
        string? legalAnalysis = null,
        string? strategyNotes = null,
        PrivilegeClass privilege = PrivilegeClass.Ordinary)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown contract kind '{kind}'.");
        }

        if (!Enum.IsDefined(privilege))
        {
            throw new DomainException($"Unknown privilege class '{privilege}'.");
        }

        Contract contract = new()
        {
            Id = ContractId.New(),
            OrganizationId = organizationId,
            DealId = dealId,
            AcceptedOfferId = acceptedOfferId,
            Title = Ensure.NotBlankMax(title, nameof(title), 300),
            Reference = Ensure.OptionalMax(reference, nameof(reference), 100),
            Kind = kind,
            Status = ContractStatus.Draft,
            OwnerUserId = ownerUserId,
            Summary = Ensure.OptionalMax(summary, nameof(summary), 4000),
            LegalAnalysis = Ensure.OptionalMax(legalAnalysis, nameof(legalAnalysis), 8000),
            StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000),
            Privilege = privilege,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        contract._events.Add(ContractEvent.Record(
            organizationId,
            contract.Id,
            ContractEventKind.Opened,
            null,
            ContractStatus.Draft,
            transition: null,
            now,
            createdBy));

        return contract;
    }

    /// <summary>Edits the descriptive fields. Never the status or the lineage.</summary>
    public void UpdateMetadata(
        string title,
        ContractKind kind,
        UserId ownerUserId,
        PrivilegeClass privilege,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reference = null,
        string? summary = null,
        string? legalAnalysis = null,
        string? strategyNotes = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown contract kind '{kind}'.");
        }

        if (!Enum.IsDefined(privilege))
        {
            throw new DomainException($"Unknown privilege class '{privilege}'.");
        }

        Title = Ensure.NotBlankMax(title, nameof(title), 300);
        Reference = Ensure.OptionalMax(reference, nameof(reference), 100);
        Kind = kind;
        OwnerUserId = ownerUserId;
        Privilege = privilege;
        Summary = Ensure.OptionalMax(summary, nameof(summary), 4000);
        LegalAnalysis = Ensure.OptionalMax(legalAnalysis, nameof(legalAnalysis), 8000);
        StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000);

        _events.Add(ContractEvent.Record(
            OrganizationId,
            Id,
            ContractEventKind.MetadataUpdated,
            Status,
            Status,
            transition: null,
            now,
            actor));

        Touch(now);
    }

    /// <summary>Adds a party to the agreement.</summary>
    public ContractParty AddParty(
        ContractPartyRef party,
        ContractPartyRole role,
        bool isRequiredSignatory,
        DateTimeOffset now,
        int expectedVersion,
        string? notes = null)
    {
        RequireVersion(expectedVersion);
        RequireNotTerminal("add a party to");

        ContractParty added = ContractParty.Create(
            OrganizationId, Id, party, role, isRequiredSignatory, notes);

        _parties.Add(added);

        Touch(now);

        return added;
    }

    /// <summary>
    /// Records that a party signed, and advances execution when that was the last one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only route to <see cref="ContractStatus.PartiallyExecuted"/> and
    /// <see cref="ContractStatus.Executed"/>. There is no command that sets either,
    /// so a contract cannot claim execution with nobody's signature behind it.
    /// </para>
    /// <para>
    /// It also means a contract with no declared required signatory cannot become
    /// executed. That is deliberate: naming who must sign is the work, and a system
    /// that let execution be asserted without it would be recording a conclusion
    /// nobody reached.
    /// </para>
    /// </remarks>
    public ContractSignature RecordSignature(
        Guid contractPartyId,
        DateOnly signedOn,
        SignatureMethod method,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? externalReference = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        if (!AcceptsSignatures)
        {
            throw new DomainException(
                $"This contract is {Status.ToString().ToLowerInvariant()}, so a signature cannot be "
                + "recorded against it. Approve it for execution first.");
        }

        ContractParty party = _parties.FirstOrDefault(x => x.Id == contractPartyId)
            ?? throw new DomainException("That party is not on this contract.");

        if (_signatures.Any(x => x.ContractPartyId == contractPartyId))
        {
            throw new DomainException($"{party.DisplayLabel} has already signed this contract.");
        }

        ContractSignature signature = ContractSignature.Create(
            OrganizationId, Id, contractPartyId, signedOn, method, now, actor, externalReference, notes);

        _signatures.Add(signature);

        // Execution follows from who still has to sign, computed after this one is
        // in the list rather than from a counter that could drift.
        bool complete = RequiredSignatories.Count > 0 && OutstandingSignatories.Count == 0;

        Advance(
            complete ? ContractTransition.ExecutionCompleted : ContractTransition.SignatureRecorded,
            now,
            actor,
            reason: null,
            detail: party.DisplayLabel);

        if (complete)
        {
            // The day the last required signature was given, not the day it was
            // typed in. Signatures are routinely recorded after the fact.
            ExecutedOn = _signatures.Max(x => x.SignedOn);
        }

        return signature;
    }

    /// <summary>Moves the contract through its drafting lifecycle.</summary>
    /// <remarks>
    /// Refuses the two transitions that must follow from signatures, naming the act
    /// that would produce them instead.
    /// </remarks>
    public void ChangeStatus(
        ContractTransition transition,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null,
        DateOnly? terminatedOn = null)
    {
        RequireVersion(expectedVersion);

        if (!DealRules.IsCallerRequestableContractTrigger((int)transition))
        {
            throw new DomainException(
                $"'{transition}' is not something a caller sets. "
                + "A contract becomes executed by recording the signatures it requires.");
        }

        if (transition == ContractTransition.TerminationRecorded)
        {
            TerminatedOn = terminatedOn
                ?? throw new DomainException("Terminating a contract needs the date it ended.");
        }

        Advance(transition, now, actor, reason, detail: null);
    }

    /// <summary>
    /// Records when the agreement takes effect.
    /// </summary>
    /// <remarks>
    /// Separate from execution and never derived from it. A date before execution
    /// is accepted, because retroactive effectiveness is ordinary.
    /// </remarks>
    public void RecordEffectiveDate(
        DateOnly effectiveOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status is ContractStatus.Abandoned)
        {
            throw new DomainException("An abandoned contract does not take effect.");
        }

        if (TerminatedOn is { } ended && effectiveOn > ended)
        {
            throw new DomainException("A contract cannot take effect after it ended.");
        }

        EffectiveOn = effectiveOn;

        _events.Add(ContractEvent.Record(
            OrganizationId,
            Id,
            ContractEventKind.EffectivenessRecorded,
            Status,
            Status,
            transition: null,
            now,
            actor,
            detail: effectiveOn.ToString("O")));

        Touch(now);
    }

    /// <summary>
    /// Notes that a drafting version was recorded, for the timeline.
    /// </summary>
    /// <remarks>
    /// Called by the handler that records the version. The version is its own
    /// aggregate, so the contract learns about it the way it learns about a
    /// signature: as a consequence applied in the same transaction.
    /// </remarks>
    public void NoteVersionRecorded(string label, DateTimeOffset now, UserId actor)
    {
        _events.Add(ContractEvent.Record(
            OrganizationId,
            Id,
            ContractEventKind.VersionRecorded,
            Status,
            Status,
            transition: null,
            now,
            actor,
            detail: label));

        Touch(now);
    }

    /// <summary>
    /// Whether the agreement is in force on a date.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, from three facts: an effective date has been
    /// recorded, it has arrived, and the agreement has not ended. A stored flag
    /// would be wrong from the moment the clock moved past either boundary.
    /// </remarks>
    public bool IsEffectiveOn(DateOnly on) =>
        EffectiveOn is { } from
        && on >= from
        && Status is not (ContractStatus.Abandoned or ContractStatus.Superseded)
        && (TerminatedOn is not { } ended || on <= ended);

    private void Advance(
        ContractTransition transition,
        DateTimeOffset now,
        UserId actor,
        string? reason,
        string? detail)
    {
        int next = DealRules.NextContractState((int)Status, (int)transition);

        if (next == 0)
        {
            throw new DomainException($"A {Status} contract cannot record '{transition}'.");
        }

        ContractStatus from = Status;
        ContractStatus to = (ContractStatus)next;

        Status = to;

        _events.Add(ContractEvent.Record(
            OrganizationId,
            Id,
            transition == ContractTransition.SignatureRecorded
                || transition == ContractTransition.ExecutionCompleted
                    ? ContractEventKind.SignatureRecorded
                    : ContractEventKind.StatusChanged,
            from,
            to,
            transition,
            now,
            actor,
            detail,
            reason));

        Touch(now);
    }

    private void RequireNotTerminal(string action)
    {
        if (IsTerminal)
        {
            throw new DomainException(
                $"This contract is {Status.ToString().ToLowerInvariant()}, so it is not possible to {action} it.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Contract), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
