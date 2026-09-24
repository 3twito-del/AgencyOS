namespace AgencyOS.Client.Presentation;

/// <summary>How a profiled field is spoken.</summary>
public enum RowFieldKind
{
    /// <summary>
    /// The value as the row shows it: text as written, a date as <c>yyyy-MM-dd</c>, an
    /// instant to the second with its offset, a number, a flag as yes or no. Spoken
    /// "Role: value" where the field has a role.
    /// </summary>
    Text,

    /// <summary>A domain token, spoken the way <see cref="DisplayLabel"/> writes it.</summary>
    Token,

    /// <summary>Money with its own currency, spoken "amount currency role".</summary>
    Money,

    /// <summary>A person, spoken with the role <see cref="PartyLine"/> gives them.</summary>
    Party,

    /// <summary>A value the role word follows: "Client money".</summary>
    Suffixed,
}

/// <summary>One field a profiled row announces.</summary>
/// <param name="Path">
/// The field the row's template binds, dotted where the template binds a nested
/// field. For <see cref="RowFieldKind.Party"/> it is the field <see cref="PartyLine"/>
/// reads, which is the template's <c>ConverterParameter</c>.
/// </param>
/// <param name="Kind">How the value is spoken.</param>
/// <param name="Role">The role word, or null where the value says what it is.</param>
/// <param name="RoleFrom">
/// Another field whose value is this one's role, as a link row's kind names what its
/// label is: "Deal: Autumn slate".
/// </param>
/// <param name="Yields">
/// Whether this is the value that is shortened when the row would run past its
/// budget. At most one per profile.
/// </param>
/// <param name="Overflow">
/// Whether the value, when shortened, is offered whole on the row's own help text
/// (owner decision D4). The template must bind it there.
/// </param>
public sealed record RowField(
    string Path,
    RowFieldKind Kind = RowFieldKind.Text,
    string? Role = null,
    string? RoleFrom = null,
    bool Yields = false,
    bool Overflow = false);

/// <summary>What one row template announces, field by field, in order.</summary>
/// <param name="Id">The id the template passes as the converter's parameter.</param>
/// <param name="Fields">Every field the row announces; nothing else is said.</param>
public sealed record RowProfile(string Id, IReadOnlyList<RowField> Fields);

/// <summary>
/// The rows whose announcement is declared rather than inferred.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RowLabel"/> infers a row's announcement from its record's field names,
/// and for most rows that is right. The operational regression gate found 70
/// templates where it was not: a value the row shows and never says, a value it says
/// under no role beside another that could answer for it, or a hidden field said in
/// place of a shown one. A record type does not know which of its fields a
/// particular template shows, so no rule over field names can repair that.
/// </para>
/// <para>
/// Each profile here is the list of what one template shows, spoken in the product's
/// own words. The template names its profile through the row converter's parameter.
/// A profile holds only fields its templates show as scan facts, so a profiled row
/// never announces a field the operator cannot see; the Windows parity tests hold
/// each one to its markup.
/// </para>
/// <para>
/// <strong>The role words live here and nowhere else.</strong> Where a field's own
/// name is not the operator's word, the role is the approved concise word: an
/// agreed value is "Agreed", a provider key is "Provider", a receivable's beneficiary
/// is "Client money" or "Agency money".
/// </para>
/// </remarks>
public static class RowProfiles
{
    private static RowField Text(string path, string? role = null) => new(path, RowFieldKind.Text, role);

    private static RowField Headline(string path, string? role = null) =>
        new(path, RowFieldKind.Text, role, Yields: true);

    private static RowField Overflowing(string path, string role) =>
        new(path, RowFieldKind.Text, role, Yields: true, Overflow: true);

    private static RowField Token(string path, string? role = null) => new(path, RowFieldKind.Token, role);

    private static RowField Money(string path, string? role = null) => new(path, RowFieldKind.Money, role);

    private static RowField Party(string field) => new(field, RowFieldKind.Party);

    private static RowProfile Profile(string id, params RowField[] fields) => new(id, fields);

