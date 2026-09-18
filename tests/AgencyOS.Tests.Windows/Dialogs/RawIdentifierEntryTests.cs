using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That an ordinary dialog never asks a person to type a canonical identifier.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R001-006</c>: twenty fields across thirteen dialogs asked for a value the
/// product displays nowhere. Repair Wave 003B replaced fourteen of them with
/// pickers or derived context. This is what stops the fifteenth appearing.
/// </para>
/// <para>
/// The rule is deliberately narrow, because §23 asks for one that can be trusted
/// rather than one that is merely present. It fires on a <strong>text input</strong>
/// whose own label says it wants an identifier for a record AgencyOS owns. It does
/// not fire on a picker, on an external reference, on a free-form business
/// reference, or on anything outside the dialogs an ordinary operator opens.
/// </para>
/// <para>
/// The six fields Audit 002 left open are listed by name below, with their reason.
/// A list rather than a pattern, so that removing one is a deliberate edit to this
/// file and adding a new one is impossible without arguing for it here.
/// </para>
/// </remarks>
public sealed class RawIdentifierEntryTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The identifier fields Audit 002 deferred, and why each one is still here.
    /// </summary>
    /// <remarks>
    /// Every entry is a decision somebody has to make, not a defect somebody
    /// forgot. When one is decided, its line comes out and the rule starts
    /// enforcing it.
    /// </remarks>
    private static readonly Dictionary<string, string> Deferred = new(StringComparer.Ordinal)
    {
        // The four owner/lead fields were decided and are pickers now
        // (AOS-R001-006). Their exemptions came out, and the rule below enforces
        // them like every other field.

        // Twelve of the fourteen kinds are pickers now (AOS-R001-006). The box
        // survives for the two that belong to a parent record and have no list of
        // their own — a material belongs to a person, a version to a contract — and
        // the dialog says so rather than hiding the kinds.
        ["LinkRecordDialog.xaml:TargetIdBox"] = "PARENT_SCOPED_KINDS_ONLY",

        // Nothing in the client constructs this dialog. An operator cannot reach it.
        ["AddIntelligenceSubjectDialog.xaml:IdBox"] = "DEBUG_ONLY / unreachable",
    };

    /// <summary>
    /// Words that mean "a record this product owns", in a field's own label.
    /// </summary>
    /// <remarks>
    /// Matched against the label a person reads, not against the control's name. A
    /// field called <c>ReferenceBox</c> asking for "the agency's internal handle"
    /// is not an identifier field; a field asking for a "deal identifier" is,
    /// whatever it is called.
    ///
    /// Whole words only. The first version of this rule matched "id" anywhere and
    /// flagged two dialogs for the word <em>said</em> — the noisy heuristic §23
    /// warns against, caught by running it.
    /// </remarks>
    private static readonly string[] IdentifierWords =
        ["identifier", "identifiers", "id", "ids", "guid", "uuid"];

    /// <summary>
    /// Labels that legitimately ask for an identifier somebody else issued.
    /// </summary>
    /// <remarks>
    /// §23 names these explicitly. An external reference is a fact about the world
    /// — a studio's own number for a contract — and typing one is the only way it
    /// can ever get in.
    /// </remarks>
    private static readonly string[] External =
        ["external", "reference", "provider", "authorization code", "message id", "internet"];

    public static TheoryData<string> DialogMarkup
    {
        get
        {
            TheoryData<string> data = [];

            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs"), "*.xaml"))
            {
                data.Add(Path.GetFileName(file));
            }

            return data;
        }
    }

    /// <summary>
    /// No dialog asks an operator to type an identifier for a record AgencyOS owns.
    /// </summary>
    /// <remarks>
    /// Fails against the pre-repair baseline on every one of the fourteen fields
    /// this wave repaired.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DialogMarkup))]
    public void NoDialogAsksForACanonicalIdentifier(string markup)
    {
        List<string> asking = [];

        foreach (XElement element in Markup(markup).DescendantsAndSelf())
        {
            if (element.Name.LocalName is not ("TextBox" or "AutoSuggestBox"))
            {
                continue;
            }

            string name = element.Attribute(Xaml + "Name")?.Value ?? "(anonymous)";

            if (Deferred.ContainsKey($"{markup}:{name}"))
            {
                continue;
            }

            string label = (element.Attribute("Header")?.Value ?? string.Empty)
                + " "
                + (element.Attribute("PlaceholderText")?.Value ?? string.Empty);

            string lowered = label.ToLowerInvariant();

            if (External.Any(x => lowered.Contains(x, StringComparison.Ordinal)))
            {
                continue;
            }

            if (Words(label).Overlaps(IdentifierWords))
            {
                asking.Add($"{name} — \"{label.Trim()}\"");
            }
        }

        Assert.True(
            asking.Count == 0,
            $"{markup} asks an operator to type an identifier the product shows nowhere "
                + $"(AOS-R001-006): {string.Join("; ", asking)}");
    }

    /// <summary>
    /// Every deferred field still exists, and is still a text input.
    /// </summary>
    /// <remarks>
    /// The other half of the rule. A list of exemptions that quietly stops matching
    /// anything is a list that has stopped protecting anything, and would let a
    /// future edit reintroduce a raw identifier under one of these names without
    /// the test noticing.
    /// </remarks>
    [Fact]
    public void EveryDeferredFieldIsStillThere()
    {
        List<string> missing = [];

        foreach ((string key, string _) in Deferred)
        {
            string[] parts = key.Split(':');

            bool present = Markup(parts[0])
                .DescendantsAndSelf()
                .Where(x => x.Name.LocalName == "TextBox")
                .Any(x => x.Attribute(Xaml + "Name")?.Value == parts[1]);

            if (!present)
            {
                missing.Add(key);
            }
        }

        Assert.True(
            missing.Count == 0,
            "These fields are exempted and no longer exist. Remove them from the list "
                + $"rather than leaving a dead exemption: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The fields this wave repaired are pickers, and carry an accessible name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named one by one rather than inferred, because "there is no TextBox" is
    /// satisfied by deleting the field altogether. What is asserted is that the
    /// operator can still choose the thing — from a <c>ComboBox</c> with a
    /// <c>Header</c>, which is where WinUI takes a combo box's accessible name from
    /// (`AOS-R001-004`, Repair Wave 002).
    /// </para>
    /// <para>
    /// `CreateContractDialog`'s accepted offer is deliberately absent: a deal
    /// carries exactly one, so it is shown rather than chosen.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("CreateDealDialog.xaml", "OpportunityBox")]
    [InlineData("CreateDealDialog.xaml", "TargetBox")]
    [InlineData("CreateContractDialog.xaml", "DealBox")]
    [InlineData("CreateOpportunityDialog.xaml", "SubjectBox")]
    [InlineData("CreateOpportunityDialog.xaml", "SubjectProjectBox")]
    [InlineData("CreatePackageDialog.xaml", "ProjectBox")]
    [InlineData("AddOpportunityTargetDialog.xaml", "TargetBox")]
    [InlineData("AddOpportunityTargetDialog.xaml", "ContactBox")]
    [InlineData("AddPackageElementDialog.xaml", "TargetBox")]
    [InlineData("AddProjectCompanyDialog.xaml", "CompanyBox")]
    [InlineData("AttachToRoleDialog.xaml", "PartyBox")]
    [InlineData("RecordPitchDialog.xaml", "MaterialBox")]
    [InlineData("RecordSubmissionDialog.xaml", "MaterialBox")]
    [InlineData("LinkResearchItemDialog.xaml", "ItemBox")]
    public void TheRepairedFieldIsANamedPicker(string markup, string name)
    {
        XElement? picker = Markup(markup)
            .DescendantsAndSelf()
            .Where(x => x.Name.LocalName == "ComboBox")
            .FirstOrDefault(x => x.Attribute(Xaml + "Name")?.Value == name);

        Assert.True(picker is not null, $"{markup} no longer offers {name} as a picker.");

        Assert.False(
            string.IsNullOrWhiteSpace(picker!.Attribute("Header")?.Value),
            $"{markup}:{name} has no Header, so a screen reader announces it as \"combo box\".");
    }

    /// <summary>
    /// The contract dialog shows the accepted offer rather than asking for it.
    /// </summary>
    [Fact]
    public void TheAcceptedOfferIsShownNotAsked()
    {
        XElement markup = Markup("CreateContractDialog.xaml");

        Assert.DoesNotContain(
            markup.DescendantsAndSelf(),
            x => x.Attribute(Xaml + "Name")?.Value == "OfferIdBox");

        Assert.Contains(
            markup.DescendantsAndSelf(),
            x => x.Name.LocalName == "InfoBar"
                && x.Attribute(Xaml + "Name")?.Value == "OfferBar");
    }

    /// <summary>The distinct words of a label, lowercased.</summary>
    /// <remarks>
    /// Whole words, compared as words. An earlier version of this rule searched for
    /// "id" as a substring and flagged two dialogs for the word <em>said</em> —
    /// which is the noisy heuristic §23 warns against. Splitting on non-letters is
    /// both simpler to read and impossible to get wrong that way.
    /// </remarks>
    private static HashSet<string> Words(string label) =>
    [
        .. new string(label.Select(x => char.IsLetter(x) ? char.ToLowerInvariant(x) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries),
    ];

    private static XElement Markup(string name) =>
        XElement.Load(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", name));

    /// <summary>Walks up from the test binary to the repository root.</summary>
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
