using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;
using Xunit;

namespace AgencyOS.Tests.Unit.Representation;

/// <summary>
/// The representation and prospect state machines, enumerated exhaustively.
/// </summary>
/// <remarks>
/// <para>
/// M3 used TLA+ because its offline queue had lost messages, crashes and
/// interleaving - a genuinely distributed protocol. M4's lifecycles have none of
/// that: they are small, synchronous state machines whose only real risk is
/// concurrent conversion, and that is made impossible by a partial unique index
/// and demonstrated by an integration test against the database.
/// </para>
/// <para>
/// So instead of a model, these tests enumerate <em>every</em> state paired with
/// <em>every</em> target and assert that exactly the transitions in the published
/// table are accepted. For a machine of this size that is not a sample - it is the
/// whole state space, which makes it a proof rather than evidence.
/// </para>
/// </remarks>
public sealed class RepresentationLifecycleTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly PersonId Person = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Every state, every target: exactly the published table is accepted.</summary>
    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (RepresentationStatus from in Enum.GetValues<RepresentationStatus>())
        {
            foreach (RepresentationStatus to in Enum.GetValues<RepresentationStatus>())
            {
                checkedPairs++;

                if (from == to)
                {
                    // Asking for the state you are already in is accepted and does
                    // nothing, so a retried command is not an error.
                    Domain.Representations.Representation same = AtStatus(from);
                    same.TransitionTo(to, Start.AddDays(10), Actor, Now);

                    Assert.Equal(from, same.Status);
                    continue;
                }

                bool allowed = Domain.Representations.Representation.AllowedTransitions[from].Contains(to);

                Domain.Representations.Representation representation = AtStatus(from);

                if (allowed)
                {
                    representation.TransitionTo(to, Start.AddDays(10), Actor, Now);
                    Assert.Equal(to, representation.Status);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => representation.TransitionTo(to, Start.AddDays(10), Actor, Now));

                    Assert.Equal(from, representation.Status);
                }
            }
        }

        // Five statuses squared: the complete state space, not a sample.
        Assert.Equal(25, checkedPairs);
    }

    /// <summary>A terminal representation stays terminal. Re-signing is a new record.</summary>
    [Theory]
    [InlineData(RepresentationStatus.Terminated)]
    [InlineData(RepresentationStatus.Expired)]
    public void TerminalStatuses_AcceptNothing(RepresentationStatus terminal)
    {
        Assert.Empty(Domain.Representations.Representation.AllowedTransitions[terminal]);
    }

    /// <summary>Every non-terminal status can still reach an ending.</summary>
    /// <remarks>
    /// A state a relationship can enter and never leave would be a trap: the agency
    /// could neither act on it nor close it.
    /// </remarks>
    [Fact]
    public void EveryLiveStatus_CanStillReachAnEnding()
    {
        foreach (RepresentationStatus status in Domain.Representations.Representation.NonTerminalStatuses)
        {
            Assert.Contains(
                Domain.Representations.Representation.AllowedTransitions[status],
                target => target is RepresentationStatus.Terminated or RepresentationStatus.Expired);
        }
    }

    [Fact]
    public void ANewRepresentation_StartsPendingAndIsNotYetAClient()
    {
        Domain.Representations.Representation representation = Create();

        Assert.Equal(RepresentationStatus.Pending, representation.Status);
        Assert.False(representation.MakesClient);
        Assert.True(representation.IsNonTerminal);

        // The creation itself is history, so the record never starts blank.
        Assert.Single(representation.Events);
    }

    [Fact]
    public void ActivatingMakesThemAClient()
    {
        Domain.Representations.Representation representation = Create();

        representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);

        Assert.True(representation.MakesClient);
    }

    /// <summary>Suspension is reversible; that is the point of it.</summary>
    [Fact]
    public void SuspendAndResume_AreBothRecorded()
    {
        Domain.Representations.Representation representation = Create();

        representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);
        representation.TransitionTo(RepresentationStatus.Suspended, Start.AddDays(30), Actor, Now, "Sabbatical.");
        representation.TransitionTo(RepresentationStatus.Active, Start.AddDays(120), Actor, Now);

        Assert.True(representation.MakesClient);

        // Created, activated, suspended, resumed: nothing overwritten.
        Assert.Equal(4, representation.Events.Count);
        Assert.Contains(representation.Events, x => x.ToStatus == RepresentationStatus.Suspended);
    }

    /// <summary>Ending the relationship closes its scopes and team rather than leaving them open.</summary>
    [Fact]
    public void Terminating_ClosesScopesAndTeam()
    {
        Domain.Representations.Representation representation = Create();

        representation.AddScope(RepresentationScopeArea.Television, Start, Now);
        representation.AssignTeamMember(Actor, RepresentationTeamRole.Lead, Start, Now);
        representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);

        DateOnly ended = Start.AddDays(200);

        representation.TransitionTo(RepresentationStatus.Terminated, ended, Actor, Now, "Client moved on.");

        Assert.Empty(representation.CurrentScopes);
        Assert.Empty(representation.CurrentTeam);
        Assert.Equal(ended, representation.EndsOn);

        // The rows survive with end dates; nothing was deleted.
        Assert.Single(representation.Scopes);
        Assert.Single(representation.Team);
        Assert.Equal(ended, representation.Scopes.Single().EndsOn);
    }

    [Fact]
    public void ATerminatedRepresentation_RefusesFurtherChange()
    {
        Domain.Representations.Representation representation = Create();

        representation.TransitionTo(RepresentationStatus.Terminated, Start, Actor, Now);

        Assert.Throws<DomainException>(
            () => representation.AddScope(RepresentationScopeArea.Film, Start, Now));

        Assert.Throws<DomainException>(
            () => representation.AssignTeamMember(Actor, RepresentationTeamRole.Agent, Start, Now));
    }

    /// <summary>A change cannot be dated before the relationship existed.</summary>
    [Fact]
    public void ATransitionBeforeTheStartDate_IsRefused()
    {
        Domain.Representations.Representation representation = Create();

        Assert.Throws<DomainException>(
            () => representation.TransitionTo(
                RepresentationStatus.Active,
                Start.AddDays(-1),
                Actor,
                Now));
    }

    [Fact]
    public void AnEndDateBeforeTheStart_IsRefused()
    {
        Assert.Throws<DomainException>(() => Domain.Representations.Representation.Create(
            Tenant,
            Person,
            new DateOnly(2026, 6, 1),
            Actor,
            Now,
            endsOn: new DateOnly(2026, 1, 1)));
    }

    // ------------------------------------------------------------ scope, team

    [Fact]
    public void AddingTheSameScopeTwice_IsIdempotent()
    {
        Domain.Representations.Representation representation = Create();

        representation.AddScope(RepresentationScopeArea.Literary, Start, Now);
        representation.AddScope(RepresentationScopeArea.Literary, Start, Now);

        Assert.Single(representation.CurrentScopes);
    }

    /// <summary>Ending a scope keeps the record that it once applied.</summary>
    [Fact]
    public void EndingAScope_PreservesItsHistory()
    {
        Domain.Representations.Representation representation = Create();

        representation.AddScope(RepresentationScopeArea.Books, Start, Now);
        representation.EndScope(RepresentationScopeArea.Books, Start.AddDays(90), Now);

        Assert.Empty(representation.CurrentScopes);
        Assert.Equal(Start.AddDays(90), Assert.Single(representation.Scopes).EndsOn);

        // And it can be picked up again later, as a new period.
        representation.AddScope(RepresentationScopeArea.Books, Start.AddDays(200), Now);

        Assert.Single(representation.CurrentScopes);
        Assert.Equal(2, representation.Scopes.Count);
    }

    [Fact]
    public void EndingAScopeThatIsNotOpen_IsRefused()
    {
        Domain.Representations.Representation representation = Create();

        Assert.Throws<DomainException>(
            () => representation.EndScope(RepresentationScopeArea.Music, Start, Now));
    }

    /// <summary>The lead is derived from the team, so there is always exactly one.</summary>
    [Fact]
    public void AssigningANewLead_EndsThePreviousOne()
    {
        Domain.Representations.Representation representation = Create();

        UserId first = new(Guid.NewGuid());
        UserId second = new(Guid.NewGuid());

        representation.AssignTeamMember(first, RepresentationTeamRole.Lead, Start, Now);
        representation.AssignTeamMember(second, RepresentationTeamRole.Lead, Start.AddDays(60), Now);

        Assert.Equal(second, representation.Lead!.UserId);
        Assert.Single(representation.CurrentTeam, x => x.Role == RepresentationTeamRole.Lead);

        // The previous lead's assignment survives, with an end date.
        Assert.Contains(representation.Team, x => x.UserId == first && x.EndsOn == Start.AddDays(60));
    }

    [Fact]
    public void ChangingSomebodysRole_EndsTheOldAssignment()
    {
        Domain.Representations.Representation representation = Create();

        UserId user = new(Guid.NewGuid());

        representation.AssignTeamMember(user, RepresentationTeamRole.Coordinator, Start, Now);
        representation.AssignTeamMember(user, RepresentationTeamRole.Agent, Start.AddDays(30), Now);

        Assert.Equal(
            RepresentationTeamRole.Agent,
            Assert.Single(representation.CurrentTeam, x => x.UserId == user).Role);

        Assert.Equal(2, representation.Team.Count(x => x.UserId == user));
    }

    [Fact]
    public void RemovingSomebodyNotOnTheTeam_IsRefused()
    {
        Domain.Representations.Representation representation = Create();

        Assert.Throws<DomainException>(
            () => representation.RemoveTeamMember(new UserId(Guid.NewGuid()), Start, Now));
    }

    [Fact]
    public void AStaleVersion_IsRefused()
    {
        Domain.Representations.Representation representation = Create();

        representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);

        Assert.Throws<ConcurrencyConflictException>(() => representation.RequireVersion(1));

        representation.RequireVersion(representation.Version);
    }

    // ------------------------------------------------------------- plumbing

    private static Domain.Representations.Representation Create() =>
        Domain.Representations.Representation.Create(Tenant, Person, Start, Actor, Now);

    /// <summary>Builds a representation sitting in a given status, legally.</summary>
    private static Domain.Representations.Representation AtStatus(RepresentationStatus status)
    {
        Domain.Representations.Representation representation = Create();

        switch (status)
        {
            case RepresentationStatus.Pending:
                break;

            case RepresentationStatus.Active:
                representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);
                break;

            case RepresentationStatus.Suspended:
                representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);
                representation.TransitionTo(RepresentationStatus.Suspended, Start.AddDays(1), Actor, Now);
                break;

            case RepresentationStatus.Terminated:
                representation.TransitionTo(RepresentationStatus.Terminated, Start, Actor, Now);
                break;

            case RepresentationStatus.Expired:
                representation.TransitionTo(RepresentationStatus.Active, Start, Actor, Now);
                representation.TransitionTo(RepresentationStatus.Expired, Start.AddDays(1), Actor, Now);
                break;

            default:
                throw new InvalidOperationException($"Unhandled status {status}.");
        }

        return representation;
    }
}

