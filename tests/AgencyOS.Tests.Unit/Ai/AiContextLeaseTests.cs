using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// What a context lease will and will not authorize.
/// </summary>
/// <remarks>
/// <para>
/// The primary new security boundary in M13. Device-local inference hands
/// server-assembled context to a Windows process and takes a result back, and the
/// lease is the whole of what makes that exchange safe to reason about (ADR-0035).
/// </para>
/// <para>
/// Every refusal below corresponds to a substitution somebody could attempt.
/// Failures here are silent in production: a lease that authorized the wrong
/// tenant, the wrong run or altered context looks exactly like one that worked.
/// </para>
/// </remarks>
public sealed class AiContextLeaseTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly OrganizationId OtherOrg = new(Guid.CreateVersion7());
    private static readonly UserId User = new(Guid.CreateVersion7());
    private static readonly UserId OtherUser = new(Guid.CreateVersion7());
    private static readonly AgentRunId Run = AgentRunId.New();
    private static readonly AgentRunId OtherRun = AgentRunId.New();
    private static readonly Guid Subject = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Context = "### Person: Odile Ferrand\nStatus: client.";

    // -------------------------------------------------------- what it grants

    [Fact]
    public void AFreshLeaseAuthorizesItsOwnExecution()
    {
        AiContextLease lease = Issue();

        Assert.Equal(AiContextLeaseState.Issued, lease.State);
        Assert.True(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    [Fact]
    public void TheWindowIsInclusiveAtItsEdge()
    {
        AiContextLease lease = Issue();

        Assert.True(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, Fingerprint(), lease.ExpiresAt));
    }

    // ------------------------------------------------------ what it refuses

    /// <summary>A lease is for one organization.</summary>
    [Fact]
    public void ALeaseCannotCrossATenant()
    {
        AiContextLease lease = Issue();

        Assert.False(lease.Authorizes(
            OtherOrg, User, Run, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    /// <summary>A lease is for one person.</summary>
    /// <remarks>
    /// The context was assembled under one user's authority. Another user holding
    /// the same lease would be executing against material narrowed for somebody
    /// else's grants.
    /// </remarks>
    [Fact]
    public void ALeaseCannotCrossAUser()
    {
        AiContextLease lease = Issue();

        Assert.False(lease.Authorizes(
            Org, OtherUser, Run, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    /// <summary>A lease is for one run.</summary>
    [Fact]
    public void ALeaseCannotCrossARun()
    {
        AiContextLease lease = Issue();

        Assert.False(lease.Authorizes(
            Org, User, OtherRun, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    /// <summary>
    /// A lease is for one residency.
    /// </summary>
    /// <remarks>
    /// This is the no-fallback guarantee at the domain level. A device-local lease
    /// presented for cloud execution authorizes nothing, so a client that failed
    /// locally cannot reuse its authorization to send the same context somewhere
    /// the policy did not permit (§E).
    /// </remarks>
    [Fact]
    public void ALeaseCannotCrossAResidency()
    {
        AiContextLease lease = Issue();

        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.ExternalCloud, Fingerprint(), Now));
        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.OrganizationControlled, Fingerprint(), Now));
    }

    /// <summary>
    /// Changed context invalidates the lease.
    /// </summary>
    /// <remarks>
    /// The property the fingerprint exists for. A client that added a document,
    /// substituted a subject or widened what it sent holds a lease that no longer
    /// matches what it is presenting (§B).
    /// </remarks>
    [Fact]
    public void ChangedContextIsNotAuthorized()
    {
        AiContextLease lease = Issue();

        string altered = AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.Person, Subject, ModelResidency.DeviceLocal,
            Context + "\n### Document: the confidential one");

        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, altered, Now));
    }

    /// <summary>An expired lease authorizes nothing.</summary>
    [Fact]
    public void AnExpiredLeaseAuthorizesNothing()
    {
        AiContextLease lease = Issue();

        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, Fingerprint(),
            lease.ExpiresAt.AddSeconds(1)));
    }

    /// <summary>
    /// A consumed lease authorizes nothing further.
    /// </summary>
    /// <remarks>
    /// One-time use, enforced on the row rather than by the client saying so. This
    /// is what stops a replayed result from producing a second canonical effect
    /// (§J).
    /// </remarks>
    [Fact]
    public void AConsumedLeaseAuthorizesNothingFurther()
    {
        AiContextLease lease = Issue();
        lease.Consume(Now, lease.Version);

        Assert.Equal(AiContextLeaseState.Consumed, lease.State);
        Assert.True(lease.IsTerminal);
        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    [Fact]
    public void ALeaseIsConsumedOnce()
    {
        AiContextLease lease = Issue();
        lease.Consume(Now, lease.Version);

        Assert.Throws<DomainException>(() => lease.Consume(Now, lease.Version));
    }

    /// <summary>An invalidated lease authorizes nothing.</summary>
    /// <remarks>
    /// Used when a run is cancelled. It does not reach context already disclosed
    /// to the device; it stops that disclosure becoming an accepted result (§C).
    /// </remarks>
    [Fact]
    public void AnInvalidatedLeaseAuthorizesNothing()
    {
        AiContextLease lease = Issue();
        lease.Invalidate(Now, lease.Version);

        Assert.Equal(AiContextLeaseState.Invalidated, lease.State);
        Assert.False(lease.Authorizes(
            Org, User, Run, ModelResidency.DeviceLocal, Fingerprint(), Now));
    }

    /// <summary>Invalidating a used lease changes nothing and is not an error.</summary>
    /// <remarks>
    /// Cancellation races execution. A cancel that arrived after the result was
    /// accepted must not fail loudly, and must not un-consume anything.
    /// </remarks>
    [Fact]
    public void InvalidatingAConsumedLeaseIsAcceptedAndChangesNothing()
    {
        AiContextLease lease = Issue();
        lease.Consume(Now, lease.Version);
        int version = lease.Version;

        lease.Invalidate(Now.AddSeconds(1), version);

        Assert.Equal(AiContextLeaseState.Consumed, lease.State);
        Assert.Equal(version, lease.Version);
    }

    [Fact]
    public void AStaleVersionIsRefused()
    {
        AiContextLease lease = Issue();

        Assert.Throws<ConcurrencyConflictException>(() => lease.Consume(Now, 99));
    }

    // ------------------------------------------------------ the fingerprint

    /// <summary>Every binding field changes the fingerprint.</summary>
    [Fact]
    public void EveryBindingFieldChangesTheFingerprint()
    {
        string baseline = Fingerprint();

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            OtherOrg, User, Run, AgentSubjectKind.Person, Subject,
            ModelResidency.DeviceLocal, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, OtherUser, Run, AgentSubjectKind.Person, Subject,
            ModelResidency.DeviceLocal, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, User, OtherRun, AgentSubjectKind.Person, Subject,
            ModelResidency.DeviceLocal, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.Company, Subject,
            ModelResidency.DeviceLocal, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.Person, Guid.CreateVersion7(),
            ModelResidency.DeviceLocal, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.Person, Subject,
            ModelResidency.OrganizationControlled, Context));

        Assert.NotEqual(baseline, AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.Person, Subject,
            ModelResidency.DeviceLocal, Context + " "));
    }

    [Fact]
    public void TheSameInputsProduceTheSameFingerprint()
    {
        Assert.Equal(Fingerprint(), Fingerprint());
    }

    /// <summary>
    /// Context cannot forge a field boundary.
    /// </summary>
    /// <remarks>
    /// The material is joined on a control character. Rendered context is
    /// AgencyOS output whose untrusted parts are JSON-escaped, so no value inside
    /// it can carry a raw one and shift the boundaries to collide with a different
    /// lease — the same reasoning as M12's approval fingerprint.
    /// </remarks>
    [Fact]
    public void ContextCannotForgeAFieldBoundary()
    {
        string one = AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.None, null, ModelResidency.DeviceLocal, "a");

        string two = AiContextLease.ComputeFingerprint(
            Org, User, Run, AgentSubjectKind.None, null, ModelResidency.DeviceLocal, "a ");

        Assert.NotEqual(one, two);
    }

    // ---------------------------------------------------------- construction

    /// <summary>
    /// A cloud run needs no lease, so one cannot be issued for it.
    /// </summary>
    /// <remarks>
    /// External inference runs on the server's own connection. A lease is how
    /// context reaches somewhere AgencyOS does not control, and issuing one for
    /// the server itself would be a token nothing consumes — and a second, weaker
    /// path to the same context.
    /// </remarks>
    [Fact]
    public void ALeaseCannotBeIssuedForCloudExecution()
    {
        Assert.Throws<DomainException>(() => AiContextLease.Issue(
            Org, User, Run, AgentSubjectKind.Person, Subject, ModelResidency.ExternalCloud,
            Fingerprint(), "windows-local", policyVersion: 1, Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ALeaseWithNoWindowIsRefused()
    {
        Assert.Throws<DomainException>(() => AiContextLease.Issue(
            Org, User, Run, AgentSubjectKind.Person, Subject, ModelResidency.DeviceLocal,
            Fingerprint(), "windows-local", policyVersion: 1, Now, TimeSpan.Zero));
    }

    /// <summary>Half a subject arc points at nothing.</summary>
    [Fact]
    public void AHalfSubjectIsRefused()
    {
        Assert.Throws<DomainException>(() => AiContextLease.Issue(
            Org, User, Run, AgentSubjectKind.Person, null, ModelResidency.DeviceLocal,
            Fingerprint(), "windows-local", policyVersion: 1, Now, TimeSpan.FromMinutes(5)));

        Assert.Throws<DomainException>(() => AiContextLease.Issue(
            Org, User, Run, AgentSubjectKind.None, Subject, ModelResidency.DeviceLocal,
            Fingerprint(), "windows-local", policyVersion: 1, Now, TimeSpan.FromMinutes(5)));
    }

    private static AiContextLease Issue() => AiContextLease.Issue(
        Org,
        User,
        Run,
        AgentSubjectKind.Person,
        Subject,
        ModelResidency.DeviceLocal,
        Fingerprint(),
        "windows-local",
        policyVersion: 1,
        Now,
        TimeSpan.FromMinutes(5));

    private static string Fingerprint() => AiContextLease.ComputeFingerprint(
        Org, User, Run, AgentSubjectKind.Person, Subject, ModelResidency.DeviceLocal, Context);
}
