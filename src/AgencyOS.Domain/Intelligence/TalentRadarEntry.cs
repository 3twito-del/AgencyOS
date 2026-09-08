using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="TalentRadarEntry"/>.</summary>
public readonly record struct TalentRadarEntryId(Guid Value)
{
    public static TalentRadarEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where a radar entry stands.
/// </summary>
/// <remarks>
/// <para>
/// This lifecycle stops exactly where M4's begins. There is no <c>Contacted</c>,
/// no <c>Courting</c> and no <c>Signed</c> here: the moment somebody is approached,
/// they are a <c>Prospect</c> and M4 owns what happens next. Duplicating those
/// stages would give the agency two places to look for the same answer, which
/// eventually disagree (ADR-0030).
/// </para>
/// <para>
/// The radar is what happens <em>before</em> anybody is approached — research,
/// evidence and a decision about whether to bother.
/// </para>
/// </remarks>
public enum TalentRadarStatus
{
    /// <summary>Noticed and being kept an eye on. Nothing has been done.</summary>
    Watching = 1,

    /// <summary>Somebody is actively looking into them.</summary>
    Researching = 2,

    /// <summary>The research is done and a person should decide.</summary>
    ReadyForReview = 3,

    /// <summary>A prospect was created. Terminal here; M4 takes it from there.</summary>
    ConvertedToProspect = 4,

    /// <summary>Looked at and set aside, with a reason. Terminal.</summary>
    Dismissed = 5,
}

/// <summary>
/// Somebody the agency is watching before deciding whether to pursue them.
/// </summary>
/// <remarks>
/// <para>
/// Pre-prospect intelligence, anchored to a real <c>Person</c> who need not be
/// represented, need not be a client, and need not know the agency exists.
/// </para>
/// <para>
/// It copies nothing. Credits, disciplines, company context and materials stay
/// where M2, M4 and M5 keep them and are projected at read time; duplicating them
/// here would create a second copy that goes stale the first time somebody updates
/// the real one (ADR-0030).
/// </para>
/// <para>
/// <strong>Conversion is a human command.</strong> No score, no threshold and no
/// rule promotes somebody to a prospect. A person decides, and the system records
/// that they did.
/// </para>
/// </remarks>
public sealed class TalentRadarEntry
{
    private TalentRadarEntry()
    {
    }

    public TalentRadarEntryId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Who is being watched.</summary>
    public PersonId PersonId { get; private set; }

    public TalentRadarStatus Status { get; private set; }

    /// <summary>Whose radar entry this is.</summary>
    public UserId OwnerUserId { get; private set; }

    /// <summary>
    /// Why they are being watched, in the analyst's words.
    /// </summary>
    /// <remarks>
    /// Required. An entry with no stated reason is a name on a list that nobody can
    /// evaluate six months later.
    /// </remarks>
    public string Rationale { get; private set; } = string.Empty;

    /// <summary>What the agency would represent them for, if it came to that.</summary>
    public string? IntendedDisciplines { get; private set; }

    /// <summary>
    /// Priority, only when a person assigned one.
    /// </summary>
    /// <remarks>
    /// Never computed. A ranked radar sorted by a number nobody chose is a list
    /// that looks objective and is not (ADR-0030).
    /// </remarks>
    public TalentRadarPriority Priority { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    /// <summary>When they first came onto the radar.</summary>
    public DateTimeOffset FirstObservedAt { get; private set; }

    /// <summary>When somebody last looked at this entry.</summary>
    public DateTimeOffset? LastReviewedAt { get; private set; }

    public UserId? LastReviewedBy { get; private set; }

    /// <summary>The prospect created from this entry, once one was.</summary>
    public ProspectId? ProspectId { get; private set; }

    /// <summary>The talent profile the conversion used or created.</summary>
    public TalentProfileId? TalentProfileId { get; private set; }

    public DateTimeOffset? ConvertedAt { get; private set; }

    public UserId? ConvertedBy { get; private set; }

    /// <summary>Why the entry was dismissed.</summary>
    public string? DismissedReason { get; private set; }

    public DateTimeOffset? DismissedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public int Version { get; private set; }

    /// <summary>Whether the entry is still being worked.</summary>
    public bool IsOpen => Status
        is TalentRadarStatus.Watching
        or TalentRadarStatus.Researching
        or TalentRadarStatus.ReadyForReview;

    public static TalentRadarEntry Create(
        OrganizationId organizationId,
        PersonId personId,
        string rationale,
        IntelligenceSensitivity sensitivity,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? intendedDisciplines = null,
        TalentRadarPriority priority = TalentRadarPriority.Unassigned,
        DateTimeOffset? firstObservedAt = null)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"'{priority}' is not a priority.");
        }

        DateTimeOffset observed = firstObservedAt ?? now;

        if (observed > now)
        {
            throw new DomainException("Somebody cannot have come onto the radar in the future.");
        }

        return new TalentRadarEntry
        {
            Id = TalentRadarEntryId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            Status = TalentRadarStatus.Watching,
            OwnerUserId = ownerUserId,
            Rationale = Ensure.NotBlankMax(rationale, nameof(rationale), 4000),
            IntendedDisciplines = Ensure.OptionalMax(
                intendedDisciplines, nameof(intendedDisciplines), 500),
            Priority = priority,
            Sensitivity = sensitivity,
            FirstObservedAt = observed,
            CreatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        string rationale,
        IntelligenceSensitivity sensitivity,
        TalentRadarPriority priority,
        int expectedVersion,
        string? intendedDisciplines = null)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Rationale = Ensure.NotBlankMax(rationale, nameof(rationale), 4000);
        IntendedDisciplines = Ensure.OptionalMax(
            intendedDisciplines, nameof(intendedDisciplines), 500);
        Sensitivity = Enum.IsDefined(sensitivity)
            ? sensitivity
            : throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        Priority = Enum.IsDefined(priority)
            ? priority
            : throw new DomainException($"'{priority}' is not a priority.");

        Version++;
    }

    /// <summary>Moves the entry between the open research states.</summary>
    public void ChangeStatus(TalentRadarStatus status, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        if (status is not (TalentRadarStatus.Watching
            or TalentRadarStatus.Researching
            or TalentRadarStatus.ReadyForReview))
        {
            throw new DomainException(
                $"{status} is reached by its own command, not by changing the status.");
        }

        Status = status;
        Version++;
    }

    public void RecordReview(UserId actor, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        LastReviewedAt = now;
        LastReviewedBy = actor;
        Version++;
    }

    /// <summary>
    /// Records that this entry became an M4 prospect.
    /// </summary>
    /// <remarks>
    /// Called only once the prospect actually exists, in the same transaction. The
    /// entry is not marked converted on the strength of an intention, because a
    /// failed conversion would leave a radar entry claiming a prospect that was
    /// never created (ADR-0030).
    /// </remarks>
    public void NoteConverted(
        ProspectId prospectId,
        TalentProfileId talentProfileId,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Status = TalentRadarStatus.ConvertedToProspect;
        ProspectId = prospectId;
        TalentProfileId = talentProfileId;
        ConvertedAt = now;
        ConvertedBy = actor;
        Version++;
    }

    /// <summary>Sets the entry aside, with a reason.</summary>
    public void Dismiss(string reason, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Status = TalentRadarStatus.Dismissed;
        DismissedReason = Ensure.NotBlankMax(reason, nameof(reason), 2000);
        DismissedAt = now;
        Version++;
    }

    private void RequireOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException(
                Status == TalentRadarStatus.ConvertedToProspect
                    ? "That entry became a prospect. The pursuit is recorded against the "
                        + "prospect from here."
                    : "That radar entry was dismissed.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(TalentRadarEntry), Id.ToString(), expectedVersion, Version);
        }
    }
}

/// <summary>How much attention a person decided an entry deserves.</summary>
/// <remarks>
/// Assigned by hand or not at all. There is no computed priority anywhere in M11.
/// </remarks>
public enum TalentRadarPriority
{
    /// <summary>Nobody has said.</summary>
    Unassigned = 0,

    Low = 1,
    Medium = 2,
    High = 3,
}
