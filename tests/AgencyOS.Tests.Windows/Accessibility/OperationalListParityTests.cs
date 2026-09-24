using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgencyOS.Client.Commands;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

// SOURCE-PROOF: Reads the client's XAML to learn which fields each list row renders
// and through which converter. Which bindings a template declares is a markup fact;
// what those bindings then show and what the row announces are executed, through
// the shipped presentation functions, against real instances of the bound types.

/// <summary>
/// That every value a list row shows is also in what the row announces.
/// </summary>
/// <remarks>
/// <para>
/// The operational regression gate's central check (<c>18-…</c> §2 and §5). Every
/// list row in the client announces through <see cref="RowLabel"/>, and every list
/// row shows some of its record's fields. Where a row shows a value its
/// announcement leaves out, a sighted operator and a screen-reader operator are
/// told different things about the same record — which is how F-02 survived until
/// Reality Closure, and how the representation scope's start date still survives
/// (C3).
/// </para>
/// <para>
/// <strong>How it works.</strong> <see cref="Catalog"/> names every row template in
/// the client and the type it is bound to. For each, an instance is built in which
/// every field holds a distinct sentinel, each visible binding is evaluated the way
/// the row evaluates it — through the same presentation function its converter
/// calls — and the result must appear in <c>RowLabel.For</c> of that instance.
/// Distinct sentinels mean a test cannot pass by finding the right value under the
/// wrong field.
/// </para>
/// <para>
/// <strong>What keeps it honest.</strong> The catalog is checked against the markup
/// both ways, so a template added later cannot go unexamined and a catalog entry
/// cannot outlive its template. Every visible binding must resolve on the declared
/// type, so a wrong type in the catalog fails rather than passing vacuously. Every
/// converter the templates use must be one this file knows how to evaluate.
/// </para>
/// <para>
/// <strong>What it does not do.</strong> It is not a XAML compiler. It reads the
/// bindings a template declares on text — <c>Text</c> and <c>Content</c> — and
/// nothing else: no styles, no triggers, no code-behind that writes a row after
/// the fact. Text inside a child that carries its own accessible name is announced
/// by that child and is not the row's to repeat.
/// </para>
/// </remarks>
public sealed class OperationalListParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>A row template, the type it is bound to, or why none can be built.</summary>
    /// <param name="File">The markup file, relative to <c>src/AgencyOS.Windows</c>.</param>
    /// <param name="Template">The template's key, or the name of the list that holds it.</param>
    /// <param name="Row">The bound type, when a test can construct one.</param>
    /// <param name="Unreachable">Why no instance can be built here, when it cannot.</param>
    public sealed record Entry(string File, string Template, Type? Row, string? Unreachable = null);

    /// <summary>
    /// Types declared inside the WinUI assembly, which this suite does not reference.
    /// </summary>
    private const string DialogStaging =
        "An authoring dialog's staging row, declared in the WinUI assembly this suite cannot load. "
        + "It shows what the operator is typing, not a record.";

    /// <summary>Every row template in the client, and what it is bound to.</summary>
    public static IReadOnlyList<Entry> Catalog { get; } =
    [
        new("App.xaml", "TaskTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.TaskResponse)),
        new("App.xaml", "PersonRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.PersonSummaryResponse)),
        new("App.xaml", "CompanyRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.CompanySummaryResponse)),
        new("App.xaml", "InteractionTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.InteractionResponse)),
        new("App.xaml", "TimelineEntryTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.TimelineEntryResponse)),
        new("App.xaml", "RelationshipRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.RelationshipResponse)),

        new("Dialogs/AllocatePaymentDialog.xaml", "AllocationList", null, DialogStaging),
        new("Dialogs/AnswerOfferDialog.xaml", "TermList", typeof(global::AgencyOS.Contracts.Deals.OfferTermResponse)),
        new("Dialogs/ComposeMessageDialog.xaml", "RecipientList", typeof(global::AgencyOS.Contracts.Documents.RecipientRequest)),
        new("Dialogs/PostJournalEntryDialog.xaml", "LineList", null, DialogStaging),
        new("Dialogs/RecordInvoiceDialog.xaml", "LineList", null, DialogStaging),
        new("Dialogs/RecordOfferDialog.xaml", "TermList", null, DialogStaging),
        new("Dialogs/RecordPaymentDialog.xaml", "AllocationList", null, DialogStaging),
        new("Dialogs/ResolveParticipantDialog.xaml", "SuggestionList", typeof(global::AgencyOS.Contracts.Documents.ParticipantSuggestionResponse)),

        new("MainWindow.xaml", "SearchResults", typeof(global::AgencyOS.Contracts.Search.SearchHit)),
        new("MainWindow.xaml", "PaletteResults", typeof(global::AgencyOS.Client.ViewModels.PaletteCommand)),

        new("Pages/AiPage.xaml", "ApprovalList", typeof(global::AgencyOS.Contracts.Ai.AiApprovalResponse)),
        new("Pages/AiPage.xaml", "RunList", typeof(global::AgencyOS.Contracts.Ai.AgentRunResponse)),
        new("Pages/AiPage.xaml", "StepList", typeof(global::AgencyOS.Contracts.Ai.AgentStepResponse)),
        new("Pages/AiPage.xaml", "PolicyList", typeof(global::AgencyOS.Contracts.Ai.AiProviderPolicyResponse)),
        new("Pages/AiPage.xaml", "ToolList", typeof(global::AgencyOS.Contracts.Ai.AiToolDescriptorResponse)),
        new("Pages/AiPage.xaml", "ModelList", typeof(global::AgencyOS.Contracts.Ai.AiModelDescriptorResponse)),

        new("Pages/CommunicationsPage.xaml", "MessageList", typeof(global::AgencyOS.Contracts.Documents.MessageSummaryResponse)),
        new("Pages/CommunicationsPage.xaml", "ParticipantList", typeof(global::AgencyOS.Contracts.Documents.ParticipantResponse)),
        new("Pages/CommunicationsPage.xaml", "AttachmentList", typeof(global::AgencyOS.Contracts.Documents.MessageAttachmentResponse)),
        new("Pages/CommunicationsPage.xaml", "MessageLinkList", typeof(global::AgencyOS.Contracts.Documents.MessageLinkResponse)),
        new("Pages/CommunicationsPage.xaml", "OutboundList", typeof(global::AgencyOS.Contracts.Documents.OutboundDispatchResponse)),
        new("Pages/CommunicationsPage.xaml", "MailboxList", typeof(global::AgencyOS.Contracts.Documents.CommunicationAccountResponse)),
        new("Pages/CommunicationsPage.xaml", "DeskList", typeof(global::AgencyOS.Contracts.Documents.OutboundDispatchResponse)),

        new("Pages/CompaniesPage.xaml", "CompanyIntelligenceList", typeof(global::AgencyOS.Client.ViewModels.EntityIntelligenceRow)),

        new("Pages/ContractsPage.xaml", "ContractList", typeof(global::AgencyOS.Contracts.Legal.ContractSummaryResponse)),
        new("Pages/ContractsPage.xaml", "VersionList", typeof(global::AgencyOS.Contracts.Legal.ContractVersionResponse)),
        new("Pages/ContractsPage.xaml", "TermList", typeof(global::AgencyOS.Contracts.Legal.ContractTermResponse)),
        new("Pages/ContractsPage.xaml", "ReconcileList", typeof(global::AgencyOS.Contracts.Legal.ReconciliationLineResponse)),
        new("Pages/ContractsPage.xaml", "PartyList", typeof(global::AgencyOS.Contracts.Legal.ContractPartyResponse)),
        new("Pages/ContractsPage.xaml", "RightsList", typeof(global::AgencyOS.Contracts.Legal.RightsGrantResponse)),
        new("Pages/ContractsPage.xaml", "OptionList", typeof(global::AgencyOS.Contracts.Legal.ContractOptionResponse)),
        new("Pages/ContractsPage.xaml", "ObligationList", typeof(global::AgencyOS.Contracts.Legal.ObligationResponse)),
        new("Pages/ContractsPage.xaml", "MoneyObligationList", typeof(global::AgencyOS.Contracts.Finance.MonetaryObligationResponse)),
        new("Pages/ContractsPage.xaml", "NoticeRequirementList", typeof(global::AgencyOS.Contracts.Legal.NoticeRequirementResponse)),
        new("Pages/ContractsPage.xaml", "NoticeList", typeof(global::AgencyOS.Contracts.Legal.NoticeRecordResponse)),
        new("Pages/ContractsPage.xaml", "TaskList", typeof(global::AgencyOS.Contracts.Legal.ContractTaskResponse)),
        new("Pages/ContractsPage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Legal.ContractHistoryEntryResponse)),

        new("Pages/DealsPage.xaml", "DealList", typeof(global::AgencyOS.Contracts.Deals.DealSummaryResponse)),
        new("Pages/DealsPage.xaml", "OfferList", typeof(global::AgencyOS.Contracts.Deals.OfferResponse)),
        new("Pages/DealsPage.xaml", "TermList", typeof(global::AgencyOS.Contracts.Deals.OfferTermResponse)),
        new("Pages/DealsPage.xaml", "ComparisonList", typeof(global::AgencyOS.Contracts.Deals.TermDifferenceResponse)),
        new("Pages/DealsPage.xaml", "TaskList", typeof(global::AgencyOS.Contracts.Deals.DealTaskResponse)),
        new("Pages/DealsPage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Deals.DealHistoryEntryResponse)),

        new("Pages/DocumentsPage.xaml", "DocumentList", typeof(global::AgencyOS.Contracts.Documents.DocumentSummaryResponse)),
        new("Pages/DocumentsPage.xaml", "VersionList", typeof(global::AgencyOS.Contracts.Documents.DocumentVersionResponse)),
        new("Pages/DocumentsPage.xaml", "LinkList", typeof(global::AgencyOS.Contracts.Documents.DocumentLinkResponse)),
        new("Pages/DocumentsPage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Documents.DocumentEventResponse)),

        new("Pages/FinancePage.xaml", "ReceivableList", typeof(global::AgencyOS.Contracts.Finance.ReceivableResponse)),
        new("Pages/FinancePage.xaml", "InvoiceList", typeof(global::AgencyOS.Contracts.Finance.InvoiceResponse)),
        new("Pages/FinancePage.xaml", "PaymentList", typeof(global::AgencyOS.Contracts.Finance.PaymentResponse)),
        new("Pages/FinancePage.xaml", "CommissionList", typeof(global::AgencyOS.Contracts.Finance.CommissionEntitlementResponse)),
        new("Pages/FinancePage.xaml", "CommissionRuleList", typeof(global::AgencyOS.Contracts.Finance.CommissionRuleResponse)),
        new("Pages/FinancePage.xaml", "BalanceList", typeof(global::AgencyOS.Contracts.Finance.AccountBalanceResponse)),
        new("Pages/FinancePage.xaml", "JournalList", typeof(global::AgencyOS.Contracts.Finance.JournalEntryResponse)),
        new("Pages/FinancePage.xaml", "ReconcileList", typeof(global::AgencyOS.Contracts.Finance.PaymentAdjustmentResponse)),
        new("Pages/FinancePage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Finance.FinanceHistoryEntryResponse)),

        new("Pages/IntelligencePage.xaml", "AwaitingList", typeof(global::AgencyOS.Contracts.Intelligence.PredictionResponse)),
        new("Pages/IntelligencePage.xaml", "DisputedList", typeof(global::AgencyOS.Contracts.Intelligence.SignalResponse)),
        new("Pages/IntelligencePage.xaml", "RadarReviewList", typeof(global::AgencyOS.Contracts.Intelligence.TalentRadarResponse)),
        new("Pages/IntelligencePage.xaml", "SignalList", typeof(global::AgencyOS.Contracts.Intelligence.SignalResponse)),
        new("Pages/IntelligencePage.xaml", "SignalEvidenceList", typeof(global::AgencyOS.Contracts.Intelligence.SignalEvidenceResponse)),
        new("Pages/IntelligencePage.xaml", "SourceList", typeof(global::AgencyOS.Contracts.Intelligence.IntelligenceSourceResponse)),
        new("Pages/IntelligencePage.xaml", "ThesisList", typeof(global::AgencyOS.Contracts.Intelligence.ThesisResponse)),
        new("Pages/IntelligencePage.xaml", "ThesisRevisionList", typeof(global::AgencyOS.Contracts.Intelligence.ThesisRevisionResponse)),
        new("Pages/IntelligencePage.xaml", "ThesisEvidenceList", typeof(global::AgencyOS.Contracts.Intelligence.ThesisEvidenceResponse)),
        new("Pages/IntelligencePage.xaml", "PredictionList", typeof(global::AgencyOS.Contracts.Intelligence.PredictionResponse)),
        new("Pages/IntelligencePage.xaml", "WatchlistList", typeof(global::AgencyOS.Contracts.Intelligence.WatchlistResponse)),
        new("Pages/IntelligencePage.xaml", "WatchlistSignalList", typeof(global::AgencyOS.Contracts.Intelligence.SignalResponse)),
        new("Pages/IntelligencePage.xaml", "RadarList", typeof(global::AgencyOS.Contracts.Intelligence.TalentRadarResponse)),
        new("Pages/IntelligencePage.xaml", "ResearchList", typeof(global::AgencyOS.Contracts.Intelligence.ResearchCaseResponse)),
        new("Pages/IntelligencePage.xaml", "ResearchTaskList", typeof(global::AgencyOS.Contracts.Intelligence.ResearchTaskResponse)),

        new("Pages/OrganizationPage.xaml", "MemberList", null,
            "Bound to MemberLine, declared in the WinUI assembly this suite cannot load. "
                + "Covered by the release-candidate client run instead."),

        new("Pages/PackagesPage.xaml", "PackageList", typeof(global::AgencyOS.Contracts.Projects.PackageSummaryResponse)),
        new("Pages/PackagesPage.xaml", "AttachedList", typeof(global::AgencyOS.Contracts.Projects.PackageElementResponse)),
        new("Pages/PackagesPage.xaml", "ProposedList", typeof(global::AgencyOS.Contracts.Projects.PackageElementResponse)),
        new("Pages/PackagesPage.xaml", "GapList", typeof(global::AgencyOS.Contracts.Projects.ProjectRoleResponse)),

        new("Pages/PeoplePage.xaml", "PersonIntelligenceList", typeof(global::AgencyOS.Client.ViewModels.EntityIntelligenceRow)),

        new("Pages/PipelinePage.xaml", "OpportunityList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunitySummaryResponse)),
        new("Pages/PipelinePage.xaml", "TargetList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunityTargetResponse)),
        new("Pages/PipelinePage.xaml", "SubmissionList", typeof(global::AgencyOS.Contracts.Opportunities.SubmissionResponse)),
        new("Pages/PipelinePage.xaml", "PitchList", typeof(global::AgencyOS.Contracts.Opportunities.PitchResponse)),
        new("Pages/PipelinePage.xaml", "SubjectList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunitySubjectResponse)),
        new("Pages/PipelinePage.xaml", "TaskList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunityTaskResponse)),
        new("Pages/PipelinePage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunityHistoryEntryResponse)),

        new("Pages/ProjectsPage.xaml", "ProjectList", typeof(global::AgencyOS.Contracts.Projects.ProjectSummaryResponse)),
        new("Pages/ProjectsPage.xaml", "RoleList", typeof(global::AgencyOS.Contracts.Projects.ProjectRoleResponse)),
        new("Pages/ProjectsPage.xaml", "AttachmentList", typeof(global::AgencyOS.Contracts.Projects.AttachmentResponse)),
        new("Pages/ProjectsPage.xaml", "CompanyList", typeof(global::AgencyOS.Contracts.Projects.ProjectCompanyResponse)),
        new("Pages/ProjectsPage.xaml", "SourceList", typeof(global::AgencyOS.Contracts.Projects.SourcePropertyResponse)),
        new("Pages/ProjectsPage.xaml", "MaterialList", typeof(global::AgencyOS.Contracts.Projects.ProjectMaterialResponse)),
        new("Pages/ProjectsPage.xaml", "PackageList", typeof(global::AgencyOS.Contracts.Projects.PackageSummaryResponse)),
        new("Pages/ProjectsPage.xaml", "PursuitList", typeof(global::AgencyOS.Contracts.Opportunities.OpportunitySummaryResponse)),
        new("Pages/ProjectsPage.xaml", "ProjectDealList", typeof(global::AgencyOS.Contracts.Deals.DealSummaryResponse)),
        new("Pages/ProjectsPage.xaml", "ProjectContractList", typeof(global::AgencyOS.Contracts.Legal.ContractSummaryResponse)),
        new("Pages/ProjectsPage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Projects.ProjectHistoryEntryResponse)),

        new("Pages/ProspectsPage.xaml", "ProspectList", typeof(global::AgencyOS.Contracts.Representation.ProspectResponse)),

        new("Pages/SavedViewsPage.xaml", "ViewList", typeof(global::AgencyOS.Contracts.SavedViews.SavedViewResponse)),
        new("Pages/SavedViewsPage.xaml", "ResultList", typeof(global::AgencyOS.Client.ViewModels.SavedViewRow)),

        new("Pages/SyncPage.xaml", "QueueList", typeof(global::AgencyOS.Client.ViewModels.PendingChangeItem)),

        new("Pages/TalentPage.xaml", "TalentList", typeof(global::AgencyOS.Contracts.Representation.TalentSummaryResponse)),
        new("Pages/TalentPage.xaml", "ScopeList", typeof(global::AgencyOS.Contracts.Representation.RepresentationScopeResponse)),
        new("Pages/TalentPage.xaml", "TeamList", typeof(global::AgencyOS.Contracts.Representation.RepresentationTeamMemberResponse)),
        new("Pages/TalentPage.xaml", "CreditList", typeof(global::AgencyOS.Contracts.Representation.CreditResponse)),
        new("Pages/TalentPage.xaml", "MaterialList", typeof(global::AgencyOS.Contracts.Representation.MaterialResponse)),
        new("Pages/TalentPage.xaml", "HistoryList", typeof(global::AgencyOS.Contracts.Representation.RepresentationHistoryEntryResponse)),
        new("Pages/TalentPage.xaml", "TaskList", typeof(global::AgencyOS.Contracts.PeopleSlice.TaskResponse)),
    ];

    /// <summary>Catalog entries a test can build an instance of.</summary>
    public static TheoryData<string, string> Reachable
    {
        get
        {
            TheoryData<string, string> data = [];

            foreach (Entry entry in Catalog.Where(x => x.Row is not null))
            {
                data.Add(entry.File, entry.Template);
            }

            return data;
        }
    }

    // --------------------------------------------------------------- ratchet

    /// <summary>
    /// The catalog names every row template in the client, and nothing else.
    /// </summary>
    /// <remarks>
    /// Both directions. A template added later without an entry fails here, so the
    /// parity property cannot quietly stop covering the product; an entry whose
    /// template was removed fails too, so the catalog cannot describe a client that
    /// no longer exists.
    /// </remarks>
    [Fact]
    public void TheCatalogCoversEveryRowTemplateAndOnlyThose()
    {
        List<string> inMarkup = [.. Templates().Select(x => Key(x.File, x.Template)).Order()];
        List<string> inCatalog = [.. Catalog.Select(x => Key(x.File, x.Template)).Order()];

        Assert.Equal(inMarkup.Count, inMarkup.Distinct().Count());
        Assert.Equal(inCatalog.Count, inCatalog.Distinct().Count());

        List<string> missing = [.. inMarkup.Except(inCatalog)];
        List<string> stale = [.. inCatalog.Except(inMarkup)];

        Assert.True(
            missing.Count == 0 && stale.Count == 0,
            "Row templates without a catalog entry: " + string.Join(", ", missing)
                + " | Catalog entries without a template: " + string.Join(", ", stale));
    }

    /// <summary>Every row template announces through <see cref="RowLabel"/>.</summary>
    /// <remarks>
    /// The property below compares against <c>RowLabel.For</c>. A template announcing
    /// through anything else would be compared against the wrong channel.
    /// </remarks>
    [Fact]
    public void EveryRowTemplateAnnouncesThroughRowLabel()
    {
        List<string> other = [.. Templates()
            .Where(x => !(x.Root.Attribute("AutomationProperties.Name")?.Value ?? string.Empty)
                .Contains("StaticResource RowLabel", StringComparison.Ordinal))
            .Select(x => Key(x.File, x.Template))];

        Assert.True(other.Count == 0, "Rows announcing some other way: " + string.Join(", ", other));
    }

    /// <summary>An entry with no bound type says why, and every other entry has one.</summary>
    [Fact]
    public void EveryUnreachableEntrySaysWhy()
    {
        foreach (Entry entry in Catalog)
        {
            Assert.True(
                entry.Row is not null ^ !string.IsNullOrWhiteSpace(entry.Unreachable),
                $"{Key(entry.File, entry.Template)} must have a type or a reason, not both or neither.");
        }
    }

    /// <summary>
    /// Every visible binding resolves on the declared type.
    /// </summary>
    /// <remarks>
    /// What stops a wrong catalog type passing vacuously: a binding the declared type
    /// cannot satisfy is a type the row is not bound to.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Reachable))]
    public void EveryVisibleBindingResolvesOnTheDeclaredType(string file, string template)
    {
        Entry entry = Find(file, template);

        foreach (Shown shown in Visible(entry))
        {
            if (shown.Path is null)
            {
                continue;
            }

            Assert.True(
                Property(entry.Row!, shown.Path) is not null,
                $"{Key(file, template)}: '{shown.Path}' is not a property of {entry.Row!.Name}.");
        }
    }

    /// <summary>Every converter a row uses is one this gate can evaluate.</summary>
    [Fact]
    public void EveryConverterARowUsesIsEvaluable()
    {
        List<string> unknown = [.. Catalog
            .Where(x => x.Row is not null)
            .SelectMany(Visible)
            .Select(x => x.Converter)
            .OfType<string>()
            .Distinct()
            .Where(x => !Converters.Contains(x))];

        Assert.True(unknown.Count == 0, "Converters with no evaluation here: " + string.Join(", ", unknown));
    }

    // -------------------------------------------------------------- property

    /// <summary>
    /// Every value a row shows appears in what it announces.
    /// </summary>
    [Theory]
    [MemberData(nameof(Reachable))]
    public void EveryShownValueIsAnnounced(string file, string template)
    {
        Entry entry = Find(file, template);

        object row = Sentinels.Build(entry.Row!);
        string announced = RowLabel.For(row);

        List<string> missing = [];

        foreach (Shown shown in Visible(entry))
        {
            string text = Render(row, shown);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // A composed caption joins its parts with the product's visible
            // separator; each part is a value in its own right, and the
            // announcement joins the same parts with commas.
            foreach (string part in text.Split(" · ", StringSplitOptions.TrimEntries))
            {
                if (part.Length > 0 && !announced.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add($"{shown.Binding} shows '{part}'");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{Key(file, template)} [{entry.Row!.Name}] announces '{announced}' and omits: "
                + string.Join("; ", missing));
    }

    // ------------------------------------------------------------ evaluation

    /// <summary>The converters a row may name, each evaluated by the function it calls.</summary>
    private static readonly HashSet<string> Converters =
    [
        "DisplayLabel", "IsoDate", "Money", "TaskWho", "TaskWhen", "TaskAbout",
        "TargetContact", "Party", "PredictionCaption", "PredictionDue",
    ];

    /// <summary>What the operator sees for one binding, the way the row computes it.</summary>
    /// <remarks>
    /// Each converter is evaluated by the client function its WinUI class delegates
    /// to, which <c>ConverterDelegationTests</c> pins. No formatting is copied here.
    /// A binding with no converter shows the value's own text, as the framework does.
    /// </remarks>
    private static string Render(object row, Shown shown)
    {
        object? value = shown.Path is null ? row : Read(row, shown.Path);

        return shown.Converter switch
        {
            null => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
            "DisplayLabel" => DisplayLabel.For(value?.ToString()),
            "IsoDate" => IsoDate.Format(value),
            "Money" => MoneyFormatting.Format(value as MoneyResponse),
            "TaskWho" => TaskLine.Who(value) ?? string.Empty,
            "TaskWhen" => TaskLine.When(value),
            "TaskAbout" => TaskLine.About(value) ?? string.Empty,
            "TargetContact" => TargetLine.Contact(value) ?? string.Empty,
            "Party" => PartyLine.For(value, shown.Parameter) ?? string.Empty,
            "PredictionCaption" => ForecastLine.Caption(value),
            "PredictionDue" => ForecastLine.ResolvesBy(value) ?? string.Empty,
            _ => throw new InvalidOperationException($"No evaluation for converter '{shown.Converter}'."),
        };
    }

    // ------------------------------------------------------------ the markup

    /// <summary>One visible binding in a row template.</summary>
    public sealed record Shown(string Binding, string? Path, string? Converter, string? Parameter);

    private sealed record RowTemplate(string File, string Template, XElement Root);

    private static IEnumerable<RowTemplate> Templates()
    {
        string root = Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows");

        foreach (string path in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative.StartsWith("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (XElement template in XElement.Load(path)
                .DescendantsAndSelf()
                .Where(x => x.Name.LocalName == "DataTemplate"))
            {
                string? identity = template.Attribute(Xaml + "Key")?.Value
                    ?? template.Ancestors()
                        .Select(x => x.Attribute(Xaml + "Name")?.Value)
                        .FirstOrDefault(x => x is not null);

                XElement? first = template.Elements().FirstOrDefault();

                if (identity is null || first is null)
                {
                    continue;
                }

                yield return new RowTemplate(relative, identity, first);
            }
        }
    }

    /// <summary>
    /// The text-bearing bindings a row renders itself.
    /// </summary>
    /// <remarks>
    /// Text inside a descendant that carries its own accessible name — the detailed
    /// notes expander — is announced by that descendant, not by the row.
    /// </remarks>
    private static IEnumerable<Shown> Visible(Entry entry)
    {
        RowTemplate template = Templates().Single(
            x => x.File == entry.File && x.Template == entry.Template);

        foreach (XElement element in template.Root.DescendantsAndSelf())
        {
            if (element != template.Root
                && element.AncestorsAndSelf()
                    .TakeWhile(x => x != template.Root)
                    .Any(x => x.Attribute("AutomationProperties.Name") is not null))
            {
                continue;
            }

            foreach (string attribute in (string[])["Text", "Content"])
            {
                if (element.Attribute(attribute)?.Value is not { } binding
                    || !binding.StartsWith("{Binding", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return Parse(binding);
            }
        }
    }

    private static Shown Parse(string binding)
    {
        string inner = binding["{Binding".Length..^1].Trim();

        string? path = null;
        string? converter = null;
        string? parameter = null;

        foreach (string part in SplitTopLevel(inner))
        {
            string piece = part.Trim();

            if (piece.StartsWith("Converter=", StringComparison.Ordinal))
            {
                converter = Regex.Match(piece, @"StaticResource\s+(\w+)").Groups[1].Value;
            }
            else if (piece.StartsWith("ConverterParameter=", StringComparison.Ordinal))
            {
                parameter = piece["ConverterParameter=".Length..].Trim();
            }
            else if (piece.Length > 0 && !piece.Contains('=', StringComparison.Ordinal))
            {
                path = piece;
            }
        }

        return new Shown(binding, path, converter, parameter);
    }

    /// <summary>Splits on commas that are not inside a nested markup extension.</summary>
    private static IEnumerable<string> SplitTopLevel(string text)
    {
        int depth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (text[i] == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    // --------------------------------------------------------------- helpers

    private static Entry Find(string file, string template) =>
        Catalog.Single(x => x.File == file && x.Template == template);

    private static string Key(string file, string template) => file + "#" + template;

    private static PropertyInfo? Property(Type type, string path)
    {
        PropertyInfo? found = null;
        Type current = type;

        foreach (string segment in path.Split('.'))
        {
            found = current.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);

            if (found is null)
            {
                return null;
            }

            current = found.PropertyType;
        }

        return found;
    }

    private static object? Read(object row, string path)
    {
        object? current = row;

        foreach (string segment in path.Split('.'))
        {
            current = current?.GetType()
                .GetProperty(segment, BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(current);
        }

        return current;
    }

    private static string RepositoryRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>
    /// Instances in which every field holds a value no other field holds.
    /// </summary>
    /// <remarks>
    /// Strings are four-letter lowercase tokens, so none is a substring of another
    /// and none is changed by the display-label rules beyond its first letter. Numbers, dates and
    /// instants are distinct and far from any value a fixture would use. Every
    /// optional field is filled: the question is whether a shown value is announced,
    /// and an absent value is shown as nothing.
    /// </remarks>
    internal static class Sentinels
    {
        public static object Build(Type type) => Make(type, new Counter(), 0)
            ?? throw new InvalidOperationException($"Cannot build {type.Name}.");

        /// <summary>
        /// A four-letter lowercase token, unique per field.
        /// </summary>
        /// <remarks>
        /// Letters only. A digit would be split off by the display-label rules, and
        /// the comparison would then fail on the sentinel rather than on the row.
        /// </remarks>
        private static string Word(int n) =>
            string.Concat("zq", (char)('a' + (n / 26 % 26)), (char)('a' + (n % 26)));

        private sealed class Counter
        {
            private int _next;

            public int Next() => ++_next;
        }

        private static object? Make(Type type, Counter counter, int depth)
        {
            Type actual = Nullable.GetUnderlyingType(type) ?? type;

            if (actual == typeof(string))
            {
                return Word(counter.Next());
            }

            if (actual == typeof(bool))
            {
                return true;
            }

            if (actual == typeof(Guid))
            {
                return Guid.NewGuid();
            }

            if (actual == typeof(int) || actual == typeof(long) || actual == typeof(short))
            {
                return System.Convert.ChangeType(6000 + counter.Next(), actual, CultureInfo.InvariantCulture);
            }

            if (actual == typeof(decimal))
            {
                return 70000m + counter.Next() + 0.25m;
            }

            if (actual == typeof(double))
            {
                return 80000d + counter.Next() + 0.5d;
            }

            if (actual == typeof(DateOnly))
            {
                return new DateOnly(2031, 1, 1).AddDays(counter.Next() * 3);
            }

            if (actual == typeof(DateTimeOffset))
            {
                return new DateTimeOffset(2032, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(counter.Next() * 3);
            }

            if (actual == typeof(DateTime))
            {
                return new DateTime(2033, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(counter.Next() * 3);
            }

            if (actual == typeof(TimeSpan))
            {
                return TimeSpan.FromMinutes(counter.Next());
            }

            if (actual.IsEnum)
            {
                Array values = Enum.GetValues(actual);

                return values.GetValue(values.Length - 1);
            }

            if (actual.IsArray)
            {
                return Array.CreateInstance(actual.GetElementType()!, 0);
            }

            if (typeof(IEnumerable).IsAssignableFrom(actual) && actual.IsGenericType)
            {
                Type element = actual.GetGenericArguments()[0];

                return actual.IsInterface
                    ? Array.CreateInstance(element, 0)
                    : Activator.CreateInstance(actual);
            }

            if (depth > 3 || actual == typeof(object))
            {
                return null;
            }

            ConstructorInfo? constructor = actual.GetConstructors()
                .OrderByDescending(x => x.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                return null;
            }

            object?[] arguments = [.. constructor.GetParameters()
                .Select(x => Make(x.ParameterType, counter, depth + 1))];

            object instance = constructor.Invoke(arguments);

            if (arguments.Length == 0)
            {
                foreach (PropertyInfo property in actual.GetProperties()
                    .Where(x => x.CanWrite && x.GetIndexParameters().Length == 0))
                {
                    property.SetValue(instance, Make(property.PropertyType, counter, depth + 1));
                }
            }

            return instance;
        }
    }
}
