using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>Opaque, immutable identifier for an <see cref="AiContextLease"/>.</summary>
public readonly record struct AiContextLeaseId(Guid Value)
{
    public static AiContextLeaseId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What has become of a lease.
/// </summary>
/// <remarks>
/// Consumption is a state rather than a flag on the client, because the client is
/// not trusted to tell the server whether it already used one. One-time use is
/// enforced where the row lives (§A, ADR-0035).
/// </remarks>
public enum AiContextLeaseState
{
    /// <summary>Issued and not yet used.</summary>
    Issued = 1,

    /// <summary>Used. It authorizes nothing further.</summary>
    Consumed = 2,

    /// <summary>Withdrawn before use, because the run was cancelled or superseded.</summary>
    Invalidated = 3,
}

/// <summary>
/// Permission to execute one run's context on one device, once.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The primary new security boundary in M13.</strong> Device-local
/// inference means server-assembled, server-authorized context is handed to a
/// Windows process and a result comes back. The lease is what makes that
/// exchange safe to reason about: it names exactly which run, which user, which
/// tenant and which subject the context was assembled for, and it binds the
/// context itself by fingerprint (§A, ADR-0035).
/// </para>
/// <para>
/// Every field exists to refuse a specific substitution. Without the tenant a
/// lease crosses organizations; without the user it crosses people; without the
/// run it lifts context from one question into another; without the subject it
/// answers about a different record; without the fingerprint the client can add
/// source material the server never authorized; without the expiry it never stops
/// being useful; without one-time consumption a single authorization becomes a
/// standing one.
/// </para>
/// <para>
/// <strong>What a lease cannot do is un-disclose.</strong> Once context has
/// reached the workstation's memory, revoking a permission does not reach into
/// that process and erase it. The lease governs what happens next — whether a
/// result is accepted, whether a canonical effect follows — and AgencyOS does not
/// claim more than that (§C).
/// </para>
/// </remarks>
public sealed class AiContextLease
{
    private AiContextLease()
    {
    }

    public AiContextLeaseId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Whose authority the context was assembled under.</summary>
    public UserId UserId { get; private set; }

    public AgentRunId AgentRunId { get; private set; }

    /// <summary>What the run is about, carried so a lease cannot answer about something else.</summary>
    public AgentSubjectKind SubjectKind { get; private set; }

    public Guid? SubjectId { get; private set; }

    /// <summary>Where the holder is permitted to execute.</summary>
    /// <remarks>
    /// Recorded on the lease rather than inferred from the run, so a result
    /// returned against it can be checked against the residency that was actually
    /// authorized rather than against whatever the run says now.
    /// </remarks>
    public ModelResidency Residency { get; private set; }

    /// <summary>
    /// The exact context this lease covers.
    /// </summary>
    /// <remarks>
    /// A hash of the assembled, authorized material. Recomputed when a result
    /// comes back, so a client that added a document, substituted a subject or
    /// widened the context holds a lease that no longer matches what it is
    /// presenting (§B).
    /// </remarks>
    public string ContextFingerprint { get; private set; } = string.Empty;

    /// <summary>Which model the client said it would use, for provenance.</summary>
    public string ModelKey { get; private set; } = string.Empty;

    /// <summary>
    /// The policy that was in force when this was issued.
    /// </summary>
    /// <remarks>
    /// A lease issued under one policy must not survive a change to it unnoticed.
    /// Carrying the version means a stale lease can be recognized rather than
    /// silently honoured (§A).
    /// </remarks>
    public int PolicyVersion { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public AiContextLeaseState State { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public int Version { get; private set; }

    /// <summary>Whether it has been used or withdrawn.</summary>
    public bool IsTerminal => State is AiContextLeaseState.Consumed or AiContextLeaseState.Invalidated;

    /// <summary>
    /// Issues a lease for one run's assembled context.
    /// </summary>
    /// <remarks>
    /// Short-lived on purpose. A lease is permission to execute now, and a window
    /// measured in hours would be a standing grant to a device — which is exactly
    /// what the one-time consumption is there to prevent.
    /// </remarks>
    public static AiContextLease Issue(
        OrganizationId organizationId,
        UserId userId,
        AgentRunId runId,
        AgentSubjectKind subjectKind,
        Guid? subjectId,
        ModelResidency residency,
        string contextFingerprint,
        string modelKey,
        int policyVersion,
        DateTimeOffset now,
        TimeSpan validFor)
    {
        if (validFor <= TimeSpan.Zero)
        {
            throw new DomainException("A lease that is already expired authorizes nothing.");
        }

        if (subjectKind is AgentSubjectKind.None != (subjectId is null))
        {
            throw new DomainException(
                "A lease either names a subject and says what kind it is, or names "
                    + "neither. Half of an arc points at nothing.");
        }

        // A lease exists to permit execution somewhere other than the server. One
        // issued for the server's own provider would be a token nothing consumes.
        if (residency == ModelResidency.ExternalCloud)
        {
            throw new DomainException(
                "External-cloud inference runs on the server's own connection and "
                    + "needs no lease. A lease is how context reaches somewhere "
                    + "AgencyOS does not control.");
        }

        return new AiContextLease
        {
            Id = AiContextLeaseId.New(),
            OrganizationId = organizationId,
            UserId = userId,
            AgentRunId = runId,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            Residency = residency,
            ContextFingerprint = Ensure.NotBlankMax(
                contextFingerprint, nameof(contextFingerprint), 64),
            ModelKey = Ensure.NotBlankMax(modelKey, nameof(modelKey), 200),
            PolicyVersion = policyVersion,
            IssuedAt = now,
            ExpiresAt = now + validFor,
            State = AiContextLeaseState.Issued,
            Version = 1,
        };
    }

    /// <summary>
    /// The value a lease binds to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SHA-256 over the tenant, the user, the run, the subject, the residency and
    /// the rendered context, joined on a unit separator. Every one of those is in
    /// the material because every one of them is a substitution somebody could
    /// otherwise attempt.
    /// </para>
    /// <para>
    /// The separator is a control character. The rendered context is JSON-escaped
    /// AgencyOS output, so no value inside it can carry a raw one and shift the
    /// field boundaries to collide with a different lease — the same reasoning as
    /// M12's approval fingerprint (ADR-0031).
    /// </para>
    /// </remarks>
    public static string ComputeFingerprint(
        OrganizationId organizationId,
        UserId userId,
        AgentRunId runId,
        AgentSubjectKind subjectKind,
        Guid? subjectId,
        ModelResidency residency,
        string renderedContext)
    {
        ArgumentNullException.ThrowIfNull(renderedContext);

        string material = string.Join(
            '\u001f',
            organizationId.Value.ToString("D"),
            userId.Value.ToString("D"),
            runId.Value.ToString("D"),
            ((int)subjectKind).ToString(CultureInfo.InvariantCulture),
            subjectId?.ToString("D") ?? string.Empty,
            ((int)residency).ToString(CultureInfo.InvariantCulture),
            renderedContext);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// Whether this authorizes exactly this execution, right now.
    /// </summary>
    /// <remarks>
    /// Every condition is re-read at the moment of use rather than trusted from
    /// issuance. A lease that was valid when the context was handed over may not
    /// be valid when the result comes back, and the interval between them is the
    /// whole reason the protocol has a specification (§I, ADR-0035).
    /// </remarks>
    public bool Authorizes(
        OrganizationId organizationId,
        UserId userId,
        AgentRunId runId,
        ModelResidency residency,
        string contextFingerprint,
        DateTimeOffset now) =>
        State == AiContextLeaseState.Issued
        && OrganizationId == organizationId
        && UserId == userId
        && AgentRunId == runId
        && Residency == residency
        && string.Equals(ContextFingerprint, contextFingerprint, StringComparison.Ordinal)
        && now <= ExpiresAt;

    /// <summary>
    /// Records that this lease has been used.
    /// </summary>
    /// <remarks>
    /// Terminal. A consumed lease authorizes nothing further, which is what stops
    /// a replayed result from producing a second effect (§J).
    /// </remarks>
    public void Consume(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(AiContextLeaseState.Issued);

        State = AiContextLeaseState.Consumed;
        ResolvedAt = now;
        Version++;
    }

    /// <summary>
    /// Withdraws an unused lease.
    /// </summary>
    /// <remarks>
    /// Used when a run is cancelled. It does not reach the context already
    /// disclosed to the device; it stops that disclosure from turning into an
    /// accepted result (§C, §K).
    /// </remarks>
    public void Invalidate(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        if (IsTerminal)
        {
            return;
        }

        State = AiContextLeaseState.Invalidated;
        ResolvedAt = now;
        Version++;
    }

    private void Guard(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(AiContextLease), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Require(AiContextLeaseState state)
    {
        if (State != state)
        {
            throw new DomainException(
                $"That lease is {State.ToString().ToLowerInvariant()} and cannot be used.");
        }
    }
}
