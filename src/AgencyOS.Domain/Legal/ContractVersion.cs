using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for a <see cref="ContractVersion"/>.</summary>
public readonly record struct ContractVersionId(Guid Value)
{
    public static ContractVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="ContractTerm"/>.</summary>
public readonly record struct ContractTermId(Guid Value)
{
    public static ContractTermId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Which way a drafting version travelled.</summary>
public enum VersionDirection
{
    /// <summary>Received from the counterparty or their counsel.</summary>
    Inbound = 1,

    /// <summary>Sent by the agency or its counsel.</summary>
    Outbound = 2,

    /// <summary>Prepared internally and not yet circulated.</summary>
    Internal = 3,
}

/// <summary>
/// Where a drafting version stands.
/// </summary>
/// <remarks>
/// A draft is a working transcription whose extracted terms may still be
/// corrected. Once recorded it is a milestone somebody relied on, and its terms
/// are frozen; superseded says a later version replaced it (ADR-0022).
/// </remarks>
public enum ContractVersionStatus
{
    /// <summary>Being transcribed. Terms editable.</summary>
    Draft = 1,

    /// <summary>Recorded as a drafting milestone. Terms frozen.</summary>
    Recorded = 2,

    /// <summary>A later version replaced it.</summary>
    Superseded = 3,
}

/// <summary>
/// One drafting state of a contract.
/// </summary>
/// <remarks>
/// <para>
/// v1 the counterparty's draft, v2 counsel's markup, v3 the revised draft, v4 the
/// execution copy. Each is a thing somebody read and formed a view on, so
/// recording v3 must not overwrite what v2 said.
/// </para>
/// <para>
/// <strong>AgencyOS does not hold the document.</strong> This row carries a
/// reference to wherever the file actually lives - an external system, a matter
/// number, a filename somebody will recognise - and no content hash, because
/// hashing bytes the system has never seen would be a claim it cannot support.
/// Canonical document storage arrives in M10, and these fields are the seam it
/// will fill (ADR-0022).
/// </para>
/// </remarks>
public sealed class ContractVersion
{
    private readonly List<ContractTerm> _terms = [];

    private ContractVersion()
    {
    }

    public ContractVersionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>Position in the drafting sequence. Unique per contract.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>What the agency calls it: "counsel markup", "execution copy".</summary>
    public string Label { get; private set; } = string.Empty;

    public VersionDirection Direction { get; private set; }

    public ContractVersionStatus Status { get; private set; }

    /// <summary>When AgencyOS was told about it.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>When it arrived, when it came from outside.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>When it went out, when the agency sent it.</summary>
    public DateOnly? SentOn { get; private set; }

    public UserId RecordedBy { get; private set; }

    /// <summary>
    /// Where the document actually lives.
    /// </summary>
    /// <remarks>
    /// Opaque to AgencyOS: a matter reference, a document-management identifier, a
    /// URL somebody with access can open. It is a pointer, not a copy.
    /// </remarks>
    public string? ExternalReference { get; private set; }

    /// <summary>Which system the reference belongs to.</summary>
    public string? SourceSystem { get; private set; }

    /// <summary>The filename as a person would recognise it.</summary>
    public string? DisplayFileName { get; private set; }

    /// <summary>The document's media type, as reported.</summary>
    public string? MediaType { get; private set; }

    /// <summary>What changed in this version, factually.</summary>
    public string? Notes { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ContractTerm> Terms => _terms;

    /// <summary>Gets a value indicating whether the extracted terms may still be edited.</summary>
    public bool IsEditable => Status == ContractVersionStatus.Draft;

    /// <summary>
    /// Gets a value indicating whether AgencyOS holds the document itself.
    /// </summary>
    /// <remarks>
    /// Always false in M8, and stated as a property rather than left implicit so
    /// the Windows surface can say so without guessing. It becomes meaningful when
    /// M10 brings a document repository.
    /// </remarks>
    public static bool HoldsDocument => false;

    public static ContractVersion Start(
        OrganizationId organizationId,
        ContractId contractId,
        int versionNumber,
        string label,
        VersionDirection direction,
        UserId recordedBy,
        DateTimeOffset now,
        DateOnly? receivedOn = null,
        DateOnly? sentOn = null,
        string? externalReference = null,
        string? sourceSystem = null,
        string? displayFileName = null,
        string? mediaType = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new DomainException($"Unknown version direction '{direction}'.");
        }

        if (versionNumber < 1)
        {
            throw new DomainException("A contract version's number must be positive.");
        }

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        if (receivedOn is { } received && received > today)
        {
            throw new DomainException("A version cannot have arrived in the future.");
        }

        if (sentOn is { } sent && sent > today)
        {
            throw new DomainException("A version cannot have been sent in the future.");
        }

        return new ContractVersion
        {
            Id = ContractVersionId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            VersionNumber = versionNumber,
            Label = Ensure.NotBlankMax(label, nameof(label), 200),
            Direction = direction,
            Status = ContractVersionStatus.Draft,
            RecordedAt = now,
            ReceivedOn = receivedOn,
            SentOn = sentOn,
            RecordedBy = recordedBy,
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 500),
            SourceSystem = Ensure.OptionalMax(sourceSystem, nameof(sourceSystem), 100),
            DisplayFileName = Ensure.OptionalMax(displayFileName, nameof(displayFileName), 300),
            MediaType = Ensure.OptionalMax(mediaType, nameof(mediaType), 100),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Adds an extracted term while the transcription is still a draft.</summary>
    public ContractTerm AddTerm(
        ContractTermCode code,
        DealTermValue value,
        DateTimeOffset now,
        int expectedVersion,
        string? clauseReference = null,
        string? label = null,
        string? notes = null,
        PrivilegeClass privilege = PrivilegeClass.Ordinary)
    {
        RequireVersion(expectedVersion);
        RequireEditable("add terms to");

        if (_terms.Any(term => term.Code == code))
        {
            throw new DomainException(
                $"This version already records '{ContractTermCatalog.Require(code).DisplayName}'. "
                + "Reconciliation joins on the term, so each appears once.");
        }

        int sequence = _terms.Count == 0 ? 1 : _terms.Max(term => term.Sequence) + 1;

        ContractTerm added = ContractTerm.Create(
            OrganizationId, Id, code, value, sequence, clauseReference, label, notes, privilege);

        _terms.Add(added);

        Touch(now);

        return added;
    }

    /// <summary>Replaces an extracted term's value while the transcription is a draft.</summary>
    public ContractTerm UpdateTerm(
        ContractTermCode code,
        DealTermValue value,
        DateTimeOffset now,
        int expectedVersion,
        string? clauseReference = null,
        string? label = null,
        string? notes = null,
        PrivilegeClass privilege = PrivilegeClass.Ordinary)
    {
        RequireVersion(expectedVersion);
        RequireEditable("change terms on");

        ContractTerm existing = _terms.FirstOrDefault(term => term.Code == code)
            ?? throw new DomainException(
                $"This version does not record '{ContractTermCatalog.Require(code).DisplayName}'.");

        ContractTerm replacement = ContractTerm.Create(
            OrganizationId,
            Id,
            code,
            value,
            existing.Sequence,
            clauseReference,
            label,
            notes,
            privilege);

        _terms.Remove(existing);
        _terms.Add(replacement);

        Touch(now);

        return replacement;
    }

    /// <summary>Removes an extracted term while the transcription is a draft.</summary>
    public void RemoveTerm(ContractTermCode code, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireEditable("remove terms from");

        ContractTerm existing = _terms.FirstOrDefault(term => term.Code == code)
            ?? throw new DomainException(
                $"This version does not record '{ContractTermCatalog.Require(code).DisplayName}'.");

        _terms.Remove(existing);

        Touch(now);
    }

    /// <summary>
    /// Records the version as a drafting milestone, freezing its terms.
    /// </summary>
    /// <remarks>
    /// A version with no extracted terms is allowed, unlike an offer with no terms:
    /// a lawyer routinely records that a draft arrived before anybody has read it,
    /// and refusing would push them to invent a term to get past the form.
    /// </remarks>
    public void Record(DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != ContractVersionStatus.Draft)
        {
            throw new DomainException(
                $"This version is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = ContractVersionStatus.Recorded;

        Touch(now);
    }

    /// <summary>
    /// Marks the version replaced by a later one.
    /// </summary>
    /// <remarks>
    /// Called by the handler that records the later version, because supersession
    /// is a fact about the sequence rather than about either row alone. Idempotent,
    /// so replaying a lost acknowledgement supersedes nothing twice.
    /// </remarks>
    public void NoteSuperseded(DateTimeOffset now)
    {
        if (Status == ContractVersionStatus.Superseded)
        {
            return;
        }

        Status = ContractVersionStatus.Superseded;

        Touch(now);
    }

    /// <summary>The flat shapes the rules kernel reconciles.</summary>
    public TermInput[] ToRulesTerms() =>
        [.. _terms.OrderBy(term => term.Sequence).Select(term => term.ToRulesInput())];

    private void RequireEditable(string action)
    {
        if (!IsEditable)
        {
            throw new DomainException(
                $"This version was recorded as {Status.ToString().ToLowerInvariant()}, so it is not "
                + $"possible to {action} it. Record a later version instead.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(ContractVersion), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