    /// <summary>Every profile, by id.</summary>
    public static IReadOnlyDictionary<string, RowProfile> All { get; } = new[]
    {
        // App
        Profile("Person", Headline("DisplayName"), Text("Title"), Text("PrimaryCompanyName")),
        Profile("Relationship", Token("Type"), Headline("To.Name")),
        Profile("TimelineEntry", Headline("Title"), Text("OccurredAt")),

        // Dialogs
        Profile("Allocation", Headline("Description"), Text("AmountDisplay")),
        Profile("JournalLine", Headline("Account", "Account"), Text("Side", "Side"), Text("AmountDisplay", "Amount")),
        Profile("Recipient", Text("Role"), Headline("Address")),
        Profile("ParticipantSuggestion", Headline("DisplayName"), Text("MatchedAddress", "Matched address")),

        // Main window
        Profile("SearchHit", Headline("Title"), Text("Subtitle", "Context"), Token("Type"), Text("MatchedOn", "Matched")),
        Profile("PaletteCommand", Headline("Title"), Text("Category", "Category"), Text("Shortcut", "Shortcut")),

        // AI
        Profile("AiApproval", Headline("Summary"), Text("ToolName", "Tool name"), Text("ExpiresAt", "Expires")),
        Profile("AgentRun", Headline("Task"), Token("Kind"), Text("StartedAt", "Started")),
        Profile("AgentStep", Headline("Summary"), Token("Kind"), Text("OccurredAt")),
        Profile("AiPolicy", Headline("ProviderKey", "Provider"), Text("MaximumSensitivity", "Maximum sensitivity")),
        Profile("AiTool", Headline("Name"), Text("RequiredPermission", "Required permission")),
        Profile("AiModel", Headline("Key", "Model"), Text("ProviderKey", "Provider")),

        // Communications
        Profile("Message", Headline("Subject"), Text("FromAddress", "From address"), Text("OccurredAt")),
        Profile("Participant", Text("Role"), Headline("Address"), Text("DisplayName"), Party("PersonDisplayName")),
        Profile(
            "MessageAttachment",
            Headline("FileName", "File name"),
            Text("MediaType", "Media type"),
            Text("HoldsContent", "Holds content")),
        Profile("Link", new RowField("TargetLabel", RoleFrom: "Target", Yields: true)),
        Profile("Outbound", Headline("Subject"), Text("MailboxAddress"), Token("State"), Text("AttemptCount", "Attempt count")),
        Profile(
            "Mailbox",
            Headline("MailboxAddress", "Mailbox address"),
            Party("OwnerDisplayName"),
            Token("State"),
            Text("LastSyncedAt", "Last synced"),
            Text("Visibility", "Visibility")),
        Profile("OutboundDesk", Headline("Subject"), Text("MailboxAddress"), Token("State")),

        // Contracts
        Profile(
            "ContractVersion",
            Text("VersionNumber", "Version number"),
            Headline("Label"),
            Token("Direction"),
            Token("Status")),
        Profile("ContractTerm", Headline("DisplayName"), Text("ClauseReference", "Clause reference"), Text("DisplayValue")),
        Profile(
            "Reconciliation",
            Headline("DisplayName"),
            Text("Negotiated.DisplayValue", "Agreed"),
            Text("Contracted.DisplayValue", "In the draft"),
            Text("Result", "Result")),
        Profile("ContractParty", Headline("DisplayName"), Text("Role"), Text("SignedOn", "Signed")),
        Profile(
            "RightsGrant",
            Headline("RightType", "Right"),
            Text("Medium", "Medium"),
            Text("Territory", "Territory"),
            Text("PeriodKind", "Period")),
        Profile("ContractOption", Headline("Subject"), Token("Kind"), Text("DeadlineOn", "Deadline"), Token("Status")),
        Profile("Obligation", Headline("Description"), Party("ObligorDisplayName"), Text("DueOn", "Due"), Token("Status")),
        Profile(
            "MoneyObligation",
            Headline("Description"),
            Token("Category", "Category"),
            Text("DueOn", "Due"),
            Token("Status")),
        Profile("NoticeRequirement", Headline("Description"), Text("DueOn", "Due")),
        Profile("Notice", Headline("Summary"), Token("Direction"), Text("OccurredOn")),

        // Deals
        Profile("Offer", Text("Sequence", "Sequence"), Token("Direction"), Headline("Summary"), Token("Status")),
        Profile(
            "TermComparison",
            Headline("DisplayName"),
            Text("Previous.DisplayValue", "Previous"),
            Text("Current.DisplayValue", "Current"),
            Text("Change", "Change")),

        // Documents
        Profile("Document", Headline("Title"), Token("Kind"), Text("Sensitivity", "Sensitivity"), Token("Status")),
        Profile(
            "DocumentVersion",
            Text("Sequence", "Sequence"),
            Headline("DisplayFileName"),
            Text("MediaType", "Media type"),
            Text("RecordedAt", "Recorded"),
            Text("ContentHash", "Content hash")),
        Profile("DocumentHistory", Headline("Summary"), Party("ActorDisplayName"), Text("OccurredAt")),

        // Finance
        Profile(
            "Receivable",
            Overflowing("ContractTitle", "Contract"),
            Party("PayerDisplayName"),
            Money("OriginalAmount", "original"),
            Money("Allocated", "allocated"),
            Money("Outstanding", "outstanding"),
            Token("Status"),
            new RowField("Beneficiary", RowFieldKind.Suffixed, "money")),
        Profile(
            "Payment",
            Party("PayerDisplayName"),
            Headline("ExternalReference", "External reference"),
            Money("Amount"),
            Money("Allocated", "allocated"),
            Money("Unapplied", "unapplied"),
            Token("Status"),
            Text("ReceivedOn", "Received")),
        Profile(
            "Commission",
            Text("ClientDisplayName", "Client"),
            Overflowing("ContractTitle", "Contract"),
            Money("Entitled", "entitled"),
            Money("Collected", "collected"),
            Money("Outstanding", "outstanding"),
            Token("Status")),
        Profile(
            "CommissionRule",
            Headline("ClientDisplayName", "Client"),
            Text("Basis", "Basis"),
            Text("EffectiveFrom", "Effective from")),
        Profile(
            "Journal",
            Headline("Memo"),
            Text("Source", "Source"),
            Money("Debits", "debits"),
            Money("Credits", "credits"),
            Token("Status"),
            Text("PostingDate", "Posting date")),
        Profile("FinanceHistory", Headline("Summary"), Party("ActorDisplayName"), Token("Kind"), Text("OccurredAt")),

        // Intelligence
        Profile("SignalEvidence", Headline("SourceTitle"), Text("Role")),
        Profile("Signal", Headline("Title"), Text("Verification", "Verification"), Text("Sensitivity", "Sensitivity")),
        Profile(
            "IntelligenceSource",
            Headline("Title"),
            Token("Kind"),
            Text("Reliability", "Reliability"),
            Text("Sensitivity", "Sensitivity")),
        Profile("ThesisEvidence", Headline("SignalTitle", "Signal"), Text("Stance", "Stance")),

        // Organization
        Profile("Member", Headline("DisplayName"), Text("Email", "Email"), Token("Role"), Text("Note", "Note")),

        // Packages, pipeline and projects
        Profile("PackageElement", Headline("DisplayName"), Text("Detail")),
        Profile("OpportunitySubject", Headline("DisplayName"), Text("Detail")),
        Profile("Submission", Headline("TargetDisplayName", "Target"), Text("Subject")),
        Profile("Pitch", Headline("TargetDisplayName", "Target"), Token("Outcome")),
        Profile("OpportunityHistory", Headline("Summary"), Text("TargetDisplayName", "Target")),
        Profile("Project", Headline("Title"), Token("Stage"), Token("Type")),
        Profile("ProjectAttachment", Headline("DisplayName"), Text("RoleType", "Role type"), Token("Status")),
        Profile("ProjectCompany", Headline("CompanyName"), Text("Capacity")),
        Profile("SourceProperty", Headline("Title"), Text("AttributedCreator", "Attributed creator")),
        Profile("ProjectMaterial", Headline("Title"), Text("PersonName", "Person name")),

        // Saved views and sync
        Profile("SavedView", Headline("Name"), Text("Target", "Target")),
        Profile("SavedViewResult", Headline("Title"), Text("Subtitle"), Token("Kind")),
        Profile("PendingChange", Headline("Description"), Token("State"), Text("Explanation")),

        // Talent
        Profile(
            "Talent",
            Headline("DisplayName"),
            Text("CareerStage", "Career stage"),
            Text("RepresentationStatus", "Representation")),
        Profile("RepresentationScope", Headline("Area"), Text("StartsOn", "Starts")),
        Profile("TalentHistory", Headline("Title"), Text("OccurredOn")),
        Profile("Credit", Headline("Title"), Text("Role"), Token("Type"), Text("Year", "Year")),
        Profile("TalentMaterial", Headline("Title"), Text("VersionLabel", "Version label"), Token("Type"), Token("Status")),
    }.ToDictionary(x => x.Id, StringComparer.Ordinal);

    /// <summary>The profile with this id, or null.</summary>
    public static RowProfile? Find(string? id) =>
        id is not null && All.TryGetValue(id, out RowProfile? profile) ? profile : null;
}
