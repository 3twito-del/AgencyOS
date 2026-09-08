using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Documents;

/// <summary>
/// What a document or a message can be about.
/// </summary>
/// <remarks>
/// <para>
/// A closed, reviewed set. Each member has a real column and a real composite
/// foreign key behind it, so a link cannot name a record that does not exist or one
/// belonging to another tenant. That is the whole reason this is an enum rather
/// than a free-text entity name beside a bare GUID (ADR-0025).
/// </para>
/// <para>
/// Adding a member is a deliberate act: a column, a foreign key and a check
/// constraint. That cost is the point — it keeps the set small and keeps every
/// member honest.
/// </para>
/// </remarks>
public enum DocumentLinkTarget
{
    Person = 1,
    Company = 2,
    TalentProfile = 3,
    Material = 4,
    Project = 5,
    Package = 6,
    Opportunity = 7,
    Submission = 8,
    Deal = 9,
    Offer = 10,
    Contract = 11,
    ContractVersion = 12,
    Invoice = 13,
    Payment = 14,
}

/// <summary>
/// A structured association between a document and something it is about.
/// </summary>
/// <remarks>
/// <para>
/// One row carries a discriminator and exactly one typed identifier, checked by the
/// database. The alternative shapes were both worse: an untyped
/// <c>(entity_type, guid)</c> pair has no referential integrity at all and lets a
/// link point at a deleted or foreign-tenant record, while a table per target would
/// be fourteen tables that all say the same thing (ADR-0025).
/// </para>
/// <para>
/// A link is <strong>context, not authorization</strong>. Being able to read the
/// deal a document is linked to grants nothing about the document; document
/// sensitivity decides that on its own.
/// </para>
/// </remarks>
public sealed class DocumentLink
{
    private DocumentLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    /// <summary>Which kind of record the link points at.</summary>
    public DocumentLinkTarget Target { get; private set; }

    /// <summary>
    /// The record's identifier.
    /// </summary>
    /// <remarks>
    /// Persisted into the typed column matching <see cref="Target"/>, which carries
    /// the composite tenant foreign key. This property is the domain's single
    /// readable view of whichever column that was.
    /// </remarks>
    public Guid TargetId { get; private set; }

    /// <summary>Why the link exists, when somebody said.</summary>
    public string? Note { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public UserId LinkedBy { get; private set; }

    internal static DocumentLink Create(
        OrganizationId organizationId,
        DocumentId documentId,
        DocumentLinkTarget target,
        Guid targetId,
        UserId linkedBy,
        DateTimeOffset now,
        string? note)
    {
        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"'{target}' is not something a document can link to.");
        }

        if (targetId == Guid.Empty)
        {
            throw new DomainException("A link must name the record it points at.");
        }

        return new DocumentLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DocumentId = documentId,
            Target = target,
            TargetId = targetId,
            Note = Ensure.OptionalMax(note, nameof(note), 500),
            LinkedAt = now,
            LinkedBy = linkedBy,
        };
    }
}

/// <summary>What a document event records.</summary>
public enum DocumentEventKind
{
    Recorded = 1,
    VersionAdded = 2,
    MetadataChanged = 3,
    Linked = 4,
    Unlinked = 5,
    Archived = 6,
    Restored = 7,
    Downloaded = 8,
    TextExtracted = 9,
    ScanRecorded = 10,
}

/// <summary>
/// One entry in a document's curated history.
/// </summary>
/// <remarks>
/// One business act, one entry. Distinct from the audit trail, which answers who
/// did what under which permission in a security vocabulary; this answers what
/// happened to the document, in the words a person would use (ADR-0012).
/// </remarks>
public sealed class DocumentEvent
{
    private DocumentEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    public DocumentVersionId? DocumentVersionId { get; private set; }

    public DocumentEventKind Kind { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public UserId ActorUserId { get; private set; }

    public static DocumentEvent Record(
        OrganizationId organizationId,
        DocumentId documentId,
        DocumentEventKind kind,
        string summary,
        UserId actorUserId,
        DateTimeOffset now,
        DocumentVersionId? documentVersionId = null,
        string? detail = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            Kind = kind,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 500),
            Detail = Ensure.OptionalMax(detail, nameof(detail), 2000),
            OccurredAt = now,
            ActorUserId = actorUserId,
        };
}
