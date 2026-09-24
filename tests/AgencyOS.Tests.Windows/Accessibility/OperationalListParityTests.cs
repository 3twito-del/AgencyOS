using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

// SOURCE-PROOF: Reads the client's XAML to learn which fields each list row renders,
// through which converter, and where; which bindings a template declares is a markup
// fact. Also reads the four WinUI-declared row types' C# declarations to prove each
// surrogate has the same name and property shape, because loading that assembly runs
// the Windows App Runtime bootstrap. What rows show and announce is executed.

/// <summary>
/// That every value a list row shows is accounted for in what an operator who
/// cannot see the row is told.
/// </summary>
/// <remarks>
/// <para>
/// The operational regression gate's central check (<c>18-…</c> §2 and §5). Every
/// list row in the client announces through <see cref="RowLabel"/>, and every list
/// row shows some of its record's fields. Where a row shows a value its
/// announcement leaves out, a sighted operator and a screen-reader operator are told
/// different things about one record.
/// </para>
/// <para>
/// <strong>Every visible value is accounted for.</strong> <see cref="Accounting"/>
/// classifies each visible binding in every row template, and the markup is checked
/// against it both ways. A <em>primary</em> value is a scan fact and must be in the
/// row's name. A <em>secondary</em> value is explanatory prose beneath a row its
/// primary facts already identify; the owner allowed it a separate accessible
/// channel, so the gate proves only that it is wired to one. Wiring is not
/// operator proof: a secondary channel is accepted only when the release-candidate
/// client run shows an operator finding and reading it, and until then every
/// secondary value is reported as pending that proof. Nothing visible is exempt.
/// </para>
/// <para>
/// <strong>Semantics, not substrings.</strong> Each value is rendered the way the
/// row renders it — through the client function its converter calls — and then
/// compared by what it is: a date is the same date whatever its format, an amount is
/// the same amount in the same currency, a person carries the role word the row
/// uses, and a number, date or flag that shares its row with others of its kind
/// must carry its role so one cannot answer for another. <see cref="Coverage"/>
/// holds the rules and is itself tested below.
/// </para>
/// <para>
/// <strong>What keeps it honest.</strong> The catalog covers every list-item
/// template and nothing else. Every row type is built. Six templates bind types
/// declared inside the WinUI assembly, whose module initializer starts the Windows App
/// Runtime when any of its types is touched; those rows are built from surrogates whose
/// name and property shape are ratcheted against the production declarations, because
/// <see cref="RowLabel"/> reads nothing else from them. Every binding must resolve on its type, every converter
/// must have an evaluator, and sentinel values are deterministic and distinct.
/// </para>
/// <para>
/// <strong>What it does not do.</strong> It is not a XAML compiler. It reads the
/// bindings a template declares on text — <c>Text</c> and <c>Content</c> — and
/// nothing else: no styles, no triggers, no code-behind that writes a row later.
/// </para>
/// </remarks>
public sealed partial class OperationalListParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>A row template and the type it is bound to.</summary>
    /// <param name="File">The markup file, relative to <c>src/AgencyOS.Windows</c>.</param>
    /// <param name="Template">The template's key, or the name of the list that holds it.</param>
    /// <param name="Row">The bound type, where a test project references it.</param>
    /// <param name="ProductType">
    /// The bound type's full name, where it is declared in the WinUI assembly. The row
    /// is built from a surrogate proved to have that type's name and shape.
    /// </param>
    public sealed record Entry(string File, string Template, Type? Row, string? ProductType = null);

    public static IReadOnlyList<Entry> Catalog { get; } =
    [
        new("App.xaml", "TaskTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.TaskResponse)),
        new("App.xaml", "PersonRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.PersonSummaryResponse)),
        new("App.xaml", "CompanyRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.CompanySummaryResponse)),
        new("App.xaml", "InteractionTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.InteractionResponse)),
        new("App.xaml", "TimelineEntryTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.TimelineEntryResponse)),
        new("App.xaml", "RelationshipRowTemplate", typeof(global::AgencyOS.Contracts.PeopleSlice.RelationshipResponse)),

        new("Dialogs/AllocatePaymentDialog.xaml", "AllocationList", null, "AgencyOS.Windows.Dialogs.AllocationRow"),
        new("Dialogs/AnswerOfferDialog.xaml", "TermList", typeof(global::AgencyOS.Contracts.Deals.OfferTermResponse)),
        new("Dialogs/ComposeMessageDialog.xaml", "RecipientList", typeof(global::AgencyOS.Contracts.Documents.RecipientRequest)),
        new("Dialogs/PostJournalEntryDialog.xaml", "LineList", null, "AgencyOS.Windows.Dialogs.JournalLineRow"),
        new("Dialogs/RecordInvoiceDialog.xaml", "LineList", null, "AgencyOS.Windows.Dialogs.AllocationRow"),
        new("Dialogs/RecordOfferDialog.xaml", "TermList", null, "AgencyOS.Windows.Dialogs.RecordOfferDialog+StagedTerm"),
        new("Dialogs/RecordPaymentDialog.xaml", "AllocationList", null, "AgencyOS.Windows.Dialogs.AllocationRow"),
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

        new("Pages/OrganizationPage.xaml", "MemberList", null, "AgencyOS.Windows.Pages.OrganizationPage+MemberLine"),

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

    /// <summary>Every catalog entry, for the per-template theories.</summary>
    public static TheoryData<string, string> Rows
    {
        get
        {
            TheoryData<string, string> data = [];

            foreach (Entry entry in Catalog)
            {
                data.Add(entry.File, entry.Template);
            }

            return data;
        }
    }

    // ------------------------------------------------------------ accounting

    /// <summary>Whether a visible value is a scan fact or explanatory prose.</summary>
    public enum Weight
    {
        /// <summary>A scan fact. It must be in the row's accessible name.</summary>
        Primary,

        /// <summary>Explanatory prose. It must be wired to an explicit secondary channel.</summary>
        Secondary,
    }

    /// <summary>Where a secondary value is offered to an operator who cannot see it.</summary>
    public enum Channel
    {
        /// <summary>No secondary channel: the value is primary.</summary>
        None,

        /// <summary>
        /// The row's own <c>AutomationProperties.HelpText</c>, bound to the value.
        /// </summary>
        HelpText,

        /// <summary>
        /// A focusable child of the row carrying its own accessible name, whose content
        /// holds the value — the detailed-notes expander.
        /// </summary>
        Descendant,
    }

    /// <summary>A binding's classification, the channel it may use, and why.</summary>
    /// <param name="Role">
    /// The word the row itself uses for this value's role, where its field name is not
    /// that word — taken from the list's own column header or the approved concise
    /// vocabulary the row's profile declares, never invented.
    /// </param>
    /// <param name="RoleFrom">
    /// Another binding on the row whose value is this one's role: a link row's kind
    /// names what its label is. The two are covered together, as one statement.
    /// </param>
    public sealed record Classification(
        Weight Weight, Channel Channel, string Reason, string? Role = null, string? RoleFrom = null);

    private static Classification Primary { get; } = new(Weight.Primary, Channel.None, string.Empty);

    private static Classification PrimaryAs(string role, string reason) =>
        new(Weight.Primary, Channel.None, reason, role);

    private static Classification PrimaryAs(string role) =>
        new(Weight.Primary, Channel.None, Concise, role);

    private static Classification PrimaryRoledBy(string roleFrom, string reason) =>
        new(Weight.Primary, Channel.None, reason, RoleFrom: roleFrom);

    private const string Concise =
        "The approved concise role word, declared once in the row's profile in RowProfiles.";

    private static Classification Secondary(Channel channel, string reason) =>
        new(Weight.Secondary, channel, reason);

    /// <summary>
    /// Every visible binding in every row template, classified.
    /// </summary>
    /// <remarks>
    /// Keyed <c>file#template#binding</c>. Classification is about what the value
    /// means on that row, not about its property name: a <c>Detail</c> that is a role
    /// or a kind is primary, a <c>Detail</c> that is a sentence of context beneath an
    /// identified event is secondary.
    /// </remarks>
    public static IReadOnlyDictionary<string, Classification> Accounting { get; } =
        new Dictionary<string, Classification>(StringComparer.Ordinal)
    {
        ["App.xaml#TaskTemplate#{Binding Title}"] = Primary,
        ["App.xaml#TaskTemplate#{Binding Converter={StaticResource TaskAbout}}"] = Primary,
        ["App.xaml#TaskTemplate#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["App.xaml#TaskTemplate#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["App.xaml#TaskTemplate#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["App.xaml#PersonRowTemplate#{Binding DisplayName}"] = Primary,
        ["App.xaml#PersonRowTemplate#{Binding Title}"] = Primary,
        ["App.xaml#PersonRowTemplate#{Binding PrimaryCompanyName}"] = Primary,
        ["App.xaml#CompanyRowTemplate#{Binding Name}"] = Primary,
        ["App.xaml#CompanyRowTemplate#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["App.xaml#InteractionTemplate#{Binding Summary}"] = Primary,
        ["App.xaml#InteractionTemplate#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["App.xaml#InteractionTemplate#{Binding DetailedNotes}"] = Secondary(Channel.Descendant, "The interaction's long account, behind the \"Detailed notes\" expander; the row is identified by its summary and type."),
        ["App.xaml#TimelineEntryTemplate#{Binding OccurredAt}"] = Primary,
        ["App.xaml#TimelineEntryTemplate#{Binding Title}"] = Primary,
        ["App.xaml#TimelineEntryTemplate#{Binding Detail}"] = Secondary(Channel.HelpText, "Supporting detail beneath a timeline entry identified by its title and time."),
        ["App.xaml#RelationshipRowTemplate#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["App.xaml#RelationshipRowTemplate#{Binding To.Name}"] = Primary,
        ["Dialogs/AllocatePaymentDialog.xaml#AllocationList#{Binding Description}"] = Primary,
        ["Dialogs/AllocatePaymentDialog.xaml#AllocationList#{Binding AmountDisplay}"] = Primary,
        ["Dialogs/AnswerOfferDialog.xaml#TermList#{Binding DisplayName}"] = Primary,
        ["Dialogs/AnswerOfferDialog.xaml#TermList#{Binding DisplayValue}"] = Primary,
        ["Dialogs/ComposeMessageDialog.xaml#RecipientList#{Binding Role}"] = Primary,
        ["Dialogs/ComposeMessageDialog.xaml#RecipientList#{Binding Address}"] = Primary,
        ["Dialogs/PostJournalEntryDialog.xaml#LineList#{Binding Account}"] = PrimaryAs("Account"),
        ["Dialogs/PostJournalEntryDialog.xaml#LineList#{Binding Side}"] = PrimaryAs("Side"),
        ["Dialogs/PostJournalEntryDialog.xaml#LineList#{Binding AmountDisplay}"] = PrimaryAs("Amount"),
        ["Dialogs/RecordInvoiceDialog.xaml#LineList#{Binding Description}"] = Primary,
        ["Dialogs/RecordInvoiceDialog.xaml#LineList#{Binding AmountDisplay}"] = Primary,
        ["Dialogs/RecordOfferDialog.xaml#TermList#{Binding DisplayName}"] = Primary,
        ["Dialogs/RecordOfferDialog.xaml#TermList#{Binding DisplayValue}"] = Primary,
        ["Dialogs/RecordPaymentDialog.xaml#AllocationList#{Binding Description}"] = Primary,
        ["Dialogs/RecordPaymentDialog.xaml#AllocationList#{Binding AmountDisplay}"] = Primary,
        ["Dialogs/ResolveParticipantDialog.xaml#SuggestionList#{Binding DisplayName}"] = Primary,
        ["Dialogs/ResolveParticipantDialog.xaml#SuggestionList#{Binding MatchedAddress}"] = Primary,
        ["MainWindow.xaml#SearchResults#{Binding Title}"] = Primary,
        ["MainWindow.xaml#SearchResults#{Binding Subtitle}"] = PrimaryAs("Context"),
        ["MainWindow.xaml#SearchResults#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["MainWindow.xaml#SearchResults#{Binding MatchedOn}"] = PrimaryAs("Matched"),
        ["MainWindow.xaml#PaletteResults#{Binding Title}"] = Primary,
        ["MainWindow.xaml#PaletteResults#{Binding Category}"] = PrimaryAs("Category"),
        ["MainWindow.xaml#PaletteResults#{Binding Shortcut}"] = PrimaryAs("Shortcut"),
        ["Pages/AiPage.xaml#ApprovalList#{Binding Summary}"] = Primary,
        ["Pages/AiPage.xaml#ApprovalList#{Binding ToolName}"] = Primary,
        ["Pages/AiPage.xaml#ApprovalList#{Binding ExpiresAt}"] = Primary,
        ["Pages/AiPage.xaml#RunList#{Binding Task}"] = Primary,
        ["Pages/AiPage.xaml#RunList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/AiPage.xaml#RunList#{Binding StartedAt}"] = Primary,
        ["Pages/AiPage.xaml#StepList#{Binding Summary}"] = Primary,
        ["Pages/AiPage.xaml#StepList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/AiPage.xaml#StepList#{Binding OccurredAt}"] = Primary,
        ["Pages/AiPage.xaml#PolicyList#{Binding ProviderKey}"] = PrimaryAs("Provider"),
        ["Pages/AiPage.xaml#PolicyList#{Binding MaximumSensitivity}"] = PrimaryAs("Maximum sensitivity"),
        ["Pages/AiPage.xaml#ToolList#{Binding Description}"] = Secondary(Channel.HelpText, "What the tool does, in prose, beneath a tool identified by its name and the permission it needs."),
        ["Pages/AiPage.xaml#ToolList#{Binding Name}"] = Primary,
        ["Pages/AiPage.xaml#ToolList#{Binding RequiredPermission}"] = Primary,
        ["Pages/AiPage.xaml#ModelList#{Binding Key}"] = PrimaryAs("Model"),
        ["Pages/AiPage.xaml#ModelList#{Binding ProviderKey}"] = PrimaryAs("Provider"),
        ["Pages/CommunicationsPage.xaml#MessageList#{Binding Subject}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MessageList#{Binding FromAddress}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MessageList#{Binding OccurredAt}"] = Primary,
        ["Pages/CommunicationsPage.xaml#ParticipantList#{Binding Role}"] = Primary,
        ["Pages/CommunicationsPage.xaml#ParticipantList#{Binding Address}"] = Primary,
        ["Pages/CommunicationsPage.xaml#ParticipantList#{Binding DisplayName}"] = Primary,
        ["Pages/CommunicationsPage.xaml#ParticipantList#{Binding Converter={StaticResource Party}, ConverterParameter=PersonDisplayName}"] = Primary,
        ["Pages/CommunicationsPage.xaml#AttachmentList#{Binding FileName}"] = PrimaryAs("File name"),
        ["Pages/CommunicationsPage.xaml#AttachmentList#{Binding MediaType}"] = PrimaryAs("Media type"),
        ["Pages/CommunicationsPage.xaml#AttachmentList#{Binding HoldsContent}"] = PrimaryAs("Holds content"),
        ["Pages/CommunicationsPage.xaml#MessageLinkList#{Binding TargetLabel}"] = PrimaryRoledBy("Target", "A link row's kind names what its label is: the kind is the label's role."),
        ["Pages/CommunicationsPage.xaml#MessageLinkList#{Binding Target}"] = Primary,
        ["Pages/CommunicationsPage.xaml#OutboundList#{Binding Subject}"] = Primary,
        ["Pages/CommunicationsPage.xaml#OutboundList#{Binding MailboxAddress}"] = Primary,
        ["Pages/CommunicationsPage.xaml#OutboundList#{Binding State, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/CommunicationsPage.xaml#OutboundList#{Binding AttemptCount}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MailboxList#{Binding MailboxAddress}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MailboxList#{Binding Converter={StaticResource Party}, ConverterParameter=OwnerDisplayName}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MailboxList#{Binding State, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MailboxList#{Binding LastSyncedAt}"] = Primary,
        ["Pages/CommunicationsPage.xaml#MailboxList#{Binding Visibility}"] = Primary,
        ["Pages/CommunicationsPage.xaml#DeskList#{Binding Subject}"] = Primary,
        ["Pages/CommunicationsPage.xaml#DeskList#{Binding MailboxAddress}"] = Primary,
        ["Pages/CommunicationsPage.xaml#DeskList#{Binding State, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/CompaniesPage.xaml#CompanyIntelligenceList#{Binding Title}"] = Primary,
        ["Pages/CompaniesPage.xaml#CompanyIntelligenceList#{Binding Kind}"] = Primary,
        ["Pages/CompaniesPage.xaml#CompanyIntelligenceList#{Binding Status}"] = Primary,
        ["Pages/ContractsPage.xaml#ContractList#{Binding Title}"] = Primary,
        ["Pages/ContractsPage.xaml#ContractList#{Binding Converter={StaticResource Party}, ConverterParameter=CounterpartyDisplayName}"] = Primary,
        ["Pages/ContractsPage.xaml#ContractList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#ContractList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#VersionList#{Binding VersionNumber}"] = Primary,
        ["Pages/ContractsPage.xaml#VersionList#{Binding Label}"] = Primary,
        ["Pages/ContractsPage.xaml#VersionList#{Binding Notes}"] = Secondary(Channel.HelpText, "What changed, in prose, beneath a version identified by its number, label, direction and status."),
        ["Pages/ContractsPage.xaml#VersionList#{Binding Direction, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#VersionList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#TermList#{Binding DisplayName}"] = Primary,
        ["Pages/ContractsPage.xaml#TermList#{Binding ClauseReference}"] = Primary,
        ["Pages/ContractsPage.xaml#TermList#{Binding DisplayValue}"] = Primary,
        ["Pages/ContractsPage.xaml#ReconcileList#{Binding DisplayName}"] = Primary,
        ["Pages/ContractsPage.xaml#ReconcileList#{Binding Negotiated.DisplayValue}"] = PrimaryAs("Agreed", "The column header above this value in ContractsPage ReconcileList reads \"Agreed\"."),
        ["Pages/ContractsPage.xaml#ReconcileList#{Binding Contracted.DisplayValue}"] = PrimaryAs("In the draft", "The column header above this value in ContractsPage ReconcileList reads \"In the draft\"."),
        ["Pages/ContractsPage.xaml#ReconcileList#{Binding Result}"] = PrimaryAs("Result", "The column header above this value in ContractsPage ReconcileList reads \"Result\"."),
        ["Pages/ContractsPage.xaml#PartyList#{Binding DisplayName}"] = Primary,
        ["Pages/ContractsPage.xaml#PartyList#{Binding Role}"] = Primary,
        ["Pages/ContractsPage.xaml#PartyList#{Binding SignedOn}"] = Primary,
        ["Pages/ContractsPage.xaml#RightsList#{Binding RightType}"] = PrimaryAs("Right"),
        ["Pages/ContractsPage.xaml#RightsList#{Binding Medium}"] = PrimaryAs("Medium"),
        ["Pages/ContractsPage.xaml#RightsList#{Binding Territory}"] = PrimaryAs("Territory"),
        ["Pages/ContractsPage.xaml#RightsList#{Binding PeriodKind}"] = PrimaryAs("Period"),
        ["Pages/ContractsPage.xaml#OptionList#{Binding Subject}"] = Primary,
        ["Pages/ContractsPage.xaml#OptionList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#OptionList#{Binding DeadlineOn}"] = Primary,
        ["Pages/ContractsPage.xaml#OptionList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#ObligationList#{Binding Description}"] = Primary,
        ["Pages/ContractsPage.xaml#ObligationList#{Binding Converter={StaticResource Party}, ConverterParameter=ObligorDisplayName}"] = Primary,
        ["Pages/ContractsPage.xaml#ObligationList#{Binding DueOn}"] = Primary,
        ["Pages/ContractsPage.xaml#ObligationList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#MoneyObligationList#{Binding Description}"] = Primary,
        ["Pages/ContractsPage.xaml#MoneyObligationList#{Binding Category, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#MoneyObligationList#{Binding DueOn}"] = Primary,
        ["Pages/ContractsPage.xaml#MoneyObligationList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#NoticeRequirementList#{Binding Description}"] = Primary,
        ["Pages/ContractsPage.xaml#NoticeRequirementList#{Binding DueOn}"] = Primary,
        ["Pages/ContractsPage.xaml#NoticeList#{Binding Summary}"] = Primary,
        ["Pages/ContractsPage.xaml#NoticeList#{Binding Direction, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#NoticeList#{Binding OccurredOn}"] = Primary,
        ["Pages/ContractsPage.xaml#TaskList#{Binding Title}"] = Primary,
        ["Pages/ContractsPage.xaml#TaskList#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ContractsPage.xaml#TaskList#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["Pages/ContractsPage.xaml#TaskList#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["Pages/ContractsPage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/ContractsPage.xaml#HistoryList#{Binding Detail}"] = Secondary(Channel.HelpText, "Secondary context beneath a history entry identified by its summary, actor and kind."),
        ["Pages/DealsPage.xaml#DealList#{Binding Name}"] = Primary,
        ["Pages/DealsPage.xaml#DealList#{Binding Converter={StaticResource Party}, ConverterParameter=CounterpartyDisplayName}"] = Primary,
        ["Pages/DealsPage.xaml#DealList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DealsPage.xaml#DealList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DealsPage.xaml#OfferList#{Binding Sequence}"] = Primary,
        ["Pages/DealsPage.xaml#OfferList#{Binding Direction, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DealsPage.xaml#OfferList#{Binding Summary}"] = Primary,
        ["Pages/DealsPage.xaml#OfferList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DealsPage.xaml#TermList#{Binding DisplayName}"] = Primary,
        ["Pages/DealsPage.xaml#TermList#{Binding Notes}"] = Secondary(Channel.HelpText, "Context that is not part of the term's value; the term is identified by its name and value."),
        ["Pages/DealsPage.xaml#TermList#{Binding DisplayValue}"] = Primary,
        ["Pages/DealsPage.xaml#ComparisonList#{Binding DisplayName}"] = Primary,
        ["Pages/DealsPage.xaml#ComparisonList#{Binding Previous.DisplayValue}"] = PrimaryAs("Previous", "The column header above this value in DealsPage ComparisonList reads \"Previous\"."),
        ["Pages/DealsPage.xaml#ComparisonList#{Binding Current.DisplayValue}"] = PrimaryAs("Current", "The column header above this value in DealsPage ComparisonList reads \"Current\"."),
        ["Pages/DealsPage.xaml#ComparisonList#{Binding Change}"] = PrimaryAs("Change", "The column header above this value in DealsPage ComparisonList reads \"Change\"."),
        ["Pages/DealsPage.xaml#TaskList#{Binding Title}"] = Primary,
        ["Pages/DealsPage.xaml#TaskList#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DealsPage.xaml#TaskList#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["Pages/DealsPage.xaml#TaskList#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["Pages/DealsPage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/DealsPage.xaml#HistoryList#{Binding Detail}"] = Secondary(Channel.HelpText, "Secondary context beneath a history entry identified by its summary, actor and kind."),
        ["Pages/DocumentsPage.xaml#DocumentList#{Binding Title}"] = Primary,
        ["Pages/DocumentsPage.xaml#DocumentList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DocumentsPage.xaml#DocumentList#{Binding Sensitivity}"] = Primary,
        ["Pages/DocumentsPage.xaml#DocumentList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/DocumentsPage.xaml#VersionList#{Binding Sequence}"] = Primary,
        ["Pages/DocumentsPage.xaml#VersionList#{Binding DisplayFileName}"] = Primary,
        ["Pages/DocumentsPage.xaml#VersionList#{Binding MediaType}"] = PrimaryAs("Media type"),
        ["Pages/DocumentsPage.xaml#VersionList#{Binding RecordedAt}"] = Primary,
        ["Pages/DocumentsPage.xaml#VersionList#{Binding ContentHash}"] = PrimaryAs("Content hash"),
        ["Pages/DocumentsPage.xaml#LinkList#{Binding TargetLabel}"] = PrimaryRoledBy("Target", "A link row's kind names what its label is: the kind is the label's role."),
        ["Pages/DocumentsPage.xaml#LinkList#{Binding Target}"] = Primary,
        ["Pages/DocumentsPage.xaml#HistoryList#{Binding OccurredAt}"] = Primary,
        ["Pages/DocumentsPage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/DocumentsPage.xaml#HistoryList#{Binding Converter={StaticResource Party}, ConverterParameter=ActorDisplayName}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding ContractTitle}"] = PrimaryAs("Contract"),
        ["Pages/FinancePage.xaml#ReceivableList#{Binding Converter={StaticResource Party}, ConverterParameter=PayerDisplayName}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding OriginalAmount, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding Allocated, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding Outstanding, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#ReceivableList#{Binding Beneficiary}"] = PrimaryAs("money"),
        ["Pages/FinancePage.xaml#InvoiceList#{Binding Reference}"] = Primary,
        ["Pages/FinancePage.xaml#InvoiceList#{Binding Converter={StaticResource Party}, ConverterParameter=DebtorDisplayName}"] = Primary,
        ["Pages/FinancePage.xaml#InvoiceList#{Binding Total, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#InvoiceList#{Binding Outstanding, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#InvoiceList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding Converter={StaticResource Party}, ConverterParameter=PayerDisplayName}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding ExternalReference}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding Amount, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding Allocated, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding Unapplied, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#PaymentList#{Binding ReceivedOn}"] = Primary,
        ["Pages/FinancePage.xaml#CommissionList#{Binding ClientDisplayName}"] = PrimaryAs("Client"),
        ["Pages/FinancePage.xaml#CommissionList#{Binding ContractTitle}"] = PrimaryAs("Contract"),
        ["Pages/FinancePage.xaml#CommissionList#{Binding Entitled, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#CommissionList#{Binding Collected, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#CommissionList#{Binding Outstanding, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#CommissionList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#CommissionRuleList#{Binding ClientDisplayName}"] = PrimaryAs("Client"),
        ["Pages/FinancePage.xaml#CommissionRuleList#{Binding Basis}"] = PrimaryAs("Basis"),
        ["Pages/FinancePage.xaml#CommissionRuleList#{Binding EffectiveFrom}"] = Primary,
        ["Pages/FinancePage.xaml#BalanceList#{Binding Name}"] = Primary,
        ["Pages/FinancePage.xaml#BalanceList#{Binding Currency}"] = Primary,
        ["Pages/FinancePage.xaml#BalanceList#{Binding Debits.Amount}"] = Primary,
        ["Pages/FinancePage.xaml#BalanceList#{Binding Credits.Amount}"] = Primary,
        ["Pages/FinancePage.xaml#BalanceList#{Binding Balance.Amount}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding Memo}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding Source}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding Debits, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding Credits, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#JournalList#{Binding PostingDate}"] = Primary,
        ["Pages/FinancePage.xaml#ReconcileList#{Binding Description}"] = Primary,
        ["Pages/FinancePage.xaml#ReconcileList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/FinancePage.xaml#ReconcileList#{Binding Amount, Converter={StaticResource Money}}"] = Primary,
        ["Pages/FinancePage.xaml#HistoryList#{Binding OccurredAt}"] = Primary,
        ["Pages/FinancePage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/FinancePage.xaml#HistoryList#{Binding Converter={StaticResource Party}, ConverterParameter=ActorDisplayName}"] = Primary,
        ["Pages/FinancePage.xaml#HistoryList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#AwaitingList#{Binding Statement}"] = Primary,
        ["Pages/IntelligencePage.xaml#AwaitingList#{Binding Converter={StaticResource PredictionDue}}"] = Primary,
        ["Pages/IntelligencePage.xaml#DisputedList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#DisputedList#{Binding Claim}"] = Secondary(Channel.HelpText, "The claim in full beneath a disputed signal identified by its title."),
        ["Pages/IntelligencePage.xaml#RadarReviewList#{Binding PersonDisplayName}"] = Primary,
        ["Pages/IntelligencePage.xaml#RadarReviewList#{Binding Rationale}"] = Secondary(Channel.HelpText, "Why the person is being watched, beneath a review entry identified by the person."),
        ["Pages/IntelligencePage.xaml#SignalList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#SignalList#{Binding Claim}"] = Secondary(Channel.HelpText, "The claim in full beneath a signal identified by its title, verification and sensitivity."),
        ["Pages/IntelligencePage.xaml#SignalList#{Binding Verification}"] = PrimaryAs("Verification"),
        ["Pages/IntelligencePage.xaml#SignalList#{Binding Sensitivity}"] = PrimaryAs("Sensitivity"),
        ["Pages/IntelligencePage.xaml#SignalEvidenceList#{Binding SourceTitle}"] = Primary,
        ["Pages/IntelligencePage.xaml#SignalEvidenceList#{Binding Role}"] = Primary,
        ["Pages/IntelligencePage.xaml#SignalEvidenceList#{Binding Excerpt}"] = Secondary(Channel.HelpText, "An analyst's quotation from the source, beneath evidence identified by its source and role."),
        ["Pages/IntelligencePage.xaml#SourceList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#SourceList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#SourceList#{Binding Reliability}"] = PrimaryAs("Reliability"),
        ["Pages/IntelligencePage.xaml#SourceList#{Binding Sensitivity}"] = PrimaryAs("Sensitivity"),
        ["Pages/IntelligencePage.xaml#ThesisList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#ThesisList#{Binding Proposition}"] = Secondary(Channel.HelpText, "The belief in full beneath a thesis identified by its title and status."),
        ["Pages/IntelligencePage.xaml#ThesisList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#ThesisRevisionList#{Binding Proposition}"] = Primary,
        ["Pages/IntelligencePage.xaml#ThesisRevisionList#{Binding ChangeNote}"] = Secondary(Channel.HelpText, "Why the belief changed, beneath a revision identified by the proposition it moved to."),
        ["Pages/IntelligencePage.xaml#ThesisEvidenceList#{Binding SignalTitle}"] = PrimaryAs("Signal"),
        ["Pages/IntelligencePage.xaml#ThesisEvidenceList#{Binding Stance}"] = PrimaryAs("Stance"),
        ["Pages/IntelligencePage.xaml#PredictionList#{Binding Statement}"] = Primary,
        ["Pages/IntelligencePage.xaml#PredictionList#{Binding Converter={StaticResource PredictionCaption}}"] = Primary,
        ["Pages/IntelligencePage.xaml#PredictionList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#PredictionList#{Binding Converter={StaticResource PredictionDue}}"] = Primary,
        ["Pages/IntelligencePage.xaml#WatchlistList#{Binding Name}"] = Primary,
        ["Pages/IntelligencePage.xaml#WatchlistList#{Binding Purpose}"] = Secondary(Channel.HelpText, "What the watchlist is for, beneath a watchlist identified by its name."),
        ["Pages/IntelligencePage.xaml#WatchlistSignalList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#WatchlistSignalList#{Binding Claim}"] = Secondary(Channel.HelpText, "The claim in full beneath a signal identified by its title."),
        ["Pages/IntelligencePage.xaml#RadarList#{Binding PersonDisplayName}"] = Primary,
        ["Pages/IntelligencePage.xaml#RadarList#{Binding Rationale}"] = Secondary(Channel.HelpText, "Why the person is being watched, beneath a radar entry identified by the person, status and priority."),
        ["Pages/IntelligencePage.xaml#RadarList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#RadarList#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#ResearchList#{Binding Question}"] = Primary,
        ["Pages/IntelligencePage.xaml#ResearchList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/IntelligencePage.xaml#ResearchTaskList#{Binding Title}"] = Primary,
        ["Pages/IntelligencePage.xaml#ResearchTaskList#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["Pages/IntelligencePage.xaml#ResearchTaskList#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["Pages/OrganizationPage.xaml#MemberList#{Binding DisplayName}"] = Primary,
        ["Pages/OrganizationPage.xaml#MemberList#{Binding Email}"] = PrimaryAs("Email"),
        ["Pages/OrganizationPage.xaml#MemberList#{Binding Role, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/OrganizationPage.xaml#MemberList#{Binding Note}"] = PrimaryAs("Note"),
        ["Pages/PackagesPage.xaml#PackageList#{Binding Name}"] = Primary,
        ["Pages/PackagesPage.xaml#PackageList#{Binding ProjectTitle}"] = Primary,
        ["Pages/PackagesPage.xaml#AttachedList#{Binding DisplayName}"] = Primary,
        ["Pages/PackagesPage.xaml#AttachedList#{Binding Detail}"] = Primary,
        ["Pages/PackagesPage.xaml#ProposedList#{Binding DisplayName}"] = Primary,
        ["Pages/PackagesPage.xaml#ProposedList#{Binding Detail}"] = Primary,
        ["Pages/PackagesPage.xaml#GapList#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PackagesPage.xaml#GapList#{Binding Label}"] = Primary,
        ["Pages/PeoplePage.xaml#PersonIntelligenceList#{Binding Title}"] = Primary,
        ["Pages/PeoplePage.xaml#PersonIntelligenceList#{Binding Kind}"] = Primary,
        ["Pages/PeoplePage.xaml#PersonIntelligenceList#{Binding Status}"] = Primary,
        ["Pages/PipelinePage.xaml#OpportunityList#{Binding Name}"] = Primary,
        ["Pages/PipelinePage.xaml#OpportunityList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PipelinePage.xaml#OpportunityList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PipelinePage.xaml#TargetList#{Binding DisplayName}"] = Primary,
        ["Pages/PipelinePage.xaml#TargetList#{Binding Converter={StaticResource TargetContact}}"] = Primary,
        ["Pages/PipelinePage.xaml#TargetList#{Binding Stage, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PipelinePage.xaml#SubmissionList#{Binding TargetDisplayName}"] = Primary,
        ["Pages/PipelinePage.xaml#SubmissionList#{Binding Subject}"] = Primary,
        ["Pages/PipelinePage.xaml#PitchList#{Binding TargetDisplayName}"] = Primary,
        ["Pages/PipelinePage.xaml#PitchList#{Binding Outcome, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PipelinePage.xaml#SubjectList#{Binding DisplayName}"] = Primary,
        ["Pages/PipelinePage.xaml#SubjectList#{Binding Detail}"] = Primary,
        ["Pages/PipelinePage.xaml#TaskList#{Binding Title}"] = Primary,
        ["Pages/PipelinePage.xaml#TaskList#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/PipelinePage.xaml#TaskList#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["Pages/PipelinePage.xaml#TaskList#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["Pages/PipelinePage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/PipelinePage.xaml#HistoryList#{Binding TargetDisplayName}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectList#{Binding Title}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectList#{Binding Stage, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectList#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#RoleList#{Binding Label}"] = Primary,
        ["Pages/ProjectsPage.xaml#RoleList#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#RoleList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#AttachmentList#{Binding DisplayName}"] = Primary,
        ["Pages/ProjectsPage.xaml#AttachmentList#{Binding RoleType}"] = Primary,
        ["Pages/ProjectsPage.xaml#AttachmentList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#CompanyList#{Binding CompanyName}"] = Primary,
        ["Pages/ProjectsPage.xaml#CompanyList#{Binding Capacity}"] = Primary,
        ["Pages/ProjectsPage.xaml#SourceList#{Binding Title}"] = Primary,
        ["Pages/ProjectsPage.xaml#SourceList#{Binding AttributedCreator}"] = Primary,
        ["Pages/ProjectsPage.xaml#MaterialList#{Binding Title}"] = Primary,
        ["Pages/ProjectsPage.xaml#MaterialList#{Binding PersonName}"] = Primary,
        ["Pages/ProjectsPage.xaml#PackageList#{Binding Name}"] = Primary,
        ["Pages/ProjectsPage.xaml#PackageList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#PursuitList#{Binding Name}"] = Primary,
        ["Pages/ProjectsPage.xaml#PursuitList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectDealList#{Binding Name}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectDealList#{Binding Converter={StaticResource Party}, ConverterParameter=CounterpartyDisplayName}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectDealList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectContractList#{Binding Title}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectContractList#{Binding Converter={StaticResource Party}, ConverterParameter=CounterpartyDisplayName}"] = Primary,
        ["Pages/ProjectsPage.xaml#ProjectContractList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/ProjectsPage.xaml#HistoryList#{Binding Summary}"] = Primary,
        ["Pages/ProjectsPage.xaml#HistoryList#{Binding Detail}"] = Secondary(Channel.HelpText, "Secondary context beneath a history entry identified by its summary, actor and kind."),
        ["Pages/ProspectsPage.xaml#ProspectList#{Binding DisplayName}"] = Primary,
        ["Pages/ProspectsPage.xaml#ProspectList#{Binding Converter={StaticResource Party}, ConverterParameter=OwnerDisplayName}"] = Primary,
        ["Pages/ProspectsPage.xaml#ProspectList#{Binding Stage, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/SavedViewsPage.xaml#ViewList#{Binding Name}"] = Primary,
        ["Pages/SavedViewsPage.xaml#ViewList#{Binding Target}"] = Primary,
        ["Pages/SavedViewsPage.xaml#ResultList#{Binding Title}"] = Primary,
        ["Pages/SavedViewsPage.xaml#ResultList#{Binding Subtitle}"] = Primary,
        ["Pages/SavedViewsPage.xaml#ResultList#{Binding Kind, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/SyncPage.xaml#QueueList#{Binding Description}"] = Primary,
        ["Pages/SyncPage.xaml#QueueList#{Binding Explanation}"] = Primary,
        ["Pages/SyncPage.xaml#QueueList#{Binding State, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/TalentPage.xaml#TalentList#{Binding DisplayName}"] = Primary,
        ["Pages/TalentPage.xaml#TalentList#{Binding CareerStage}"] = PrimaryAs("Career stage"),
        ["Pages/TalentPage.xaml#TalentList#{Binding RepresentationStatus}"] = PrimaryAs("Representation"),
        ["Pages/TalentPage.xaml#ScopeList#{Binding Area}"] = Primary,
        ["Pages/TalentPage.xaml#ScopeList#{Binding StartsOn, Converter={StaticResource IsoDate}}"] = Primary,
        ["Pages/TalentPage.xaml#TeamList#{Binding DisplayName}"] = Primary,
        ["Pages/TalentPage.xaml#TeamList#{Binding Role}"] = Primary,
        ["Pages/TalentPage.xaml#CreditList#{Binding Title}"] = Primary,
        ["Pages/TalentPage.xaml#CreditList#{Binding Role}"] = Primary,
        ["Pages/TalentPage.xaml#CreditList#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/TalentPage.xaml#CreditList#{Binding Year}"] = Primary,
        ["Pages/TalentPage.xaml#MaterialList#{Binding Title}"] = Primary,
        ["Pages/TalentPage.xaml#MaterialList#{Binding VersionLabel}"] = Primary,
        ["Pages/TalentPage.xaml#MaterialList#{Binding Type, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/TalentPage.xaml#MaterialList#{Binding Status, Converter={StaticResource DisplayLabel}}"] = Primary,
        ["Pages/TalentPage.xaml#HistoryList#{Binding OccurredOn}"] = Primary,
        ["Pages/TalentPage.xaml#HistoryList#{Binding Title}"] = Primary,
        ["Pages/TalentPage.xaml#HistoryList#{Binding Detail}"] = Secondary(Channel.HelpText, "Supporting detail beneath a history entry identified by its title and date."),
        ["Pages/TalentPage.xaml#TaskList#{Binding Title}"] = Primary,
        ["Pages/TalentPage.xaml#TaskList#{Binding Converter={StaticResource TaskWho}}"] = Primary,
        ["Pages/TalentPage.xaml#TaskList#{Binding Converter={StaticResource TaskWhen}}"] = Primary,
        ["Pages/TalentPage.xaml#TaskList#{Binding Priority, Converter={StaticResource DisplayLabel}}"] = Primary,
    };

    // --------------------------------------------------------------- ratchet

    /// <summary>
    /// The catalog names every list-item template in the client, and nothing else.
    /// </summary>
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

    /// <summary>
    /// Every template in the catalog is a list's item template.
    /// </summary>
    /// <remarks>
    /// Either declared inline as a <c>ListView.ItemTemplate</c>, or keyed in a
    /// resource dictionary and used — at least once, and only — as a
    /// <c>ListView</c>'s <c>ItemTemplate</c>. A template used any other way would
    /// not be a row, and comparing it with a row announcement would mean nothing.
    /// </remarks>
    [Fact]
    public void EveryCatalogTemplateIsAListItemTemplate()
    {
        Dictionary<string, List<string>> uses = KeyedTemplateUses();
        List<string> wrong = [];

        foreach (RowTemplate template in Templates())
        {
            XElement? holder = template.Declaration.Parent;

            if (holder?.Name.LocalName == "ListView.ItemTemplate")
            {
                continue;
            }

            string? key = template.Declaration.Attribute(Xaml + "Key")?.Value;

            if (key is not null
                && uses.TryGetValue(key, out List<string>? usage)
                && usage.Count > 0
                && usage.All(x => x == "ListView.ItemTemplate"))
            {
                continue;
            }

            wrong.Add(Key(template.File, template.Template));
        }

        Assert.True(wrong.Count == 0, "Templates that are not a list's item template: " + string.Join(", ", wrong));
    }

    /// <summary>Every row template announces through <see cref="RowLabel"/>.</summary>
    [Fact]
    public void EveryRowTemplateAnnouncesThroughRowLabel()
    {
        List<string> other = [.. Templates()
            .Where(x => !(x.Root.Attribute("AutomationProperties.Name")?.Value ?? string.Empty)
                .Contains("StaticResource RowLabel", StringComparison.Ordinal))
            .Select(x => Key(x.File, x.Template))];

        Assert.True(other.Count == 0, "Rows announcing some other way: " + string.Join(", ", other));
    }

    /// <summary>
    /// Every catalog entry names a type this suite builds — none is excused.
    /// </summary>
    [Fact]
    public void EveryCatalogEntryHasABuildableRowType()
    {
        foreach (Entry entry in Catalog)
        {
            Assert.True(
                entry.Row is not null ^ entry.ProductType is not null,
                $"{Key(entry.File, entry.Template)} must name exactly one row type.");

            Type type = RowType(entry);

            Assert.NotNull(Sentinels.Build(type));
        }
    }

    /// <summary>Every visible binding is classified, and every classification is of a binding.</summary>
    [Fact]
    public void EveryVisibleBindingIsAccountedFor()
    {
        List<string> inMarkup = [.. Catalog
            .SelectMany(x => Visible(x).Select(s => AccountingKey(x, s)))
            .Order()];

        List<string> unaccounted = [.. inMarkup.Except(Accounting.Keys)];
        List<string> stale = [.. Accounting.Keys.Except(inMarkup)];

        Assert.True(
            unaccounted.Count == 0 && stale.Count == 0,
            "Visible bindings with no classification: " + string.Join(", ", unaccounted)
                + " | Classifications with no binding: " + string.Join(", ", stale));
    }

    /// <summary>
    /// Every role word the accounting supplies is one the page itself shows.
    /// </summary>
    /// <remarks>
    /// A role word is taken from the product, never invented: each must be the literal
    /// text of a visible label in the markup file that holds the row, or the approved
    /// concise word the row's own profile declares for that field.
    /// </remarks>
    [Fact]
    public void EveryAccountedRoleWordIsVisibleOnItsPage()
    {
        foreach ((string key, Classification value) in Accounting.Where(x => x.Value.Role is not null))
        {
            string file = key.Split('#')[0];
            string markup = PageText(file);
            Entry entry = Find(file, key.Split('#')[1]);
            Shown shown = Parse(key.Split('#', 3)[2]);

            bool visible = markup.Contains($"Text=\"{value.Role}\"", StringComparison.Ordinal);
            bool declared = ProfileOf(entry) is { } profile
                && FieldFor(profile, shown) is { } field
                && string.Equals(field.Role, value.Role, StringComparison.Ordinal);

            Assert.True(
                visible || declared,
                $"{key}: the role word '{value.Role}' is neither a visible label in {file} nor its profile's word.");
        }
    }

    /// <summary>The whole markup file that holds a row.</summary>
    internal static string PageText(string file) =>
        System.IO.File.ReadAllText(Path.Combine(WindowsRoot, file));

    /// <summary>Every secondary value says where it goes and why it is not a scan fact.</summary>
    [Fact]
    public void EverySecondaryValueNamesAChannelAndAReason()
    {
        foreach ((string key, Classification value) in Accounting)
        {
            if (value.Weight == Weight.Primary)
            {
                Assert.Equal(Channel.None, value.Channel);
                continue;
            }

            Assert.NotEqual(Channel.None, value.Channel);
            Assert.True(value.Reason.Length >= 40, $"{key}: the reason is too thin to disagree with.");
        }
    }

    /// <summary>Every visible binding resolves on the row type.</summary>
    /// <remarks>
    /// What stops a wrong catalog type passing vacuously: a binding the type cannot
    /// satisfy is a type the row is not bound to.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryVisibleBindingResolvesOnTheRowType(string file, string template)
    {
        Entry entry = Find(file, template);
        Type type = RowType(entry);

        foreach (Shown shown in Visible(entry))
        {
            if (shown.Path is null)
            {
                continue;
            }

            Assert.True(
                Property(type, shown.Path) is not null,
                $"{Key(file, template)}: '{shown.Path}' is not a property of {type.Name}.");
        }
    }

    /// <summary>Every converter a row uses is one this gate can evaluate.</summary>
    [Fact]
    public void EveryConverterARowUsesIsEvaluable()
    {
        List<string> unknown = [.. Catalog
            .SelectMany(Visible)
            .Select(x => x.Converter)
            .OfType<string>()
            .Distinct()
            .Where(x => !Evaluators.Contains(x))];

        Assert.True(unknown.Count == 0, "Converters with no evaluation here: " + string.Join(", ", unknown));
    }

    // -------------------------------------------------------------- property

    /// <summary>Every primary value a row shows is in its accessible name, by role and value.</summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryPrimaryValueIsAnnounced(string file, string template)
    {
        Entry entry = Find(file, template);
        object row = Sentinels.Build(RowType(entry));
        string announced = Announce(entry, row);
        List<string> missing = [];

        List<Shown> primary = [.. Primaries(entry)];

        foreach (Shown value in primary)
        {
            if (Coverage.Check(row, value, primary, announced) is { } reason)
            {
                missing.Add(reason);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{Key(file, template)} [{row.GetType().Name}] announces '{announced}' and does not cover: "
                + string.Join("; ", missing));
    }

    /// <summary>What a row announces: <see cref="RowLabel"/>, under its template's profile.</summary>
    internal static string Announce(Entry entry, object row) =>
        RowLabel.For(row, ProfileId(Template(entry)));

    /// <summary>A row's primary bindings, each with the role its accounting gives it.</summary>
    internal static IEnumerable<Shown> Primaries(Entry entry) =>
        Visible(entry)
            .Where(x => Accounting[AccountingKey(entry, x)].Weight == Weight.Primary)
            .Select(x => x with
            {
                Role = Accounting[AccountingKey(entry, x)].Role,
                RoleFrom = Accounting[AccountingKey(entry, x)].RoleFrom,
            });

    /// <summary>The profile id a template passes to the row converter, if any.</summary>
    private static string? ProfileId(RowTemplate template) =>
        template.Root.Attribute("AutomationProperties.Name")?.Value is { } name
            ? Parse(name).Parameter
            : null;

    /// <summary>The profile a catalog entry's template names, if any.</summary>
    internal static RowProfile? ProfileOf(Entry entry) => RowProfiles.Find(ProfileId(Template(entry)));

    /// <summary>The profile field that speaks a visible binding, if any.</summary>
    internal static RowField? FieldFor(RowProfile profile, Shown shown) =>
        profile.Fields.FirstOrDefault(x => x.Kind == RowFieldKind.Party
            ? shown.Converter == "Party" && shown.Parameter == x.Path
            : shown.Path == x.Path);

    /// <summary>
    /// Every secondary value is wired to the channel its classification names.
    /// </summary>
    /// <remarks>
    /// Passing here is structural only. A wired secondary value is still
    /// <em>pending live proof</em> until the release-candidate client run shows an
    /// operator finding and reading it from the same row.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Rows))]
    public void EverySecondaryValueIsWiredToItsChannel(string file, string template)
    {
        Entry entry = Find(file, template);
        RowTemplate markup = Template(entry);
        object row = Sentinels.Build(RowType(entry));

        List<string> unwired = [];

        foreach (Shown value in Visible(entry))
        {
            Classification classification = Accounting[AccountingKey(entry, value)];

            if (classification.Weight == Weight.Primary)
            {
                continue;
            }

            string text = Render(row, value);

            if (!Wired(markup, value, classification.Channel, row, text))
            {
                unwired.Add($"{value.Binding} (shows '{text}') is not wired to {classification.Channel}");
            }
        }

        Assert.True(unwired.Count == 0, $"{Key(file, template)}: " + string.Join("; ", unwired));
    }

    /// <summary>
    /// Whether a secondary value reaches the channel its classification names.
    /// </summary>
    private static bool Wired(RowTemplate markup, Shown value, Channel channel, object row, string text)
    {
        switch (channel)
        {
            case Channel.Descendant:
                // The value's own element sits inside a focusable child that carries
                // an accessible name of its own.
                return markup.Root
                    .Descendants()
                    .Where(x => x.Attribute("AutomationProperties.Name") is not null
                        && x.Name.LocalName is "Expander" or "Button" or "HyperlinkButton")
                    .Any(x => x.Descendants().Any(d => IsBinding(d, value)));

            case Channel.HelpText:
                // The row's own help text, evaluated the way the row would evaluate
                // it: bound to the value directly, or through a converter this gate
                // can run.
                return HelpText(markup, row) is { } help && help.Contains(text, StringComparison.Ordinal);

            default:
                return false;
        }
    }

    /// <summary>What a row's own help text says, or null where it binds none.</summary>
    private static string? HelpText(RowTemplate markup, object row)
    {
        if (markup.Root.Attribute("AutomationProperties.HelpText")?.Value is not { } help
            || !help.StartsWith("{Binding", StringComparison.Ordinal))
        {
            return null;
        }

        Shown channel = Parse(help);

        return channel.Converter is null || Evaluators.Contains(channel.Converter)
            ? Render(row, channel)
            : null;
    }

    private static bool IsBinding(XElement element, Shown value) =>
        (string[])["Text", "Content"] is var names
        && names.Any(x => element.Attribute(x)?.Value == value.Binding);

    // ------------------------------------------------- the comparator, tested

    private sealed record TwoParties(string PayerDisplayName, string DebtorDisplayName);

    private sealed record TwoDates(DateOnly DueOn, DateOnly SignedOn);

    private sealed record Priced(MoneyResponse Amount);

    private sealed record Ledger(string Currency, MoneyResponse Debits);

    private sealed record Unstated(MoneyResponse Debits);

    private sealed record Flags(bool IsSelf, bool IsOnlyOwner);

    private static readonly DateOnly First = new(2031, 1, 1);

    private static readonly DateOnly Second = new(2031, 2, 1);

    private static string? Check(object row, string binding, string announced, params string[] rowBindings) =>
        Coverage.Check(
            row,
            Parse(binding),
            [.. (rowBindings.Length == 0 ? [binding] : rowBindings).Select(Parse)],
            announced);

    /// <summary>A name said under the wrong role does not cover the right one.</summary>
    [Fact]
    public void TheComparatorRefusesTheRightNameUnderTheWrongRole()
    {
        TwoParties row = new("Ana Reyes", "Ben Okafor");
        const string payer = "{Binding Converter={StaticResource Party}, ConverterParameter=PayerDisplayName}";

        Assert.Null(Check(row, payer, "INV-1, Payer: Ana Reyes"));
        Assert.NotNull(Check(row, payer, "INV-1, Debtor: Ana Reyes"));
    }

    /// <summary>Swapping two dates between their roles fails both.</summary>
    [Fact]
    public void TheComparatorFailsTwoDatesSwappedBetweenRoles()
    {
        TwoDates row = new(First, Second);
        string[] both = ["{Binding DueOn}", "{Binding SignedOn}"];

        Assert.Null(Check(row, both[0], "Party, Due 2031-01-01, Signed 2031-02-01", both));
        Assert.Null(Check(row, both[1], "Party, Due 2031-01-01, Signed 2031-02-01", both));

        Assert.NotNull(Check(row, both[0], "Party, Due 2031-02-01, Signed 2031-01-01", both));
        Assert.NotNull(Check(row, both[1], "Party, Due 2031-02-01, Signed 2031-01-01", both));
    }

    /// <summary>The same date under the wrong role does not cover a row showing two dates.</summary>
    [Fact]
    public void TheComparatorRefusesTheSameDateUnderTheWrongRole()
    {
        TwoDates row = new(First, First);
        string[] both = ["{Binding DueOn}", "{Binding SignedOn}"];

        Assert.Null(Check(row, both[0], "Party, Due 2031-01-01", both));
        Assert.NotNull(Check(row, both[1], "Party, Due 2031-01-01", both));
    }

    /// <summary>A date is the same date in another of the product's formats.</summary>
    [Fact]
    public void TheComparatorAcceptsADateInAnotherFormat()
    {
        TwoDates row = new(First, Second);
        string local = First.ToString("d", CultureInfo.CurrentCulture);

        Assert.Null(Check(row, "{Binding DueOn}", "Party, Due " + local));
        Assert.Null(Check(row, "{Binding DueOn, Converter={StaticResource IsoDate}}", "Party, Due " + local));
    }

    /// <summary>The same amount in another currency is not the same money.</summary>
    [Fact]
    public void TheComparatorRefusesTheSameAmountInAnotherCurrency()
    {
        Priced row = new(new MoneyResponse(100m, "USD"));
        const string money = "{Binding Amount, Converter={StaticResource Money}}";

        Assert.Null(Check(row, money, "Fee, 100.00 USD"));

        // Followed by the separator the row uses — the first run read the comma as
        // part of the currency and reported covered money as missing.
        Assert.Null(Check(row, money, "Fee, 100.00 USD, Open"));

        Assert.NotNull(Check(row, money, "Fee, 100.00 GBP"));
        Assert.NotNull(Check(row, money, "Fee, 101.00 USD"));
    }

    /// <summary>
    /// A bare amount beside the row's own Currency column is covered by that amount in
    /// that currency — the ledger row's case.
    /// </summary>
    [Fact]
    public void TheComparatorAcceptsABareAmountInTheRowsStatedCurrency()
    {
        Ledger row = new("USD", new MoneyResponse(70m, "USD"));
        string[] shown = ["{Binding Currency}", "{Binding Debits.Amount}"];

        Assert.Null(Check(row, shown[1], "Cash, 70.00 USD debits", shown));
        Assert.NotNull(Check(row, shown[1], "Cash, 70.00 GBP debits", shown));
    }

    /// <summary>A bare amount with no currency anywhere on the row is not covered.</summary>
    [Fact]
    public void TheComparatorRefusesABareAmountWhoseCurrencyIsNotOnTheRow()
    {
        Unstated row = new(new MoneyResponse(70m, "USD"));

        Assert.NotNull(Check(row, "{Binding Debits.Amount}", "Cash, 70.00 USD debits"));
    }

    /// <summary>A flag is covered by its own role, not by another flag's.</summary>
    [Fact]
    public void TheComparatorCoversAFlagOnlyByItsRole()
    {
        Flags row = new(true, true);
        string[] both = ["{Binding IsSelf}", "{Binding IsOnlyOwner}"];

        Assert.Null(Check(row, both[0], "Ana Reyes, Is self", both));
        Assert.NotNull(Check(row, both[1], "Ana Reyes, Is self", both));
    }

    /// <summary>A word inside a longer word does not count as said.</summary>
    [Fact]
    public void TheComparatorRequiresWholeWords()
    {
        TwoParties row = new("zqab", "zqac");

        Assert.NotNull(Check(row, "{Binding PayerDisplayName}", "zqabc"));
        Assert.Null(Check(row, "{Binding PayerDisplayName}", "zqab, zqac"));
    }

    private sealed record Filing(string Name, string Claimant, string Respondent);

    private sealed record Grant(string Name, string Medium, string Territory);

    private sealed record Brief(string Name, string Claimant);

    private sealed record Stamped(DateTime RecordedAt);

    private sealed record Offset(DateTimeOffset RecordedAt);

    /// <summary>Two texts in their right roles are covered.</summary>
    [Fact]
    public void TheComparatorAcceptsTwoTextsInTheirRoles()
    {
        Filing row = new("Case 12", "Ana Reyes", "Ben Okafor");
        string[] shown = ["{Binding Name}", "{Binding Claimant}", "{Binding Respondent}"];
        const string said = "Case 12, Claimant Ana Reyes, Respondent Ben Okafor";

        Assert.Null(Check(row, shown[1], said, shown));
        Assert.Null(Check(row, shown[2], said, shown));
    }

    /// <summary>The same two texts swapped between their roles fail both.</summary>
    [Fact]
    public void TheComparatorFailsTwoTextsSwappedBetweenRoles()
    {
        Filing row = new("Case 12", "Ana Reyes", "Ben Okafor");
        string[] shown = ["{Binding Name}", "{Binding Claimant}", "{Binding Respondent}"];

        Assert.NotNull(Check(row, shown[1], "Case 12, Claimant Ben Okafor, Respondent Ana Reyes", shown));
        Assert.NotNull(Check(row, shown[2], "Case 12, Claimant Ben Okafor, Respondent Ana Reyes", shown));

        // Both values present, neither attributed: value presence alone is not enough.
        Assert.NotNull(Check(row, shown[1], "Case 12, Ana Reyes, Ben Okafor", shown));
    }

    /// <summary>Two domain tokens swapped between their roles fail.</summary>
    [Fact]
    public void TheComparatorFailsTwoTokensSwappedBetweenRoles()
    {
        Grant row = new("Rights", "Film", "Europe");
        string[] shown =
        [
            "{Binding Name}",
            "{Binding Medium, Converter={StaticResource DisplayLabel}}",
            "{Binding Territory, Converter={StaticResource DisplayLabel}}",
        ];

        Assert.Null(Check(row, shown[1], "Rights, Medium Film, Territory Europe", shown));
        Assert.NotNull(Check(row, shown[1], "Rights, Medium Europe, Territory Film", shown));
        Assert.NotNull(Check(row, shown[2], "Rights, Medium Europe, Territory Film", shown));
    }

    private sealed record Priced2(MoneyResponse Amount, string Name, string DisplayValue, string ClauseReference);

    /// <summary>A term's stated value is the value of its headline and needs no label.</summary>
    [Fact]
    public void TheComparatorDoesNotAskAStatedValueForARoleLabel()
    {
        Priced2 row = new(new MoneyResponse(1m, "USD"), "Fee", "185,000.00 USD", "7.2");
        string[] shown = ["{Binding Name}", "{Binding ClauseReference}", "{Binding DisplayValue}"];

        Assert.Null(Check(row, shown[2], "Fee, 7.2, 185,000.00 USD", shown));
    }

    /// <summary>A role word the row's own header gives is the one required.</summary>
    [Fact]
    public void TheComparatorUsesTheRowsOwnRoleWord()
    {
        Filing row = new("Case 12", "Ana Reyes", "Ben Okafor");
        Shown[] shown =
        [
            Parse("{Binding Name}"),
            Parse("{Binding Claimant}") with { Role = "Agreed" },
            Parse("{Binding Respondent}") with { Role = "In the draft" },
        ];

        Assert.Null(Coverage.Check(row, shown[1], shown, "Case 12, Agreed Ana Reyes, In the draft Ben Okafor"));
        Assert.NotNull(Coverage.Check(row, shown[1], shown, "Case 12, Claimant Ana Reyes, Respondent Ben Okafor"));
    }

    /// <summary>A headline and one other text need no role label.</summary>
    [Fact]
    public void TheComparatorDoesNotAskAHeadlineForARoleLabel()
    {
        Brief row = new("Case 12", "Ana Reyes");
        string[] shown = ["{Binding Name}", "{Binding Claimant}"];

        Assert.Null(Check(row, shown[0], "Case 12, Ana Reyes", shown));
        Assert.Null(Check(row, shown[1], "Case 12, Ana Reyes", shown));
    }

    /// <summary>A raw instant on the same day at another time is not the same value.</summary>
    [Fact]
    public void TheComparatorRefusesTheSameDayAtAnotherTime()
    {
        Stamped row = new(new DateTime(2032, 1, 4, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.NotNull(Check(row, "{Binding RecordedAt}", "Note, 2032-01-04 13:00:00"));
        Assert.NotNull(Check(row, "{Binding RecordedAt}", "Note, 2032-01-04"));
    }

    /// <summary>The same date and time written another way is the same value.</summary>
    [Fact]
    public void TheComparatorAcceptsTheSameInstantInAnotherFormat()
    {
        DateTime moment = new(2032, 1, 4, 12, 30, 15, DateTimeKind.Unspecified);
        Stamped row = new(moment);

        Assert.Null(Check(row, "{Binding RecordedAt}", "Note, 2032-01-04 12:30:15"));
        Assert.Null(Check(row, "{Binding RecordedAt}", "Note, " + moment.ToString("G", CultureInfo.CurrentCulture)));
    }

    /// <summary>
    /// An instant with an offset is refused under an incompatible offset, and without one.
    /// </summary>
    [Fact]
    public void TheComparatorRefusesAnInstantUnderAnotherOffset()
    {
        Offset row = new(new DateTimeOffset(2032, 1, 4, 12, 0, 0, TimeSpan.Zero));
        const string shown = "{Binding RecordedAt}";

        Assert.Null(Check(row, shown, "Note, 2032-01-04 12:00:00 +00:00"));

        // The same moment written in another offset is the same instant.
        Assert.Null(Check(row, shown, "Note, 2032-01-04 14:00:00 +02:00"));

        // The same wall-clock time in another offset is a different instant.
        Assert.NotNull(Check(row, shown, "Note, 2032-01-04 12:00:00 +02:00"));

        // The row states an offset; an announcement without one drops it.
        Assert.NotNull(Check(row, shown, "Note, 2032-01-04 12:00:00"));
    }

    /// <summary>A date shows no time, and acquires no time requirement.</summary>
    [Fact]
    public void TheComparatorAsksNoTimeOfADate()
    {
        TwoDates row = new(First, Second);

        Assert.Null(Check(row, "{Binding DueOn}", "Party, Due 2031-01-01"));
        Assert.Null(Check(row, "{Binding DueOn, Converter={StaticResource IsoDate}}", "Party, Due 2031-01-01"));
    }

    /// <summary>Two builds of one type are the same row, down to its identifiers.</summary>
    [Fact]
    public void SentinelsAreDeterministic()
    {
        foreach (Entry entry in Catalog)
        {
            object first = Sentinels.Build(RowType(entry));
            object second = Sentinels.Build(RowType(entry));

            Assert.Equal(RowLabel.For(first), RowLabel.For(second));

            foreach (PropertyInfo property in first.GetType().GetProperties()
                .Where(x => x.PropertyType == typeof(Guid) && x.GetIndexParameters().Length == 0))
            {
                Assert.Equal(property.GetValue(first), property.GetValue(second));
            }
        }
    }

    /// <summary>No two text, number, date or identifier fields of one row share a value.</summary>
    [Fact]
    public void SentinelsAreDistinctWithinEveryRow()
    {
        foreach (Entry entry in Catalog)
        {
            object row = Sentinels.Build(RowType(entry));

            List<object> values = [.. row.GetType().GetProperties()
                .Where(x => x.GetIndexParameters().Length == 0
                    && (x.PropertyType == typeof(string) || x.PropertyType == typeof(Guid)
                        || x.PropertyType == typeof(decimal) || x.PropertyType == typeof(int)
                        || x.PropertyType == typeof(DateOnly) || x.PropertyType == typeof(DateTimeOffset)))
                .Select(x => x.GetValue(row))
                .OfType<object>()
                .Where(x => x is not string text || text.Length > 0)];

            Assert.True(
                values.Count == values.Distinct().Count(),
                $"{Key(entry.File, entry.Template)} has two fields holding one value.");
        }
    }

    // ------------------------------------------------------------ comparator

    /// <summary>
    /// The semantic comparison of what a row shows with what it announces.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Text</b> is covered when the announcement contains it as a whole word
    /// sequence — with its role word beside it when another text on the row could
    /// answer for it.</item>
    /// <item><b>Token</b> — a domain value shown through <c>DisplayLabel</c> — is
    /// covered by the same words.</item>
    /// <item><b>Number</b> is covered by the same number, in either the invariant
    /// or the current culture.</item>
    /// <item><b>Flag</b> is covered by its role word, because a true flag has no
    /// value of its own to say; the row announces a set flag by name.</item>
    /// <item><b>Date</b> — a <see cref="DateOnly"/>, or any value shown through the
    /// date converter — is covered by the same calendar date in any of the product's
    /// formats. It shows no time, and none is required.</item>
    /// <item><b>Instant</b> — a raw <see cref="DateTime"/> or <see cref="DateTimeOffset"/>
    /// — is covered only by the same date and time to the second, and for an offset
    /// value by the same instant written with an offset: everything the row shows.</item>
    /// <item><b>Money</b> is covered by the same amount in the same currency,
    /// whatever the number format.</item>
    /// <item><b>Bare amount</b> — a money amount shown without its currency — is
    /// covered only when the row visibly states that money's currency elsewhere
    /// (the ledger row's single Currency column) and the announcement says the
    /// amount in it. Where no currency is visible, it is not covered: the reader
    /// could not have known it either.</item>
    /// <item><b>Party</b> is covered only by the role word and the name together,
    /// as <see cref="PartyLine"/> writes them.</item>
    /// <item><b>Composed</b> converter output is covered part by part.</item>
    /// </list>
    /// <para>
    /// Role identity: where a row shows more than one number, date or flag, or more
    /// than one text or token that is neither the row's headline nor one of the
    /// product's qualifiers, each must be announced with its role, or one could answer
    /// for another.
    /// </para>
    /// </remarks>
    internal static class Coverage
    {
        /// <summary>Null when covered; otherwise why not.</summary>
        public static string? Check(object row, Shown value, IReadOnlyList<Shown> rowBindings, string announced)
        {
            // A binding that is another's role is covered only as that role, beside
            // the value it names: the kind of a link is said as "Deal: Autumn slate",
            // not on its own.
            if (rowBindings.FirstOrDefault(x => x.RoleFrom is not null && x.RoleFrom == value.Path) is { } dependent)
            {
                return Check(row, dependent, rowBindings, announced) is null
                    ? null
                    : $"{value.Binding} [role of {dependent.Binding}] shows '{Render(row, value)}'";
            }

            string text = Render(row, value);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            object? raw = value.Path is null ? row : Read(row, value.Path);
            Kind kind = KindOf(value, raw, row);
            bool needsRole = value.RoleFrom is not null || NeedsRole(row, value, kind, rowBindings);

            bool covered = kind switch
            {
                Kind.Text or Kind.Token => needsRole
                    ? Segments(announced).Any(s => ContainsWords(s, text) && ContainsWords(s, RoleWord(value, row)))
                    : ContainsWords(announced, text),
                Kind.Number => Segments(announced).Any(s =>
                    Numbers(raw).Any(n => ContainsWords(s, n))
                    && (!needsRole || ContainsWords(s, RoleWord(value, row)))),
                Kind.Flag => ContainsWords(announced, RoleWord(value, row)),
                Kind.Date => Segments(announced).Any(s =>
                    Dates(raw).Any(d => s.Contains(d, StringComparison.OrdinalIgnoreCase))
                    && (!needsRole || ContainsWords(s, RoleWord(value, row)))),
                Kind.Instant => Segments(announced).Any(s =>
                    SameInstant(raw, s)
                    && (!needsRole || ContainsWords(s, RoleWord(value, row)))),
                Kind.Money => raw is MoneyResponse money && HasMoney(announced, money.Amount, money.Currency),
                Kind.BareAmount => BareAmountCovered(row, value, rowBindings, announced),
                Kind.Party or Kind.Composed => text
                    .Split(" \u00B7 ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .All(part => announced.Contains(part, StringComparison.Ordinal)),
                _ => false,
            };

            return covered ? null : $"{value.Binding} [{kind}] shows '{text}'";
        }

        public enum Kind
        {
            Text,
            Token,
            Number,
            Flag,
            Date,
            Instant,
            Money,
            BareAmount,
            Party,
            Composed,
        }

        public static Kind KindOf(Shown value, object? raw, object row) => value.Converter switch
        {
            "Money" => Kind.Money,
            "IsoDate" => Kind.Date,
            "Party" => Kind.Party,
            "DisplayLabel" => Kind.Token,
            "TaskWho" or "TaskWhen" or "TaskAbout" or "TargetContact"
                or "PredictionCaption" or "PredictionDue" => Kind.Composed,
            _ => raw switch
            {
                DateOnly => Kind.Date,
                DateTimeOffset or DateTime => Kind.Instant,
                bool => Kind.Flag,
                decimal when IsMoneyAmount(row, value) => Kind.BareAmount,
                int or long or short or decimal or double => Kind.Number,
                _ => Kind.Text,
            },
        };

        /// <summary>
        /// Whether a value must carry its role word because another could answer for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Numbers, dates and flags: whenever the row shows more than one of the kind,
        /// dates and instants counting together.
        /// </para>
        /// <para>
        /// Text and domain tokens: whenever the row shows two or more that could
        /// answer for each other. A field is exempt when its value already says what it
        /// is, which the product has settled in <see cref="RowLabel"/>'s own
        /// vocabularies: the headline it names the row by, its qualifiers (status,
        /// stage, kind and the like), the related record it uses as context, and a
        /// term's stated value. Those are read from <see cref="RowLabel"/> by
        /// reflection, so the gate cannot drift from the product's own choice of them,
        /// and are judged on the row's own field.
        /// </para>
        /// </remarks>
        public static bool NeedsRole(object row, Shown value, Kind kind, IReadOnlyList<Shown> rowBindings)
        {
            Kind KindOfBinding(Shown x) => KindOf(x, x.Path is null ? row : Read(row, x.Path), row);

            if (kind is Kind.Number or Kind.Flag)
            {
                return rowBindings.Count(x => KindOfBinding(x) == kind) > 1;
            }

            if (kind is Kind.Date or Kind.Instant)
            {
                return rowBindings.Count(x => KindOfBinding(x) is Kind.Date or Kind.Instant) > 1;
            }

            if (kind is Kind.Text or Kind.Token)
            {
                return RoleAmbiguous(row, value, kind)
                    && rowBindings.Count(x => KindOfBinding(x) is var k
                        && k is Kind.Text or Kind.Token
                        && RoleAmbiguous(row, x, k)) > 1;
            }

            return false;
        }

        private static bool RoleAmbiguous(object row, Shown value, Kind kind)
        {
            if (value.Path is not { } path)
            {
                return false;
            }

            // Judged on the row's own field: Previous.DisplayValue is the row's
            // Previous, not a stated value.
            string field = path.Split('.')[0];

            return !string.Equals(field, Headline(row), StringComparison.Ordinal)
                && !Settled.Any(name => Vocabulary(name).Contains(field, StringComparer.Ordinal));
        }

        /// <summary>
        /// <see cref="RowLabel"/>'s vocabularies whose values it announces bare, by settled
        /// convention: what a row is called, where it stands, the related record that
        /// tells it apart, and the value a term states.
        /// </summary>
        private static readonly string[] Settled = ["Headline", "Qualifiers", "Context", "Stated"];

        /// <summary>The field <see cref="RowLabel"/> names this row by, if any.</summary>
        private static string? Headline(object row)
        {
            foreach (string name in Vocabulary("Headline"))
            {
                if (row.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } property
                    && property.GetValue(row) is string { Length: > 0 })
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>One of <see cref="RowLabel"/>'s own vocabularies, read rather than copied.</summary>
        private static string[] Vocabulary(string name) =>
            typeof(RowLabel).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string[]
                ?? throw new InvalidOperationException($"RowLabel has no vocabulary called {name}.");

        /// <summary>
        /// Whether an announced segment states the same instant the row shows.
        /// </summary>
        /// <remarks>
        /// A raw instant is shown with its date, its time to the second and — for a
        /// <see cref="DateTimeOffset"/> — its offset. The announcement may write it
        /// another way, but it may not drop what the row shows: a different time, a
        /// time missing, or an instant that lacks the offset the row states is not the
        /// same value. It is compared as an instant, so the same moment written in
        /// another offset is the same value; the same wall-clock time in a different
        /// offset is not.
        /// </remarks>
        public static bool SameInstant(object? raw, string segment)
        {
            Match span = Regex.Match(segment, @"\d.*$");

            if (!span.Success || !span.Value.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            string text = span.Value.Trim();
            bool offset = Regex.IsMatch(text, @"(?:[+-]\d{2}:?\d{2}|Z)$");

            foreach (CultureInfo culture in (CultureInfo[])[CultureInfo.CurrentCulture, CultureInfo.InvariantCulture])
            {
                if (!DateTimeOffset.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset said))
                {
                    continue;
                }

                switch (raw)
                {
                    case DateTimeOffset moment when offset
                        && Seconds(said.UtcDateTime) == Seconds(moment.UtcDateTime):
                        return true;

                    case DateTime moment when Seconds(said.DateTime) == Seconds(moment):
                        return true;
                }
            }

            return false;
        }

        private static long Seconds(DateTime moment) => moment.Ticks / TimeSpan.TicksPerSecond;

        /// <summary>Whether a bound decimal is the <c>Amount</c> of a <see cref="MoneyResponse"/>.</summary>
        private static bool IsMoneyAmount(object row, Shown value) =>
            value.Path is { } path
            && path.EndsWith(".Amount", StringComparison.Ordinal)
            && Read(row, path[..^".Amount".Length]) is MoneyResponse;

        private static bool BareAmountCovered(
            object row, Shown value, IReadOnlyList<Shown> rowBindings, string announced)
        {
            if (Read(row, value.Path![..^".Amount".Length]) is not MoneyResponse money)
            {
                return false;
            }

            // The currency must be on screen, in this row, as a value of its own.
            bool stated = rowBindings.Any(x =>
                x.Converter is null
                && x.Path is { } path
                && path.Split('.')[^1] == "Currency"
                && Read(row, path) is string currency
                && string.Equals(currency, money.Currency, StringComparison.Ordinal));

            return stated && HasMoney(announced, money.Amount, money.Currency);
        }

        /// <summary>Whether the announcement states this amount in this currency.</summary>
        public static bool HasMoney(string announced, decimal amount, string currency)
        {
            foreach (Match match in MoneyShape().Matches(announced))
            {
                if (!string.Equals(match.Groups["currency"].Value, currency, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (CultureInfo culture in (CultureInfo[])[CultureInfo.InvariantCulture, CultureInfo.CurrentCulture])
                {
                    if (decimal.TryParse(match.Groups["amount"].Value, NumberStyles.Number, culture, out decimal said)
                        && said == amount)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The ways the product writes one calendar date.</summary>
        public static IEnumerable<string> Dates(object? raw)
        {
            List<DateOnly> days = raw switch
            {
                DateOnly date => [date],
                _ => [],
            };

            foreach (DateOnly day in days.Distinct())
            {
                yield return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                yield return day.ToString("d", CultureInfo.CurrentCulture);
                yield return day.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
                yield return day.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            }
        }

        private static IEnumerable<string> Numbers(object? raw)
        {
            if (raw is IFormattable number)
            {
                yield return number.ToString(null, CultureInfo.InvariantCulture);
                yield return number.ToString(null, CultureInfo.CurrentCulture);
                yield return number.ToString("N0", CultureInfo.CurrentCulture);
            }
        }

        /// <summary>The words a row uses for a field's role.</summary>
        /// <remarks>
        /// The accounting's word where it gives one; the value of the binding it names
        /// as the role, as <see cref="DisplayLabel"/> writes it, where the role is
        /// another field; otherwise the field's own name, as <see cref="DisplayLabel"/>
        /// writes it, without the "on" or "at" that only says it is a date — which is
        /// how the row already names a set flag.
        /// </remarks>
        public static string RoleWord(Shown value, object row)
        {
            if (value.Role is { Length: > 0 } role)
            {
                return role;
            }

            if (value.RoleFrom is { } source)
            {
                return DisplayLabel.For(Convert.ToString(Read(row, source), CultureInfo.CurrentCulture));
            }

            string leaf = (value.Path ?? string.Empty).Split('.')[^1];
            string words = DisplayLabel.For(leaf);

            foreach (string suffix in (string[])[" on", " at"])
            {
                if (words.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && words.Length > suffix.Length)
                {
                    words = words[..^suffix.Length];
                }
            }

            return words;
        }

        public static bool ContainsWords(string haystack, string needle) =>
            needle.Length > 0
            && Regex.IsMatch(
                haystack,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(needle.Trim()) + @"(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The row's announcement split at the separator <see cref="RowLabel"/> uses.</summary>
        private static string[] Segments(string announced) => announced.Split(", ");
    }

    [GeneratedRegex(@"(?<amount>-?[\d.,\u00A0\u202F]+\d)\s(?<currency>[^\s,]+)")]
    private static partial Regex MoneyShapeImpl();

    private static Regex MoneyShape() => MoneyShapeImpl();

    // ------------------------------------------------------------ evaluation

    /// <summary>The converters a row may name, each evaluated by the function it calls.</summary>
    private static readonly HashSet<string> Evaluators =
    [
        "DisplayLabel", "IsoDate", "Money", "TaskWho", "TaskWhen", "TaskAbout",
        "TargetContact", "Party", "PredictionCaption", "PredictionDue",
    ];

    /// <summary>What the operator sees for one binding, the way the row computes it.</summary>
    /// <remarks>
    /// Each converter is evaluated by the client function its WinUI class delegates
    /// to; no formatting is copied here. A binding with no converter shows the
    /// value's own text, as the framework does.
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
    /// <param name="Role">The row's own word for this value's role, from the accounting.</param>
    /// <param name="RoleFrom">The binding whose value is this one's role, from the accounting.</param>
    public sealed record Shown(
        string Binding, string? Path, string? Converter, string? Parameter, string? Role = null, string? RoleFrom = null);

    private sealed record RowTemplate(string File, string Template, XElement Declaration, XElement Root);

    private static string WindowsRoot => Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows");

    private static IEnumerable<(string Relative, XElement Document)> Markup()
    {
        foreach (string path in Directory.EnumerateFiles(WindowsRoot, "*.xaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(WindowsRoot, path).Replace('\\', '/');

            if (relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative.StartsWith("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (relative, XElement.Load(path));
        }
    }

    private static IEnumerable<RowTemplate> Templates()
    {
        foreach ((string relative, XElement document) in Markup())
        {
            foreach (XElement template in document
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

                yield return new RowTemplate(relative, identity, template, first);
            }
        }
    }

    /// <summary>Every use of a keyed template, as the attribute that uses it.</summary>
    private static Dictionary<string, List<string>> KeyedTemplateUses()
    {
        Dictionary<string, List<string>> uses = new(StringComparer.Ordinal);

        foreach ((_, XElement document) in Markup())
        {
            foreach (XElement element in document.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    Match match = Regex.Match(attribute.Value, @"^\{StaticResource\s+(\w+)\}$");

                    if (!match.Success || !attribute.Name.LocalName.EndsWith("Template", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string key = match.Groups[1].Value;

                    if (!uses.TryGetValue(key, out List<string>? list))
                    {
                        uses[key] = list = [];
                    }

                    list.Add(element.Name.LocalName + "." + attribute.Name.LocalName);
                }
            }
        }

        return uses;
    }

    private static RowTemplate Template(Entry entry) =>
        Templates().Single(x => x.File == entry.File && x.Template == entry.Template);

    /// <summary>Every text-bearing binding a row renders, wherever in the row it sits.</summary>
    private static IEnumerable<Shown> Visible(Entry entry)
    {
        foreach (XElement element in Template(entry).Root.DescendantsAndSelf())
        {
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

    internal static Shown Parse(string binding)
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

    private static string AccountingKey(Entry entry, Shown shown) =>
        entry.File + "#" + entry.Template + "#" + shown.Binding;

    internal static Type RowType(Entry entry) =>
        entry.Row ?? ProductRows.For(entry.ProductType!);

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

    internal static object? Read(object row, string path)
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
    /// Stand-ins for the four row types declared inside the WinUI assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loading <c>AgencyOS.Windows.dll</c> runs its module initializer, which starts
    /// the Windows App Runtime bootstrap; a test that did that would be initializing
    /// the GUI stack to read four records. So these are surrogates, and they are
    /// honest only because of three facts, each checked:
    /// </para>
    /// <list type="number">
    /// <item><see cref="RowLabel"/>, <see cref="TaskLine"/>, <see cref="PartyLine"/>
    /// and <see cref="TargetLine"/> read a row through reflection — its type's name and
    /// its public properties' names, types and values. The one type-identity branch,
    /// <see cref="ForecastLine.IsPrediction"/>, matches neither these rows nor their
    /// surrogates.</item>
    /// <item>Each surrogate has its production type's name and exactly its public
    /// properties, with the same types, which
    /// <see cref="EverySurrogateMatchesItsProductionDeclaration"/> reads from the
    /// production declaration and compares.</item>
    /// <item>No production declaration overrides <c>ToString</c> or declares a
    /// computed property, so no hidden behaviour can change what a row says.</item>
    /// </list>
    /// </remarks>
    internal static class ProductRows
    {
        public sealed record AllocationRow(Guid ReceivableId, decimal Amount, string Description)
        {
            public string AmountDisplay { get; init; } = string.Empty;
        }

        public sealed record JournalLineRow(string Account, string Side, decimal Amount)
        {
            public string AmountDisplay { get; init; } = string.Empty;
        }

        public sealed record MemberLine(
            Guid MembershipId,
            string DisplayName,
            string Email,
            string Role,
            string Note,
            bool IsSelf,
            bool IsOnlyOwner);

        public sealed class StagedTerm
        {
            public StagedTerm(
                string code,
                string displayName,
                string displayValue,
                global::AgencyOS.Contracts.Deals.TermValueRequest value)
            {
                Code = code;
                DisplayName = displayName;
                DisplayValue = displayValue;
                Value = value;
            }

            public string Code { get; }

            public string DisplayName { get; }

            public string DisplayValue { get; }

            public global::AgencyOS.Contracts.Deals.TermValueRequest Value { get; }
        }

        /// <summary>Each production type, the file that declares it, and its stand-in.</summary>
        public static IReadOnlyList<(string ProductType, string Source, Type Surrogate)> All { get; } =
        [
            ("AgencyOS.Windows.Dialogs.AllocationRow", "Dialogs/RecordPaymentDialog.xaml.cs", typeof(AllocationRow)),
            ("AgencyOS.Windows.Dialogs.JournalLineRow", "Dialogs/PostJournalEntryDialog.xaml.cs", typeof(JournalLineRow)),
            ("AgencyOS.Windows.Pages.OrganizationPage+MemberLine", "Pages/OrganizationPage.xaml.cs", typeof(MemberLine)),
            ("AgencyOS.Windows.Dialogs.RecordOfferDialog+StagedTerm", "Dialogs/RecordOfferDialog.xaml.cs", typeof(StagedTerm)),
        ];

        public static Type For(string productType) =>
            All.Single(x => x.ProductType == productType).Surrogate;
    }

    /// <summary>Every surrogate row type, for the shape ratchet.</summary>
    public static TheoryData<string> Surrogates
    {
        get
        {
            TheoryData<string> data = [];

            foreach ((string productType, _, _) in ProductRows.All)
            {
                data.Add(productType);
            }

            return data;
        }
    }

    /// <summary>
    /// Each surrogate has its production type's name and exactly its public properties.
    /// </summary>
    /// <remarks>
    /// Read from the production declaration: the positional parameters of a record,
    /// plus every public property declared in its body. A declaration that overrides
    /// <c>ToString</c> or declares a computed property fails, because either could
    /// change what the row says without changing its shape.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Surrogates))]
    public void EverySurrogateMatchesItsProductionDeclaration(string productType)
    {
        (_, string source, Type surrogate) = ProductRows.All.Single(x => x.ProductType == productType);
        string name = productType.Split('.', '+')[^1];
        string text = System.IO.File.ReadAllText(Path.Combine(WindowsRoot, source));

        Match declaration = Regex.Match(
            text, $@"(?:record|class)\s+{name}\b\s*(?:\((?<positional>[^)]*)\))?\s*(?<body>\{{)?");

        Assert.True(declaration.Success, $"{name} is not declared in {source}.");

        SortedSet<string> declared = [];

        if (declaration.Groups["positional"].Success)
        {
            foreach (string parameter in declaration.Groups["positional"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                declared.Add(parts[^1] + ":" + Clr(parts[^2]));
            }
        }

        string body = declaration.Groups["body"].Success
            ? Block(text, declaration.Groups["body"].Index)
            : string.Empty;

        Assert.Empty(HiddenBehaviour(body));

        foreach (Match property in Regex.Matches(body, @"public\s+(?<type>[\w<>?.]+)\s+(?<name>\w+)\s*\{\s*get;"))
        {
            declared.Add(property.Groups["name"].Value + ":" + Clr(property.Groups["type"].Value));
        }

        SortedSet<string> stand = [.. surrogate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name + ":" + x.PropertyType.Name)];

        Assert.Equal(name, surrogate.Name);
        Assert.Equal(declared, stand);
    }

    /// <summary>
    /// Anything in a declaration's body that could change what a row says without
    /// changing its shape: a <c>ToString</c>, or a public property that is not a plain
    /// stored one.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. It reads each <c>public</c> property in the body and accepts
    /// only the auto-property forms — <c>{ get; }</c>, <c>{ get; init; }</c>,
    /// <c>{ get; set; }</c>, with or without an initialiser. An expression body, a
    /// block-bodied or expression-bodied accessor, or a backing field behind a getter
    /// is reported. Positional record parameters are outside the body and are always
    /// stored.
    /// </remarks>
    internal static List<string> HiddenBehaviour(string body)
    {
        List<string> found = [];

        if (Regex.IsMatch(body, @"\bToString\s*\("))
        {
            found.Add("ToString");
        }

        foreach (Match property in Regex.Matches(
            body, @"public\s+(?:(?:override|virtual|new|required)\s+)*[\w<>?.,\[\]]+\s+(?<name>\w+)\s*(?<tail>=>|\{)"))
        {
            if (property.Groups["tail"].Value == "=>")
            {
                found.Add(property.Groups["name"].Value);
                continue;
            }

            string accessors = Block(body, property.Groups["tail"].Index);

            if (!Regex.IsMatch(accessors, @"^\{\s*get;\s*(?:(?:init|set);\s*)?\}$"))
            {
                found.Add(property.Groups["name"].Value);
            }
        }

        return found;
    }

    /// <summary>The detector for hidden behaviour tells the shapes apart.</summary>
    [Theory]
    [InlineData("{ public string Code { get; } }", "")]
    [InlineData("{ public string AmountDisplay { get; init; } = string.Empty; }", "")]
    [InlineData("{ public string Name { get; set; } }", "")]
    [InlineData("{ public string Label => Code + Name; }", "Label")]
    [InlineData("{ public string Label { get { return Code; } } }", "Label")]
    [InlineData("{ public string Label { get => Code; } }", "Label")]
    [InlineData("{ private string _l; public string Label { get { return _l; } set { _l = value; } } }", "Label")]
    [InlineData("{ public override string ToString() => Code; }", "ToString")]
    public void TheHiddenBehaviourDetectorTellsTheShapesApart(string body, string expected)
    {
        Assert.Equal(expected, string.Join(",", HiddenBehaviour(body)));
    }

    /// <summary>The body of a declaration, from its opening brace to the matching one.</summary>
    private static string Block(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return text[open..(i + 1)];
            }
        }

        return text[open..];
    }

    /// <summary>A C# type keyword as the runtime names it.</summary>
    private static string Clr(string keyword) => keyword.TrimEnd('?') switch
    {
        "string" => nameof(String),
        "decimal" => nameof(Decimal),
        "bool" => nameof(Boolean),
        "int" => nameof(Int32),
        var other => other.Split('.')[^1],
    };

    /// <summary>
    /// Instances in which every field holds a deterministic value no other field holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strings are four-letter lowercase tokens, so none is a substring of another
    /// and none is changed by the display-label rules beyond its first letter.
    /// Numbers, dates and instants are distinct. Identifiers are derived from a
    /// counter, not random. Every optional field is filled.
    /// </para>
    /// <para>
    /// A flag can only be true or false, and an enum has few members, so their values
    /// cannot be unique. The comparator makes up for it: a flag is covered only by its
    /// role word, and a row showing two of a kind must name each one's role.
    /// </para>
    /// <para>
    /// One real invariant is modelled: money inside a record that states its own
    /// <c>Currency</c> is denominated in that currency, as a ledger account's balance
    /// is. Without it a bare amount beside a Currency column could never be judged.
    /// </para>
    /// </remarks>
    internal static class Sentinels
    {
        public static object Build(Type type) => Make(type, new Counter(), 0)
            ?? throw new InvalidOperationException($"Cannot build {type.Name}.");

        internal static string Word(int n) =>
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
                int n = counter.Next();

                return new Guid(n, 0x0a05, 0x0c0e, [0, 0, 0, 0, 0, 0, 0, 1]);
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

                return values.GetValue(counter.Next() % values.Length);
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

            ConstructorInfo? constructor = actual
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(x => !x.GetParameters().Any(p => p.ParameterType == actual))
                .OrderByDescending(x => x.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                return null;
            }

            ParameterInfo[] parameters = constructor.GetParameters();
            object?[] arguments = [.. parameters.Select(x => Make(x.ParameterType, counter, depth + 1))];

            // Money in a record that states its own currency is in that currency.
            int currency = Array.FindIndex(
                parameters, x => x.ParameterType == typeof(string)
                    && string.Equals(x.Name, "currency", StringComparison.OrdinalIgnoreCase));

            if (currency >= 0 && arguments[currency] is string code)
            {
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i] is MoneyResponse money)
                    {
                        arguments[i] = money with { Currency = code };
                    }
                }
            }

            object instance = constructor.Invoke(arguments);

            // Settable properties the constructor did not fill — an init-only
            // display string on a staging row is what that row shows.
            HashSet<string> filled = new(parameters.Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            foreach (PropertyInfo property in actual.GetProperties()
                .Where(x => x.CanWrite && x.GetIndexParameters().Length == 0 && !filled.Contains(x.Name)))
            {
                property.SetValue(instance, Make(property.PropertyType, counter, depth + 1));
            }

            return instance;
        }
    }
}
