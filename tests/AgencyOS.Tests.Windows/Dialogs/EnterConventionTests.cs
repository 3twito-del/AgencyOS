using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That <c>Enter</c> means one thing, and that every departure from it is deliberate.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-016</c>, decided by the owner: for an ordinary dialog whose primary
/// button is the normal affirmative create/save/apply action, <c>Enter</c> commits.
/// Before the decision 28 of 65 dialogs committed and 36 cancelled, following no
/// rule an operator could learn — <c>RecordInteractionDialog</c> committed and
/// <c>RecordSignalDialog</c> discarded.
/// </para>
/// <para>
/// The convention has explicit safety exceptions, and this is where they live. An
/// exception needs a classification and a reason; "that is what the markup already
/// said" is not one, which is why the reason is stored beside the name and read by
/// a test rather than left in a commit message.
/// </para>
/// </remarks>
public sealed class EnterConventionTests
{
    /// <summary>How a dialog's primary action relates to the convention.</summary>
    private enum Kind
    {
        /// <summary>An ordinary create, save or apply. Enter commits.</summary>
        NormalAffirmative,

        /// <summary>Approving something that then happens. Enter must not.</summary>
        Approval,

        /// <summary>Leaves the product, or cannot be taken back by editing.</summary>
        IrreversibleOrExternal,
    }

    /// <summary>
    /// Every dialog that departs from the convention, and why.
    /// </summary>
    /// <remarks>
    /// Deliberately short. A long list would mean the convention had not been
    /// decided, only described.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, (Kind Kind, string Default, string Reason)> Exceptions =
        new Dictionary<string, (Kind, string, string)>(StringComparer.Ordinal)
        {
            ["ApproveAiActionDialog"] = (
                Kind.Approval,
                "Secondary",
                "Its primary is 'Approve and run', which executes a tool. The product "
                    + "already defaulted away from it deliberately, and the finding "
                    + "named that as the one clearly considered case. Enter rejects, "
                    + "which is the safe answer to a proposal."),
            ["PostJournalEntryDialog"] = (
                Kind.IrreversibleOrExternal,
                "None",
                "Posting is the act of making a canonical financial record. The domain "
                    + "says so: 'a posted entry is immutable and counts; a reversed "
                    + "entry still counts, because the money moved and then moved back "
                    + "and both movements happened.' It cannot be un-posted, only "
                    + "answered by a further entry."),
            ["ConnectMailboxDialog"] = (
                Kind.IrreversibleOrExternal,
                "None",
                "Connecting hands an authorization code to an external provider and "
                    + "establishes a live connection to somebody's mailbox. The effect "
                    + "leaves the product."),
            ["ConvertProspectDialog"] = (
                Kind.IrreversibleOrExternal,
                "None",
                "Its primary is 'Sign'. It creates the representation and closes the "
                    + "pursuit in one transaction — the agency taking somebody on as a "
                    + "client — and the prospect stops being a prospect."),
        };

