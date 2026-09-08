using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Communications;

/// <summary>Opaque, immutable identifier for a <see cref="CommunicationThread"/>.</summary>
public readonly record struct CommunicationThreadId(Guid Value)
{
    public static CommunicationThreadId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="CommunicationMessage"/>.</summary>
public readonly record struct CommunicationMessageId(Guid Value)
{
    public static CommunicationMessageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="CommunicationAttachment"/>.</summary>
public readonly record struct CommunicationAttachmentId(Guid Value)
{
    public static CommunicationAttachmentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Which way a message went.</summary>
public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2,
}

/// <summary>What part somebody played in a message.</summary>
public enum ParticipantRole
{
    From = 1,
    To = 2,
    Cc = 3,
    Bcc = 4,
    ReplyTo = 5,
    Sender = 6,
}

/// <summary>
/// A provider conversation, where the provider groups messages into one.
/// </summary>
/// <remarks>
/// Threading is the provider's opinion, not AgencyOS's. Where the provider offers a
/// conversation identifier it is recorded and used; where it does not, messages
/// stand alone rather than being grouped by a subject-line heuristic that would put
/// two unrelated "Re: contract" exchanges in the same place (ADR-0026).
/// </remarks>
public sealed class CommunicationThread
{
    private CommunicationThread()
    {
    }

    public CommunicationThreadId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationAccountId AccountId { get; private set; }

    /// <summary>The provider's conversation identifier.</summary>
    public string ExternalThreadId { get; private set; } = string.Empty;

    /// <summary>The subject as it stood when the thread was first seen.</summary>
    public string? Subject { get; private set; }

    public DateTimeOffset FirstMessageAt { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    public int MessageCount { get; private set; }

    public static CommunicationThread Open(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalThreadId,
        DateTimeOffset occurredAt,
        string? subject = null) =>
        new()
        {
            Id = CommunicationThreadId.New(),
            OrganizationId = organizationId,
            AccountId = accountId,
            ExternalThreadId = Ensure.NotBlankMax(externalThreadId, nameof(externalThreadId), 300),
            Subject = Ensure.OptionalMax(subject, nameof(subject), 500),
            FirstMessageAt = occurredAt,
            LastMessageAt = occurredAt,
            MessageCount = 0,
        };

    /// <summary>Widens the thread's span as messages arrive, in any order.</summary>
    public void NoteMessage(DateTimeOffset occurredAt)
    {
        if (occurredAt < FirstMessageAt)
        {
            FirstMessageAt = occurredAt;
        }

        if (occurredAt > LastMessageAt)
        {
            LastMessageAt = occurredAt;
        }

        MessageCount++;
    }
}

/// <summary>
/// One concrete message that passed between real mailboxes.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an M2 <c>Interaction</c>. An interaction is a human event
/// somebody recorded — a meeting, a call, a conversation in a corridor. A message
/// is an artifact that exists in a mail system whether or not AgencyOS knows about
/// it. A message may be surfaced in a timeline beside interactions, but it is never
/// copied into one, because then there would be two editable truths about the same
/// email and they would diverge (ADR-0026).
/// </para>
/// <para>
/// The body is treated as hostile. HTML from a stranger's mail client is stored
/// sanitized or not at all, external images are never fetched on preview, and
/// nothing in the body is executed anywhere (ADR-0026).
/// </para>
/// </remarks>
public sealed class CommunicationMessage
{
    private readonly List<CommunicationParticipant> _participants = [];
    private readonly List<CommunicationAttachment> _attachments = [];
    private readonly List<CommunicationLink> _links = [];

    private CommunicationMessage()
    {
    }

    public CommunicationMessageId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationAccountId AccountId { get; private set; }

    public CommunicationThreadId? ThreadId { get; private set; }

    /// <summary>
    /// The provider's identifier for the message in this mailbox.
    /// </summary>
    /// <remarks>
    /// An external identity, unique within the account and nowhere else. It is not
    /// a substitute for the AgencyOS identifier, and the same message in two
    /// mailboxes is two rows with two provider ids (ADR-0026).
    /// </remarks>
    public string ExternalMessageId { get; private set; } = string.Empty;

    /// <summary>
    /// RFC 5322 Message-ID, when the provider supplies it.
    /// </summary>
    /// <remarks>
    /// The one identifier that survives a message being moved, copied between
    /// folders, or seen from the other side of the exchange. Used for
    /// reconciliation after a lost send acknowledgement (ADR-0028).
    /// </remarks>
    public string? InternetMessageId { get; private set; }

    public MessageDirection Direction { get; private set; }

    public string? Subject { get; private set; }

    /// <summary>The plain-text body, which is what AgencyOS reads and shows by default.</summary>
    public string? BodyText { get; private set; }

    /// <summary>
    /// The HTML body, after sanitization.
    /// </summary>
    /// <remarks>
    /// Null unless sanitization succeeded. Storing the provider's raw HTML and
    /// sanitizing at render time would mean every future reader had to remember to
    /// do it, and one of them would not.
    /// </remarks>
    public string? SanitizedHtml { get; private set; }

    /// <summary>When the message was sent, as the provider reports it.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When it arrived, as the provider reports it.</summary>
    public DateTimeOffset? ReceivedAt { get; private set; }

    /// <summary>When AgencyOS first saw it. A third date, never merged with the others.</summary>
    public DateTimeOffset SynchronizedAt { get; private set; }

    /// <summary>The provider folder it was in when last seen.</summary>
    public string? Folder { get; private set; }

    /// <summary>Whether the provider says it has attachments, before any are ingested.</summary>
    public bool HasAttachments { get; private set; }

    /// <summary>Whether the provider reports it as deleted at the source.</summary>
    public bool IsDeletedAtProvider { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<CommunicationParticipant> Participants => _participants;

    public IReadOnlyList<CommunicationAttachment> Attachments => _attachments;

    public IReadOnlyList<CommunicationLink> Links => _links;

    /// <summary>When the message happened, whichever date the provider gave.</summary>
    public DateTimeOffset OccurredAt => SentAt ?? ReceivedAt ?? SynchronizedAt;

    public static CommunicationMessage Record(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalMessageId,
        MessageDirection direction,
        DateTimeOffset synchronizedAt,
        CommunicationThreadId? threadId = null,
        string? internetMessageId = null,
        string? subject = null,
        string? bodyText = null,
        string? sanitizedHtml = null,
        DateTimeOffset? sentAt = null,
        DateTimeOffset? receivedAt = null,
        string? folder = null,
        bool hasAttachments = false) =>
        new()
        {
            Id = CommunicationMessageId.New(),
            OrganizationId = organizationId,
            AccountId = accountId,
            ThreadId = threadId,
            ExternalMessageId =
                Ensure.NotBlankMax(externalMessageId, nameof(externalMessageId), 300),
            InternetMessageId =
                Ensure.OptionalMax(internetMessageId, nameof(internetMessageId), 500),
            Direction = direction,
            Subject = Ensure.OptionalMax(subject, nameof(subject), 500),
            BodyText = Ensure.OptionalMax(bodyText, nameof(bodyText), 1_000_000),
            SanitizedHtml = Ensure.OptionalMax(sanitizedHtml, nameof(sanitizedHtml), 1_000_000),
            SentAt = sentAt,
            ReceivedAt = receivedAt,
            SynchronizedAt = synchronizedAt,
            Folder = Ensure.OptionalMax(folder, nameof(folder), 200),
            HasAttachments = hasAttachments,
            Version = 1,
        };

    /// <summary>
    /// Applies a later view of the same message from the provider.
    /// </summary>
    /// <remarks>
    /// A message can be moved between folders, marked read, or deleted at the
    /// source. Those are updates to what the provider says, not new messages, and
    /// treating them as new is how a delta sync turns one email into six.
    /// </remarks>
    public void ApplyProviderUpdate(
        DateTimeOffset now,
        string? folder = null,
        bool? isDeletedAtProvider = null,
        string? subject = null)
    {
        if (folder is not null)
        {
            Folder = Ensure.OptionalMax(folder, nameof(folder), 200);
        }

        if (isDeletedAtProvider is { } deleted)
        {
            IsDeletedAtProvider = deleted;
        }

        if (subject is not null)
        {
            Subject = Ensure.OptionalMax(subject, nameof(subject), 500);
        }

        SynchronizedAt = now;
        Version++;
    }

    /// <summary>
    /// Records who was on the message.
    /// </summary>
    /// <remarks>
    /// The raw address and display name are kept exactly as observed, always. An
    /// AgencyOS link sits beside them and never replaces them, so a later reader can
    /// still see what the mail actually said (ADR-0026).
    /// </remarks>
    public CommunicationParticipant AddParticipant(
        ParticipantRole role,
        string address,
        string? displayName = null)
    {
        CommunicationParticipant participant = CommunicationParticipant.Create(
            OrganizationId, Id, role, address, displayName);

        _participants.Add(participant);
        return participant;
    }

    public CommunicationAttachment AddAttachment(
        string externalAttachmentId,
        string fileName,
        string mediaType,
        long byteLength,
        bool isInline = false)
    {
        CommunicationAttachment attachment = CommunicationAttachment.Record(
            OrganizationId, Id, externalAttachmentId, fileName, mediaType, byteLength, isInline);

        _attachments.Add(attachment);
        HasAttachments = true;

        return attachment;
    }

    /// <summary>Connects the message to a record it is evidence about.</summary>
    /// <remarks>
    /// Evidence and context. Linking an email to a deal does not move the deal, and
    /// it does not grant the reader access to the message either (ADR-0026).
    /// </remarks>
    public CommunicationLink Link(
        DocumentLinkTarget target,
        Guid targetId,
        UserId linkedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_links.Any(x => x.Target == target && x.TargetId == targetId))
        {
            throw new DomainException("This message is already linked to that record.");
        }

        CommunicationLink link = CommunicationLink.Create(
            OrganizationId, Id, target, targetId, linkedBy, now, note);

        _links.Add(link);
        return link;
    }

    public CommunicationLink Unlink(Guid linkId)
    {
        CommunicationLink link = _links.SingleOrDefault(x => x.Id == linkId)
            ?? throw new DomainException("That link is not on this message.");

        _links.Remove(link);
        return link;
    }
}

/// <summary>
/// Somebody on a message, as the mail system named them.
/// </summary>
/// <remarks>
/// An address is not an identity. Two people share a family mailbox, one person has
/// six addresses, and an address that once belonged to an agent now belongs to their
/// replacement. So the raw values are canonical here and any AgencyOS link is an
/// explicit, reversible assertion somebody made (ADR-0026).
/// </remarks>
public sealed class CommunicationParticipant
{
    private CommunicationParticipant()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationMessageId MessageId { get; private set; }

    public ParticipantRole Role { get; private set; }

    /// <summary>The address exactly as it appeared.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>The display name exactly as it appeared.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>The person somebody said this is, when somebody said.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>The company somebody said this is, when somebody said.</summary>
    public Guid? CompanyId { get; private set; }

    /// <summary>Who made the identification, so it can be questioned later.</summary>
    public UserId? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    internal static CommunicationParticipant Create(
        OrganizationId organizationId,
        CommunicationMessageId messageId,
        ParticipantRole role,
        string address,
        string? displayName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            MessageId = messageId,
            Role = role,
            Address = Ensure.NotBlankMax(address, nameof(address), 320).ToLowerInvariant(),
            DisplayName = Ensure.OptionalMax(displayName, nameof(displayName), 200),
        };

    /// <summary>
    /// Records that this address is a known person or company.
    /// </summary>
    /// <remarks>
    /// Always an explicit act. AgencyOS suggests candidates from an exact address
    /// match and stops there: where an address matches two records it offers both
    /// and asks, because a wrong identification quietly attributes somebody's
    /// correspondence to the wrong person (ADR-0026).
    /// </remarks>
    public void ResolveTo(Guid? personId, Guid? companyId, UserId resolvedBy, DateTimeOffset now)
    {
        if (personId is null == companyId is null)
        {
            throw new DomainException(
                "An address resolves to exactly one of a person or a company.");
        }

        PersonId = personId;
        CompanyId = companyId;
        ResolvedBy = resolvedBy;
        ResolvedAt = now;
    }

    /// <summary>Withdraws an identification without touching what the mail said.</summary>
    public void ClearResolution()
    {
        PersonId = null;
        CompanyId = null;
        ResolvedBy = null;
        ResolvedAt = null;
    }
}

/// <summary>
/// A file attached to a message.
/// </summary>
/// <remarks>
/// Metadata arrives with the message; the bytes do not. Downloading every
/// attachment on sight would pull gigabytes of unrequested video through the
/// server, so ingestion is a deliberate act with a size policy, and the attachment
/// says plainly whether AgencyOS holds the bytes (ADR-0024, ADR-0026).
/// </remarks>
public sealed class CommunicationAttachment
{
    private CommunicationAttachment()
    {
    }

    public CommunicationAttachmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationMessageId MessageId { get; private set; }

    /// <summary>The provider's identifier for the attachment.</summary>
    public string ExternalAttachmentId { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string MediaType { get; private set; } = string.Empty;

    /// <summary>The size the provider reports, before anything is fetched.</summary>
    public long ByteLength { get; private set; }

    /// <summary>Whether it is embedded in the body rather than attached.</summary>
    public bool IsInline { get; private set; }

    /// <summary>The canonical version, once the bytes have actually been ingested.</summary>
    public DocumentVersionId? DocumentVersionId { get; private set; }

    public DateTimeOffset? IngestedAt { get; private set; }

    public UserId? IngestedBy { get; private set; }

    /// <summary>Whether AgencyOS holds the bytes, rather than only knowing they exist.</summary>
    public bool HoldsContent => DocumentVersionId is not null;

    internal static CommunicationAttachment Record(
        OrganizationId organizationId,
        CommunicationMessageId messageId,
        string externalAttachmentId,
        string fileName,
        string mediaType,
        long byteLength,
        bool isInline) =>
        new()
        {
            Id = CommunicationAttachmentId.New(),
            OrganizationId = organizationId,
            MessageId = messageId,
            ExternalAttachmentId =
                Ensure.NotBlankMax(externalAttachmentId, nameof(externalAttachmentId), 300),
            FileName = Ensure.NotBlankMax(fileName, nameof(fileName), 300),
            MediaType = Ensure.NotBlankMax(mediaType, nameof(mediaType), 150),
            ByteLength = byteLength,
            IsInline = isInline,
        };

    /// <summary>Records that the bytes are now a canonical document version.</summary>
    public void NoteIngested(DocumentVersionId documentVersionId, UserId by, DateTimeOffset now)
    {
        if (DocumentVersionId is not null)
        {
            throw new DomainException("This attachment has already been ingested.");
        }

        DocumentVersionId = documentVersionId;
        IngestedBy = by;
        IngestedAt = now;
    }
}

/// <summary>
/// A message's association with a domain record.
/// </summary>
/// <remarks>
/// The same exclusive-arc shape as <see cref="DocumentLink"/>, and for the same
/// reasons (ADR-0025).
/// </remarks>
public sealed class CommunicationLink
{
    private CommunicationLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationMessageId MessageId { get; private set; }

    public DocumentLinkTarget Target { get; private set; }

    public Guid TargetId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public UserId LinkedBy { get; private set; }

    internal static CommunicationLink Create(
        OrganizationId organizationId,
        CommunicationMessageId messageId,
        DocumentLinkTarget target,
        Guid targetId,
        UserId linkedBy,
        DateTimeOffset now,
        string? note)
    {
        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"'{target}' is not something a message can link to.");
        }

        if (targetId == Guid.Empty)
        {
            throw new DomainException("A link must name the record it points at.");
        }

        return new CommunicationLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            MessageId = messageId,
            Target = target,
            TargetId = targetId,
            Note = Ensure.OptionalMax(note, nameof(note), 500),
            LinkedAt = now,
            LinkedBy = linkedBy,
        };
    }
}
