using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;
using Xunit;

namespace AgencyOS.Tests.Unit.Opportunities;

/// <summary>
/// The opportunity and target state machines, enumerated exhaustively.
/// </summary>
/// <remarks>
/// <para>
/// No TLA+, for the reason M4 and M5 gave. Nothing in M6 is a distributed protocol
/// that M3's idempotency and sync guarantees do not already cover; the genuinely
/// concurrent hazards - two people closing one target, a duplicate submission
/// retry - are answered by database constraints and demonstrated against
/// PostgreSQL.
/// </para>
/// <para>
/// What replaces a model is enumeration: every state paired with every target,
/// asserting exactly the published table is accepted. For machines this size that
/// is the whole state space rather than a sample.
/// </para>
/// </remarks>
public sealed class OpportunityStatusTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateOnly Opened = new(2026, 1, 5);
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (OpportunityStatus from in Enum.GetValues<OpportunityStatus>())
        {
            foreach (OpportunityStatus to in Enum.GetValues<OpportunityStatus>())
            {
                checkedPairs++;

                if (from == to)
                {
                    // Asking for the state you are already in does nothing, so a
                    // retried command lands where the first attempt did.
                    Opportunity same = AtStatus(from);
                    int before = same.Version;

                    same.ChangeStatus(to, Opened.AddDays(30), Now, Actor, same.Version, OutcomeFor(to));

                    Assert.Equal(from, same.Status);
                    Assert.Equal(before, same.Version);
                    continue;
                }

                bool allowed = Opportunity.AllowedTransitions[from].Contains(to);

                Opportunity opportunity = AtStatus(from);

                if (allowed)
                {
                    opportunity.ChangeStatus(
                        to, Opened.AddDays(30), Now, Actor, opportunity.Version, OutcomeFor(to));

                    Assert.Equal(to, opportunity.Status);
                }
                else
                {
                    Assert.Throws<DomainException>(() => opportunity.ChangeStatus(
                        to, Opened.AddDays(30), Now, Actor, opportunity.Version, OutcomeFor(to)));

                    Assert.Equal(from, opportunity.Status);
                }
            }
        }

        // Five statuses squared: the complete state space.
        Assert.Equal(25, checkedPairs);
    }

    /// <summary>Cancelled is terminal; closed is not.</summary>
    /// <remarks>
    /// A buyer coming back weeks after a pursuit was closed is ordinary, so a
    /// closed pursuit reopens. Calling something off and then carrying on is a new
    /// pursuit, and recording it as the same one would erase that it was stopped.
    /// </remarks>
    [Fact]
    public void OnlyCancelled_IsTerminal()
    {
        Assert.Empty(Opportunity.AllowedTransitions[OpportunityStatus.Cancelled]);
        Assert.NotEmpty(Opportunity.AllowedTransitions[OpportunityStatus.Closed]);
    }

    /// <summary>Closing records what came of the pursuit.</summary>
    [Fact]
    public void ClosingWithoutAnOutcome_IsRefused()
    {
        Opportunity opportunity = AtStatus(OpportunityStatus.Active);

        DomainException failure = Assert.Throws<DomainException>(() => opportunity.ChangeStatus(
            OpportunityStatus.Closed, Opened.AddDays(30), Now, Actor, opportunity.Version));

        Assert.Contains("outcome", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An outcome belongs only to a closed pursuit.</summary>
    [Fact]
    public void AnOutcomeOnAnythingButClosing_IsRefused()
    {
        Opportunity opportunity = AtStatus(OpportunityStatus.Active);

        Assert.Throws<DomainException>(() => opportunity.ChangeStatus(
            OpportunityStatus.Paused,
            Opened.AddDays(30),
            Now,
            Actor,
            opportunity.Version,
            OpportunityOutcome.Placed));
    }

    /// <summary>
    /// The outcome vocabulary says nothing about money.
    /// </summary>
    /// <remarks>
    /// Whether a deal happened is an M7 question. A Won on an M6 record would be a
    /// claim this milestone has no way to substantiate, and somebody would report
    /// on it (ADR-0020).
    /// </remarks>
    [Fact]
    public void Outcomes_AreAboutThePursuitRatherThanADeal()
    {
        string[] names = [.. Enum.GetNames<OpportunityOutcome>()];

        Assert.DoesNotContain("Won", names);
        Assert.DoesNotContain("Lost", names);
        Assert.DoesNotContain("Sold", names);
        Assert.DoesNotContain("Closed", names);
    }

    /// <summary>Reopening clears the ending, because it no longer describes anything.</summary>
    [Fact]
    public void Reopening_ClearsTheEndDateAndOutcome()
    {
        Opportunity opportunity = AtStatus(OpportunityStatus.Active);

        opportunity.ChangeStatus(
            OpportunityStatus.Closed,
            Opened.AddDays(30),
            Now,
            Actor,
            opportunity.Version,
            OpportunityOutcome.NoInterest);

        Assert.NotNull(opportunity.ClosedOn);
        Assert.NotNull(opportunity.Outcome);

        opportunity.ChangeStatus(
            OpportunityStatus.Active, Opened.AddDays(60), Now, Actor, opportunity.Version);

        Assert.Null(opportunity.ClosedOn);
        Assert.Null(opportunity.Outcome);
    }

    /// <summary>A pursuit cannot end before it opened.</summary>
    [Fact]
    public void EndingBeforeItOpened_IsRefused()
    {
        Opportunity opportunity = AtStatus(OpportunityStatus.Active);

        Assert.Throws<DomainException>(() => opportunity.ChangeStatus(
            OpportunityStatus.Cancelled, Opened.AddDays(-1), Now, Actor, opportunity.Version));
    }

    /// <summary>
    /// Market activity is only recordable against a pursuit being worked.
    /// </summary>
    /// <remarks>
    /// A draft has not gone out; a closed pursuit is history. Recording a
    /// submission against either would describe something that did not happen the
    /// way the record says.
    /// </remarks>
    [Theory]
    [InlineData(OpportunityStatus.Draft, false)]
    [InlineData(OpportunityStatus.Active, true)]
    [InlineData(OpportunityStatus.Paused, false)]
    [InlineData(OpportunityStatus.Closed, false)]
    [InlineData(OpportunityStatus.Cancelled, false)]
    public void OnlyAnActivePursuit_AcceptsMarketActivity(OpportunityStatus status, bool accepts)
    {
        Opportunity opportunity = AtStatus(status);

        Assert.Equal(accepts, opportunity.AcceptsMarketActivity);

        if (accepts)
        {
            opportunity.RequireMarketActive();
        }
        else
        {
            Assert.Throws<DomainException>(opportunity.RequireMarketActive);
        }
    }

    /// <summary>Every status change is recorded, not just the current value.</summary>
    [Fact]
    public void EveryStatusChange_IsRecorded()
    {
        Opportunity opportunity = Create();

        opportunity.ChangeStatus(OpportunityStatus.Active, Opened, Now, Actor, opportunity.Version);
        opportunity.ChangeStatus(OpportunityStatus.Paused, Opened, Now, Actor, opportunity.Version);
        opportunity.ChangeStatus(OpportunityStatus.Active, Opened, Now, Actor, opportunity.Version);

        Assert.Equal(4, opportunity.Events.Count);
        Assert.Single(opportunity.Events, x => x.Kind == OpportunityChangeKind.Created);
    }

    [Fact]
    public void AStaleChange_IsRefused()
    {
        Opportunity opportunity = Create();

        opportunity.ChangeStatus(OpportunityStatus.Active, Opened, Now, Actor, opportunity.Version);

        Assert.Throws<ConcurrencyConflictException>(() => opportunity.ChangeStatus(
            OpportunityStatus.Paused, Opened, Now, Actor, expectedVersion: 1));
    }

    // ---------------------------------------------------------------- subjects

    /// <summary>Each kind of pursuit requires the subject that makes it that kind.</summary>
    /// <remarks>
    /// A talent engagement with no client is not a sparser record, it is a
    /// different thing wearing the wrong label. Checked when the pursuit is taken
    /// out rather than at creation, because a draft is exactly where somebody
    /// assembles the pieces.
    /// </remarks>
    [Theory]
    [InlineData(OpportunityKind.TalentEngagement, OpportunitySubjectKind.TalentProfile)]
    [InlineData(OpportunityKind.ProjectMarket, OpportunitySubjectKind.Project)]
    [InlineData(OpportunityKind.PackageMarket, OpportunitySubjectKind.Package)]
    [InlineData(OpportunityKind.Staffing, OpportunitySubjectKind.ProjectRole)]
    public void EachKind_RequiresItsOwnSubject(
        OpportunityKind kind,
        OpportunitySubjectKind required)
    {
        Assert.Equal(required, Opportunity.RequiredSubjectKind(kind));

        Opportunity opportunity = Create(kind);

        // Nothing attached: refused.
        Assert.Throws<DomainException>(opportunity.RequireCoherentSubjects);

        // The wrong kind attached: still refused.
        opportunity.AddSubject(
            SubjectOfKind(required == OpportunitySubjectKind.Project
                ? OpportunitySubjectKind.Package
                : OpportunitySubjectKind.Project),
            OpportunitySubjectRole.Context,
            Now,
            opportunity.Version);

        Assert.Throws<DomainException>(opportunity.RequireCoherentSubjects);

        // The right kind: accepted.
        opportunity.AddSubject(
            SubjectOfKind(required), OpportunitySubjectRole.Primary, Now, opportunity.Version);

        opportunity.RequireCoherentSubjects();
    }

    /// <summary>Open kinds accept any subject, but still need one.</summary>
    [Theory]
    [InlineData(OpportunityKind.Partnership)]
    [InlineData(OpportunityKind.Other)]
    public void OpenKinds_NeedASubjectButNotAParticularOne(OpportunityKind kind)
    {
        Assert.Null(Opportunity.RequiredSubjectKind(kind));

        Opportunity opportunity = Create(kind);

        Assert.Throws<DomainException>(opportunity.RequireCoherentSubjects);

        opportunity.AddSubject(
            SubjectOfKind(OpportunitySubjectKind.Project),
            OpportunitySubjectRole.Primary,
            Now,
            opportunity.Version);

        opportunity.RequireCoherentSubjects();
    }

    /// <summary>The primary subject is derived from the kind, never flagged.</summary>
    /// <remarks>
    /// A stored "is primary" boolean is a second source of truth that drifts the
    /// moment somebody adds a subject and forgets to move the flag.
    /// </remarks>
    [Fact]
    public void ThePrimarySubject_IsDerivedFromTheKind()
    {
        Opportunity opportunity = Create(OpportunityKind.PackageMarket);

        opportunity.AddSubject(
            SubjectOfKind(OpportunitySubjectKind.Project),
            OpportunitySubjectRole.Context,
            Now,
            opportunity.Version);

        OpportunitySubject package = opportunity.AddSubject(
            SubjectOfKind(OpportunitySubjectKind.Package),
            OpportunitySubjectRole.Primary,
            Now,
            opportunity.Version);

        Assert.Equal(package.Id, opportunity.PrimarySubject!.Id);
    }

    /// <summary>Adding the same subject twice is what a retry looks like.</summary>
    [Fact]
    public void AddingTheSameSubjectTwice_IsIdempotent()
    {
        Opportunity opportunity = Create(OpportunityKind.ProjectMarket);

        OpportunitySubjectRef subject = SubjectOfKind(OpportunitySubjectKind.Project);

        OpportunitySubject first = opportunity.AddSubject(
            subject, OpportunitySubjectRole.Primary, Now, opportunity.Version);

        int after = opportunity.Version;

        OpportunitySubject second = opportunity.AddSubject(
            subject, OpportunitySubjectRole.Primary, Now, opportunity.Version);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(opportunity.Subjects);
        Assert.Equal(after, opportunity.Version);
    }

    /// <summary>Removing the last required subject would leave an incoherent record.</summary>
    [Fact]
    public void RemovingTheOnlyRequiredSubject_IsRefused()
    {
        Opportunity opportunity = Create(OpportunityKind.ProjectMarket);

        OpportunitySubject subject = opportunity.AddSubject(
            SubjectOfKind(OpportunitySubjectKind.Project),
            OpportunitySubjectRole.Primary,
            Now,
            opportunity.Version);

        DomainException failure = Assert.Throws<DomainException>(
            () => opportunity.RemoveSubject(subject.Id, Now, opportunity.Version));

        Assert.Contains("must keep one", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A subject names exactly one thing.</summary>
    [Fact]
    public void ASubjectNamingNothing_IsRefused()
    {
        Opportunity opportunity = Create();

        Assert.Throws<DomainException>(() => opportunity.AddSubject(
            default, OpportunitySubjectRole.Primary, Now, opportunity.Version));
    }

    /// <summary>A finished pursuit is kept as it stood.</summary>
    [Fact]
    public void ATerminalPursuit_RefusesSubjectChanges()
    {
        Opportunity opportunity = AtStatus(OpportunityStatus.Cancelled);

        Assert.Throws<DomainException>(() => opportunity.AddSubject(
            SubjectOfKind(OpportunitySubjectKind.Project),
            OpportunitySubjectRole.Primary,
            Now,
            opportunity.Version));
    }

    internal static Opportunity Create(OpportunityKind kind = OpportunityKind.ProjectMarket) =>
        Opportunity.Create(Tenant, "The Undertow to market", kind, Actor, Opened, Actor, Now);

    internal static OpportunitySubjectRef SubjectOfKind(OpportunitySubjectKind kind) => kind switch
    {
        OpportunitySubjectKind.TalentProfile => OpportunitySubjectRef.Talent(TalentProfileId.New()),
        OpportunitySubjectKind.Project => OpportunitySubjectRef.Project(ProjectId.New()),
        OpportunitySubjectKind.Package => OpportunitySubjectRef.Package(PackageId.New()),
        _ => OpportunitySubjectRef.ProjectRole(ProjectRoleId.New()),
    };

    private static OpportunityOutcome? OutcomeFor(OpportunityStatus status) =>
        status == OpportunityStatus.Closed ? OpportunityOutcome.NoInterest : null;

    /// <summary>Builds a pursuit already at a status, by legal moves only.</summary>
    private static Opportunity AtStatus(OpportunityStatus status)
    {
        Opportunity opportunity = Create();

        if (status == OpportunityStatus.Draft)
        {
            return opportunity;
        }

        foreach (OpportunityStatus step in PathTo(status))
        {
            opportunity.ChangeStatus(
                step, Opened, Now, Actor, opportunity.Version, OutcomeFor(step));
        }

        return opportunity;
    }

    private static IEnumerable<OpportunityStatus> PathTo(OpportunityStatus target) => target switch
    {
        OpportunityStatus.Active => [OpportunityStatus.Active],
        OpportunityStatus.Paused => [OpportunityStatus.Active, OpportunityStatus.Paused],
        OpportunityStatus.Closed => [OpportunityStatus.Active, OpportunityStatus.Closed],
        OpportunityStatus.Cancelled => [OpportunityStatus.Cancelled],
        _ => [],
    };
}

/// <summary>The per-target pipeline, enumerated exhaustively.</summary>
public sealed class OpportunityTargetStageTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly CompanyId Buyer = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (OpportunityTargetStage from in Enum.GetValues<OpportunityTargetStage>())
        {
            foreach (OpportunityTargetStage to in Enum.GetValues<OpportunityTargetStage>())
            {
                checkedPairs++;

                if (from == to)
                {
                    OpportunityTarget same = AtStage(from);
                    int before = same.Version;

                    same.MoveTo(to, Now, Now, Actor, same.Version);

                    Assert.Equal(from, same.Stage);
                    Assert.Equal(before, same.Version);
                    continue;
                }

                bool allowed = OpportunityTarget.AllowedTransitions[from].Contains(to);

                OpportunityTarget target = AtStage(from);

                if (allowed)
                {
                    target.MoveTo(to, Now, Now, Actor, target.Version);
                    Assert.Equal(to, target.Stage);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => target.MoveTo(to, Now, Now, Actor, target.Version));

                    Assert.Equal(from, target.Stage);
                }
            }
        }

        // Nine stages squared: the complete state space.
        Assert.Equal(81, checkedPairs);
    }

    /// <summary>
    /// There is no Submitted stage, and that is the point.
    /// </summary>
    /// <remarks>
    /// Whether material has gone out is answered by the submission rows. A stage
    /// saying the same thing would be a second source of truth that can disagree
    /// with them, which is exactly what M6 was told not to build (ADR-0020).
    /// </remarks>
    [Fact]
    public void TheStageVocabulary_DoesNotDuplicateSubmissionFacts()
    {
        string[] names = [.. Enum.GetNames<OpportunityTargetStage>()];

        Assert.DoesNotContain("Submitted", names);
        Assert.DoesNotContain("Pitched", names);
    }

    /// <summary>
    /// Advanced is the M7 boundary and says nothing about terms.
    /// </summary>
    /// <remarks>
    /// It records that commercial discussion has become concrete, without claiming
    /// an offer exists. There is no Won, no Offer, no Deal - M6 has no way to
    /// describe those and a placeholder would be worse than saying less.
    /// </remarks>
    [Fact]
    public void TheStageVocabulary_StopsShortOfADeal()
    {
        string[] names = [.. Enum.GetNames<OpportunityTargetStage>()];

        Assert.Contains("Advanced", names);
        Assert.DoesNotContain("Won", names);
        Assert.DoesNotContain("OfferReceived", names);
        Assert.DoesNotContain("Closed", names);
    }

    /// <summary>Terminal stages accept nothing further.</summary>
    [Theory]
    [InlineData(OpportunityTargetStage.Passed)]
    [InlineData(OpportunityTargetStage.Withdrawn)]
    [InlineData(OpportunityTargetStage.Exhausted)]
    public void TerminalStages_AcceptNothing(OpportunityTargetStage terminal)
    {
        Assert.Empty(OpportunityTarget.AllowedTransitions[terminal]);

        OpportunityTarget target = AtStage(terminal);

        Assert.False(target.IsOpen);
        Assert.Throws<DomainException>(target.RequireNotTerminal);
    }

    /// <summary>Closing a target clears its next action, so it leaves every follow-up list.</summary>
    [Fact]
    public void AClosedTarget_HasNoNextAction()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Contacted);

        target.Update(null, null, new DateOnly(2026, 3, 1), null, Now, target.Version);

        Assert.NotNull(target.NextActionOn);

        target.MoveTo(OpportunityTargetStage.Passed, Now, Now, Actor, target.Version);

        Assert.Null(target.NextActionOn);
        Assert.NotNull(target.ClosedOn);
    }

    /// <summary>A target is a company or a person, never both and never neither.</summary>
    [Fact]
    public void ATargetWithBothOrNeitherEndpoint_IsRefused()
    {
        Assert.Throws<DomainException>(() => OpportunityTarget.Create(
            Tenant, OpportunityId.New(), Buyer, new PersonId(Guid.NewGuid()), null, Now, Actor));

        Assert.Throws<DomainException>(() => OpportunityTarget.Create(
            Tenant, OpportunityId.New(), null, null, null, Now, Actor));
    }

    /// <summary>A contact belongs to a company target.</summary>
    /// <remarks>
    /// Against a person target it would be either the same person or somebody with
    /// no stated relationship to them.
    /// </remarks>
    [Fact]
    public void AContactOnAPersonTarget_IsRefused()
    {
        Assert.Throws<DomainException>(() => OpportunityTarget.Create(
            Tenant,
            OpportunityId.New(),
            null,
            new PersonId(Guid.NewGuid()),
            new PersonId(Guid.NewGuid()),
            Now,
            Actor));
    }

    /// <summary>
    /// Recording a submission moves a contacted target along, once.
    /// </summary>
    /// <remarks>
    /// This is the only place the pipeline reacts to a submission, and it is a real
    /// transition with an event rather than a flag - so whether anything was sent
    /// stays answerable from the submission rows alone.
    /// </remarks>
    [Fact]
    public void ASubmission_MovesAContactedTargetToEngaged()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Contacted);

        target.NoteSubmission(SubmissionId.New(), Now, Now, Actor);

        Assert.Equal(OpportunityTargetStage.Engaged, target.Stage);
    }

    /// <summary>An already-engaged target does not move backwards or forwards for a submission.</summary>
    [Fact]
    public void ASubmissionToAnEngagedTarget_RecordsWithoutMovingIt()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Interested);

        target.NoteSubmission(SubmissionId.New(), Now, Now, Actor);

        Assert.Equal(OpportunityTargetStage.Interested, target.Stage);
        Assert.Contains(target.Events, x => x.Kind == OpportunityTargetEventKind.Noted);
    }

    /// <summary>Material cannot be recorded as sent to somebody nobody has approached.</summary>
    [Theory]
    [InlineData(OpportunityTargetStage.Identified)]
    [InlineData(OpportunityTargetStage.Approved)]
    public void ASubmissionBeforeContact_IsRefused(OpportunityTargetStage stage)
    {
        OpportunityTarget target = AtStage(stage);

        DomainException failure = Assert.Throws<DomainException>(
            () => target.NoteSubmission(SubmissionId.New(), Now, Now, Actor));

        Assert.Contains("approach", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A stage change is recorded by moving, never as a loose event.</summary>
    [Fact]
    public void RecordingAStageChangeAsAnEvent_IsRefused()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Contacted);

        Assert.Throws<DomainException>(() => target.RecordEvent(
            OpportunityTargetEventKind.StageChanged, Now, Now, Actor, target.Version));
    }

    /// <summary>Every stage change is recorded, with what caused it.</summary>
    [Fact]
    public void EveryStageChange_IsRecordedWithItsCause()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Contacted);

        SubmissionId submission = SubmissionId.New();

        target.NoteSubmission(submission, Now, Now, Actor);

        OpportunityTargetEvent entry = target.Events
            .Where(x => x.Kind == OpportunityTargetEventKind.StageChanged)
            .OrderBy(x => x.RecordedAt)
            .Last();

        Assert.Equal(OpportunityTargetStage.Contacted, entry.FromStage);
        Assert.Equal(OpportunityTargetStage.Engaged, entry.ToStage);
        Assert.Equal(submission, entry.SubmissionId);
    }

    [Fact]
    public void AStaleMove_IsRefused()
    {
        OpportunityTarget target = AtStage(OpportunityTargetStage.Contacted);

        int stale = target.Version;

        target.MoveTo(OpportunityTargetStage.Engaged, Now, Now, Actor, stale);

        Assert.Throws<ConcurrencyConflictException>(
            () => target.MoveTo(OpportunityTargetStage.Interested, Now, Now, Actor, stale));
    }

    internal static OpportunityTarget AtStage(OpportunityTargetStage stage)
    {
        OpportunityTarget target = OpportunityTarget.Create(
            Tenant, OpportunityId.New(), Buyer, null, null, Now, Actor);

        foreach (OpportunityTargetStage step in PathTo(stage))
        {
            target.MoveTo(step, Now, Now, Actor, target.Version);
        }

        return target;
    }

    private static IEnumerable<OpportunityTargetStage> PathTo(OpportunityTargetStage target) =>
        target switch
        {
            OpportunityTargetStage.Identified => [],
            OpportunityTargetStage.Approved => [OpportunityTargetStage.Approved],
            OpportunityTargetStage.Contacted =>
                [OpportunityTargetStage.Approved, OpportunityTargetStage.Contacted],
            OpportunityTargetStage.Engaged =>
                [
                    OpportunityTargetStage.Approved,
                    OpportunityTargetStage.Contacted,
                    OpportunityTargetStage.Engaged,
                ],
            OpportunityTargetStage.Interested =>
                [
                    OpportunityTargetStage.Approved,
                    OpportunityTargetStage.Contacted,
                    OpportunityTargetStage.Interested,
                ],
            OpportunityTargetStage.Advanced =>
                [
                    OpportunityTargetStage.Approved,
                    OpportunityTargetStage.Contacted,
                    OpportunityTargetStage.Interested,
                    OpportunityTargetStage.Advanced,
                ],
            OpportunityTargetStage.Passed =>
                [
                    OpportunityTargetStage.Approved,
                    OpportunityTargetStage.Contacted,
                    OpportunityTargetStage.Passed,
                ],
            OpportunityTargetStage.Withdrawn => [OpportunityTargetStage.Withdrawn],
            _ =>
                [
                    OpportunityTargetStage.Approved,
                    OpportunityTargetStage.Contacted,
                    OpportunityTargetStage.Exhausted,
                ],
        };
}

