using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>Opaque, immutable identifier for an <see cref="AiProviderPolicy"/>.</summary>
public readonly record struct AiProviderPolicyId(Guid Value)
{
    public static AiProviderPolicyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What an organization allows AgencyOS to transmit to one model provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the row that stops a server key becoming a data export.</strong>
/// A provider credential configured on the server makes inference technically
/// possible; it does not make it permitted. Until an organization has a row here
/// saying otherwise, nothing of theirs leaves (§43).
/// </para>
/// <para>
/// The policy answers a different question from authorization. "May Ariel read
/// this source-sensitive signal" is decided by M11's grants. "May AgencyOS send
/// that signal to a provider" is decided here, and the two are not the same
/// permission — a firm can perfectly reasonably let its analysts read something it
/// will not put in anybody else's datacentre (§5, §42).
/// </para>
/// <para>
/// No provider credential is stored on this row, encrypted or otherwise. A
/// provider API key is server infrastructure, held in configuration; putting a
/// tenant-editable copy in the database would make a database read into a
/// credential theft (§3).
/// </para>
/// </remarks>
public sealed class AiProviderPolicy
{
    private AiProviderPolicy()
    {
    }

    public AiProviderPolicyId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Which provider, as the server configuration names it.</summary>
    public string ProviderKey { get; private set; } = string.Empty;

    /// <summary>
    /// Whether this organization has turned the provider on at all.
    /// </summary>
    /// <remarks>
    /// False is the default for a new organization, and the absence of a row means
    /// the same thing. Two ways of saying no, and no way of accidentally saying yes.
    /// </remarks>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// The most sensitive classification this organization permits transmitting.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a set, because the classifications are ordered by how
    /// much damage disclosure does and a policy allowing restricted material but
    /// not confidential would be incoherent. The default ceiling is the lowest one.
    /// </remarks>
    public ModelDataSensitivity MaximumSensitivity { get; private set; }

    /// <summary>
    /// Whether AI may propose canonical writes here at all.
    /// </summary>
    /// <remarks>
    /// Separate from the sensitivity ceiling: a firm may be content for a model to
    /// read and summarize while wanting nothing proposed for approval. Off does not
    /// weaken approval — a write still needs a person — it removes the proposal
    /// from the product (§42).
    /// </remarks>
    public bool AllowsCanonicalWriteProposals { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId UpdatedBy { get; private set; }

    public int Version { get; private set; }

    /// <summary>
    /// The policy a new organization has before anybody configures one.
    /// </summary>
    /// <remarks>
    /// Not persisted. Returned when no row exists, so the closed default is a
    /// property of the code rather than of somebody having remembered to insert a
    /// row (§43).
    /// </remarks>
    public static AiProviderPolicy ClosedDefault(
        OrganizationId organizationId,
        string providerKey) =>
        new()
        {
            Id = default,
            OrganizationId = organizationId,
            ProviderKey = providerKey,
            IsEnabled = false,
            MaximumSensitivity = ModelDataSensitivity.Internal,
            AllowsCanonicalWriteProposals = false,
            UpdatedAt = default,
            Version = 0,
        };

    public static AiProviderPolicy Create(
        OrganizationId organizationId,
        string providerKey,
        bool isEnabled,
        ModelDataSensitivity maximumSensitivity,
        bool allowsCanonicalWriteProposals,
        UserId actor,
        DateTimeOffset now)
    {
        RequireReachableCeiling(maximumSensitivity);

        return new AiProviderPolicy
        {
            Id = AiProviderPolicyId.New(),
            OrganizationId = organizationId,
            ProviderKey = Ensure.NotBlankMax(providerKey, nameof(providerKey), 100),
            IsEnabled = isEnabled,
            MaximumSensitivity = maximumSensitivity,
            AllowsCanonicalWriteProposals = allowsCanonicalWriteProposals,
            UpdatedAt = now,
            UpdatedBy = actor,
            Version = 1,
        };
    }

    public void Update(
        bool isEnabled,
        ModelDataSensitivity maximumSensitivity,
        bool allowsCanonicalWriteProposals,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(AiProviderPolicy), Id.ToString(), expectedVersion, Version);
        }

        RequireReachableCeiling(maximumSensitivity);

        IsEnabled = isEnabled;
        MaximumSensitivity = maximumSensitivity;
        AllowsCanonicalWriteProposals = allowsCanonicalWriteProposals;
        UpdatedAt = now;
        UpdatedBy = actor;
        Version++;
    }

    /// <summary>Whether material of this sensitivity may be transmitted.</summary>
    public bool Permits(ModelDataSensitivity sensitivity) =>
        IsEnabled
        && sensitivity != ModelDataSensitivity.Restricted
        && sensitivity <= MaximumSensitivity;

    /// <summary>
    /// Refuses a ceiling that would permit restricted material.
    /// </summary>
    /// <remarks>
    /// Restricted means an organization has said this never leaves, and a setting
    /// that could override it would make the classification advisory. There is
    /// deliberately no administrator, no role and no configuration flag that raises
    /// the ceiling this far (§5, §43).
    /// </remarks>
    private static void RequireReachableCeiling(ModelDataSensitivity ceiling)
    {
        if (ceiling == ModelDataSensitivity.Restricted)
        {
            throw new DomainException(
                "Restricted material is never transmitted to a model provider, so "
                    + "it cannot be set as a ceiling. Choose Protected or lower.");
        }
    }
}

/// <summary>
/// How damaging it would be for this material to leave AgencyOS.
/// </summary>
/// <remarks>
/// <para>
/// A single scale that every existing classification maps onto, because the
/// question "may this be transmitted" has to be answerable about a mixed context
/// block containing a signal, a contract term and an email. Four separate
/// classification vocabularies could not be compared; this one can.
/// </para>
/// <para>
/// The mapping is deliberately pessimistic. Where an existing classification could
/// reasonably map to two levels, it maps to the higher one: the cost of being
/// wrong downward is that somebody's confidence is broken, and the cost of being
/// wrong upward is that a brief is less useful (§5).
/// </para>
/// </remarks>
public enum ModelDataSensitivity
{
    /// <summary>Ordinary internal working material.</summary>
    Internal = 1,

    /// <summary>
    /// Commercially confidential: deal economics, contract terms, finance.
    /// </summary>
    Confidential = 2,

    /// <summary>
    /// Material whose disclosure identifies a person who spoke in confidence, or
    /// which is legally privileged.
    /// </summary>
    /// <remarks>
    /// M11's source-sensitive classification and M10's privileged classification
    /// both land here. Neither becomes eligible for external inference merely
    /// because the reader may see it inside AgencyOS.
    /// </remarks>
    Protected = 3,

    /// <summary>
    /// Material an organization has marked as never leaving.
    /// </summary>
    /// <remarks>
    /// No policy ceiling can reach this level: <see cref="AiProviderPolicy.Permits"/>
    /// compares against a ceiling that cannot be set here, so restricted material
    /// is excluded from every provider by construction rather than by an
    /// administrator remembering.
    /// </remarks>
    Restricted = 4,
}
