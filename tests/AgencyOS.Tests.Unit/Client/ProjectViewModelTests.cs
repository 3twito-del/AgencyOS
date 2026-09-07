using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Projects;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>The slate list: filters, states and the summary it shows.</summary>
public sealed class ProjectListViewModelTests
{
    [Fact]
    public async Task TheList_LoadsAndReportsWhatItFound()
    {
        FakeAgencyOsApi api = new();

        api.Projects.Add(Project("The Undertow", "Active", "Development", openRoles: 2));
        api.Projects.Add(Project("Salt Road", "Active", "Packaging", openRoles: 0));

        ProjectListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Projects.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.Equal(1, viewModel.WithOpenRoles);
    }

    /// <summary>The list defaults to what is being worked, not to everything.</summary>
    /// <remarks>
    /// A slate that opens showing every archived project it has ever held is a
    /// slate nobody reads.
    /// </remarks>
    [Fact]
    public async Task TheList_DefaultsToActiveProjects()
    {
        FakeAgencyOsApi api = new();

        ProjectListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal("Active", api.LastProjectFilter.Status);
    }

    [Fact]
    public async Task TheMissingRoleFilter_ReachesTheServer()
    {
        FakeAgencyOsApi api = new();

        ProjectListViewModel viewModel = new(api) { MissingRole = "Director" };

        await viewModel.LoadAsync();

        Assert.Equal("Director", api.LastProjectFilter.MissingRole);
    }

    [Fact]
    public async Task AnEmptySlate_IsAStateRatherThanAnError()
    {
        FakeAgencyOsApi api = new();

        ProjectListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    /// An unreachable server is a state the surface reports, not a crash.
    /// </summary>
    /// <remarks>
    /// Only the failures the client actually understands are turned into messages.
    /// Anything unrecognized is deliberately left to propagate rather than being
    /// flattened into a sentence that might be wrong.
    /// </remarks>
    [Fact]
    public async Task AnUnreachableServer_IsSurfacedRatherThanSwallowed()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new HttpRequestException("no route to host"),
        };

        ProjectListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Contains("no route to host", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An unrecognized failure is not disguised as a friendly message.</summary>
    [Fact]
    public async Task AnUnrecognizedFailure_Propagates()
    {
        FakeAgencyOsApi api = new() { NextFailure = new InvalidOperationException("bug") };

        ProjectListViewModel viewModel = new(api);

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.LoadAsync());
    }

    internal static ProjectSummaryResponse Project(
        string title,
        string status,
        string stage,
        int openRoles = 0,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            null,
            "FeatureFilm",
            status,
            stage,
            2027,
            null,
            null,
            null,
            null,
            openRoles,
            0,
            0,
            DateTimeOffset.UtcNow,
            1);
}

/// <summary>One project's surface: what it has, and what it still needs.</summary>
public sealed class ProjectDetailViewModelTests
{
    [Fact]
    public async Task Gaps_AreRolesNothingCurrentlyHolds()
    {
        FakeAgencyOsApi api = new();

        Guid projectId = Guid.NewGuid();
        api.Projects.Add(ProjectListViewModelTests.Project("The Undertow", "Active", "Packaging", id: projectId));

        Guid director = Guid.NewGuid();
        Guid writer = Guid.NewGuid();

        api.Roles[projectId] =
        [
            new ProjectRoleResponse(director, "Director", null, "Open", true, null, []),
            new ProjectRoleResponse(
                writer,
                "Writer",
                null,
                "Filled",
                false,
                null,
                [Attachment(writer, "Ada Reyes", "Attached", holds: true)]),
        ];

        ProjectDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(projectId);

        ProjectRoleResponse gap = Assert.Single(viewModel.Gaps);

        Assert.Equal("Director", gap.Type);
    }

    /// <summary>
    /// A role whose only attachment has ended counts as a gap again.
    /// </summary>
    /// <remarks>
    /// This is the case that makes "missing a director" mean something. A role left
    /// marked filled after its holder withdrew would quietly drop the project out
    /// of every list of work still to do.
    /// </remarks>
    [Fact]
    public async Task ARoleWhoseHolderWithdrew_IsAGapAgain()
    {
        FakeAgencyOsApi api = new();

        Guid projectId = Guid.NewGuid();
        api.Projects.Add(ProjectListViewModelTests.Project("The Undertow", "Active", "Development", id: projectId));

        Guid director = Guid.NewGuid();

        api.Roles[projectId] =
        [
            new ProjectRoleResponse(
                director,
                "Director",
                null,
                "Open",
                true,
                null,
                [Attachment(director, "Bo Ferreira", "Withdrawn", holds: false)]),
        ];

        ProjectDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(projectId);

        Assert.Single(viewModel.Gaps);
    }

