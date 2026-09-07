using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Talent;

/// <summary>Opaque, immutable identifier for a <see cref="TalentProfile"/>.</summary>
public readonly record struct TalentProfileId(Guid Value)
{
    public static TalentProfileId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a person does professionally.
/// </summary>
/// <remarks>
/// A controlled vocabulary rather than free text, because these values are
/// filtered on and a typo would silently shrink a list. It is deliberately a small
/// set: the agency represents people, and a person who does three of these is one
/// person with three disciplines, not three records.
/// </remarks>
public enum ProfessionalDiscipline
{
    Actor = 1,
    Writer = 2,
    Director = 3,
    Producer = 4,
    Showrunner = 5,
    Creator = 6,
    Composer = 7,
    Author = 8,
    Presenter = 9,
    Other = 99,
}

/// <summary>
/// Roughly where someone is in their career.
/// </summary>
/// <remarks>
/// Coarse on purpose. A finer scale would invite arguments about the boundary and
/// would not change any decision the system supports today.
/// </remarks>
public enum CareerStage
{
    /// <summary>Not assessed. The honest default, rather than guessing.</summary>
    Unknown = 0,

    Emerging = 1,
    Established = 2,
    Veteran = 3,
}

/// <summary>
/// A discipline a talent profile claims, recorded with when it was added.
/// </summary>
/// <remarks>
/// Removal ends the row rather than deleting it, so "we used to represent them as
/// a writer" survives. <c>docs/08_DATA_MODEL_FOUNDATION.md</c> principle 3:
/// historical truth is preserved.
/// </remarks>
public sealed class TalentDiscipline
{
    private TalentDiscipline()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public TalentProfileId TalentProfileId { get; private set; }

    public ProfessionalDiscipline Discipline { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>When this discipline stopped applying, or null while it still does.</summary>
    public DateOnly? EndsOn { get; private set; }

    public bool IsOpen => EndsOn is null;

    internal static TalentDiscipline Open(
        OrganizationId organizationId,
        TalentProfileId profileId,
        ProfessionalDiscipline discipline,
        DateOnly startsOn)
    {
        return new TalentDiscipline
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            TalentProfileId = profileId,
            Discipline = discipline,
            StartsOn = startsOn,
        };
    }

    internal void End(DateOnly endsOn)
    {
        if (endsOn < StartsOn)
        {
            throw new DomainException(
                $"A discipline cannot end on {endsOn:O} when it started on {StartsOn:O}.");
        }

        EndsOn = endsOn;
    }
}

/// <summary>
/// Agency-specific representation metadata about a person.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="Person"/>. A person is a human being and
/// exists whether or not the agency has a view about them; a talent profile is the
/// agency's working record of how it represents them. Merging the two would put
/// internal positioning on the same record as a contact's phone number, and would
/// mean every person row carried columns that are meaningless for most of them.
/// </para>
/// <para>
/// There is deliberately no <c>IsClient</c> flag here. Being a client is a
/// consequence of holding an active representation, so it is derived rather than
/// stored - a stored flag is a second source of truth that drifts the first time
/// somebody terminates a representation without remembering to clear it.
/// </para>
/// </remarks>
public sealed class TalentProfile
{
    private readonly List<TalentDiscipline> _disciplines = [];

    private TalentProfile()
    {
    }

    public TalentProfileId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The person this profile describes. One profile per person per tenant.</summary>
    public PersonId PersonId { get; private set; }

    public CareerStage CareerStage { get; private set; }

    /// <summary>A short factual summary, shareable inside the agency.</summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// Internal positioning: how the agency thinks about representing them.
    /// </summary>
    /// <remarks>
    /// Judgment, not fact, and the most sensitive field on the record. Reading it
    /// requires <c>talent.notes.read</c>; callers without it receive the profile
    /// with this field absent rather than a refusal, so an observer can still see
    /// who the client is (ADR-0017).
    /// </remarks>
    public string? PositioningNotes { get; private set; }

    /// <summary>Primary market they work out of, for example "London" or "Los Angeles".</summary>
    public string? BaseMarket { get; private set; }

    /// <summary>Languages they work in, as recorded.</summary>
    public string? Languages { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<TalentDiscipline> Disciplines => _disciplines;

    /// <summary>Disciplines that still apply.</summary>
    public IEnumerable<ProfessionalDiscipline> CurrentDisciplines =>
        _disciplines.Where(x => x.IsOpen).Select(x => x.Discipline);

    public static TalentProfile Create(
        OrganizationId organizationId,
        PersonId personId,
        UserId createdBy,
        DateTimeOffset now,
        CareerStage careerStage = CareerStage.Unknown,
        string? summary = null,
        string? positioningNotes = null,
        string? baseMarket = null,
        string? languages = null)
    {
        return new TalentProfile
        {
            Id = TalentProfileId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            CareerStage = careerStage,
            Summary = Ensure.OptionalMax(summary, nameof(summary), 2000),
            PositioningNotes = Ensure.OptionalMax(positioningNotes, nameof(positioningNotes), 4000),
            BaseMarket = Ensure.OptionalMax(baseMarket, nameof(baseMarket), 128),
            Languages = Ensure.OptionalMax(languages, nameof(languages), 256),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        DateTimeOffset now,
        CareerStage careerStage,
        string? summary,
        string? positioningNotes,
        string? baseMarket,
        string? languages)
    {
        CareerStage = careerStage;
        Summary = Ensure.OptionalMax(summary, nameof(summary), 2000);
        PositioningNotes = Ensure.OptionalMax(positioningNotes, nameof(positioningNotes), 4000);
        BaseMarket = Ensure.OptionalMax(baseMarket, nameof(baseMarket), 128);
        Languages = Ensure.OptionalMax(languages, nameof(languages), 256);
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Records that the person works in a discipline.
    /// </summary>
    /// <remarks>
    /// Re-adding a discipline that already applies is accepted and does nothing.
    /// The caller asked for a state that already holds, and refusing would make an
    /// idempotent retry look like an error.
    /// </remarks>
    public void AddDiscipline(ProfessionalDiscipline discipline, DateOnly on, DateTimeOffset now)
    {
        if (_disciplines.Any(x => x.IsOpen && x.Discipline == discipline))
        {
            return;
        }

        _disciplines.Add(TalentDiscipline.Open(OrganizationId, Id, discipline, on));

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Ends a discipline without erasing that it once applied.</summary>
    public void RemoveDiscipline(ProfessionalDiscipline discipline, DateOnly on, DateTimeOffset now)
    {
        TalentDiscipline? open = _disciplines.FirstOrDefault(x => x.IsOpen && x.Discipline == discipline);

        if (open is null)
        {
            throw new DomainException($"This profile does not currently claim the discipline '{discipline}'.");
        }

        open.End(on);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(TalentProfile), Id.ToString(), expectedVersion, Version);
        }
    }
}
