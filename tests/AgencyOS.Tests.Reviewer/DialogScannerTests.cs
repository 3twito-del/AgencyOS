using System.IO;
using AgencyOS.Reviewer.Surface;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That the dialog inventory describes the client it is pointed at.
/// </summary>
/// <remarks>
/// <para>
/// This scanner is what tells the runtime pass where to go. Audit 002 Phase A
/// found three defects in it before any result was published: it treated a
/// dialog's own code-behind as the page that opens it, it stopped at the first
/// file that merely named the class, and it read a missing <c>x:Name</c> as a
/// missing control — which alone would have reported 24 reachable dialogs as
/// unreachable.
/// </para>
/// <para>
/// Each of those is pinned below against the real client, because that is the
/// only sample that matters: a scanner that passes on a fixture and misreads the
/// product is exactly what produced those three defects.
/// </para>
/// </remarks>
public sealed class DialogScannerTests
{
    private static readonly Lazy<IReadOnlyList<DialogRecord>> Dialogs =
        new(() => DialogScanner.Scan(SourceIndex.Load(Find())));

    /// <summary>The scanner sees the dialogs the client actually declares.</summary>
    /// <remarks>
    /// A scanner whose discovery silently found nothing would report a clean
    /// inventory forever.
    /// </remarks>
    [Fact]
    public void TheClientsDialogsAreFound() =>
        Assert.True(Dialogs.Value.Count > 50,
            $"Only {Dialogs.Value.Count} dialogs were found in the client.");

    /// <summary>
    /// A dialog is not opened by its own code-behind.
    /// </summary>
    /// <remarks>
    /// Every dialog names its own class, so including the Dialogs folder in the
    /// pages to search made each dialog look like the thing that opens it, and
    /// all 61 came back unopenable.
    /// </remarks>
    [Fact]
    public void ADialogIsNotItsOwnOpener()
    {
        foreach (DialogRecord dialog in Dialogs.Value)
        {
            foreach (DialogOpening opening in dialog.Openings)
            {
                Assert.NotEqual(dialog.DialogId, opening.Page);
            }
        }
    }

    /// <summary>
    /// An opener without a name is still an opener.
    /// </summary>
    /// <remarks>
    /// Most of these buttons carry no <c>x:Name</c>, because nothing in the code
    /// needs to reach them. Reading that as "no control" reported 24 reachable
    /// dialogs as unreachable. What the runtime pass clicks is the label.
    /// </remarks>
    [Fact]
    public void AnUnnamedOpenerIsStillFound()
    {
        DialogRecord addRole = Dialogs.Value.Single(x => x.DialogId == "AddProjectRoleDialog");
        DialogOpening opening = addRole.Openings.Single();

        Assert.True(opening.HasControl);
        Assert.Equal("Add role", opening.ControlLabel);
        Assert.Equal("ProjectsPage", opening.Page);
        Assert.Equal("projects", opening.Workspace);
    }

    /// <summary>
    /// A dialog opened from several pages records all of them.
    /// </summary>
    /// <remarks>
    /// The reason-for-this dialogs are shared. Recording only the first sends the
    /// runtime pass to a page where the button is not.
    /// </remarks>
    [Fact]
    public void EveryOpeningPathIsRecorded()
    {
        DialogRecord shared = Dialogs.Value.Single(x => x.DialogId == "FinanceReasonDialog");

        Assert.True(shared.Openings.Count > 1,
            "FinanceReasonDialog is constructed on more than one page.");
    }

    /// <summary>
    /// The dialogs nothing constructs are still found and still reported.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-017</c>. If the scanner ever starts inventing an opener for
    /// these, a real finding disappears quietly.
    /// </remarks>
    [Fact]
    public void TheDialogsNothingConstructsAreReported()
    {
        string[] dead =
        [
            .. Dialogs.Value.Where(x => x.Openings.Count == 0)
                .Select(x => x.DialogId)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "AddIntelligenceSubjectDialog",
                "CalculateCommissionDialog",
                "RaiseReceivableDialog",
                "RecordMonetaryObligationDialog",
            ],
            dead);
    }

    /// <summary>
    /// The typed-identifier fields are counted.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-006</c>: 20 fields across 13 dialogs. Pinned so the finding
    /// cannot shrink without somebody noticing.
    /// </remarks>
    [Fact]
    public void TheTypedIdentifierFieldsAreCounted()
    {
        int dialogs = Dialogs.Value.Count(x => x.Fields.Any(f => f.TakesRawIdentifier));
        int fields = Dialogs.Value.Sum(x => x.Fields.Count(f => f.TakesRawIdentifier));

        Assert.Equal(13, dialogs);
        Assert.Equal(20, fields);
    }

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
