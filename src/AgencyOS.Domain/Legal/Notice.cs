using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for a <see cref="NoticeRequirement"/>.</summary>
public readonly record struct NoticeRequirementId(Guid Value)
{
    public static NoticeRequirementId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How a notice must be, or was, delivered.
/// </summary>
/// <remarks>
/// Descriptive. AgencyOS delivers nothing: <see cref="Email"/> means the contract
/// permits email, or somebody reported that email was used (ADR-0022).
/// </remarks>
public enum NoticeMethod
{
    /// <summary>Written notice, method unspecified.</summary>
    Written = 1,

    Email = 2,
    Courier = 3,
    RegisteredPost = 4,
    HandDelivery = 5,

    Other = 99,
}

/// <summary>Which way a recorded notice travelled.</summary>
public enum NoticeDirection
{
    /// <summary>Given by our side.</summary>
    Given = 1,

    /// <summary>Received from the other side.</summary>
    Received = 2,
}

/// <summary>
/// A contractual rule about giving notice.
/// </summary>
/// <remarks>
/// Separate from a generic interaction, because a notice requirement is a rule
/// with a deadline and consequences rather than a conversation. "Sixty days before
/// the option expires, in writing, to the address in clause 14" is a thing that
/// has to be surfaced before it matters, and an interaction record surfaces
/// nothing (ADR-0022).
/// </remarks>
public sealed class NoticeRequirement
{
    private NoticeRequirement()
    {
    }

    public NoticeRequirementId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The drafting version this requirement was read from.</summary>
    public ContractVersionId ContractVersionId { get; private set; }

    public string? ClauseReference { get; private set; }

    /// <summary>The party who must give the notice. A party on this contract.</summary>
    public Guid ObligorPartyId { get; private set; }

    /// <summary>The party who must receive it. A party on this contract.</summary>
    public Guid RecipientPartyId { get; private set; }

    /// <summary>What the notice is about.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>When it must be given, as the contract expresses it.</summary>
    public DeadlineRule Due { get; private set; } = null!;

    /// <summary>The deadline once it is knowable. A cache of the rule.</summary>
    public DateOnly? ResolvedDueOn { get; private set; }

    /// <summary>How the contract requires it to be delivered.</summary>
    public NoticeMethod Method { get; private set; }

    /// <summary>The address or contact the contract names, as written.</summary>
    public string? AddressReference { get; private set; }

    /// <summary>The option this notice concerns, when it concerns one.</summary>
    public ContractOptionId? RelatedOptionId { get; private set; }

    /// <summary>The obligation this notice concerns, when it concerns one.</summary>
    public ObligationId? RelatedObligationId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public static NoticeRequirement Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        Guid obligorPartyId,
        Guid recipientPartyId,
        string description,
        DeadlineRule due,
        NoticeMethod method,
        UserId recordedBy,
        DateTimeOffset now,
        DateOnly? anchorDate = null,
        string? clauseReference = null,
        string? addressReference = null,
        ContractOptionId? relatedOptionId = null,
        ObligationId? relatedObligationId = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(due);

        if (!Enum.IsDefined(method))
        {
            throw new DomainException($"Unknown notice method '{method}'.");
        }

        if (obligorPartyId == recipientPartyId)
        {
            throw new DomainException("A party cannot be required to give notice to itself.");
        }

        NoticeRequirement requirement = new()
        {
            Id = NoticeRequirementId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractVersionId = versionId,
            ClauseReference = Ensure.OptionalMax(clauseReference, nameof(clauseReference), 100),
            ObligorPartyId = obligorPartyId,
            RecipientPartyId = recipientPartyId,
            Description = Ensure.NotBlankMax(description, nameof(description), 2000),
            Due = due.Validated(),
            Method = method,
            AddressReference = Ensure.OptionalMax(addressReference, nameof(addressReference), 1000),
            RelatedOptionId = relatedOptionId,
            RelatedObligationId = relatedObligationId,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };

        requirement.ResolvedDueOn = due.Resolve(anchorDate);

        return requirement;
    }

    /// <summary>Recomputes the deadline once the event it hangs off has a date.</summary>
    public void ResolveDueDate(DateOnly? anchorDate, DateTimeOffset now, int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(NoticeRequirement), Id.ToString(), expectedVersion, Version);
        }

        ResolvedDueOn = Due.Resolve(anchorDate);

        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// A record that a notice was given or received.
/// </summary>
/// <remarks>
/// <strong>AgencyOS does not send notices.</strong> It has no outbound transport,
/// holds no credentials and cannot confirm that anything arrived. This row is
/// somebody's assertion that a notice passed between the parties, exactly as an M6
/// submission is somebody's assertion that material went out - and the commands
/// and screens say "record" for the same reason (ADR-0020, ADR-0022).
/// </remarks>
public sealed class NoticeRecord
{
    private NoticeRecord()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The requirement this notice answers, when it answers one.</summary>
    public NoticeRequirementId? NoticeRequirementId { get; private set; }

