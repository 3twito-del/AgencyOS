using System.Collections.ObjectModel;

namespace AgencyOS.Client.ViewModels;

/// <summary>One thing the user can do, addressable by name.</summary>
/// <param name="Id">Stable identifier, used by tests and by keyboard bindings.</param>
/// <param name="Title">What the command is called.</param>
/// <param name="Category">Grouping shown beside the title.</param>
/// <param name="Shortcut">Keyboard shortcut, when the command has one.</param>
public sealed record PaletteCommand(string Id, string Title, string Category, string? Shortcut = null);

/// <summary>
/// Keyboard-first access to every implemented command.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/12_WINDOWS_NATIVE.md</c> asks for a universal command palette and
/// keyboard-first operation. This holds the command set and the matching rule; the
/// WinUI layer supplies the surface and the invocation.
/// </para>
/// <para>
/// Only commands that actually exist are listed. A palette that offers actions the
/// build cannot perform teaches users to distrust it.
/// </para>
/// </remarks>
public sealed class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<PaletteCommand> _all;
    private string _query = string.Empty;
    private PaletteCommand? _selected;

    public CommandPaletteViewModel()
        : this(DefaultCommands())
    {
    }

    public CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _all = [.. commands];
        Results = [.. _all];
        _selected = Results.Count > 0 ? Results[0] : null;
    }

    public ObservableCollection<PaletteCommand> Results { get; }

    public override bool IsEmpty => Results.Count == 0;

    /// <summary>Gets every command, filtered or not.</summary>
    public IReadOnlyList<PaletteCommand> AllCommands => _all;

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
            {
                Filter();
            }
        }
    }

    public PaletteCommand? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>Moves the selection, wrapping at both ends.</summary>
    /// <remarks>
    /// Wrapping matters for a keyboard surface: reaching the end of a short list
    /// and having the arrow key stop responding reads as a bug.
    /// </remarks>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            Selected = null;
            return;
        }

        int current = _selected is null ? -1 : Results.IndexOf(_selected);
        int next = current < 0 ? 0 : (current + delta) % Results.Count;

        if (next < 0)
        {
            next += Results.Count;
        }

        Selected = Results[next];
    }

    private void Filter()
    {
        string query = _query.Trim();

        IEnumerable<PaletteCommand> matches = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Id.Contains(query, StringComparison.OrdinalIgnoreCase));

        Results.Clear();

        foreach (PaletteCommand command in matches)
        {
            Results.Add(command);
        }

        Selected = Results.Count > 0 ? Results[0] : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The commands this build actually implements.</summary>
    /// <remarks>
    /// Only implemented commands are listed. A palette that offers actions the
    /// build cannot perform teaches users to distrust it, so this list grows with
    /// the milestone rather than ahead of it.
    /// </remarks>
    public static IReadOnlyList<PaletteCommand> DefaultCommands() =>
    [
        new("go.command-center", "Go to Command Center", "Navigate", "Ctrl+1"),
        new("go.people", "Go to People", "Navigate", "Ctrl+2"),
        new("go.companies", "Go to Companies", "Navigate", "Ctrl+3"),
        new("go.talent", "Go to Talent", "Navigate", "Ctrl+4"),
        new("go.prospects", "Go to Prospects", "Navigate", "Ctrl+5"),
        new("go.projects", "Go to Projects", "Navigate", "Ctrl+6"),
        new("go.packages", "Go to Packages", "Navigate", "Ctrl+7"),
        new("go.pipeline", "Go to Pipeline", "Navigate", "Ctrl+8"),
        new("go.saved-views", "Go to Saved Views", "Navigate", "Ctrl+9"),
        new("go.sync", "Go to Sync and Offline", "Navigate", "F8"),
        new("search.open", "Search everything", "Find", "Ctrl+K"),
        new("person.create", "New person", "Create", "Ctrl+N"),
        new("company.create", "New company", "Create"),
        new("interaction.record", "Record interaction", "Capture", "Ctrl+I"),
        new("task.create", "New task", "Create", "Ctrl+T"),
        new("relationship.create", "Connect two parties", "Create"),
        new("prospect.create", "New prospect", "Represent"),
        new("prospect.convert", "Convert prospect to client", "Represent"),
        new("credit.add", "Add credit", "Represent"),
        new("material.add", "Add material", "Represent"),
        new("project.create", "New project", "Slate"),
        new("project.open", "Open project", "Slate"),
        new("project.role.add", "Add role to project", "Slate"),
        new("project.attach", "Attach someone to a role", "Slate"),
        new("project.company.add", "Record company involvement", "Slate"),
        new("package.create", "New package", "Slate"),
        new("package.open", "Open package", "Slate"),
        new("package.element.add", "Add package element", "Slate"),
        new("opportunity.create", "New opportunity", "Market"),
        new("opportunity.open", "Open opportunity", "Market"),
        new("opportunity.target.add", "Add target", "Market"),
        new("submission.record", "Record submission", "Market", "Ctrl+Shift+S"),
        new("pitch.record", "Record pitch", "Market", "Ctrl+Shift+P"),
        new("target.response.record", "Record target response", "Market"),
        new("go.overdue", "Open overdue follow-ups", "Market"),

        // Deals (M7). The wording says "record" throughout, because AgencyOS
        // records that an offer passed between the parties and sends nothing.
        new("go.deals", "Go to Deals", "Navigate", "Ctrl+0"),
        new("deal.create", "Create deal", "Deals"),
        new("deal.open", "Open deal", "Deals"),
        new("offer.record.inbound", "Record inbound offer", "Deals", "Ctrl+Shift+I"),
        new("offer.record.counter", "Record counter", "Deals", "Ctrl+Shift+C"),
        new("offer.compare", "Compare offers", "Deals"),
        new("offer.accept", "Accept offer", "Deals"),
        new("offer.answer", "Record offer response", "Deals"),
        new("go.negotiations", "Open negotiations", "Deals"),
        new("go.terms.agreed", "Open terms agreed", "Deals"),
        new("go.deals.awaiting", "Open deals awaiting a response", "Deals"),

        // Contracts (M8). "Record" throughout again, and for two reasons now:
        // AgencyOS neither transmits a notice nor verifies a signature, so every
        // verb here describes writing down what somebody says happened (ADR-0022).
        new("go.contracts", "Go to Contracts", "Navigate", "Ctrl+Shift+K"),
        new("contract.create", "Create contract", "Contracts"),
        new("contract.open", "Open contract", "Contracts"),
        new("contract.version.record", "Record contract version", "Contracts", "Ctrl+Shift+V"),
        new("contract.reconcile", "Reconcile draft against agreed terms", "Contracts", "Ctrl+Shift+R"),
        new("contract.party.add", "Add contract party", "Contracts"),
        new("contract.signature.record", "Record signature", "Contracts", "Ctrl+Shift+G"),
        new("contract.effective.record", "Record effective date", "Contracts"),
        new("contract.status.change", "Change contract status", "Contracts"),
        new("rights.grant.record", "Record rights grant", "Contracts"),
        new("option.record", "Record option", "Contracts"),
        new("option.resolve", "Record option outcome", "Contracts"),
        new("obligation.record", "Record obligation", "Contracts"),
        new("obligation.resolve", "Record obligation outcome", "Contracts"),
        new("notice.record", "Record notice given or received", "Contracts"),
        new("go.legal.deadlines", "Open legal deadlines", "Contracts"),
        new("go.legal.command-center", "Open legal command center", "Contracts"),
        new("go.contracts.awaiting", "Open contracts awaiting signature", "Contracts"),
        // Finance (M9). Every verb is one AgencyOS actually performs. "Record
        // invoice", not "send invoice", because there is no transport. "Record
        // payment", not "collect payment", because the agency did not do the
        // collecting - a bank did, and somebody is writing down that it happened.
        // "Allocate payment", not "match payment", because nothing is matched
        // automatically (ADR-0023).
        new("go.finance", "Go to Finance", "Navigate", "Ctrl+Shift+F"),
        new("go.receivables", "Open receivables", "Finance"),
        new("go.receivables.overdue", "Open overdue receivables", "Finance"),
        new("go.invoices", "Open invoices", "Finance"),
        new("go.payments", "Open payments", "Finance"),
        new("go.payments.unapplied", "Open unapplied payments", "Finance"),
        new("go.commissions", "Open commissions", "Finance"),
        new("go.ledger", "Open ledger", "Finance"),
        new("go.finance.command-center", "Open finance command center", "Finance"),
        new("obligation.monetary.record", "Record monetary obligation", "Finance"),
        new("obligation.monetary.quantify", "Quantify obligation amount", "Finance"),
        new("receivable.raise", "Raise receivable", "Finance"),
        new("receivable.write-off", "Write off receivable", "Finance"),
        new("invoice.record", "Record invoice", "Finance", "Ctrl+Shift+N"),
        new("invoice.issue", "Record invoice as issued", "Finance"),
        new("payment.record", "Record payment", "Finance", "Ctrl+Shift+M"),
        new("payment.allocate", "Allocate payment", "Finance", "Ctrl+Shift+A"),
        new("payment.allocation.reverse", "Reverse allocation", "Finance"),
        new("payment.reverse", "Reverse payment", "Finance"),
        new("adjustment.record", "Record deduction", "Finance"),
        new("commission.rule.create", "Create commission rule", "Finance"),
        new("commission.calculate", "Calculate commission", "Finance"),
        new("commission.adjust", "Adjust commission", "Finance"),
        new("journal.post", "Post journal entry", "Finance"),
        new("journal.reverse", "Reverse journal entry", "Finance"),
        new("receivable.reconcile", "Reconcile receivable", "Finance", "Ctrl+Shift+Y"),

        // Documents and communications (M10). Two verbs are doing careful work
        // here. "Record document" adds a version and never replaces one, so the
        // command that adds bytes to an existing document is called
        // "Add document version" rather than "Update document". And nothing in
        // this group says "send" except the two commands that genuinely cause an
        // external message: composing writes a draft, and queueing is the point
        // after which AgencyOS cannot take the message back (ADR-0028).
        new("go.documents", "Go to Documents", "Navigate", "Ctrl+Shift+D"),
        new("go.communications", "Go to Communications", "Navigate", "Ctrl+Shift+E"),
        new("document.record", "Record document", "Documents", "Ctrl+Shift+U"),
        new("document.open", "Open document", "Documents"),
        new("document.version.add", "Add document version", "Documents"),
        new("document.download", "Download document version", "Documents"),
        new("document.link", "Link document to a record", "Documents"),
        new("document.unlink", "Remove document link", "Documents"),
        new("document.update", "Edit document details", "Documents"),
        new("document.archive", "Archive document", "Documents"),
        new("document.restore", "Restore archived document", "Documents"),
        new("go.documents.unfiled", "Open unfiled documents", "Documents"),
        new("mailbox.connect", "Connect mailbox", "Communications"),
        new("mailbox.disconnect", "Disconnect mailbox", "Communications"),
        new("mailbox.visibility", "Change mailbox visibility", "Communications"),
        new("go.mailboxes", "Open connected mailboxes", "Communications"),
        new("go.messages", "Open messages", "Communications"),
        new("go.messages.unlinked", "Open unfiled correspondence", "Communications"),
        new("message.open", "Open message", "Communications"),
        new("message.link", "Link message to a record", "Communications"),
        new("message.unlink", "Remove message link", "Communications"),
        new("message.participant.resolve", "Identify an address", "Communications"),
        new("attachment.ingest", "Store attachment as a document", "Communications"),
        new("message.compose", "Compose message", "Communications", "Ctrl+Shift+W"),
        new("message.queue", "Queue message for sending", "Communications"),
        new("message.cancel", "Cancel queued message", "Communications"),
        new("go.outbound", "Open outbound messages", "Communications"),
        new("go.outbound.unknown", "Open sends with unknown outcome", "Communications"),
        new("go.communications.command-center", "Open communications command center", "Communications"),

        // Intelligence (M11). Every verb here describes writing down what a person
        // decided. "Record signal", never "detect signal", because nothing detects
        // anything; "State a prediction", because the probability is the
        // forecaster's assertion and not a system output; and there is no
        // "summarize", "extract" or "score" command anywhere, because there is no
        // route on the server that would answer one (§18, §54, ADR-0030).
        new("go.intelligence", "Go to Intelligence", "Navigate", "F7"),
        new("go.intelligence.desk", "Open the intelligence desk", "Intelligence"),
        new("go.intelligence.predictions", "Open predictions", "Intelligence"),
        new("go.intelligence.radar", "Open the talent radar", "Intelligence"),
        new("intelligence.source.record", "Record a source", "Intelligence"),
        new("intelligence.signal.record", "Record a signal", "Intelligence"),
        new(
            "intelligence.signal.verification",
            "Change what corroborates a claim",
            "Intelligence"),
        new("intelligence.thesis.create", "State a thesis", "Intelligence"),
        new("intelligence.thesis.revise", "Revise a thesis", "Intelligence"),
        new("intelligence.thesis.retire", "Retire a thesis", "Intelligence"),
        new("intelligence.prediction.create", "State a prediction", "Intelligence"),
        new("intelligence.prediction.forecast", "State a new probability", "Intelligence"),
        new("intelligence.prediction.resolve", "Resolve a prediction", "Intelligence"),
        new("intelligence.watchlist.create", "Create a watchlist", "Intelligence"),
        new("intelligence.watchlist.review", "Record a watchlist review", "Intelligence"),
        new("intelligence.radar.add", "Put somebody on the radar", "Intelligence"),
        new("intelligence.radar.convert", "Hand a radar entry to representation", "Intelligence"),
        new("intelligence.radar.dismiss", "Take somebody off the radar", "Intelligence"),
        new("intelligence.research.open", "Open a research case", "Intelligence"),
        new("intelligence.research.link", "Attach something to a research case", "Intelligence"),

        new("view.save", "Save current view", "View"),
        new("view.run", "Run selected view", "View"),
        new("sync.now", "Synchronize now", "Sync", "F9"),
        new("cache.reset", "Reset local cache", "Sync"),
        new("view.refresh", "Refresh", "View", "F5"),
    ];
}
