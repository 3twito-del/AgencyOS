using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using Xunit;

namespace AgencyOS.Tests.Unit.Projects;

/// <summary>
/// The project, attachment and package state machines, enumerated exhaustively.
/// </summary>
/// <remarks>
/// <para>
/// No TLA+, for the reason M4 gave and M5 repeats: nothing here is a distributed
/// protocol. These are small synchronous machines, and their only genuinely
/// concurrent hazards - two parties filling one exclusive role, the same element
/// added to a package twice - are made impossible by database constraints and
/// demonstrated by integration tests against PostgreSQL.
/// </para>
/// <para>
/// What replaces a model is enumeration: every state paired with every target,
/// asserting exactly the published table is accepted. For machines this size that
/// is the whole state space rather than a sample.
/// </para>
/// </remarks>
public sealed class ProjectStatusTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryStatusTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (ProjectStatus from in Enum.GetValues<ProjectStatus>())
        {
            foreach (ProjectStatus to in Enum.GetValues<ProjectStatus>())
            {
                checkedPairs++;

                if (from == to)
                {
                    // Asking for the state you are already in does nothing, so a
                    // retried command lands where the first attempt did.
                    Project same = AtStatus(from);
                    int before = same.Version;

                    same.ChangeStatus(to, Now, Actor, same.Version);

                    Assert.Equal(from, same.Status);
                    Assert.Equal(before, same.Version);
                    continue;
                }

                bool allowed = Project.AllowedStatusTransitions[from].Contains(to);

                Project project = AtStatus(from);

                if (allowed)
                {
                    project.ChangeStatus(to, Now, Actor, project.Version);
                    Assert.Equal(to, project.Status);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => project.ChangeStatus(to, Now, Actor, project.Version));

                    Assert.Equal(from, project.Status);
                }
            }
        }

        // Five statuses squared: the complete state space.
        Assert.Equal(25, checkedPairs);
    }

    /// <summary>Archived is the only status with nowhere left to go.</summary>
    [Fact]
    public void OnlyArchived_IsTerminal()
    {
        foreach (ProjectStatus status in Enum.GetValues<ProjectStatus>())
        {
            bool terminal = Project.AllowedStatusTransitions[status].Count == 0;

            Assert.Equal(status == ProjectStatus.Archived, terminal);
        }
    }

    /// <summary>
    /// A cancelled project can come back. A completed one cannot.
    /// </summary>
    /// <remarks>
    /// Revival is an ordinary industry event and the model has to allow it. A
    /// finished film does not un-finish, though: what follows a completed project
    /// is a different project, and recording it as the same one would lose that.
    /// </remarks>
    [Fact]
    public void Cancelled_CanBeRevived_ButCompletedCannot()
    {
        Assert.Contains(ProjectStatus.Active, Project.AllowedStatusTransitions[ProjectStatus.Cancelled]);
        Assert.DoesNotContain(ProjectStatus.Active, Project.AllowedStatusTransitions[ProjectStatus.Completed]);
    }

    /// <summary>
    /// Stage and status are independent except for one rule, checked over the whole matrix.
    /// </summary>
    /// <remarks>
    /// Every stage can follow every other while the project is live, because
    /// projects genuinely fall back out of production. Once the status says the
    /// project has stopped, the stage stops with it - so the record still says how
    /// far it actually got.
    /// </remarks>
    [Fact]
    public void StageMoves_FreelyWhileLive_AndNotAtAllOnceStopped()
    {
        int checkedPairs = 0;

        foreach (ProjectStatus status in Enum.GetValues<ProjectStatus>())
        {
            bool frozen = Project.StageFrozenStatuses.Contains(status);

            foreach (DevelopmentStage from in Enum.GetValues<DevelopmentStage>())
            {
                foreach (DevelopmentStage to in Enum.GetValues<DevelopmentStage>())
                {
                    checkedPairs++;

                    Project project = AtStatusAndStage(status, from);

                    if (from == to)
                    {
                        project.ChangeStage(to, Now, Actor, project.Version);
                        Assert.Equal(from, project.Stage);
                        continue;
                    }

                    if (frozen)
                    {
                        Assert.Throws<DomainException>(
                            () => project.ChangeStage(to, Now, Actor, project.Version));

                        Assert.Equal(from, project.Stage);
                    }
                    else
                    {
                        project.ChangeStage(to, Now, Actor, project.Version);
                        Assert.Equal(to, project.Stage);
                    }
                }
            }
        }

        // Five statuses by seven stages squared: the whole matrix.
        Assert.Equal(5 * 7 * 7, checkedPairs);
    }

    /// <summary>Regression is a first-class move, not a workaround.</summary>
    [Fact]
    public void AProjectThatFallsApart_CanReturnToDevelopment()
    {
        Project project = AtStatusAndStage(ProjectStatus.Active, DevelopmentStage.PreProduction);

        project.ChangeStage(DevelopmentStage.Development, Now, Actor, project.Version, "Financing collapsed");

        Assert.Equal(DevelopmentStage.Development, project.Stage);

        ProjectEvent entry = Assert.Single(
            project.Events, x => x.Kind == ProjectChangeKind.StageChanged);

        Assert.Equal(DevelopmentStage.PreProduction, entry.FromStage);
        Assert.Equal(DevelopmentStage.Development, entry.ToStage);
        Assert.Equal("Financing collapsed", entry.Reason);
    }

    /// <summary>Cancelling preserves how far the project actually got.</summary>
    [Fact]
    public void Cancelling_KeepsTheStageItReached()
    {
        Project project = AtStatusAndStage(ProjectStatus.Active, DevelopmentStage.PreProduction);

        project.ChangeStatus(ProjectStatus.Cancelled, Now, Actor, project.Version, "Studio passed");

        Assert.Equal(DevelopmentStage.PreProduction, project.Stage);

        DomainException failure = Assert.Throws<DomainException>(
            () => project.ChangeStage(DevelopmentStage.Production, Now, Actor, project.Version));

        Assert.Contains("PreProduction", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Every status and stage change is recorded, not just the current value.</summary>
    [Fact]
    public void EveryLifecycleChange_IsRecorded()
    {
        Project project = Create();

        project.ChangeStage(DevelopmentStage.Development, Now, Actor, project.Version);
        project.ChangeStatus(ProjectStatus.Inactive, Now, Actor, project.Version);
        project.ChangeStatus(ProjectStatus.Active, Now, Actor, project.Version);

        Assert.Equal(4, project.Events.Count);
        Assert.Single(project.Events, x => x.Kind == ProjectChangeKind.Created);
        Assert.Single(project.Events, x => x.Kind == ProjectChangeKind.StageChanged);
        Assert.Equal(2, project.Events.Count(x => x.Kind == ProjectChangeKind.StatusChanged));
    }

    /// <summary>A stale version is refused rather than applied over newer state.</summary>
    [Fact]
    public void AStaleChange_IsRefused()
    {
        Project project = Create();

        project.ChangeStage(DevelopmentStage.Development, Now, Actor, project.Version);

        Assert.Throws<ConcurrencyConflictException>(
            () => project.ChangeStatus(ProjectStatus.Inactive, Now, Actor, expectedVersion: 1));
    }

    [Fact]
    public void AnImpossibleYear_IsRefused()
    {
        Assert.Throws<DomainException>(() => Project.Create(
            Tenant, "Mistyped", ProjectType.FeatureFilm, Actor, Now, year: 202));
    }

    internal static Project Create() =>
        Project.Create(Tenant, "The Undertow", ProjectType.FeatureFilm, Actor, Now);

    private static Project AtStatus(ProjectStatus status) =>
        AtStatusAndStage(status, DevelopmentStage.Concept);

    /// <summary>Builds a project already at a status, by legal moves only.</summary>
    /// <remarks>
    /// Reaching the state through the machine rather than by reflection means the
    /// fixture cannot construct something the domain would refuse - which would
    /// make the test prove nothing.
    /// </remarks>
    private static Project AtStatusAndStage(ProjectStatus status, DevelopmentStage stage)
    {
        Project project = Project.Create(
            Tenant, "The Undertow", ProjectType.FeatureFilm, Actor, Now, stage: stage);

        if (status == ProjectStatus.Active)
        {
            return project;
        }

        // Every status is reachable from Active in one step.
        project.ChangeStatus(status, Now, Actor, project.Version);

        return project;
    }
}

/// <summary>The attachment state machine, enumerated exhaustively.</summary>
public sealed class AttachmentLifecycleTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly PersonId Person = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (AttachmentStatus from in Enum.GetValues<AttachmentStatus>())
        {
            foreach (AttachmentStatus to in Enum.GetValues<AttachmentStatus>())
            {
                checkedPairs++;

                if (from == to)
                {
                    Attachment same = AtStatus(from);
                    int before = same.Version;

                    same.ChangeStatus(to, Start.AddDays(30), Now, Actor, same.Version);

                    Assert.Equal(from, same.Status);
                    Assert.Equal(before, same.Version);
                    continue;
                }

                bool allowed = Attachment.AllowedTransitions[from].Contains(to);

                Attachment attachment = AtStatus(from);

                if (allowed)
                {
                    attachment.ChangeStatus(to, Start.AddDays(30), Now, Actor, attachment.Version);
                    Assert.Equal(to, attachment.Status);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => attachment.ChangeStatus(to, Start.AddDays(30), Now, Actor, attachment.Version));

                    Assert.Equal(from, attachment.Status);
                }
            }
        }

        // Five statuses squared: the complete state space.
        Assert.Equal(25, checkedPairs);
    }

    /// <summary>
    /// Being in discussion does not occupy a role, and that is deliberate.
    /// </summary>
    /// <remarks>
    /// Several directors can genuinely be in talks for one job at once. Treating
    /// that as occupancy would both block legitimate records and make the role look
    /// filled when nobody has committed to anything.
    /// </remarks>
    [Theory]
    [InlineData(AttachmentStatus.InDiscussion, false)]
    [InlineData(AttachmentStatus.Attached, true)]
    [InlineData(AttachmentStatus.Conditional, true)]
    [InlineData(AttachmentStatus.Ended, false)]
    [InlineData(AttachmentStatus.Withdrawn, false)]
    public void OnlyCommittedStatuses_HoldTheRole(AttachmentStatus status, bool holds)
    {
        Assert.Equal(holds, Attachment.HoldsRole(status));
    }

    /// <summary>There is no Targeted state, on purpose.</summary>
    /// <remarks>
    /// Wanting somebody for a role is the agency's intention, not a fact about the
    /// project. It lives as a proposed package element and becomes an opportunity
    /// in M6 (ADR-0019). This test exists so that adding one is a deliberate,
    /// visible decision rather than a convenience.
    /// </remarks>
    [Fact]
    public void AttachmentStatuses_AreAllClaimsAboutTheWorld()
    {
        string[] names = [.. Enum.GetNames<AttachmentStatus>()];

        Assert.DoesNotContain("Targeted", names);
        Assert.DoesNotContain("Proposed", names);
        Assert.DoesNotContain("Wanted", names);
    }

    /// <summary>Ending closes the period rather than deleting the record.</summary>
    [Fact]
    public void EndingAnAttachment_ClosesItsPeriod()
    {
        Attachment attachment = AtStatus(AttachmentStatus.Attached);

        attachment.ChangeStatus(
            AttachmentStatus.Withdrawn, Start.AddDays(60), Now, Actor, attachment.Version, "Took another job");

        Assert.Equal(Start.AddDays(60), attachment.EndsOn);
        Assert.False(attachment.IsCurrent);
        Assert.False(attachment.HoldsTheRole);

        // The history says it happened, and why.
        AttachmentEvent last = attachment.Events.OrderBy(x => x.RecordedAt).Last();

        Assert.Equal(AttachmentStatus.Attached, last.FromStatus);
        Assert.Equal(AttachmentStatus.Withdrawn, last.ToStatus);
        Assert.Equal("Took another job", last.Reason);
    }

    /// <summary>An attachment cannot end before it started.</summary>
    [Fact]
    public void EndingBeforeItStarted_IsRefused()
    {
        Attachment attachment = AtStatus(AttachmentStatus.Attached);

        Assert.Throws<DomainException>(() => attachment.ChangeStatus(
            AttachmentStatus.Ended, Start.AddDays(-1), Now, Actor, attachment.Version));
    }

    /// <summary>An attachment cannot be created already over.</summary>
    /// <remarks>
    /// Recording how it began and then ending it keeps the history honest. Creating
    /// one straight into a terminal state would produce a record of something that
    /// apparently never happened.
    /// </remarks>
    [Theory]
    [InlineData(AttachmentStatus.Ended)]
    [InlineData(AttachmentStatus.Withdrawn)]
    public void CreatingATerminalAttachment_IsRefused(AttachmentStatus terminal)
    {
        Project project = ProjectStatusTests.Create();

        ProjectRole role = project.AddRole(ProjectRoleType.Director, Now, project.Version);

        Assert.Throws<DomainException>(() => project.Attach(
            role.Id,
            AttachmentParty.Person(Person),
            terminal,
            Start,
            Now,
            Actor,
            project.Version));
    }

    /// <summary>A party is a person or a company, never neither.</summary>
    [Fact]
    public void AnAttachmentWithNoParty_IsRefused()
    {
        Project project = ProjectStatusTests.Create();

        ProjectRole role = project.AddRole(ProjectRoleType.Producer, Now, project.Version);

        Assert.Throws<DomainException>(() => project.Attach(
            role.Id,
            default,
            AttachmentStatus.Attached,
            Start,
            Now,
            Actor,
            project.Version));
    }

    private static Attachment AtStatus(AttachmentStatus status)
    {
        Project project = ProjectStatusTests.Create();

        ProjectRole role = project.AddRole(ProjectRoleType.Director, Now, project.Version);

        // Terminal states are reached by ending, because that is the only way they
        // arise in the real model.
        AttachmentStatus opening = status is AttachmentStatus.Ended or AttachmentStatus.Withdrawn
            ? AttachmentStatus.Attached
            : status;

        Attachment attachment = project.Attach(
            role.Id,
            AttachmentParty.Person(Person),
            opening,
            Start,
            Now,
            Actor,
            project.Version);

        if (opening != status)
        {
            attachment.ChangeStatus(status, Start.AddDays(5), Now, Actor, attachment.Version);
        }

        return attachment;
    }
}

