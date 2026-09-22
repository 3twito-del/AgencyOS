using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That every channel showing a target's contact reaches the same wording for it.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline target carries three identities: the party approached, the
/// individual dealt with at a company target, and the internal member
/// responsible. Two of the three are people, and the row named one of them with
/// nothing saying which.
/// </para>
/// <para>
/// What the wording <em>says</em> is asserted in <c>SemanticRoleTests</c>, against
/// <c>TargetLine</c> and <c>RowLabel</c> directly. These are the structural half:
/// that the three places which show this value all ask that one helper, so they
/// cannot drift back into three answers. The dialog is checked by reading its
/// source, because a <c>ContentDialog</c> needs a XAML host this suite does not
/// have — the same reason <c>DealPaperTruthTests</c> pins the next-action banner
/// that way.
/// </para>
/// </remarks>
public sealed class TargetSurfaceSemanticsTests
{
    /// <summary>The target row asks the shared wording rather than the record.</summary>
    [Fact]
    public void TheTargetRowAsksTheSharedWordingForItsContact()
    {
        string markup = Read("Pages", "PipelinePage.xaml");

        Assert.Contains("StaticResource TargetContact", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding ContactDisplayName", markup, StringComparison.Ordinal);
    }

    /// <summary>The shared wording is registered once, for every surface to use.</summary>
    [Fact]
    public void TheContactWordingIsRegisteredOnce()
    {
        string app = Read("App.xaml");

        Assert.Contains("x:Key=\"TargetContact\"", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pitch dialog names the contact through the same helper.
    /// </summary>
    /// <remarks>
    /// It composed <c>"{DisplayName} - {contact}"</c>, where the dash said the two
    /// values were related and nothing said how.
    /// </remarks>
    [Fact]
    public void ThePitchDialogNamesTheContactThroughTheSharedWording()
    {
        string source = Read("Dialogs", "RecordPitchDialog.xaml.cs");

        Assert.Contains("TargetLine.Contact(target)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "target.ContactDisplayName is { Length: > 0 }", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] file) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, "src", "AgencyOS.Windows", .. file]));

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
