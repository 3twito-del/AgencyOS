using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the two core chains can be started from where an operator stands.
/// </summary>
/// <remarks>
/// <para>
/// Reality Closure found both chains broken at their heads: signatures are
/// recorded against parties and refused outside <c>ApprovedForExecution</c>, so a
/// contract could never be executed; receivables are raised from a monetary
/// obligation and payments are allocated against receivables, so the whole
/// delivered finance tail had nothing to attach to. The domain, the API and the
/// client all had the capability. Nothing in the product reached it.
/// </para>
/// <para>
/// <strong>What these prove.</strong> That the action exists in the operator's
/// natural context, carries an accessible name, is gated rather than absent, and
/// runs through the shipped dialog and client method rather than a parallel path.
/// A dialog that can be constructed in a test is not operator delivery; a command
/// on the surface where the work happens is the claim, and it is checked here
/// against the markup and the page that owns it.
/// </para>
/// <para>
/// What the chains then <em>do</em> is proven live, in the journeys recorded in
/// <c>WAVE-03-CORE-AUTHORING-HEADS.md</c>. No source string can show that.
/// </para>
/// </remarks>
public sealed class AuthoringRouteTests
{
    /// <summary>Each restored head, and the control an operator reaches it by.</summary>
    public static TheoryData<string, string> Heads => new()
    {
        { "PartyButton", "Add party" },
        { "ApproveButton", "Approve for signature" },
        { "MoneyObligationButton", "Record money owed" },
        { "RaiseReceivableButton", "Raise receivable" },
    };

    /// <summary>
    /// Every head is a control on the contract surface, not a palette secret.
    /// </summary>
    /// <remarks>
    /// A command reachable only by knowing it exists is not discoverable, which is
    /// the distinction between theoretical and operator capability this wave was
    /// called to close.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Heads))]
    public void EveryRestoredHeadIsAControlOnTheContractSurface(string control, string label)
    {
        string markup = Markup();

        Assert.Contains($"x:Name=\"{control}\"", markup, StringComparison.Ordinal);
        Assert.Contains(label, markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every head has a handler on the page that owns it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Heads))]
    public void EveryRestoredHeadHasAHandler(string control, string label)
    {
        _ = label;

        XElement element = Assert.Single(
            XDocument.Parse(Markup()).Descendants(),
            x => (string?)x.Attribute(Name) == control);

        string? click = (string?)element.Attribute("Click");

        Assert.NotNull(click);
        Assert.Contains($"private void {click}(", Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each head runs through the shipped dialog and the shipped client method.
    /// </summary>
    /// <remarks>
    /// Three of the four dialogs were already written and had no opener. Building a
    /// second authoring path beside them would have left the originals orphaned and
    /// the rules in two places.
    /// </remarks>
    [Theory]
    [InlineData("AddContractPartyDialog", "AddContractPartyAsync")]
    [InlineData("ChangeContractStatusRequest", "ChangeContractStatusAsync")]
    [InlineData("RecordMonetaryObligationDialog", "RecordMonetaryObligationAsync")]
    [InlineData("RaiseReceivableDialog", "RaiseReceivableAsync")]
    public void EachHeadUsesTheShippedDialogAndMethod(string dialog, string method)
    {
        string page = Page();

        Assert.Contains(dialog, page, StringComparison.Ordinal);
        Assert.Contains($"api.{method}(", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The operator never supplies an identifier the product already knows.
    /// </summary>
    /// <remarks>
    /// The contract, its version, its parties and the selected obligation all come
    /// from what is on screen. An operator copying a GUID between screens is the
    /// failure this wave exists to remove.
    /// </remarks>
    [Fact]
    public void TheProductSuppliesTheIdentifiersNotTheOperator()
    {
        string page = Page();

        Assert.Contains("contract.Contract.Id", page, StringComparison.Ordinal);
        Assert.Contains("version.Id", page, StringComparison.Ordinal);
        Assert.Contains("_detail.Parties", page, StringComparison.Ordinal);
        Assert.Contains(
            "MoneyObligationList.SelectedItem is not MonetaryObligationResponse",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A head that cannot be taken yet says so rather than disappearing.
    /// </summary>
    /// <remarks>
    /// Money owed is recorded against a drafting version and runs between two
    /// parties, so the caption explains which is missing. A disabled control with
    /// no reason is a dead end an operator cannot act on.
    /// </remarks>
    [Fact]
    public void ARefusedHeadExplainsWhatIsMissing()
    {
        string page = Page();

        Assert.Contains(
            "Record a version before recording what it obliges anybody to pay.",
            page,
            StringComparison.Ordinal);

        Assert.Contains(
            "Money owed is between two parties on this contract.",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The money a contract obliges has somewhere to appear.
    /// </summary>
    /// <remarks>
    /// Recording an obligation the operator cannot then see would restore half a
    /// chain. The list is what makes the next step selectable.
    /// </remarks>
    [Fact]
    public void WhatIsOwedAppearsOnTheContract()
    {
        Assert.Contains("x:Name=\"MoneyObligationList\"", Markup(), StringComparison.Ordinal);
        Assert.Contains("ListMonetaryObligationsAsync(contractId)", Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every restored control carries an accessible name.
    /// </summary>
    /// <remarks>
    /// <c>AppBarButton</c> and <c>Button</c> take their name from their label or
    /// content, and the list needs one of its own.
    /// </remarks>
    [Fact]
    public void EveryRestoredControlCanBeAnnounced()
    {
        XDocument markup = XDocument.Parse(Markup());

        foreach (string control in (string[])
                 ["PartyButton", "ApproveButton", "MoneyObligationButton", "RaiseReceivableButton"])
        {
            XElement element = Assert.Single(
                markup.Descendants(), x => (string?)x.Attribute(Name) == control);

            bool named = element.Attribute("Label") is not null
                || element.Attribute("Content") is not null
                || element.Attribute(Automation) is not null;

            Assert.True(named, $"{control} has nothing to announce");
        }

        XElement list = Assert.Single(
            markup.Descendants(), x => (string?)x.Attribute(Name) == "MoneyObligationList");

        Assert.Equal("Money owed", (string?)list.Attribute(Automation));
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static XName Automation =>
        XName.Get("AutomationProperties.Name");

    private static string Markup() => Read("ContractsPage.xaml");

    private static string Page() => Read("ContractsPage.xaml.cs");

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