/// <summary>The package state machine and its element rules.</summary>
public sealed class PackageLifecycleTests
{
    private static readonly OrganizationId Tenant = new(Guid.NewGuid());
    private static readonly UserId Actor = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransition_MatchesThePublishedTable()
    {
        int checkedPairs = 0;

        foreach (PackageStatus from in Enum.GetValues<PackageStatus>())
        {
            foreach (PackageStatus to in Enum.GetValues<PackageStatus>())
            {
                checkedPairs++;

                if (from == to)
                {
                    Package same = AtStatus(from);
                    int before = same.Version;

                    same.ChangeStatus(to, Now, Actor, same.Version);

                    Assert.Equal(from, same.Status);
                    Assert.Equal(before, same.Version);
                    continue;
                }

                bool allowed = Package.AllowedTransitions[from].Contains(to);

                Package package = AtStatus(from);

                if (allowed)
                {
                    package.ChangeStatus(to, Now, Actor, package.Version);
                    Assert.Equal(to, package.Status);
                }
                else
                {
                    Assert.Throws<DomainException>(
                        () => package.ChangeStatus(to, Now, Actor, package.Version));

                    Assert.Equal(from, package.Status);
                }
            }
        }

        // Seven statuses squared: the complete state space.
        Assert.Equal(49, checkedPairs);
    }

