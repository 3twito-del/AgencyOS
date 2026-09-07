using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Projects;

/// <summary>Opaque, immutable identifier for a <see cref="Package"/>.</summary>
public readonly record struct PackageId(Guid Value)
{
    public static PackageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How far along an internal package is.
/// </summary>
/// <remarks>
/// Deliberately about the agency's own readiness and nothing else. There is no
/// submitted, pitched, or in-negotiation state here: the moment a package goes out
/// to a buyer, that is an opportunity, and opportunities are M6. Putting a
/// submission state on the package would mean M6 either duplicating it or
/// inheriting a half-built pipeline (ADR-0019).
/// </remarks>
public enum PackageStatus
{
    /// <summary>Being sketched out.</summary>
    Draft = 1,

    /// <summary>Actively being put together.</summary>
    Assembling = 2,

    /// <summary>Complete enough to take out.</summary>
    Ready = 3,

    /// <summary>In use.</summary>
    Active = 4,

    /// <summary>Deliberately parked. Reversible.</summary>
    Paused = 5,

    /// <summary>Finished with.</summary>
    Closed = 6,

    /// <summary>Given up on.</summary>
    Abandoned = 7,
}

/// <summary>What a package element points at.</summary>
/// <remarks>
/// The distinction that matters is between <see cref="AttachedParty"/> - somebody
/// who really is attached, referenced through the attachment record - and
/// <see cref="ProposedPerson"/>, who is somebody the agency would like. Both
/// belong in a package; only the first is a fact about the project.
/// </remarks>
public enum PackageElementKind
{
    /// <summary>An existing attachment, included by reference.</summary>
    AttachedParty = 1,

    /// <summary>Somebody the agency wants for the package but who is not attached.</summary>
    ProposedPerson = 2,

    /// <summary>A company the agency wants involved, or is tracking.</summary>
    ProposedCompany = 3,

    /// <summary>A role the package still needs to fill.</summary>
    OpenRole = 4,

    /// <summary>A piece of material the package rests on.</summary>
    Material = 5,

    /// <summary>The underlying property.</summary>
    SourceProperty = 6,
}

/// <summary>A recorded change to a package's status.</summary>
public sealed class PackageEvent
{
    private PackageEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PackageId PackageId { get; private set; }

    public PackageStatus? FromStatus { get; private set; }

    public PackageStatus ToStatus { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static PackageEvent Record(
        OrganizationId organizationId,
        PackageId packageId,
        PackageStatus? fromStatus,
        PackageStatus toStatus,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new PackageEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PackageId = packageId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// One item the agency is assembling into a package.
/// </summary>
/// <remarks>
/// A package element is an internal work product, not a claim about the world. It
/// can name a director who really is attached, a star the agency hopes to get, and
/// a role nobody has been approached for, all in the same list - which is exactly
/// what a package is. What it must never do is make the second of those look like
/// the first, which is why the kind is explicit and why proposals cannot be
/// mistaken for attachments (ADR-0019).
/// </remarks>
public sealed class PackageElement
{
    private PackageElement()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PackageId PackageId { get; private set; }

    public PackageElementKind Kind { get; private set; }

    /// <summary>
    /// The thing this element points at.
    /// </summary>
    /// <remarks>
    /// A raw identifier interpreted according to <see cref="Kind"/>: an attachment,
    /// a person, a company, a project role, a material or a source property. A
    /// column per kind would be six mostly-null columns and a check constraint
    /// nobody could read.
    /// </remarks>
    public Guid TargetId { get; private set; }

    /// <summary>Why this is in the package, in the assembler's own words.</summary>
    public string? Note { get; private set; }

    /// <summary>Where it sits in the package's own ordering.</summary>
    public int Position { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    internal static PackageElement Create(
        OrganizationId organizationId,
        PackageId packageId,
        PackageElementKind kind,
        Guid targetId,
        int position,
        DateTimeOffset now,
        UserId addedBy,
        string? note)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown package element kind '{kind}'.");
        }

        if (targetId == Guid.Empty)
        {
            throw new DomainException("A package element must point at something.");
        }

        return new PackageElement
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PackageId = packageId,
            Kind = kind,
            TargetId = targetId,
            Position = position,
            Note = Ensure.OptionalMax(note, nameof(note), 2000),
            AddedAt = now,
            AddedBy = addedBy,
        };
    }

    internal void MoveTo(int position) => Position = position;
}

/// <summary>
/// A grouping of project elements the agency is assembling and tracking together.
/// </summary>
/// <remarks>
/// <para>
/// An AgencyOS work product rather than a fact about the industry. A package is
/// the agency's answer to "what are we putting together here", and it is allowed
/// to contain hopes as well as facts. That is the whole reason it exists as a
/// separate concept from the project's roster.
/// </para>
/// <para>
/// A package is not a deal. Nothing here records terms, money, or an agreement
/// with anybody; those are M7 and M8.
/// </para>
/// </remarks>
public sealed class Package
{
    /// <summary>
    /// The complete transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Assembly is not one-way: a package found to be missing something goes back
    /// from Ready to Assembling, and that is ordinary rather than exceptional.
    /// Closed and Abandoned are terminal, because reviving a package means
    /// assembling a new one and the old one is worth keeping as it stood.
    /// </remarks>
    public static IReadOnlyDictionary<PackageStatus, IReadOnlySet<PackageStatus>> AllowedTransitions
    { get; } = new Dictionary<PackageStatus, IReadOnlySet<PackageStatus>>
    {
        [PackageStatus.Draft] = Freeze(
            PackageStatus.Assembling,
            PackageStatus.Abandoned),

        [PackageStatus.Assembling] = Freeze(
            PackageStatus.Draft,
            PackageStatus.Ready,
            PackageStatus.Paused,
            PackageStatus.Abandoned),

        [PackageStatus.Ready] = Freeze(
            PackageStatus.Assembling,
            PackageStatus.Active,
            PackageStatus.Paused,
            PackageStatus.Abandoned),

        [PackageStatus.Active] = Freeze(
            PackageStatus.Paused,
            PackageStatus.Closed,
            PackageStatus.Abandoned),

        [PackageStatus.Paused] = Freeze(
            PackageStatus.Assembling,
            PackageStatus.Ready,
            PackageStatus.Active,
            PackageStatus.Closed,
            PackageStatus.Abandoned),

        [PackageStatus.Closed] = Freeze(),
        [PackageStatus.Abandoned] = Freeze(),
    };

    private readonly List<PackageEvent> _events = [];
    private readonly List<PackageElement> _elements = [];

    private Package()
    {
    }

    public PackageId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PackageStatus Status { get; private set; }

    /// <summary>What the package is arguing, in a sentence.</summary>
    public string? Thesis { get; private set; }

    /// <summary>
    /// Internal strategy. Requires <c>packages.strategy.read</c>.
    /// </summary>
    /// <remarks>
    /// A project's logline is a shared fact; a package's strategy is what the
    /// agency privately thinks its play is, and often names who it expects to say
    /// no. That is a materially different sensitivity and gets its own permission
    /// (ADR-0019).
    /// </remarks>
    public string? StrategyNotes { get; private set; }

    /// <summary>The internal owner. A package with nobody assembling it is not a package.</summary>
    public UserId LeadUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<PackageEvent> Events => _events;

    public IReadOnlyCollection<PackageElement> Elements => _elements;

    /// <summary>Gets a value indicating whether this package can still change.</summary>
    public bool IsOpen => AllowedTransitions[Status].Count > 0;

    public static Package Create(
        OrganizationId organizationId,
        ProjectId projectId,
        string name,
        UserId leadUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? thesis = null,
        string? strategyNotes = null)
    {
        Package package = new()
        {
            Id = PackageId.New(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Name = Ensure.NotBlankMax(name, nameof(name), 300),
            Status = PackageStatus.Draft,
            Thesis = Ensure.OptionalMax(thesis, nameof(thesis), 2000),
            StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000),
            LeadUserId = leadUserId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        package._events.Add(PackageEvent.Record(
            organizationId, package.Id, null, PackageStatus.Draft, now, createdBy, reason: null));

        return package;
    }

    public void Update(
        string name,
        UserId leadUserId,
        DateTimeOffset now,
        int expectedVersion,
        string? thesis = null,
        string? strategyNotes = null)
    {
        RequireVersion(expectedVersion);

        Name = Ensure.NotBlankMax(name, nameof(name), 300);
        LeadUserId = leadUserId;
        Thesis = Ensure.OptionalMax(thesis, nameof(thesis), 2000);
        StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000);

        Touch(now);
    }

    /// <summary>
    /// Moves the package to a new status.
    /// </summary>
    /// <remarks>Already being at the target returns without complaint.</remarks>
    public void ChangeStatus(
        PackageStatus target,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"Unknown package status '{target}'.");
        }

        if (Status == target)
        {
            return;
        }

        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new DomainException(
                $"A package cannot move from {Status} to {target}. "
                    + $"From {Status} it can move to: {Describe(AllowedTransitions[Status])}.");
        }

        PackageStatus from = Status;

        Status = target;

        _events.Add(PackageEvent.Record(OrganizationId, Id, from, target, now, changedBy, reason));

        Touch(now);
    }

    /// <summary>
    /// Adds an element, or returns quietly if the same one is already there.
    /// </summary>
    /// <remarks>
    /// Adding the same thing twice is what a retry looks like, so it is not an
    /// error. A partial unique index enforces the same thing in the database for
    /// two requests that race past this check.
    /// </remarks>
    public PackageElement AddElement(
        PackageElementKind kind,
        Guid targetId,
        DateTimeOffset now,
        UserId addedBy,
        int expectedVersion,
        string? note = null)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        PackageElement? existing =
            _elements.FirstOrDefault(x => x.Kind == kind && x.TargetId == targetId);

        if (existing is not null)
        {
            return existing;
        }

        int position = _elements.Count == 0 ? 0 : _elements.Max(x => x.Position) + 1;

        PackageElement element = PackageElement.Create(
            OrganizationId, Id, kind, targetId, position, now, addedBy, note);

        _elements.Add(element);

        Touch(now);

        return element;
    }

    /// <summary>Removes an element, or returns quietly if it is already gone.</summary>
    /// <remarks>
    /// A real removal rather than an archive. A package element is the agency's
    /// own working list, not a business fact: nothing downstream refers to it and
    /// no question is answered by keeping something somebody took out. The removal
    /// is still audited.
    /// </remarks>
    public void RemoveElement(Guid elementId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        PackageElement? element = _elements.FirstOrDefault(x => x.Id == elementId);

        if (element is null)
        {
            return;
        }

        _elements.Remove(element);

        Touch(now);
    }

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Package),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }

    private void RequireOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException(
                $"A {Status} package cannot be changed. It is kept as it stood; "
                    + "assemble a new package if the work has restarted.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string Describe(IReadOnlySet<PackageStatus> statuses) =>
        statuses.Count == 0 ? "nothing, it is terminal" : string.Join(", ", statuses);

    private static IReadOnlySet<PackageStatus> Freeze(params PackageStatus[] statuses) =>
        new HashSet<PackageStatus>(statuses);
}
