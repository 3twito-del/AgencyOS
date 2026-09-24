using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Pairs each outcome control with a declared way to originate what it
// acts on, in markup and code-behind. The pairing is a structural fact about what a
// surface offers.

/// <summary>
/// That nothing in the product resolves an object nothing in the product creates.
/// </summary>
/// <remarks>
/// <para>
/// The shape Reality Closure wave 5 found, and the one worth keeping. F-13 was
/// about capability with no route at all. This is narrower and easier to miss: a
/// surface that is <em>almost</em> complete, offering the second half of an
/// object's life while the first half was never wired. It passes every test, looks
/// finished in a screenshot, and is useless.
/// </para>
/// <para>
/// It had happened three times. M8 delivered "Record outcome" for contract options
/// and for obligations, over lists no supported workflow could fill. M4 delivered a
/// representation status lifecycle, published its transition table, showed the
/// status on the Talent page, and offered no way to change it — so the product
/// could acquire clients and could not lose them.
/// </para>
/// <para>
/// This pins the pairs. A later wave that adds an outcome control without a way to
/// originate what it acts on fails here.
/// </para>
/// </remarks>
public sealed class CreationPairingTests
{
    /// <summary>An object's two halves, as controls on the same surface.</summary>
    /// <param name="Object">What the pair is about, for the failure message.</param>
    /// <param name="Page">The workspace it lives in.</param>
    /// <param name="Creates">The control that originates one.</param>
    /// <param name="Resolves">The control that records what became of one.</param>
    public static TheoryData<string, string, string, string> Pairs => new()
    {
        { "a contract option", "ContractsPage", "RecordOptionButton", "ResolveOptionButton" },
        { "a contract obligation", "ContractsPage", "RecordObligationButton", "ResolveObligationButton" },
        { "money owed", "ContractsPage", "MoneyObligationButton", "RaiseReceivableButton" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void NothingResolvesWhatNothingCreates(
        string subject, string page, string creates, string resolves)
    {
        XDocument markup = XDocument.Parse(Markup(page));

        foreach (string control in (string[])[creates, resolves])
        {
            Assert.True(
                markup.Descendants().Any(x => (string?)x.Attribute(Name) == control),
                $"{subject} has no {control}, so one half of its life is unreachable");
        }
    }

    /// <summary>
    /// A talent profile is created by a workflow that says it created one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fourth residual row, and the one that needed no repair.
    /// <c>CreateTalentProfileAsync</c> has no production caller, which is what put
    /// it on the list — but a profile is not authored on its own. Handing a radar
    /// entry to representation creates one where none exists, the handler says so
    /// in its result, and the workspace repeats it to the operator.
    /// </para>
    /// <para>
    /// So the route is real and this pins the part that could quietly rot: if the
    /// conversion ever stopped reporting which of the two things happened, an
    /// operator would have no way to know whether the person now has a profile,
    /// and the Talent workspace is rooted in profiles.
    /// </para>
    /// </remarks>
    [Fact]
    public void HandingARadarEntryToRepresentationSaysWhetherItCreatedAProfile()
    {
        string page = Code("IntelligencePage");

        Assert.Contains("ConvertRadarEntryAsync", page, StringComparison.Ordinal);
        Assert.Contains("conversion.CreatedTalentProfile", page, StringComparison.Ordinal);

        // Both outcomes are named. One sentence for both would leave the operator
        // unable to tell a new profile from a reused one.
        Assert.Contains("A prospect and a talent profile were created.", page, StringComparison.Ordinal);
        Assert.Contains(
            "A prospect was created against the existing talent profile.",
            page,
            StringComparison.Ordinal);
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static string Markup(string page) => Read($"{page}.xaml");

    private static string Code(string page) => Read($"{page}.xaml.cs");

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", file));

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
