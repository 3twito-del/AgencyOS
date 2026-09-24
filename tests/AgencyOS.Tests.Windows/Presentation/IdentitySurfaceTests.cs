using System.Text.RegularExpressions;
using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Pins that captions ask PartyLine, through the registered converter,
// only for roles its vocabulary knows. The words are executed in IdentityRoleTests.

/// <summary>
/// That the seen channel asks the same vocabulary the spoken channel answers from.
/// </summary>
/// <remarks>
/// <para>
/// F-04, F-09 and F-10. What a row means by a name is decided in
/// <c>PartyLine</c>, and <c>IdentityRoleTests</c> decides whether that answer is
/// right. These prove the markup still routes its identities through it - the
/// F-07 failure applied to a binding, where a correct formatter is reached by
/// nothing.
/// </para>
/// <para>
/// A caption bound straight to the field renders a bare name, and nothing in the
/// build would say so: the page compiles, the row looks populated, and only the
/// announcement and the screen disagree about what the name is.
/// </para>
/// </remarks>
public sealed class IdentitySurfaceTests
{
    /// <summary>
    /// Every identity whose role could be mistaken is attributed on screen.
    /// </summary>
    /// <remarks>
    /// The census population, by page and field. Each of these sat beneath a
    /// headline as a bare name.
    /// </remarks>
    [Theory]
    [InlineData("ProspectsPage", "OwnerDisplayName")]
    [InlineData("CommunicationsPage", "OwnerDisplayName")]
    [InlineData("CommunicationsPage", "PersonDisplayName")]
    [InlineData("FinancePage", "PayerDisplayName")]
    [InlineData("FinancePage", "DebtorDisplayName")]
    [InlineData("FinancePage", "ActorDisplayName")]
    [InlineData("ContractsPage", "CounterpartyDisplayName")]
    [InlineData("ContractsPage", "ObligorDisplayName")]
    [InlineData("DealsPage", "CounterpartyDisplayName")]
    [InlineData("DocumentsPage", "ActorDisplayName")]
    public void AnAmbiguousIdentityIsAttributedOnScreen(string page, string field)
    {
        string markup = Page(page);

        Assert.DoesNotContain("{Binding " + field + "}", markup, StringComparison.Ordinal);

        Assert.Contains(
            "{Binding Converter={StaticResource Party}, ConverterParameter=" + field + "}",
            markup,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every role the markup asks for is one the vocabulary can state.
    /// </summary>
    /// <remarks>
    /// <c>PartyLine</c> answers with silence where it does not know a field, so a
    /// caption asking for a role it has no word for renders nothing at all. That
    /// is the right behaviour and an invisible way to lose a caption, so the two
    /// are pinned to each other here.
    /// </remarks>
    [Fact]
    public void EveryRoleTheMarkupAsksForIsOneTheVocabularyKnows()
    {
        List<string> unknown = [];

        foreach (string page in Directory.GetFiles(Pages, "*.xaml"))
        {
            foreach (Match asked in Regex.Matches(
                File.ReadAllText(page),
                @"StaticResource Party\}, ConverterParameter=(\w+)\}"))
            {
                string field = asked.Groups[1].Value;

                if (PartyLine.Role(field) is null)
                {
                    unknown.Add(Path.GetFileNameWithoutExtension(page) + "." + field);
                }
            }
        }

        Assert.True(
            unknown.Count == 0,
            "These captions ask for a role PartyLine has no word for, and render "
                + "empty: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// The radar rows are deliberately left bare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The person a radar review is about is that row's headline and its subject,
    /// and a list of radar reviews affords no other reading of the name at the top
    /// of the row. Labelling it would be the label noise the rule warns against -
    /// the same reason a prospect keeps its own name unlabelled.
    /// </para>
    /// <para>
    /// Recorded so a later sweep does not "finish the job" by prefixing every
    /// identity in the product.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRadarRowsKeepTheirSubjectUnlabelled()
    {
        string markup = Page("IntelligencePage");

        Assert.Equal(2, Regex.Matches(markup, @"\{Binding PersonDisplayName\}").Count);
        Assert.DoesNotContain("ConverterParameter=PersonDisplayName", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The converter is registered, or every attributed caption renders empty.
    /// </summary>
    [Fact]
    public void TheConverterTheCaptionsAskForIsRegistered()
    {
        string application = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "App.xaml"));

        Assert.Contains(
            "<presentation:PartyConverter x:Key=\"Party\" />",
            application,
            StringComparison.Ordinal);
    }

    private static string Pages =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages");

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(Pages, $"{page}.xaml"));

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