    /// <summary>Assembly is not one-way.</summary>
    /// <remarks>
    /// A package found to be missing something goes back from Ready to Assembling.
    /// That is ordinary rather than exceptional, and a forward-only machine would
    /// make people abandon packages to correct them.
    /// </remarks>
    [Fact]
    public void AReadyPackage_CanGoBackToAssembling()
    {
        Assert.Contains(PackageStatus.Assembling, Package.AllowedTransitions[PackageStatus.Ready]);
    }

    /// <summary>There is no submission or negotiation state here, on purpose.</summary>
    /// <remarks>
    /// The moment a package goes to a buyer, that is an opportunity, and
    /// opportunities are M6. A submission state on the package would mean M6 either
    /// duplicating it or inheriting a half-built pipeline (ADR-0019).
    /// </remarks>
    [Fact]
    public void PackageStatuses_AreAboutInternalReadinessOnly()
    {
        string[] names = [.. Enum.GetNames<PackageStatus>()];

        Assert.DoesNotContain("Submitted", names);
        Assert.DoesNotContain("Pitched", names);
        Assert.DoesNotContain("Negotiating", names);
        Assert.DoesNotContain("Sold", names);
    }

    [Theory]
    [InlineData(PackageStatus.Closed)]
    [InlineData(PackageStatus.Abandoned)]
    public void TerminalPackages_AcceptNothing(PackageStatus terminal)
    {
        Assert.Empty(Package.AllowedTransitions[terminal]);

        Package package = AtStatus(terminal);

        Assert.False(package.IsOpen);

        Assert.Throws<DomainException>(() => package.AddElement(
            PackageElementKind.ProposedPerson, Guid.NewGuid(), Now, Actor, package.Version));
    }

