using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="Watchlist"/>.</summary>
public readonly record struct WatchlistId(Guid Value)
{
    public static WatchlistId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Whether a watchlist is still being watched.</summary>
public enum WatchlistStatus
{
    Active = 1,

    /// <summary>Kept for the record, no longer monitored.</summary>
    Archived = 2,
}

/// <summary>
/// An explicit collection of things somebody decided to monitor.
/// </summary>
/// <remarks>
/// <para>
/// Membership is stated, not computed. M11 has no rule engine and no filter
/// language of its own — inventing a second query DSL beside M3's saved views would
/// give the agency two ways to express "everything matching X" that drift apart
/// (ADR-0030).
/// </para>
/// <para>
/// What is <em>new</em> about a watched thing is derived by subject overlap at read
/// time. There is no <c>Signal.IsOnWatchlist</c> flag, because a flag would have to
/// be maintained on every watchlist change and would be wrong the moment one was
/// missed.
/// </para>
/// <para>
/// M11 does not monitor anything by itself. Nothing polls the web, and no
/// notification fires. This records what the agency has decided to watch; a person
/// still looks.
/// </para>
/// </remarks>
public sealed class Watchlist
{
    private readonly List<WatchlistEntry> _entries = [];

    private Watchlist()
    {
    }

    public WatchlistId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Why this list exists, so somebody else can tell what belongs on it.</summary>
    public string? Purpose { get; private set; }

    public WatchlistStatus Status { get; private set; }

    public UserId OwnerUserId { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    /// <summary>When somebody last went through it. Null means nobody has.</summary>
    public DateTimeOffset? LastReviewedAt { get; private set; }

    public UserId? LastReviewedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<WatchlistEntry> Entries => _entries;

    public static Watchlist Create(
        OrganizationId organizationId,
        string name,
        IntelligenceSensitivity sensitivity,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? purpose = null)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        return new Watchlist
        {
            Id = WatchlistId.New(),
            OrganizationId = organizationId,
            Name = Ensure.NotBlankMax(name, nameof(name), 200),
            Purpose = Ensure.OptionalMax(purpose, nameof(purpose), 2000),
            Status = WatchlistStatus.Active,
            OwnerUserId = ownerUserId,
            Sensitivity = sensitivity,
            CreatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        string name,
        IntelligenceSensitivity sensitivity,
        int expectedVersion,
        string? purpose = null)
    {
        RequireVersion(expectedVersion);

        Name = Ensure.NotBlankMax(name, nameof(name), 200);
        Purpose = Ensure.OptionalMax(purpose, nameof(purpose), 2000);
        Sensitivity = Enum.IsDefined(sensitivity)
            ? sensitivity
            : throw new DomainException($"'{sensitivity}' is not a sensitivity.");

        Version++;
    }

    public void Archive(DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status == WatchlistStatus.Archived)
        {
            throw new DomainException("That watchlist is already archived.");
        }

        Status = WatchlistStatus.Archived;
        Version++;
    }

    public void Restore(int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status == WatchlistStatus.Active)
        {
            throw new DomainException("That watchlist is already active.");
        }

        Status = WatchlistStatus.Active;
        Version++;
    }

    /// <summary>Records that somebody went through the list.</summary>
    /// <remarks>
    /// The anchor for "what is new since I last looked". Without it the only
    /// available answer is "new since some fixed window", which is a different and
    /// less useful question.
    /// </remarks>
    public void RecordReview(UserId actor, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        LastReviewedAt = now;
        LastReviewedBy = actor;
        Version++;
    }

    public WatchlistEntry AddEntry(
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (Status != WatchlistStatus.Active)
        {
            throw new DomainException("An archived watchlist is not added to.");
        }

        if (_entries.Any(x => x.Kind == kind && x.SubjectId == subjectId))
        {
            throw new DomainException("That is already on this watchlist.");
        }

        WatchlistEntry entry = WatchlistEntry.Create(
            OrganizationId, Id, kind, subjectId, addedBy, now, note);

        _entries.Add(entry);

        return entry;
    }

    public void RemoveEntry(Guid entryId)
    {
        WatchlistEntry entry = _entries.SingleOrDefault(x => x.Id == entryId)
            ?? throw new DomainException("That is not on this watchlist.");

        _entries.Remove(entry);
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Watchlist), Id.ToString(), expectedVersion, Version);
        }
    }
}
