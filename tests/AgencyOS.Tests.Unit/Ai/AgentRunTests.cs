using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// The states a run can be in, and the history it keeps.
/// </summary>
/// <remarks>
/// A run is the only place that records what a model was asked and what came back,
/// so the invariants worth testing are the ones that keep that record honest: a
/// terminal run cannot acquire more history, a failure has a category, and the
/// counts describe what actually happened (§16, §64, §68).
/// </remarks>
public sealed class AgentRunTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly UserId User = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewRun_IsQueuedAndHasNotFailed()
    {
        AgentRun run = Start();

        Assert.Equal(AgentRunStatus.Queued, run.Status);
        Assert.Equal(AgentFailureKind.None, run.Failure);
        Assert.Null(run.CompletedAt);
        Assert.False(run.IsTerminal);
    }

    /// <summary>Half an arc points at nothing.</summary>
    [Fact]
    public void ASubjectKindWithoutAnId_IsRefused()
    {
        Assert.Throws<DomainException>(() => AgentRun.Start(
            Org, User, AgentKind.DealBrief, "Brief me", "fake", "fake-default",
            "deal-brief", 1, Now, AgentSubjectKind.Deal, subjectId: null));

        Assert.Throws<DomainException>(() => AgentRun.Start(
            Org, User, AgentKind.DealBrief, "Brief me", "fake", "fake-default",
            "deal-brief", 1, Now, AgentSubjectKind.None, subjectId: Guid.CreateVersion7()));
    }

    /// <summary>Waiting for a person is a state, not an absence of one.</summary>
    /// <remarks>
    /// Holding a pending request in memory would lose it on deploy, and a run that
    /// is waiting would be indistinguishable from one that has hung.
    /// </remarks>
    [Fact]
    public void ARunCanWaitForAPersonAndResume()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);
        run.AwaitApproval(Now, run.Version);

        Assert.Equal(AgentRunStatus.AwaitingApproval, run.Status);
        Assert.False(run.IsTerminal);

        run.Begin(Now, run.Version);

        Assert.Equal(AgentRunStatus.Running, run.Status);
    }

    [Fact]
    public void ACompletedRun_RecordsWhenAndCannotBeChanged()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);
        run.Complete("Here is the brief.", Now.AddSeconds(30), run.Version);

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(Now.AddSeconds(30), run.CompletedAt);
        Assert.True(run.IsTerminal);
        Assert.Throws<DomainException>(() =>
            run.Complete("Different.", Now.AddMinutes(1), run.Version));
    }

    /// <summary>
    /// A failure says what kind. "It failed" is not an answer somebody can act on.
    /// </summary>
    [Fact]
    public void AFailedRun_CarriesACategory()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);
        run.Fail(
            AgentFailureKind.ProviderTimeout,
            "The provider did not answer within 120 seconds.",
            Now.AddMinutes(2),
            run.Version);

        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal(AgentFailureKind.ProviderTimeout, run.Failure);
        Assert.NotNull(run.FailureDetail);
    }

    /// <summary>A failure with no category is refused.</summary>
    [Fact]
    public void FailingWithNoCategory_IsRefused()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);

        Assert.Throws<DomainException>(() =>
            run.Fail(AgentFailureKind.None, "Something.", Now, run.Version));
    }

    [Fact]
    public void ACancelledRun_IsTerminalAndRecordsWhen()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);
        run.Cancel(Now.AddSeconds(5), run.Version);

        Assert.Equal(AgentRunStatus.Cancelled, run.Status);
        Assert.Equal(Now.AddSeconds(5), run.CompletedAt);
        Assert.True(run.IsTerminal);
    }

    /// <summary>
    /// A finished run's history is what happened, and nothing further happened.
    /// </summary>
    /// <remarks>
    /// The database enforces this with a trigger as well. Both are wanted: the
    /// aggregate stops the ordinary mistake, and the trigger stops the one that
    /// bypasses the aggregate (§17).
    /// </remarks>
    [Fact]
    public void ATerminalRun_TakesNoFurtherSteps()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);
        run.Complete("Done.", Now, run.Version);

        Assert.Throws<DomainException>(() =>
            run.AppendStep(AgentStepKind.ModelInvocation, "One more turn.", Now));
    }

    /// <summary>The counts describe what happened, so they are derived from it.</summary>
    [Fact]
    public void StepsAreNumberedAndCounted()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);

        run.AppendStep(AgentStepKind.ContextAssembled, "Gathered 4 records.", Now);
        run.AppendStep(AgentStepKind.ModelInvocation, "Asked the model.", Now);
        run.AppendStep(AgentStepKind.ToolCall, "person.get", Now);
        run.AppendStep(AgentStepKind.ToolResult, "Answered.", Now);
        run.AppendStep(AgentStepKind.ModelInvocation, "Asked the model.", Now);

        Assert.Equal(2, run.ModelInvocationCount);
        Assert.Equal(1, run.ToolCallCount);
    }

    [Fact]
    public void AStaleVersion_IsRefused()
    {
        AgentRun run = Start();
        run.Begin(Now, run.Version);

        Assert.Throws<ConcurrencyConflictException>(() => run.Cancel(Now, 1));
    }

    private static AgentRun Start() => AgentRun.Start(
        Org,
        User,
        AgentKind.ResearchCopilot,
        "What do we know about the Netta project?",
        "fake",
        "fake-default",
        "research-copilot",
        1,
        Now);
}
