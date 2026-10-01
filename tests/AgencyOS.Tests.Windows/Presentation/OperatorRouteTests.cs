using System.Xml.Linq;
using AgencyOS.Client.Commands;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Asserts that each restored act is a declared, gated, named control
// running the shipped dialog and method. The acts themselves were proved live when
// they were restored.

/// <summary>
/// That the eight capabilities left API-only after wave 3 can now be reached.
/// </summary>
/// <remarks>
/// <para>
/// Reality Closure classified twelve capabilities as <em>shipped but not
/// delivered</em>: present in the domain, present in the API, present in the client
/// and reachable by no route a normal Windows operator could take. Wave 3 closed
/// the four that were chain heads. These are the remaining eight, and they are leaf
/// acts - the corrections, the withdrawals and the one date nobody can infer.
/// </para>
/// <para>
/// <strong>What these prove, and what they do not.</strong> That each act exists as
/// a named control in the workspace where the work happens, carries an accessible
/// name, is gated rather than absent, takes its identifiers from what is on screen,
/// runs through a shipped dialog and the shipped client method, and reveals what it
/// changed. They do not prove the act works: that is the live evidence in
/// <c>WAVE-04-REMAINING-OPERATOR-ROUTES.md</c>, because no source string can show a
/// receivable moving to cancelled.
/// </para>
/// <para>
/// They also hold two distinctions the wording could quietly lose. Cancelling a
/// receivable is not writing one off, and reversing an allocation is not reversing
/// the payment: in both pairs the first says the record was wrong and the second
/// says the money was.
/// </para>
/// </remarks>
public sealed class OperatorRouteTests
{
    /// <summary>One restored act: where it lives and what it runs through.</summary>
    /// <param name="Control">The name an operator reaches it by.</param>
    /// <param name="Label">What the control says, which is the domain's own verb.</param>
    /// <param name="Page">The workspace the act belongs to.</param>
    /// <param name="Flow">The method that performs it.</param>
    /// <param name="Dialog">The dialog it collects through, shipped or new.</param>
    /// <param name="Method">The client method that was orphaned.</param>
    /// <param name="Command">The palette identifier that supplements the control.</param>
    public sealed record Route(
        string Control,
        string Label,
        string Page,
        string Flow,
        string Dialog,
        string Method,
        string Command);

    /// <summary>Every act this wave restored, in one list both views read.</summary>
    private static Route[] All =>
    [
        new("EffectiveDateButton", "Record effective date", "ContractsPage",
            "RecordEffectiveDateAsync", "RecordEffectiveDateDialog",
            "RecordContractEffectiveDateAsync", "contract.effective-date.record"),
        new("QuantifyButton", "Quantify", "ContractsPage",
            "QuantifyObligationAsync", "QuantifyObligationDialog",
            "QuantifyObligationAsync", "obligation.quantify"),
        new("ReleaseButton", "Release", "ContractsPage",
            "ReleaseObligationAsync", "FinanceReasonDialog",
            "ReleaseObligationAsync", "obligation.release"),
        new("CommissionButton", "Calculate commission", "ContractsPage",
            "CalculateCommissionAsync", "CalculateCommissionDialog",
            "CalculateCommissionAsync", "commission.calculate"),
        new("CancelReceivableButton", "Cancel receivable", "FinancePage",
            "CancelReceivableAsync", "FinanceReasonDialog",
            "CancelReceivableAsync", "receivable.cancel"),
        new("IssueInvoiceButton", "Issue", "FinancePage",
            "IssueInvoiceAsync", "IssueInvoiceDialog",
            "IssueInvoiceAsync", "invoice.issue"),
        new("VoidInvoiceButton", "Void", "FinancePage",
            "VoidInvoiceAsync", "FinanceReasonDialog",
            "VoidInvoiceAsync", "invoice.void"),
        new("ReverseAllocationButton", "Reverse allocation", "FinancePage",
            "ReverseAllocationAsync", "ReverseAllocationDialog",
            "ReverseAllocationAsync", "allocation.reverse"),
    ];

    public static TheoryData<Route> Restored => [.. All];