    /// <summary>A closed role is not a gap: somebody closed it deliberately.</summary>
    [Fact]
    public async Task AClosedRole_IsNotAGap()
    {
        FakeAgencyOsApi api = new();

        Guid projectId = Guid.NewGuid();
        api.Projects.Add(ProjectListViewModelTests.Project("The Undertow", "Active", "Development", id: projectId));

        api.Roles[projectId] =
        [
            new ProjectRoleResponse(Guid.NewGuid(), "Composer", null, "Closed", false, null, []),
        ];

        ProjectDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(projectId);

        Assert.Empty(viewModel.Gaps);
    }

    [Fact]
    public async Task TheStanding_SaysStatusStageAndWhatIsOutstanding()
    {
        FakeAgencyOsApi api = new();

        Guid projectId = Guid.NewGuid();
        api.Projects.Add(ProjectListViewModelTests.Project("The Undertow", "Active", "Packaging", id: projectId));

        api.Roles[projectId] =
        [
            new ProjectRoleResponse(Guid.NewGuid(), "Director", null, "Open", true, null, []),
        ];

        ProjectDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(projectId);

        Assert.Contains("Active", viewModel.Standing, StringComparison.Ordinal);
        Assert.Contains("Packaging", viewModel.Standing, StringComparison.Ordinal);
        Assert.Contains("1 role outstanding", viewModel.Standing, StringComparison.Ordinal);
    }

    internal static AttachmentResponse Attachment(Guid roleId, string name, string status, bool holds) =>
        new(
            Guid.NewGuid(),
            roleId,
            "Director",
            null,
            Guid.NewGuid(),
            null,
            name,
            status,
            new DateOnly(2026, 1, 1),
            holds ? null : new DateOnly(2026, 6, 1),
            holds,
            null,
            null,
            DateTimeOffset.UtcNow,
            1);
}

/// <summary>The package workspace, and the distinction it exists to preserve.</summary>
public sealed class PackageViewModelTests
{
    /// <summary>
    /// The one property that matters: facts and hopes land in different lists.
    /// </summary>
    /// <remarks>
    /// A package holds an attached director and a star the agency has not
    /// approached, side by side. If the surface merged them, somebody would
    /// eventually read the second as the first - and tell a buyer so.
    /// </remarks>
    [Fact]
    public async Task AttachedAndProposed_AreKeptApart()
    {
        FakeAgencyOsApi api = new();

        Guid packageId = Guid.NewGuid();
        api.Packages.Add(Package(packageId));

        api.Elements[packageId] =
        [
            Element("Ada Reyes", "AttachedParty", isAttached: true),
            Element("Bo Ferreira", "ProposedPerson", isAttached: false),
            Element("Northgate Pictures", "ProposedCompany", isAttached: false),
        ];

        PackageViewModel viewModel = new(api);

        await viewModel.LoadAsync(packageId);

        Assert.Equal("Ada Reyes", Assert.Single(viewModel.Attached).DisplayName);
        Assert.Equal(2, viewModel.Proposed.Count);
    }

    /// <summary>
    /// An element pointing at an attachment that has ended is no longer a fact.
    /// </summary>
    /// <remarks>
    /// The split is on what is currently true rather than on the element's kind, so
    /// a package does not keep claiming somebody who has since walked away.
    /// </remarks>
    [Fact]
    public async Task AnAttachedElementThatNoLongerHolds_MovesToProposed()
    {
        FakeAgencyOsApi api = new();

        Guid packageId = Guid.NewGuid();
        api.Packages.Add(Package(packageId));

        api.Elements[packageId] = [Element("Ada Reyes", "AttachedParty", isAttached: false)];

        PackageViewModel viewModel = new(api);

        await viewModel.LoadAsync(packageId);

        Assert.Empty(viewModel.Attached);
        Assert.Single(viewModel.Proposed);
    }