    public NoticeDirection Direction { get; private set; }

    /// <summary>The party who gave it.</summary>
    public Guid SenderPartyId { get; private set; }

    /// <summary>The party it went to.</summary>
    public Guid RecipientPartyId { get; private set; }

    /// <summary>The day it was given or received, as reported. Freely backdated.</summary>
    public DateOnly OccurredOn { get; private set; }

    public NoticeMethod Method { get; private set; }

    /// <summary>An identifier from wherever the notice actually went.</summary>
    /// <remarks>Opaque. AgencyOS assigns it no meaning and verifies nothing against it.</remarks>
    public string? ExternalReference { get; private set; }

    /// <summary>What the notice said, factually.</summary>
    public string? Summary { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>When AgencyOS was told.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public static NoticeRecord Record(
        OrganizationId organizationId,
        ContractId contractId,
        NoticeDirection direction,
        Guid senderPartyId,
        Guid recipientPartyId,
        DateOnly occurredOn,
        NoticeMethod method,
        UserId recordedBy,
        DateTimeOffset now,
        NoticeRequirementId? requirementId = null,
        string? externalReference = null,
        string? summary = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new DomainException($"Unknown notice direction '{direction}'.");
        }

        if (!Enum.IsDefined(method))
        {
            throw new DomainException($"Unknown notice method '{method}'.");
        }

        if (senderPartyId == recipientPartyId)
        {
            throw new DomainException("A party cannot give notice to itself.");
        }

        if (occurredOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainException("A notice cannot have been given in the future.");
        }

        return new NoticeRecord
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractId = contractId,
            NoticeRequirementId = requirementId,
            Direction = direction,
            SenderPartyId = senderPartyId,
            RecipientPartyId = recipientPartyId,
            OccurredOn = occurredOn,
            Method = method,
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 200),
            Summary = Ensure.OptionalMax(summary, nameof(summary), 2000),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
        };
    }
}

/// <summary>
/// How one contract relates to another.
/// </summary>
/// <remarks>
/// An amendment is a distinct legal instrument that changes another, not version 5
/// of the original. Recording it as a version would erase that it was separately
/// negotiated, separately signed and separately effective (ADR-0022).
/// </remarks>
public enum ContractRelationshipKind
{
    /// <summary>This instrument amends the other.</summary>
    AmendmentOf = 1,

    /// <summary>This instrument replaces the other entirely.</summary>
    Supersedes = 2,

    /// <summary>This instrument is a side letter to the other.</summary>
    SideLetterTo = 3,

    /// <summary>This instrument restates the other.</summary>
    Restates = 4,

    /// <summary>Related, without one changing the other.</summary>
    RelatedTo = 5,
}

/// <summary>A link between two contracts.</summary>
public sealed class ContractRelationship
{
    private ContractRelationship()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The instrument the relationship is stated from.</summary>
    public ContractId ContractId { get; private set; }

    /// <summary>The instrument it points at.</summary>
    public ContractId RelatedContractId { get; private set; }

    public ContractRelationshipKind Kind { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public static ContractRelationship Create(
        OrganizationId organizationId,
        ContractId contractId,
        ContractId relatedContractId,
        ContractRelationshipKind kind,
        UserId recordedBy,
        DateTimeOffset now,
        string? notes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown contract relationship '{kind}'.");
        }

        if (contractId == relatedContractId)
        {
            throw new DomainException("A contract cannot be related to itself.");
        }

        return new ContractRelationship
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractId = contractId,
            RelatedContractId = relatedContractId,
            Kind = kind,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            RecordedAt = now,
            RecordedBy = recordedBy,
        };
    }
}

/// <summary>
/// Joins an ordinary task to the contract work it manages.
/// </summary>
/// <remarks>
/// The M6 and M7 shape, reused a third time. A task may point at the obligation or
/// option it is about, so "review the discrepancy" and "chase the signature" carry
/// their subject without <c>TaskItem</c> growing three more nullable columns.
/// </remarks>
public sealed class ContractTaskLink
{
    private ContractTaskLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The task. A task belongs to at most one contract.</summary>
    public TaskItemId TaskItemId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The obligation it manages, when it manages one.</summary>
    public ObligationId? ObligationId { get; private set; }

    /// <summary>The option it manages, when it manages one.</summary>
    public ContractOptionId? ContractOptionId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static ContractTaskLink Create(
        OrganizationId organizationId,
        TaskItemId taskItemId,
        ContractId contractId,
        DateTimeOffset now,
        ObligationId? obligationId = null,
        ContractOptionId? optionId = null)
    {
        if (taskItemId.Value == Guid.Empty)
        {
            throw new DomainException("A task link must name a task.");
        }

        return new ContractTaskLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            TaskItemId = taskItemId,
            ContractId = contractId,
            ObligationId = obligationId,
            ContractOptionId = optionId,
            LinkedAt = now,
        };
    }
}