    /// <summary>An ordinary dialog commits on Enter.</summary>
    [Fact]
    public void AnOrdinaryDialogCommitsOnEnter()
    {
        List<string> wrong = [];

        foreach (string markup in Dialogs())
        {
            string name = Path.GetFileNameWithoutExtension(markup);

            if (Exceptions.ContainsKey(name))
            {
                continue;
            }

            string? actual = XElement.Load(markup).Attribute("DefaultButton")?.Value;

            if (actual != "Primary")
            {
                wrong.Add($"{name} defaults to {actual ?? "nothing"}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "Enter does not commit, and no exception says why (AOS-R002-016): "
                + string.Join("; ", wrong));
    }

    /// <summary>Every dialog is classified, including ones added later.</summary>
    /// <remarks>
    /// The completeness mechanism. Without it a 66th dialog could arrive with any
    /// default at all and this suite would pass, which is how the split being
    /// repaired arose in the first place.
    /// </remarks>
    [Fact]
    public void EveryDialogIsClassified()
    {
        List<string> unclassified = [];

        foreach (string markup in Dialogs())
        {
            string name = Path.GetFileNameWithoutExtension(markup);
            string? actual = XElement.Load(markup).Attribute("DefaultButton")?.Value;

            bool classified = actual == "Primary" || Exceptions.ContainsKey(name);

            if (!classified)
            {
                unclassified.Add(name);
            }
        }

        Assert.Empty(unclassified);
    }

    /// <summary>An exception is a decision, not an observation.</summary>
    /// <remarks>
    /// Guards the reason itself. A reason that merely restates the markup would let
    /// the exception list grow back into the thing it replaced.
    /// </remarks>
    [Fact]
    public void EveryExceptionGivesARealReason()
    {
        foreach ((string name, (Kind kind, string expected, string reason)) in Exceptions)
        {
            Assert.NotEqual(Kind.NormalAffirmative, kind);
            Assert.NotEqual("Primary", expected);
            Assert.True(reason.Length > 60, $"{name}'s reason is too thin to be a decision.");

            foreach (string empty in new[] { "already", "existing markup", "as before", "unchanged" })
            {
                Assert.DoesNotContain(
                    $"{empty} said", reason, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Each exception has the default its reason calls for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, not assumed. The first attempt left these three on <c>Close</c>,
    /// and a live run showed what that meant: <c>Enter</c> in a half-filled
    /// journal entry dismissed the dialog and threw the entry away. Not committing
    /// is the requirement; discarding is not the way to meet it.
    /// </para>
    /// <para>
    /// <c>None</c> makes <c>Enter</c> do nothing, so the operator reaches the
    /// primary action deliberately. <c>ApproveAiActionDialog</c> keeps
    /// <c>Secondary</c>, because rejecting is the safe answer to a proposal and
    /// that was the product's own considered choice.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryExceptionHasTheDefaultItsReasonCallsFor()
    {
        foreach ((string name, (Kind _, string expected, string _) ) in Exceptions)
        {
            XElement dialog = XElement.Load(Path.Combine(
                RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", name + ".xaml"));

            Assert.Equal(expected, dialog.Attribute("DefaultButton")?.Value);
        }
    }

    /// <summary>The exception list names dialogs that exist.</summary>
    [Fact]
    public void EveryExceptionNamesARealDialog()
    {
        HashSet<string> present = Dialogs()
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(Exceptions.Keys, x => Assert.Contains(x, present));
    }

    /// <summary>The one case the finding named is still safe.</summary>
    /// <remarks>
    /// Named on its own because it is the product's own precedent: the primary is
    /// "Approve and run" and the default deliberately is not it.
    /// </remarks>
    [Fact]
    public void ApprovingAnAiActionIsNotTheEnterDefault()
    {
        XElement dialog = XElement.Load(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", "ApproveAiActionDialog.xaml"));

        Assert.Equal("Approve and run", dialog.Attribute("PrimaryButtonText")?.Value);
        Assert.NotEqual("Primary", dialog.Attribute("DefaultButton")?.Value);
    }

    /// <summary>Cancelling is unchanged.</summary>
    /// <remarks>
    /// The convention moves what Enter does. Escape and the close button are how an
    /// operator abandons a dialog, and neither was touched.
    /// </remarks>
    [Fact]
    public void EveryDialogCanStillBeAbandoned()
    {
        List<string> stuck = [];

        foreach (string markup in Dialogs())
        {
            XElement dialog = XElement.Load(markup);

            if (string.IsNullOrWhiteSpace(dialog.Attribute("CloseButtonText")?.Value))
            {
                stuck.Add(Path.GetFileNameWithoutExtension(markup) ?? markup);
            }
        }

        Assert.Empty(stuck);
    }

    private static IEnumerable<string> Dialogs() =>
        Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs"), "*.xaml");

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
