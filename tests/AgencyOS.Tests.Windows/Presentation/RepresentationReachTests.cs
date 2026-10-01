using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgencyOS.Client.ViewModels;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Structural guards on the representation routes, client methods,
// handlers and buttons. The rules those surfaces obey are executed in
// RepresentationMaintenanceTests.

/// <summary>
/// Structural guards on the wiring between the representation scope and team
/// routes, the client, the palette and the Talent page.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R001-010</c>. Four endpoints had no client caller — add a scope, end one,
/// assign somebody to the team, remove them — while the Talent workspace showed
/// their result. An operator could read who works a relationship and what is
/// represented, and could change neither.
/// </para>
/// <para>
/// Source is read rather than executed: page and dialog code-behind cannot be
/// constructed off a UI thread. The rules these surfaces obey are tested for real
/// in <c>RepresentationMaintenanceTests</c>.
/// </para>
/// <para>
/// <strong>What these prove, and no more (F-07).</strong> They were named for
/// reach — "every route has a caller", "the workspace invokes them", "the buttons
/// wait for a relationship" — and assert that a method, a handler, a registration
/// or an attribute is present. Each is now named for that. They are scoped to the
/// four routes of <c>AOS-R001-010</c> and make no claim about any other
/// representation route.
/// </para>
/// </remarks>
public sealed class RepresentationReachTests
{
    /// <summary>
    /// Each of the four change routes is declared by the server and has a client
    /// method that addresses it.
    /// </summary>
    [Theory]
    [InlineData("/scopes", "AddRepresentationScopeAsync")]
    [InlineData("/scopes/end", "EndRepresentationScopeAsync")]
    [InlineData("/team", "AssignRepresentationTeamMemberAsync")]
    [InlineData("/team/remove", "RemoveRepresentationTeamMemberAsync")]
    public void EachChangeRouteHasAClientMethodAddressingIt(string route, string method)
    {
        string endpoints = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Api", "Endpoints", "M4Endpoints.cs"));

        Assert.Contains(
            $"\"/representations/{{representationId:guid}}{route}\"",
            endpoints,
            StringComparison.Ordinal);

        string client = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Client", "AgencyOsApiClient.cs"));

        Assert.Contains($"public Task {method}(", client, StringComparison.Ordinal);
        Assert.Contains(
            $"/representations/{{representationId}}{route}\"",
            client,
            StringComparison.Ordinal);
    }

    /// <summary>The Talent page defines the scope and team handlers and wires each to an event.</summary>
    [Theory]
    [InlineData("ChangeScopeAsync")]
    [InlineData("ChangeTeamAsync")]
    public void TheTalentPageDefinesAndWiresTheChangeHandlers(string method)
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains($"private async Task {method}()", page, StringComparison.Ordinal);
        Assert.Matches(new Regex($@"Click=|_ = {method}\(\)"), page);
    }

    /// <summary>
    /// Both commands are offered by the real palette, and the Talent page answers
    /// each of them.
    /// </summary>
    /// <remarks>
    /// The palette is the product's keyboard-first way to do anything, so a
    /// command that exists only as a button is half-reachable. The first half is
    /// executed: the palette's own command list is built and searched, rather than
    /// the registry's source read. The second is a source fact, because page
    /// code-behind cannot be constructed here.
    /// </remarks>
    [Theory]
    [InlineData("representation.scope.change")]
    [InlineData("representation.team.change")]
    public void BothAreOfferedByThePaletteAndAnsweredByTheTalentPage(string commandId)
    {
        Assert.Contains(
            new CommandPaletteViewModel().AllCommands, x => x.Id == commandId);

        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains($"case \"{commandId}\":", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The buttons start disabled in markup, and the page's code sets their enabled
    /// state.
    /// </summary>
    /// <remarks>
    /// There is nothing to change scopes or a team on until the person has a
    /// representation, and a button that refuses after the fact is worse than one
    /// that says so by being unavailable.
    /// </remarks>
    [Theory]
    [InlineData("ChangeScopeButton")]
    [InlineData("ChangeTeamButton")]
    public void TheButtonsStartDisabledAndThePageSetsTheirEnabledState(string button)
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement found = Assert.Single(
            XElement.Load(Path.Combine(
                RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml")).Descendants(),
            x => x.Attribute(name)?.Value == button);

        Assert.Equal("False", found.Attribute("IsEnabled")?.Value);

        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains($"{button}.IsEnabled = ", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page builds each change request from the loaded representation's version.
    /// </summary>
    /// <remarks>
    /// All four commands take an expected version, and sending one the client never
    /// read would turn a stale write into a silent one. This shows only that the
    /// page passes the loaded one. No test here exercises the server refusing a
    /// stale version on these four routes; <c>RepresentationTests.AStaleRepresentationVersion_IsRefused</c>
    /// proves that for a status transition, which is a different route.
    /// </remarks>
    [Fact]
    public void EachChangeRequestIsBuiltFromTheLoadedVersion()
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains("dialog.ToRequest(representation.Version)", page, StringComparison.Ordinal);
        Assert.Contains("dialog.ToAssignRequest(representation.Version)", page, StringComparison.Ordinal);
        Assert.Contains("dialog.ToRemoveRequest(representation.Version)", page, StringComparison.Ordinal);
    }

    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