/// <summary>Submissions and pitches: what they record, and what they refuse to claim.</summary>
public sealed class MarketActivityTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The snapshot is what the material said when it went.
    /// </summary>
    /// <remarks>
    /// Materials get retitled and redrafted. Without a copy of the fields at the
    /// time, editing one six months later would silently rewrite the record of what
    /// an agent believed they sent (ADR-0020).
    /// </remarks>
    [Fact]
    public void ASubmittedMaterial_KeepsWhatItSaidAtTheTime()
    {
        Submission submission = Record();

        MaterialId material = MaterialId.New();

        submission.AddMaterial(material, "The Undertow", MaterialType.Screenplay, "Draft 4");

        SubmissionMaterial recorded = Assert.Single(submission.Materials);

        Assert.Equal("The Undertow", recorded.TitleAtSubmission);
        Assert.Equal(MaterialType.Screenplay, recorded.TypeAtSubmission);
        Assert.Equal("Draft 4", recorded.VersionLabelAtSubmission);
        Assert.Equal(material, recorded.MaterialId);
    }

    /// <summary>The same material is not sent twice in one submission.</summary>
    [Fact]
    public void TheSameMaterialTwice_IsRefused()
    {
        Submission submission = Record();

        MaterialId material = MaterialId.New();

        submission.AddMaterial(material, "The Undertow", MaterialType.Screenplay, "Draft 4");

        Assert.Throws<DomainException>(() => submission.AddMaterial(
            material, "The Undertow", MaterialType.Screenplay, "Draft 5"));
    }

    /// <summary>Materials keep the order they were added in.</summary>
    [Fact]
    public void Materials_KeepTheirOrder()
    {
        Submission submission = Record();

        submission.AddMaterial(MaterialId.New(), "Script", MaterialType.Screenplay, null);
        submission.AddMaterial(MaterialId.New(), "Lookbook", MaterialType.Lookbook, null);

        Assert.Equal([0, 1], submission.Materials.Select(x => x.Position).Order());
    }

    /// <summary>A submission cannot be recorded as sent in the future.</summary>
    [Fact]
    public void ASubmissionSentInTheFuture_IsRefused()
    {
        Assert.Throws<DomainException>(() => Submission.Record(
            Tenant,
            OpportunityId.New(),
            OpportunityTargetId.New(),
            Now.AddDays(30),
            Actor,
            SubmissionChannel.Email,
            Actor,
            Now));
    }

    /// <summary>A reply cannot be expected before the submission went.</summary>
    [Fact]
    public void AResponseExpectedBeforeItWasSent_IsRefused()
    {
        Assert.Throws<DomainException>(() => Submission.Record(
            Tenant,
            OpportunityId.New(),
            OpportunityTargetId.New(),
            Now,
            Actor,
            SubmissionChannel.Email,
            Actor,
            Now,
            responseExpectedBy: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1)));
    }

    /// <summary>
    /// Amending a submission changes what was written about it, not what happened.
    /// </summary>
    /// <remarks>
    /// A submission is a record of an event. What can be corrected afterwards is
    /// the agent's own notes - not when it went, not to whom, and not what was in
    /// it.
    /// </remarks>
    [Fact]
    public void Amending_LeavesTheEventItselfAlone()
    {
        Submission submission = Record();

        DateTimeOffset sent = submission.SentAt;
        OpportunityTargetId target = submission.OpportunityTargetId;

        submission.Amend("New subject", "New notes", null, null, Now, submission.Version);

        Assert.Equal(sent, submission.SentAt);
        Assert.Equal(target, submission.OpportunityTargetId);
        Assert.Equal("New subject", submission.Subject);
    }

    /// <summary>Pitch outcomes stop short of anything commercial.</summary>
    [Fact]
    public void PitchOutcomes_AreNotOfferShaped()
    {
        string[] names = [.. Enum.GetNames<PitchOutcome>()];

        Assert.DoesNotContain("OfferReceived", names);
        Assert.DoesNotContain("Countered", names);
        Assert.DoesNotContain("Accepted", names);
        Assert.DoesNotContain("DealClosed", names);
    }

    /// <summary>A pitch carries the interaction it is the reading of.</summary>
    [Fact]
    public void APitch_NamesOneInteraction()
    {
        InteractionId interaction = InteractionId.New();

        OpportunityPitch pitch = OpportunityPitch.Record(
            Tenant,
            OpportunityId.New(),
            OpportunityTargetId.New(),
            interaction,
            PitchKind.Formal,
            PitchOutcome.Interested,
            Now,
            Actor,
            Now);

        Assert.Equal(interaction, pitch.InteractionId);
    }

    /// <summary>An outcome can be revised; agents learn what a room meant a day later.</summary>
    [Fact]
    public void APitchOutcome_CanBeCorrected()
    {
        OpportunityPitch pitch = OpportunityPitch.Record(
            Tenant,
            OpportunityId.New(),
            OpportunityTargetId.New(),
            InteractionId.New(),
            PitchKind.Formal,
            PitchOutcome.NoDecision,
            Now,
            Actor,
            Now);

        pitch.Amend(PitchOutcome.Interested, "The Undertow", null, Now, pitch.Version);

        Assert.Equal(PitchOutcome.Interested, pitch.Outcome);
    }

    [Fact]
    public void AStaleAmendment_IsRefused()
    {
        Submission submission = Record();

        submission.Amend("One", null, null, null, Now, submission.Version);

        Assert.Throws<ConcurrencyConflictException>(
            () => submission.Amend("Two", null, null, null, Now, expectedVersion: 1));
    }

    private static Submission Record() => Submission.Record(
        Tenant,
        OpportunityId.New(),
        OpportunityTargetId.New(),
        Now,
        Actor,
        SubmissionChannel.Email,
        Actor,
        Now);
}
