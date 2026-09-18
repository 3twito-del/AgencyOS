using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the representation commands the server has can be reached from the product.
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
/// </remarks>
public sealed class RepresentationReachTests
{
    /// <summary>Each route the server exposes is called by the client.</summary>
    [Theory]
    [InlineData("/scopes", "AddRepresentationScopeAsync")]
    [InlineData("/scopes/end", "EndRepresentationScopeAsync")]
    [InlineData("/team", "AssignRepresentationTeamMemberAsync")]
    [InlineData("/team/remove", "RemoveRepresentationTeamMemberAsync")]
    public void EveryRepresentationRouteHasACaller(string route, string method)
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

    /// <summary>The workspace that shows the result can start the change.</summary>
    [Theory]
    [InlineData("ChangeScopeAsync")]
    [InlineData("ChangeTeamAsync")]
    public void TheTalentWorkspaceInvokesThem(string method)
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains($"private async Task {method}()", page, StringComparison.Ordinal);
        Assert.Matches(new Regex($@"Click=|_ = {method}\(\)"), page);
    }

    /// <summary>Both are reachable by keyboard as well as by button.</summary>
    /// <remarks>
    /// The palette is the product's keyboard-first way to do anything, so a
    /// command that exists only as a button is half-reachable.
    /// </remarks>
    [Theory]
    [InlineData("representation.scope.change")]
    [InlineData("representation.team.change")]
    public void BothAreInThePalette(string commandId)
    {
        string commands = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Client", "Commands", "AgencyOsCommands.cs"));

        Assert.Contains($"\"{commandId}\"", commands, StringComparison.Ordinal);

        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains($"case \"{commandId}\":", page, StringComparison.Ordinal);
    }

    /// <summary>The buttons exist and start disabled.</summary>
    /// <remarks>
    /// There is nothing to change scopes or a team on until the person has a
    /// representation, and a button that refuses after the fact is worse than one
    /// that says so by being unavailable.
    /// </remarks>
    [Theory]
    [InlineData("ChangeScopeButton")]
    [InlineData("ChangeTeamButton")]
    public void TheButtonsWaitForARelationship(string button)
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

    /// <summary>Every call sends the version the operator was looking at.</summary>
    /// <remarks>
    /// All four commands are version-checked on the server. Sending a version the
    /// client never read would turn a stale write into a silent one.
    /// </remarks>
    [Fact]
    public void EveryChangeCarriesTheObservedVersion()
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
