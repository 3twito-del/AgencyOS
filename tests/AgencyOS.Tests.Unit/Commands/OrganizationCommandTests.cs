using AgencyOS.Client.Commands;
using AgencyOS.Client.ViewModels;
using Xunit;

namespace AgencyOS.Tests.Unit.Commands;

// SOURCE-PROOF: The registry half is executed. That the shell dispatches the command
// is read from MainWindow's markup and code-behind, which cannot be constructed
// here.

/// <summary>
/// That the organization surface can be found from the palette, without becoming a workspace.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-015</c>, decided by the owner: `OrganizationPage` stays the
/// navigation pane's settings destination, and the palette gains a command that
/// reaches it. No capability was blocked before this; the defect was that
/// somebody reaching for the palette to manage members would not find it there.
/// </para>
/// <para>
/// Promoting it to an eighteenth workspace was the option not taken, because
/// seventeen already do not fit the pane (<c>AOS-R001-013</c>).
/// </para>
/// </remarks>
public sealed class OrganizationCommandTests
{
    private const string Id = "organization.open";

    /// <summary>The command exists in the one registry the palette reads.</summary>
    [Fact]
    public void TheCommandIsRegistered() =>
        Assert.NotNull(CommandRegistry.Default.Find(Id));

    /// <summary>It is offered by the palette.</summary>
    [Fact]
    public void ThePaletteOffersIt()
    {
        CommandPaletteViewModel palette = new();

        palette.Query = "organization";

        Assert.Contains(palette.Results, x => x.Id == Id);
    }

    /// <summary>It announces itself in words, not as an identifier.</summary>
    /// <remarks>
    /// The same property <c>AOS-R002-018</c> is about: what a palette row says is
    /// the row's title, and a command whose title were its id would be unusable to
    /// somebody listening rather than reading.
    /// </remarks>
    [Fact]
    public void ItAnnouncesAHumanTitle()
    {
        CommandDefinition command = Assert.IsType<CommandDefinition>(
            CommandRegistry.Default.Find(Id));

        Assert.Equal("Go to Organization settings", command.Label);
        Assert.DoesNotContain(".", command.Label, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(command.Category));
    }

    /// <summary>
    /// It is dispatchable rather than a workspace selection.
    /// </summary>
    /// <remarks>
    /// <c>Navigate</c> means "select a workspace" in this registry, and the
    /// registry's own test requires such a command to name one that exists. The
    /// settings destination is not a workspace, so the command is an
    /// <c>Invoke</c> the shell answers — which is also what ADR-0032 requires: the
    /// palette lists only what it can dispatch.
    /// </remarks>
    [Fact]
    public void ItIsAnInvokeWithNoWorkspace()
    {
        CommandDefinition command = Assert.IsType<CommandDefinition>(
            CommandRegistry.Default.Find(Id));

        Assert.Equal(CommandActionKind.Invoke, command.Action);
        Assert.Null(command.Workspace);
    }

    /// <summary>The shell answers it.</summary>
    /// <remarks>
    /// A palette entry that dispatched nowhere is the defect ADR-0032 was written
    /// about. Read from source because the shell cannot be constructed off a UI
    /// thread; the live dispatch is in this wave's runtime evidence.
    /// </remarks>
    [Fact]
    public void TheShellDispatchesIt()
    {
        string shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml.cs"));

        Assert.Contains($"case \"{Id}\":", shell, StringComparison.Ordinal);
        Assert.Contains(
            "Navigation.SelectedItem = Navigation.SettingsItem;",
            shell,
            StringComparison.Ordinal);
    }

    /// <summary>Organization is still not a workspace.</summary>
    /// <remarks>
    /// The decision's other half. If this ever fails, the pane has gained an
    /// eighteenth destination and `AOS-R001-013` has been made worse.
    /// </remarks>
    [Fact]
    public void OrganizationIsNotAWorkspace()
    {
        Assert.Equal(17, AgencyOsWorkspaces.All.Count);
        Assert.DoesNotContain("organization", AgencyOsWorkspaces.Tags);
        Assert.Null(AgencyOsWorkspaces.Find("organization"));
    }

    /// <summary>The pane still owns the settings destination.</summary>
    [Fact]
    public void TheSettingsDestinationRemains()
    {
        string markup = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml"));

        Assert.Contains("IsSettingsVisible=\"True\"", markup, StringComparison.Ordinal);

        string shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml.cs"));

        Assert.Contains("args.IsSettingsSelected", shell, StringComparison.Ordinal);
        Assert.Contains("typeof(Pages.OrganizationPage)", shell, StringComparison.Ordinal);
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
