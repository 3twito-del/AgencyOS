using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// What a proposed tool call binds itself to.
/// </summary>
/// <remarks>
/// The fingerprint tests here are the counterpart to
/// <see cref="AiApprovalTests"/>: one side proves an approval only authorizes its
/// own fingerprint, and this side proves the fingerprint actually distinguishes
/// the things it has to distinguish (§13, §48).
/// </remarks>
public sealed class AiToolRequestTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly OrganizationId OtherOrg = new(Guid.CreateVersion7());
    private static readonly AgentRunId Run = AgentRunId.New();
    private static readonly AgentRunId OtherRun = AgentRunId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Arguments = """{"dueOn":"2026-10-01","title":"Call Dana"}""";

    /// <summary>A read needs nobody. A write is not executable until somebody says so.</summary>
    [Fact]
    public void AReadOnlyRequest_IsProposedAndAWriteAwaitsApproval()
    {
        Assert.Equal(ToolRequestStatus.Proposed, Propose(ToolEffect.ReadOnly).Status);
        Assert.Equal(
            ToolRequestStatus.AwaitingApproval, Propose(ToolEffect.CanonicalWrite).Status);
    }

    /// <summary>
    /// No external-effect tool is reachable by a model in this build.
    /// </summary>
    /// <remarks>
    /// Refused at construction, not filtered at the registry. Sending an email is a
    /// canonical workflow a person carries out, and the aggregate says so where no
    /// future registration can talk it round (§12, §15).
    /// </remarks>
    [Fact]
    public void AnExternalEffectRequest_IsRefused()
    {
        Assert.Throws<DomainException>(() => Propose(ToolEffect.ExternalEffect));
    }

    [Fact]
    public void TheSameRequest_HasTheSameFingerprint()
    {
        Assert.Equal(Propose().Fingerprint, Propose().Fingerprint);
    }

    /// <summary>Different arguments are a different action.</summary>
    [Fact]
    public void DifferentArguments_ProduceDifferentFingerprints()
    {
        string one = AiToolRequest.ComputeFingerprint(Org, Run, "task.create", 1, Arguments);
        string two = AiToolRequest.ComputeFingerprint(
            Org, Run, "task.create", 1, """{"dueOn":"2026-10-01","title":"Call Dror"}""");

        Assert.NotEqual(one, two);
    }

    /// <summary>
    /// A fingerprint cannot be replayed across tenants or lifted between runs.
    /// </summary>
    /// <remarks>
    /// Both are in the hashed material for the same reason: an approval granted in
    /// one organization, or in one run, must not authorize the identical arguments
    /// somewhere else.
    /// </remarks>
    [Fact]
    public void TenantAndRun_ChangeTheFingerprint()
    {
        string baseline = AiToolRequest.ComputeFingerprint(Org, Run, "task.create", 1, Arguments);

        Assert.NotEqual(
            baseline,
            AiToolRequest.ComputeFingerprint(OtherOrg, Run, "task.create", 1, Arguments));

        Assert.NotEqual(
            baseline,
            AiToolRequest.ComputeFingerprint(Org, OtherRun, "task.create", 1, Arguments));
    }

    /// <summary>A tool that changed its meaning is a different tool.</summary>
    [Fact]
    public void ToolNameAndVersion_ChangeTheFingerprint()
    {
        string baseline = AiToolRequest.ComputeFingerprint(Org, Run, "task.create", 1, Arguments);

        Assert.NotEqual(
            baseline,
            AiToolRequest.ComputeFingerprint(Org, Run, "task.close", 1, Arguments));

        Assert.NotEqual(
            baseline,
            AiToolRequest.ComputeFingerprint(Org, Run, "task.create", 2, Arguments));
    }

    /// <summary>
    /// The field separator cannot be forged from inside an argument value.
    /// </summary>
    /// <remarks>
    /// The material is joined on a unit separator, and JSON escapes control
    /// characters, so no argument value can carry a raw one and shift the field
    /// boundaries so that two different requests collide.
    /// </remarks>
    [Fact]
    public void ArgumentsCannotForgeAFieldBoundary()
    {
        string one = AiToolRequest.ComputeFingerprint(
            Org, Run, "task", 1, """{"t":"create1"}""");

        string two = AiToolRequest.ComputeFingerprint(
            Org, Run, "task", 1, """{"t":"create"}""");

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void AnExecutedRequest_CannotRunTwice()
    {
        AiToolRequest request = Propose(ToolEffect.CanonicalWrite);
        request.Approve(Now, request.Version);
        request.Execute("Created.", Now, request.Version);

        Assert.Equal(ToolRequestStatus.Executed, request.Status);
        Assert.Throws<DomainException>(() =>
            request.Execute("Created again.", Now, request.Version));
    }

    [Fact]
    public void ARejectedRequest_CannotBeApproved()
    {
        AiToolRequest request = Propose(ToolEffect.CanonicalWrite);
        request.Reject("Not this one.", Now, request.Version);

        Assert.Throws<DomainException>(() => request.Approve(Now, request.Version));
    }

    /// <summary>An expired request is finished, not merely stale.</summary>
    [Fact]
    public void AnExpiredRequest_IsTerminal()
    {
        AiToolRequest request = Propose(ToolEffect.CanonicalWrite);
        request.Expire(Now.AddHours(1), request.Version);

        Assert.True(request.IsTerminal);
        Assert.Throws<DomainException>(() => request.Approve(Now.AddHours(1), request.Version));
    }

    [Fact]
    public void AVersionBelowOne_IsRefused()
    {
        Assert.Throws<DomainException>(() => AiToolRequest.Propose(
            Org, Run, "task.create", 0, ToolEffect.ReadOnly, Arguments, "Creates a task.", Now));
    }

    private static AiToolRequest Propose(ToolEffect effect = ToolEffect.ReadOnly) =>
        AiToolRequest.Propose(
            Org, Run, "task.create", 1, effect, Arguments, "Creates a task.", Now);
}
