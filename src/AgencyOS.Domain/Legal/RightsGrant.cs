using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for a <see cref="RightsGrant"/>.</summary>
public readonly record struct RightsGrantId(Guid Value)
{
    public static RightsGrantId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What is being granted.
/// </summary>
/// <remarks>
/// Controlled but deliberately shallow. A deep rights taxonomy would encode this
/// decade's distribution business into the schema, and the business changes faster
/// than a schema should; <see cref="Other"/> plus a description is the valve
/// (ADR-0022).
/// </remarks>
public enum RightType
{
    /// <summary>The right to make the work.</summary>
    Production = 1,

    /// <summary>The right to distribute it.</summary>
    Distribution = 2,

    /// <summary>The right to exhibit it.</summary>
    Exhibition = 3,

    /// <summary>The right to adapt it into another form.</summary>
    Adaptation = 4,

    /// <summary>The right to make a sequel.</summary>
    Sequel = 5,

    /// <summary>The right to make a prequel.</summary>
    Prequel = 6,

    /// <summary>The right to remake it.</summary>
    Remake = 7,

    /// <summary>The right to make a spin-off.</summary>
    Spinoff = 8,

    /// <summary>The right to make and sell merchandise.</summary>
    Merchandising = 9,

    /// <summary>The right to promote using the work or a person's likeness.</summary>
    Promotional = 10,

    /// <summary>The right to publish.</summary>
    Publishing = 11,

    /// <summary>Rights the contract groups as ancillary.</summary>
    Ancillary = 12,

    Other = 99,
}

/// <summary>
/// The medium a grant covers.
/// </summary>
/// <remarks>
/// Shallow for the same reason as <see cref="RightType"/>. A grant covering
/// something this list does not name is <see cref="Other"/> with the contract's
/// own words beside it, rather than a value shoehorned into the nearest match.
/// </remarks>
public enum RightsMedium
{
    /// <summary>All media, as the contract says.</summary>
    AllMedia = 1,

    Film = 2,
    Television = 3,
    Streaming = 4,
    Theatrical = 5,
    Digital = 6,
    Audio = 7,
    Podcast = 8,
    Publishing = 9,
    Stage = 10,
    Interactive = 11,

    Other = 99,
}

/// <summary>
/// Where a grant applies.
/// </summary>
/// <remarks>
/// Four values and a detail string, not a geopolitical ontology. Contracts say
/// "worldwide", "United States", "North America", or list territories; the fourth
/// case carries the list as the contract wrote it, because parsing it into
/// countries would invent precision the clause does not have.
/// </remarks>
public enum RightsTerritory
{
    Worldwide = 1,
    UnitedStates = 2,
    NorthAmerica = 3,

    /// <summary>A territory or set of territories the contract names. See the detail.</summary>
    Specified = 99,
}

/// <summary>How exclusive a grant is.</summary>
public enum GrantExclusivity
{
    /// <summary>Nobody else, including the grantor.</summary>
    Exclusive = 1,

    /// <summary>Nobody else, but the grantor keeps their own use.</summary>
    SoleExclusive = 2,

    /// <summary>Others may hold the same right.</summary>
    NonExclusive = 3,
}

/// <summary>How long a grant runs.</summary>
/// <remarks>
/// Open-ended and perpetual are distinct: one means the contract did not say when
/// it ends, the other means it never does, and a reader must be able to tell.
/// </remarks>
public enum GrantPeriodKind
{
    /// <summary>Runs from a date and never ends.</summary>
    Perpetual = 1,

    /// <summary>Runs between two dates.</summary>
    Fixed = 2,

    /// <summary>Runs from a date with no end recorded.</summary>
    OpenEnded = 3,

    /// <summary>The contract states no period this build can structure.</summary>
    Unstated = 4,
}

/// <summary>Where a recorded grant stands.</summary>
public enum RightsGrantStatus
{
    /// <summary>The grant as most recently recorded.</summary>
    Active = 1,

    /// <summary>Replaced by a later grant, usually through an amendment.</summary>
    Superseded = 2,

    /// <summary>Ended, as recorded.</summary>
    Ended = 3,
}

/// <summary>
/// A grant of rights recorded from a contract.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A grant row means "this contract records this grant".</strong> It is
/// not a finding that the grantor held the rights, that the chain of title is
/// good, or that the grant is enforceable. AgencyOS has read no title documents
/// and could not reach any of those conclusions; a system that implied otherwise
/// would be relied on in exactly the situation where being wrong is expensive
/// (ADR-0022).
/// </para>
/// <para>
/// History works by supersession, never by overwrite. A grant that started
/// worldwide and became United States only under an amendment is two rows, each
/// pointing at the instrument that created it, so "what did we believe we had
/// before the amendment" stays answerable (§37).
/// </para>
/// </remarks>
public sealed class RightsGrant
{
    private RightsGrant()
    {
    }

    public RightsGrantId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The drafting version this grant was read from.</summary>
    public ContractVersionId ContractVersionId { get; private set; }

    /// <summary>Where in that version it came from.</summary>
    public string? ClauseReference { get; private set; }

    /// <summary>The party granting. A party on this contract.</summary>
    public Guid GrantorPartyId { get; private set; }

    /// <summary>The party receiving. A party on this contract.</summary>
    public Guid GranteePartyId { get; private set; }

    public RightType RightType { get; private set; }

    public RightsMedium Medium { get; private set; }

    public RightsTerritory Territory { get; private set; }

    /// <summary>The territory as the contract words it, when it names one.</summary>
    public string? TerritoryDetail { get; private set; }

    public GrantExclusivity Exclusivity { get; private set; }

    public GrantPeriodKind PeriodKind { get; private set; }

    public DateOnly? StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    /// <summary>The underlying property, when the grant concerns one.</summary>
    public SourcePropertyId? SourcePropertyId { get; private set; }

    /// <summary>The project, when the grant concerns one.</summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>What the grantor holds back.</summary>
    public string? Reservations { get; private set; }

    public string? Notes { get; private set; }

    public RightsGrantStatus Status { get; private set; }

    /// <summary>The grant that replaced this one, when one did.</summary>
    public RightsGrantId? SupersededByGrantId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>Whether the grant would exclude other holders of the same right.</summary>
    public bool ExcludesOthers => DealRules.ExcludesOthers((int)Exclusivity);

    /// <summary>The period as the rules kernel reads it.</summary>
    public GrantPeriodInput ToRulesPeriod() =>
        new()
        {
            Kind = (int)PeriodKind,
            Starts = StartsOn,
            Ends = EndsOn,
        };

    /// <summary>
    /// Whether the grant is running on a date.
    /// </summary>
    /// <remarks>
    /// A superseded or ended grant is not running whatever its dates say, and an
    /// unstated period answers no: the contract did not say, so the system does not
    /// claim the right is live.
    /// </remarks>
    public bool CoversOn(DateOnly on) =>
        Status == RightsGrantStatus.Active && DealRules.PeriodCoversOn(ToRulesPeriod(), on);

    public static RightsGrant Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        Guid grantorPartyId,
        Guid granteePartyId,
        RightType rightType,
        RightsMedium medium,
        RightsTerritory territory,
        GrantExclusivity exclusivity,
        GrantPeriodKind periodKind,
        UserId recordedBy,
        DateTimeOffset now,
        DateOnly? startsOn = null,
        DateOnly? endsOn = null,
        string? territoryDetail = null,
        string? clauseReference = null,
        SourcePropertyId? sourcePropertyId = null,
        ProjectId? projectId = null,
        string? reservations = null,
        string? notes = null)
    {
        foreach ((bool defined, string name) in new[]
        {
            (Enum.IsDefined(rightType), nameof(rightType)),
            (Enum.IsDefined(medium), nameof(medium)),
            (Enum.IsDefined(territory), nameof(territory)),
            (Enum.IsDefined(exclusivity), nameof(exclusivity)),
            (Enum.IsDefined(periodKind), nameof(periodKind)),
        })
        {
            if (!defined)
            {
                throw new DomainException($"Unknown {name} on a rights grant.");
            }
        }

        if (grantorPartyId == granteePartyId)
        {
            throw new DomainException("A party cannot grant rights to itself.");
        }

        // A specified territory that does not say which is a grant nobody can act
        // on, and the words are the only thing that carries the meaning.
        if (territory == RightsTerritory.Specified && string.IsNullOrWhiteSpace(territoryDetail))
        {
            throw new DomainException(
                "A grant limited to specified territories must say which, as the contract words it.");
        }

        RightsGrant grant = new()
        {
            Id = RightsGrantId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractVersionId = versionId,
            ClauseReference = Ensure.OptionalMax(clauseReference, nameof(clauseReference), 100),
            GrantorPartyId = grantorPartyId,
            GranteePartyId = granteePartyId,
            RightType = rightType,
            Medium = medium,
            Territory = territory,
            TerritoryDetail = Ensure.OptionalMax(territoryDetail, nameof(territoryDetail), 500),
            Exclusivity = exclusivity,
            PeriodKind = periodKind,
            StartsOn = startsOn,
            EndsOn = endsOn,
            SourcePropertyId = sourcePropertyId,
            ProjectId = projectId,
            Reservations = Ensure.OptionalMax(reservations, nameof(reservations), 4000),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            Status = RightsGrantStatus.Active,
            RecordedAt = now,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };

        // The period is checked by the rules kernel, so an end before a start is
        // impossible whichever path built the row.
        if (DealRules.DescribePeriodProblem(grant.ToRulesPeriod()) is { } problem)
        {
            throw new DomainException(problem);
        }

        return grant;
    }

    /// <summary>
    /// Records that a later grant replaced this one.
    /// </summary>
    /// <remarks>
    /// The row keeps everything it said. What the agency believed it held before
    /// the amendment is a question somebody will ask, and overwriting the values
    /// would erase the answer.
    /// </remarks>
    public void Supersede(RightsGrantId replacement, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != RightsGrantStatus.Active)
        {
            throw new DomainException(
                $"This grant is already {Status.ToString().ToLowerInvariant()}.");
        }

        if (replacement == Id)
        {
            throw new DomainException("A grant cannot supersede itself.");
        }

        Status = RightsGrantStatus.Superseded;
        SupersededByGrantId = replacement;

        Touch(now);
    }

    /// <summary>Records that the grant ended.</summary>
    public void End(DateOnly endedOn, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != RightsGrantStatus.Active)
        {
            throw new DomainException(
                $"This grant is already {Status.ToString().ToLowerInvariant()}.");
        }

        if (StartsOn is { } from && endedOn < from)
        {
            throw new DomainException("A grant cannot end before it starts.");
        }

        Status = RightsGrantStatus.Ended;
        EndsOn = endedOn;
        PeriodKind = GrantPeriodKind.Fixed;

        if (StartsOn is null)
        {
            // An ended grant with no start is a fixed period the kernel would
            // refuse, so it stays open-ended with an end recorded beside it.
            PeriodKind = GrantPeriodKind.OpenEnded;
        }

        Touch(now);
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(RightsGrant), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