    /// <summary>
    /// Strategy absent and strategy empty are indistinguishable to the surface.
    /// </summary>
    /// <remarks>
    /// A caller without <c>packages.strategy.read</c> receives the field absent. If
    /// the UI showed "hidden" it would confirm a note exists, which is exactly what
    /// the redaction is for.
    /// </remarks>
    [Fact]
    public async Task WithoutTheGrant_TheSurfaceShowsNothingRatherThanAPlaceholder()
    {
        FakeAgencyOsApi api = new() { PackageStrategy = null };

        Guid packageId = Guid.NewGuid();
        api.Packages.Add(Package(packageId));

        PackageViewModel viewModel = new(api);

        await viewModel.LoadAsync(packageId);

        Assert.False(viewModel.HasStrategy);
        Assert.Null(viewModel.Package!.StrategyNotes);
    }

    [Fact]
    public async Task WithTheGrant_TheStrategyIsShown()
    {
        FakeAgencyOsApi api = new() { PackageStrategy = "Go to Northgate first; they owe us." };

        Guid packageId = Guid.NewGuid();
        api.Packages.Add(Package(packageId));

        PackageViewModel viewModel = new(api);

        await viewModel.LoadAsync(packageId);

        Assert.True(viewModel.HasStrategy);
    }

    [Fact]
    public async Task Readiness_CountsWhatIsThereAndWhatIsMissing()
    {
        FakeAgencyOsApi api = new();

        Guid packageId = Guid.NewGuid();
        api.Packages.Add(Package(packageId));

        api.Elements[packageId] =
        [
            Element("Ada Reyes", "AttachedParty", isAttached: true),
            Element("Bo Ferreira", "ProposedPerson", isAttached: false),
        ];

        api.PackageGaps.Add(new ProjectRoleResponse(
            Guid.NewGuid(), "Composer", null, "Open", false, null, []));

        PackageViewModel viewModel = new(api);

        await viewModel.LoadAsync(packageId);

        Assert.Contains("1 attached", viewModel.Readiness, StringComparison.Ordinal);
        Assert.Contains("1 proposed", viewModel.Readiness, StringComparison.Ordinal);
        Assert.Contains("1 still to fill", viewModel.Readiness, StringComparison.Ordinal);
    }

    private static PackageSummaryResponse Package(Guid id) => new(
        id,
        Guid.NewGuid(),
        "The Undertow",
        "The Undertow package",
        "Assembling",
        Guid.NewGuid(),
        "Marcus Reid",
        0,
        0,
        DateTimeOffset.UtcNow,
        1);

    private static PackageElementResponse Element(string name, string kind, bool isAttached) => new(
        Guid.NewGuid(),
        kind,
        Guid.NewGuid(),
        name,
        null,
        isAttached,
        null,
        0);
}

/// <summary>The M5 palette commands are offered and routed.</summary>
public sealed class M5PaletteTests
{
    [Theory]
    [InlineData("go.projects")]
    [InlineData("go.packages")]
    [InlineData("project.create")]
    [InlineData("project.open")]
    [InlineData("project.role.add")]
    [InlineData("project.attach")]
    [InlineData("project.company.add")]
    [InlineData("package.create")]
    [InlineData("package.open")]
    [InlineData("package.element.add")]
    public void TheSlateCommands_AreOffered(string commandId)
    {
        Assert.Contains(CommandPaletteViewModel.DefaultCommands(), x => x.Id == commandId);
    }

    /// <summary>
    /// Every offered command is one the build can actually perform.
    /// </summary>
    /// <remarks>
    /// A palette that lists actions nothing handles teaches users to distrust it,
    /// so the list grows with the milestone rather than ahead of it.
    /// </remarks>
    [Fact]
    public void EveryCommand_HasANonEmptyIdAndCategory()
    {
        foreach (PaletteCommand command in CommandPaletteViewModel.DefaultCommands())
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Id));
            Assert.False(string.IsNullOrWhiteSpace(command.Category));
            Assert.False(string.IsNullOrWhiteSpace(command.Title));
        }
    }

    /// <summary>Navigation shortcuts stay unique as the milestone adds pages.</summary>
    [Fact]
    public void NavigationShortcuts_DoNotCollide()
    {
        string[] shortcuts =
        [
            .. CommandPaletteViewModel.DefaultCommands()
                .Where(x => x.Shortcut is { Length: > 0 })
                .Select(x => x.Shortcut!),
        ];

        Assert.Equal(shortcuts.Length, shortcuts.Distinct(StringComparer.Ordinal).Count());
    }
}