/// <summary>The prospect state machine, enumerated exhaustively.</summary>
public sealed class ProspectLifecycleTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly PersonId Person = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateOnly Identified = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (ProspectStage from in Enum.GetValues<ProspectStage>())
        {
            foreach (ProspectStage to in Enum.GetValues<ProspectStage>())
            {
                checkedPairs++;

                if (from == to)
                {
                    Prospect same = AtStage(from);
                    same.TransitionTo(to, Identified.AddDays(5), Actor, Now);

                    Assert.Equal(from, same.Stage);
                    continue;
                }

                bool allowed = Prospect.AllowedTransitions[from].Contains(to);

                Prospect prospect = AtStage(from);

                if (allowed)
                {
                    prospect.TransitionTo(to, Identified.AddDays(5), Actor, Now);
                    Assert.Equal(to, prospect.Stage);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => prospect.TransitionTo(to, Identified.AddDays(5), Actor, Now));

                    Assert.Equal(from, prospect.Stage);
                }
            }
        }

        // Six stages squared: the complete state space.
        Assert.Equal(36, checkedPairs);
    }

    /// <summary>Pursuit only moves forward: a contacted prospect is never merely identified again.</summary>
    [Fact]
    public void PursuitNeverMovesBackwards()
    {
        Assert.DoesNotContain(ProspectStage.Identified, Prospect.AllowedTransitions[ProspectStage.Contacted]);
        Assert.DoesNotContain(ProspectStage.Identified, Prospect.AllowedTransitions[ProspectStage.Courting]);
        Assert.DoesNotContain(ProspectStage.Contacted, Prospect.AllowedTransitions[ProspectStage.Courting]);
    }

    [Theory]
    [InlineData(ProspectStage.Declined)]
    [InlineData(ProspectStage.Lost)]
    [InlineData(ProspectStage.Converted)]
    public void TerminalStages_AcceptNothing(ProspectStage terminal)
    {
        Assert.Empty(Prospect.AllowedTransitions[terminal]);
        Assert.DoesNotContain(terminal, Prospect.OpenStages);
    }

    /// <summary>Closing a pursuit clears the follow-up: there is nothing left to chase.</summary>
    [Theory]
    [InlineData(ProspectStage.Declined)]
    [InlineData(ProspectStage.Lost)]
    public void ClosingAPursuit_ClearsTheFollowUp(ProspectStage stage)
    {
        Prospect prospect = Prospect.Create(
            Tenant,
            Person,
            Actor,
            Identified,
            Actor,
            Now,
            nextFollowUpOn: Identified.AddDays(7));

        Assert.NotNull(prospect.NextFollowUpOn);

        prospect.TransitionTo(stage, Identified.AddDays(3), Actor, Now);

        Assert.Null(prospect.NextFollowUpOn);
        Assert.False(prospect.IsOpen);
    }

    /// <summary>Conversion links the representation and closes the pursuit together.</summary>
    [Fact]
    public void Conversion_LinksTheRepresentationAndClosesThePursuit()
    {
        Prospect prospect = Create();
        RepresentationId representationId = RepresentationId.New();

        prospect.MarkConverted(representationId, Identified.AddDays(30), Actor, Now);

        Assert.Equal(ProspectStage.Converted, prospect.Stage);
        Assert.Equal(representationId, prospect.ConvertedToRepresentationId);
        Assert.False(prospect.IsOpen);
    }

    [Fact]
    public void AChangeBeforeIdentification_IsRefused()
    {
        Prospect prospect = Create();

        Assert.Throws<DomainException>(
            () => prospect.TransitionTo(ProspectStage.Contacted, Identified.AddDays(-1), Actor, Now));
    }

    [Fact]
    public void AClosedPursuit_RefusesFurtherEdits()
    {
        Prospect prospect = Create();

        prospect.TransitionTo(ProspectStage.Lost, Identified.AddDays(10), Actor, Now);

        Assert.Throws<DomainException>(
            () => prospect.Update(Actor, "referral", null, null, Now));
    }

    [Fact]
    public void EveryStageChange_IsRecorded()
    {
        Prospect prospect = Create();

        prospect.TransitionTo(ProspectStage.Contacted, Identified.AddDays(1), Actor, Now);
        prospect.TransitionTo(ProspectStage.Courting, Identified.AddDays(5), Actor, Now);
        prospect.TransitionTo(ProspectStage.Declined, Identified.AddDays(9), Actor, Now, "Signed elsewhere.");

        // Created, contacted, courting, declined.
        Assert.Equal(4, prospect.Events.Count);
        Assert.Equal("Signed elsewhere.", prospect.Events.Last().Reason);
    }

    private static Prospect Create() =>
        Prospect.Create(Tenant, Person, Actor, Identified, Actor, Now);

    private static Prospect AtStage(ProspectStage stage)
    {
        Prospect prospect = Create();

        switch (stage)
        {
            case ProspectStage.Identified:
                break;

            case ProspectStage.Contacted:
                prospect.TransitionTo(ProspectStage.Contacted, Identified, Actor, Now);
                break;

            case ProspectStage.Courting:
                prospect.TransitionTo(ProspectStage.Courting, Identified, Actor, Now);
                break;

            case ProspectStage.Declined:
                prospect.TransitionTo(ProspectStage.Declined, Identified, Actor, Now);
                break;

            case ProspectStage.Lost:
                prospect.TransitionTo(ProspectStage.Lost, Identified, Actor, Now);
                break;

            case ProspectStage.Converted:
                prospect.MarkConverted(RepresentationId.New(), Identified, Actor, Now);
                break;

            default:
                throw new InvalidOperationException($"Unhandled stage {stage}.");
        }

        return prospect;
    }
}