    /// <summary>
    /// Every restored act is a control in the workspace the work happens in.
    /// </summary>
    /// <remarks>
    /// The distinction between theoretical and operator capability. A capability
    /// nobody can find is not delivered, however complete the layer beneath it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EveryRestoredActIsAControlInItsOwnWorkspace(Route route)
    {
        XElement control = Control(route);

        Assert.Equal("Button", control.Name.LocalName);
        Assert.Equal(route.Label, (string?)control.Attribute("Content"));
    }

    /// <summary>Every restored act has a handler on the page that owns it.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EveryRestoredActHasAHandlerOnItsOwnPage(Route route)
    {
        string click = (string?)Control(route).Attribute("Click")
            ?? throw new Xunit.Sdk.XunitException($"{route.Control} does nothing when pressed");

        string page = Code(route.Page);

        Assert.Contains($"private void {click}(", page, StringComparison.Ordinal);
        Assert.Contains($"_ = {route.Flow}();", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each act runs through a shipped dialog and the client method that was
    /// orphaned, rather than a second path beside them.
    /// </summary>
    /// <remarks>
    /// Four of the eight reuse dialogs that already existed, and one of those -
    /// <c>CalculateCommissionDialog</c> - was written, finished and opened by
    /// nothing at all. Building a parallel authoring path would have left the
    /// original orphaned and the rules in two places.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EachActRunsThroughTheShippedDialogAndMethod(Route route)
    {
        string flow = Flow(route);

        Assert.Contains(route.Dialog, flow, StringComparison.Ordinal);
        Assert.Contains($"api.{route.Method}(", flow, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal arrives from the server and is shown, not pre-empted.
    /// </summary>
    /// <remarks>
    /// Every one of these acts has a business guard behind it - an invoice already
    /// issued, an obligation that already carries a figure, a receivable money has
    /// been applied to. The page holds no second edition of any of them: the call
    /// goes through <c>Guarded</c>, which puts the server's own sentence on screen.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ARefusalIsTheServersAndIsShown(Route route)
    {
        Assert.Contains("Guarded(", Flow(route), StringComparison.Ordinal);
    }

    /// <summary>
    /// The product supplies every identifier, and the operator supplies none.
    /// </summary>
    /// <remarks>
    /// An operator copying a GUID between screens is the failure the whole finding
    /// is about. Each flow reads the record it acts on out of the selection, takes
    /// the concurrency version from that same record, and parses no identifier from
    /// anything typed.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void TheProductSuppliesTheIdentifiersNotTheOperator(Route route)
    {
        string flow = Flow(route);

        Assert.True(
            flow.Contains("SelectedItem is", StringComparison.Ordinal)
                || flow.Contains("_detail?.Contract is", StringComparison.Ordinal),
            $"{route.Flow} does not take its subject from what is on screen");

        Assert.DoesNotContain("Guid.Parse", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.TryParse", flow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every act reveals what it changed, where it happened.
    /// </summary>
    /// <remarks>
    /// An act whose only evidence is that no error appeared is not recoverable by
    /// an operator. Commission is the exception that proves it: its entitlement
    /// lives on another workspace, so that flow reads the figure back and says both
    /// the number and where it went.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EveryActRevealsWhatItChanged(Route route)
    {
        string flow = Flow(route);

        Assert.True(
            flow.Contains("await LoadAsync()", StringComparison.Ordinal)
                || flow.Contains("await LoadMoneyAsync(", StringComparison.Ordinal)
                || flow.Contains("ReportCommissionAsync", StringComparison.Ordinal),
            $"{route.Flow} changes something and shows nothing afterwards");
    }

    /// <summary>
    /// A refused act does not refresh, so its explanation survives to be read.
    /// </summary>
    /// <remarks>
    /// Found live. Every act refreshed unconditionally, and the refresh cleared the
    /// message area on its way in — so a refusal appeared and was erased before
    /// anybody could read it, leaving a screen that said nothing had gone wrong
    /// and a record that had not changed. An act that did not happen has nothing
    /// new to show, so the refresh is now gated on the act going through.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ARefusedActDoesNotRefreshOverItsOwnExplanation(Route route)
    {
        string flow = Flow(route);

        if (route.Control == "CommissionButton")
        {
            // Commission reads its result back rather than reloading a list, and
            // returns early when nothing was calculated.
            Assert.Contains("if (entitlement == Guid.Empty)", flow, StringComparison.Ordinal);
            return;
        }

        Assert.Contains("if (await Guarded(", flow, StringComparison.Ordinal);
    }

    /// <summary>
    /// A read does not clear what the last act said.
    /// </summary>
    /// <remarks>
    /// The other half of the same defect: selecting a contract reloads the money it
    /// owes, and that reload must not wipe a refusal the operator has not read yet.
    /// Acts clear the message area; reads leave it alone.
    /// </remarks>
    [Fact]
    public void ARefreshDoesNotClearWhatTheLastActSaid()
    {
        string page = Code("ContractsPage");

        Assert.Contains("await Reading(", Body(page, "LoadMoneyAsync"), StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DetailBar.IsOpen = false", Helper(page, "Reading"), StringComparison.Ordinal);
        Assert.Contains(
            "DetailBar.IsOpen = false", Helper(page, "Guarded"), StringComparison.Ordinal);
    }

    /// <summary>
    /// An act that cannot be taken yet is disabled, not missing.
    /// </summary>
    /// <remarks>
    /// A control that vanishes teaches an operator the capability does not exist. A
    /// disabled one teaches them it is not available on this record, which is the
    /// truth, and the gate is reassigned as the selection changes rather than set
    /// once.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void AnActThatCannotBeTakenIsGatedRatherThanAbsent(Route route)
    {
        Assert.Equal("False", (string?)Control(route).Attribute("IsEnabled"));
        Assert.Contains($"{route.Control}.IsEnabled =", Code(route.Page), StringComparison.Ordinal);
    }

    /// <summary>Every restored control can be announced and reached by keyboard.</summary>
    /// <remarks>
    /// A button takes its accessible name from its content and sits in the tab
    /// order by construction, so the name being present and non-empty is the whole
    /// of it. The dialogs name their own fields.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EveryRestoredControlCanBeAnnounced(Route route)
    {
        XElement control = Control(route);

        string? announced = (string?)control.Attribute("AutomationProperties.Name")
            ?? (string?)control.Attribute("Content");

        Assert.False(string.IsNullOrWhiteSpace(announced));
    }

    /// <summary>
    /// The palette offers every restored act, and is never the only way to it.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R002-003</c>: the palette is a valid accelerator and an invalid
    /// discovery mechanism. Both halves are asserted here, because a command that
    /// exists only in the registry is exactly the shape this wave was called to
    /// remove.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ThePaletteSupplementsTheControlAndDoesNotReplaceIt(Route route)
    {
        Assert.Contains(AgencyOsCommands.All, x => x.Id == route.Command);
        Assert.Contains($"case \"{route.Command}\":", Code(route.Page), StringComparison.Ordinal);

        // And the control exists, which is what makes the palette a supplement.
        _ = Control(route);
    }

    /// <summary>
    /// Nothing restored here is named for deletion, because nothing deletes.
    /// </summary>
    /// <remarks>
    /// Six of the eight are corrective acts on financial records, and every one of
    /// them keeps the row. A control labelled Delete or Remove over an operation
    /// that cancels, voids, reverses or releases would tell the operator the
    /// opposite of what the books will say afterwards.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void NoCorrectiveActIsNamedForDeletion(Route route)
    {
        foreach (string forbidden in (string[])["Delete", "Remove", "Erase", "Discard"])
        {
            Assert.DoesNotContain(forbidden, route.Label, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Cancelling a receivable and writing one off stay different acts.
    /// </summary>
    /// <remarks>
    /// Written off is a debt the agency expected and gave up collecting, and it
    /// posts a loss. Cancelled is a claim that should never have been raised, and
    /// it posts nothing. One control for both would put a bad debt in the accounts
    /// for a clerical error.
    /// </remarks>
    [Fact]
    public void CancellingAReceivableIsNotWritingItOff()
    {
        string cancel = Flow(Named("CancelReceivableButton"));
        string writeOff = Body(Code("FinancePage"), "WriteOffAsync");

        Assert.Contains("api.CancelReceivableAsync(", cancel, StringComparison.Ordinal);
        Assert.Contains("api.WriteOffReceivableAsync(", writeOff, StringComparison.Ordinal);

        Assert.DoesNotContain("WriteOff", cancel, StringComparison.Ordinal);

        // And they say different things about the books, not one sentence twice.
        Assert.Contains("no loss is posted", cancel, StringComparison.Ordinal);
        Assert.Contains("recording the loss", writeOff, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reversing an allocation and reversing a payment stay different acts.
    /// </summary>
    /// <remarks>
    /// Reversing a payment says the money never arrived. Reversing an allocation
    /// says it arrived and answered the wrong receivable. Recording the second as
    /// the first would take a real receipt off the books.
    /// </remarks>
    [Fact]
    public void ReversingAnAllocationIsNotReversingThePayment()
    {
        string allocation = Flow(Named("ReverseAllocationButton"));
        string payment = Body(Code("FinancePage"), "ReversePaymentAsync");

        Assert.Contains("api.ReverseAllocationAsync(", allocation, StringComparison.Ordinal);
        Assert.Contains("api.ReversePaymentAsync(", payment, StringComparison.Ordinal);

        // Two controls, two labels. "Reverse" alone belongs to the payment.
        Assert.Equal("Reverse", (string?)Control(Named("ReverseAllocationButton"))
            .Parent!
            .Elements()
            .Single(x => (string?)x.Attribute(Name) == "ReversePaymentButton")
            .Attribute("Content"));
    }

    /// <summary>
    /// Recording an effective date does not make a contract look executed.
    /// </summary>
    /// <remarks>
    /// The whole reason this date has never been inferred. A contract signed in
    /// March and in force from January is ordinary, and the page has always shown
    /// the two facts separately; the new route had to carry that distinction rather
    /// than assume the operator holds it. It is also why the control is not gated on
    /// execution - requiring it would invent a rule the domain does not hold.
    /// </remarks>
    [Fact]
    public void RecordingAnEffectiveDateDoesNotAssertExecution()
    {
        string dialog = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs",
            "RecordEffectiveDateDialog.xaml"));

        Assert.Contains("Effective is not executed", dialog, StringComparison.Ordinal);

        string page = Code("ContractsPage");

        // The two sentences are still built separately, from two fields.
        Assert.Contains("Not fully executed", page, StringComparison.Ordinal);
        Assert.Contains("no effective date recorded", page, StringComparison.Ordinal);

        // And the gate names the one state the server refuses, not execution.
        Assert.Contains("Status != \"Abandoned\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EffectiveDateButton.IsEnabled = loaded && contract.ExecutedOn",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The obligation acts and the receivable raised from one stay separate.
    /// </summary>
    /// <remarks>
    /// Quantifying an obligation and releasing one act on the contract's own
    /// record; cancelling a receivable acts on the money claim raised from it. They
    /// are different aggregates with different lifecycles, and this fails if one
    /// flow reaches into the other.
    /// </remarks>
    [Fact]
    public void AnObligationIsNotAReceivable()
    {
        foreach (string flow in (string[])["QuantifyObligationAsync", "ReleaseObligationAsync"])
        {
            string body = Body(Code("ContractsPage"), flow);

            Assert.Contains("obligation.Id", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Receivable", body, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ reading

    private static Route Named(string control) =>
        All.Single(x => x.Control == control);

    private static XElement Control(Route route) =>
        Assert.Single(
            XDocument.Parse(Markup(route.Page)).Descendants(),
            x => (string?)x.Attribute(Name) == route.Control);

    private static string Flow(Route route) => Body(Code(route.Page), route.Flow);

    /// <summary>
    /// One method's text, so an assertion is about that act and not the file.
    /// </summary>
    /// <remarks>
    /// The declaration rather than the first mention, because every one of these is
    /// dispatched from a handler and from the palette before it is declared. Brace
    /// counting rather than a regular expression: the bodies hold string literals
    /// with braces in them, and a lazy match would stop at the first one.
    /// </remarks>
    private static string Body(string source, string method) => From(source, $"Task {method}(");

    /// <summary>The body of a helper, told apart by the delegate it takes.</summary>
    private static string Helper(string source, string method) =>
        From(source, $"{method}(Func<Task> action)");

    private static string From(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start > 0, $"'{signature}' was not found");

        int open = source.IndexOf('{', start);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new Xunit.Sdk.XunitException($"'{signature}' has no closing brace");
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