    /// <summary>Adding the same element twice is what a retry looks like, so it is not an error.</summary>
    [Fact]
    public void AddingTheSameElementTwice_IsIdempotent()
    {
        Package package = AtStatus(PackageStatus.Assembling);
        Guid target = Guid.NewGuid();

        PackageElement first = package.AddElement(
            PackageElementKind.ProposedPerson, target, Now, Actor, package.Version);

        int afterFirst = package.Version;

        PackageElement second = package.AddElement(
            PackageElementKind.ProposedPerson, target, Now, Actor, package.Version);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(package.Elements);
        Assert.Equal(afterFirst, package.Version);
    }

    /// <summary>The same identifier in two different roles is two elements, not one.</summary>
    /// <remarks>
    /// Uniqueness is per kind and target together. A person proposed for a package
    /// and a material that happens to share no relationship with them are different
    /// things even if the identifiers collided, and the kind is what separates them.
    /// </remarks>
    [Fact]
    public void TheSameTargetUnderTwoKinds_AreDistinctElements()
    {
        Package package = AtStatus(PackageStatus.Assembling);
        Guid target = Guid.NewGuid();

        package.AddElement(PackageElementKind.ProposedPerson, target, Now, Actor, package.Version);
        package.AddElement(PackageElementKind.Material, target, Now, Actor, package.Version);

        Assert.Equal(2, package.Elements.Count);
    }

    /// <summary>Removing something that is already gone is not an error either.</summary>
    [Fact]
    public void RemovingAnAbsentElement_IsIdempotent()
    {
        Package package = AtStatus(PackageStatus.Assembling);
        int before = package.Version;

        package.RemoveElement(Guid.NewGuid(), Now, package.Version);

        Assert.Equal(before, package.Version);
    }

    /// <summary>A package element must point at something.</summary>
    [Fact]
    public void AnElementPointingAtNothing_IsRefused()
    {
        Package package = AtStatus(PackageStatus.Assembling);

        Assert.Throws<DomainException>(() => package.AddElement(
            PackageElementKind.Material, Guid.Empty, Now, Actor, package.Version));
    }

    [Fact]
    public void AStalePackageChange_IsRefused()
    {
        Package package = AtStatus(PackageStatus.Draft);

        package.ChangeStatus(PackageStatus.Assembling, Now, Actor, package.Version);

        Assert.Throws<ConcurrencyConflictException>(
            () => package.ChangeStatus(PackageStatus.Ready, Now, Actor, expectedVersion: 1));
    }

    private static Package AtStatus(PackageStatus status)
    {
        Package package = Package.Create(
            Tenant, ProjectId.New(), "The Undertow package", Actor, Actor, Now);

        if (status == PackageStatus.Draft)
        {
            return package;
        }

        // Walk there by legal moves, so the fixture cannot build a state the domain
        // would refuse.
        foreach (PackageStatus step in PathTo(status))
        {
            package.ChangeStatus(step, Now, Actor, package.Version);
        }

        return package;
    }

    private static IEnumerable<PackageStatus> PathTo(PackageStatus target) => target switch
    {
        PackageStatus.Assembling => [PackageStatus.Assembling],
        PackageStatus.Ready => [PackageStatus.Assembling, PackageStatus.Ready],
        PackageStatus.Active => [PackageStatus.Assembling, PackageStatus.Ready, PackageStatus.Active],
        PackageStatus.Paused => [PackageStatus.Assembling, PackageStatus.Paused],
        PackageStatus.Closed =>
            [PackageStatus.Assembling, PackageStatus.Ready, PackageStatus.Active, PackageStatus.Closed],
        PackageStatus.Abandoned => [PackageStatus.Abandoned],
        _ => [],
    };
}
