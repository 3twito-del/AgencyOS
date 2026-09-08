namespace AgencyOS.Client.Commands;

/// <summary>
/// Every command this build implements, defined once.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This list is the source of truth for both the palette and the keyboard.</strong>
/// Before M13 a command could exist as a palette entry, as an accelerator installed
/// in the window, and as a string compared inside a page - three places, none of
/// which knew about the others (ADR-0032).
/// </para>
/// <para>
/// That cost two things. <c>Ctrl+9</c> silently changed meaning when M12 inserted a
/// navigation item, because accelerators addressed the menu by position. And the
/// palette advertised twenty-six commands nothing dispatched and thirteen shortcuts
/// the window never installed - the palette's own documentation promised that only
/// implemented commands are listed, and it had stopped being true.
/// </para>
/// <para>
/// Both are now structural. Navigation carries a destination tag rather than an
/// index, so inserting a workspace cannot repoint a shortcut. The window installs
/// accelerators from this list, so an advertised gesture is an installed gesture.
/// And <see cref="CommandRegistry"/> refuses a duplicate identifier or gesture at
/// construction, so a collision fails the build rather than the user.
/// </para>
/// </remarks>
public static class AgencyOsCommands
{
    /// <summary>Every implemented command.</summary>
    public static IReadOnlyList<CommandDefinition> All { get; } =
    [
        // Navigation. Every one carries the destination tag rather than a menu
        // index, which is the whole reason Ctrl+9 could silently change meaning when
        // M12 inserted a workspace above Saved Views.
        new(
            "go.command-center",
            "Go to Command Center",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "command-center",
            Gesture: CommandGesture.Control(CommandKey.D1)),
        new(
            "go.people",
            "Go to People",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "people",
            Gesture: CommandGesture.Control(CommandKey.D2)),
        new(
            "go.companies",
            "Go to Companies",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "companies",
            Gesture: CommandGesture.Control(CommandKey.D3)),
        new(
            "go.talent",
            "Go to Talent",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "talent",
            Gesture: CommandGesture.Control(CommandKey.D4)),
        new(
            "go.prospects",
            "Go to Prospects",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "prospects",
            Gesture: CommandGesture.Control(CommandKey.D5)),
        new(
            "go.projects",
            "Go to Projects",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "projects",
            Gesture: CommandGesture.Control(CommandKey.D6)),
        new(
            "go.packages",
            "Go to Packages",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "packages",
            Gesture: CommandGesture.Control(CommandKey.D7)),
        new(
            "go.pipeline",
            "Go to Pipeline",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "pipeline",
            Gesture: CommandGesture.Control(CommandKey.D8)),
        new(
            "go.saved-views",
            "Go to Saved Views",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "saved-views",
            Gesture: CommandGesture.Control(CommandKey.D9)),
        new(
            "go.sync",
            "Go to Sync and Offline",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "sync",
            Gesture: CommandGesture.Function(CommandKey.F8)),
        new(
            "go.deals",
            "Go to Deals",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "deals",
            Gesture: CommandGesture.Control(CommandKey.D0)),
        new(
            "go.contracts",
            "Go to Contracts",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "contracts",
            Gesture: CommandGesture.ControlShift(CommandKey.K)),
        new(
            "go.finance",
            "Go to Finance",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "finance",
            Gesture: CommandGesture.ControlShift(CommandKey.F)),
        new(
            "go.documents",
            "Go to Documents",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "documents",
            Gesture: CommandGesture.ControlShift(CommandKey.D)),
        new(
            "go.communications",
            "Go to Communications",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "communications",
            Gesture: CommandGesture.ControlShift(CommandKey.E)),
        new(
            "go.intelligence",
            "Go to Intelligence",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "intelligence",
            Gesture: CommandGesture.Function(CommandKey.F7)),
        new(
            "go.ai",
            "Go to AI",
            "Navigate",
            CommandActionKind.Navigate,
            Workspace: "ai",
            Gesture: CommandGesture.Function(CommandKey.F6)),

        // Finding things.
        new(
            "search.open",
            "Search everything",
            "Find",
            CommandActionKind.Invoke,
            Gesture: CommandGesture.Control(CommandKey.K)),

        // Creating records.
        new(
            "person.create",
            "New person",
            "Create",
            CommandActionKind.Invoke,
            Workspace: "people",
            Gesture: CommandGesture.Control(CommandKey.N)),
        new(
            "company.create",
            "New company",
            "Create",
            CommandActionKind.Invoke,
            Workspace: "companies"),
        new(
            "relationship.create",
            "Connect two parties",
            "Create",
            CommandActionKind.Invoke,
            Workspace: "people"),

        // Capturing what happened.
        new(
            "interaction.record",
            "Record interaction",
            "Capture",
            CommandActionKind.Invoke,
            Workspace: "people",
            Gesture: CommandGesture.Control(CommandKey.I)),

        // Representation (M4).
        new(
            "prospect.convert",
            "Convert prospect to client",
            "Represent",
            CommandActionKind.Invoke,
            Workspace: "prospects"),
        new(
            "credit.add",
            "Add credit",
            "Represent",
            CommandActionKind.Invoke,
            Workspace: "talent"),
        new(
            "material.add",
            "Add material",
            "Represent",
            CommandActionKind.Invoke,
            Workspace: "talent"),

        // Projects and packaging (M5).
        new(
            "project.create",
            "New project",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "projects"),
        new(
            "project.role.add",
            "Add role to project",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "projects"),
        new(
            "project.attach",
            "Attach someone to a role",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "projects"),
        new(
            "project.company.add",
            "Record company involvement",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "projects"),
        new(
            "package.create",
            "New package",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "packages"),
        new(
            "package.element.add",
            "Add package element",
            "Slate",
            CommandActionKind.Invoke,
            Workspace: "packages"),
        new("package.open", "Open packages", "Slate",
            CommandActionKind.Navigate, Workspace: "packages"),
        new("project.open", "Open projects", "Slate",
            CommandActionKind.Navigate, Workspace: "projects"),

        // Opportunities and submissions (M6).
        new(
            "opportunity.create",
            "New opportunity",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline"),
        new(
            "opportunity.target.add",
            "Add target",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline"),
        new(
            "submission.record",
            "Record submission",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline",
            Gesture: CommandGesture.ControlShift(CommandKey.S)),
        new(
            "pitch.record",
            "Record pitch",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline",
            Gesture: CommandGesture.ControlShift(CommandKey.P)),
        new(
            "target.response.record",
            "Record target response",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline"),
        new(
            "go.overdue",
            "Open overdue follow-ups",
            "Market",
            CommandActionKind.Invoke,
            Workspace: "pipeline"),
        new("opportunity.open", "Open opportunities", "Market",
            CommandActionKind.Navigate, Workspace: "pipeline"),

        // Deals (M7). Every verb says "record": AgencyOS writes down that an offer
        // passed between the parties, and transmits nothing.
        new(
            "deal.create",
            "Create deal",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "offer.record.inbound",
            "Record inbound offer",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals",
            Gesture: CommandGesture.ControlShift(CommandKey.I)),
        new(
            "offer.record.counter",
            "Record counter",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals",
            Gesture: CommandGesture.ControlShift(CommandKey.C)),
        new(
            "offer.compare",
            "Compare offers",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "offer.accept",
            "Accept offer",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "offer.answer",
            "Record offer response",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "go.negotiations",
            "Open negotiations",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "go.terms.agreed",
            "Open terms agreed",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new(
            "go.deals.awaiting",
            "Open deals awaiting a response",
            "Deals",
            CommandActionKind.Invoke,
            Workspace: "deals"),
        new("deal.open", "Open deals", "Deals",
            CommandActionKind.Navigate, Workspace: "deals"),

        // Contracts (M8). "Record" throughout, because AgencyOS neither sends a
        // notice nor verifies a signature.
        new(
            "contract.create",
            "Create contract",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts"),
        new(
            "contract.version.record",
            "Record contract version",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts",
            Gesture: CommandGesture.ControlShift(CommandKey.V)),
        new(
            "contract.reconcile",
            "Reconcile draft against agreed terms",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts",
            Gesture: CommandGesture.ControlShift(CommandKey.R)),
        new(
            "contract.signature.record",
            "Record signature",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts",
            Gesture: CommandGesture.ControlShift(CommandKey.G)),
        new(
            "option.resolve",
            "Record option outcome",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts"),
        new(
            "obligation.resolve",
            "Record obligation outcome",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts"),
        new(
            "notice.record",
            "Record notice given or received",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts"),
        new(
            "go.contracts.awaiting",
            "Open contracts awaiting signature",
            "Contracts",
            CommandActionKind.Invoke,
            Workspace: "contracts"),
        new("contract.open", "Open contracts", "Contracts",
            CommandActionKind.Navigate, Workspace: "contracts"),

        // Finance (M9). Every verb is one AgencyOS performs: record, allocate, post.
        // Never "collect" or "match" - a bank collected, and nothing is matched
        // automatically.
        new(
            "go.receivables",
            "Open receivables",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.receivables.overdue",
            "Open overdue receivables",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.invoices",
            "Open invoices",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.payments",
            "Open payments",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.payments.unapplied",
            "Open unapplied payments",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.commissions",
            "Open commissions",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "go.ledger",
            "Open ledger",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "receivable.write-off",
            "Write off receivable",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "invoice.record",
            "Record invoice",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance",
            Gesture: CommandGesture.ControlShift(CommandKey.N)),
        new(
            "payment.record",
            "Record payment",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance",
            Gesture: CommandGesture.ControlShift(CommandKey.M)),
        new(
            "payment.allocate",
            "Allocate payment",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance",
            Gesture: CommandGesture.ControlShift(CommandKey.A)),
        new(
            "payment.reverse",
            "Reverse payment",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "adjustment.record",
            "Record deduction",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "commission.rule.create",
            "Create commission rule",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "journal.post",
            "Post journal entry",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "journal.reverse",
            "Reverse journal entry",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance"),
        new(
            "receivable.reconcile",
            "Reconcile receivable",
            "Finance",
            CommandActionKind.Invoke,
            Workspace: "finance",
            Gesture: CommandGesture.ControlShift(CommandKey.Y)),

        // Documents (M10). "Add version", never "update": a version is added and
        // never replaces one.
        new(
            "document.record",
            "Record document",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents",
            Gesture: CommandGesture.ControlShift(CommandKey.U)),
        new(
            "document.version.add",
            "Add document version",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "document.download",
            "Download document version",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "document.link",
            "Link document to a record",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "document.unlink",
            "Remove document link",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "document.archive",
            "Archive document",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "document.restore",
            "Restore archived document",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new(
            "go.documents.unfiled",
            "Open unfiled documents",
            "Documents",
            CommandActionKind.Invoke,
            Workspace: "documents"),
        new("document.open", "Open documents", "Documents",
            CommandActionKind.Navigate, Workspace: "documents"),

        // Communications (M10). Only two commands cause an external message, and
        // queueing is the point after which AgencyOS cannot take it back.
        new(
            "mailbox.connect",
            "Connect mailbox",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "mailbox.disconnect",
            "Disconnect mailbox",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "mailbox.visibility",
            "Change mailbox visibility",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.mailboxes",
            "Open connected mailboxes",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.messages",
            "Open messages",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.messages.unlinked",
            "Open unfiled correspondence",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "message.link",
            "Link message to a record",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "message.unlink",
            "Remove message link",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "message.participant.resolve",
            "Identify an address",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "attachment.ingest",
            "Store attachment as a document",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "message.compose",
            "Compose message",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications",
            Gesture: CommandGesture.ControlShift(CommandKey.W)),
        new(
            "message.queue",
            "Queue message for sending",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "message.cancel",
            "Cancel queued message",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.outbound",
            "Open outbound messages",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.outbound.unknown",
            "Open sends with unknown outcome",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new(
            "go.communications.command-center",
            "Open communications command center",
            "Communications",
            CommandActionKind.Invoke,
            Workspace: "communications"),
        new("message.open", "Open messages", "Communications",
            CommandActionKind.Navigate, Workspace: "communications"),

        // Intelligence (M11). Every verb records what a person decided. No
        // summarize, no extract and no score, because there is no route that would
        // answer one.
        new(
            "go.intelligence.desk",
            "Open the intelligence desk",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "go.intelligence.predictions",
            "Open predictions",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "go.intelligence.radar",
            "Open the talent radar",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.source.record",
            "Record a source",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.signal.record",
            "Record a signal",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.signal.verification",
            "Change what corroborates a claim",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.thesis.create",
            "State a thesis",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.thesis.revise",
            "Revise a thesis",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.thesis.retire",
            "Retire a thesis",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.prediction.create",
            "State a prediction",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.prediction.forecast",
            "State a new probability",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.prediction.resolve",
            "Resolve a prediction",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.watchlist.create",
            "Create a watchlist",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.watchlist.review",
            "Record a watchlist review",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.radar.add",
            "Put somebody on the radar",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.radar.convert",
            "Hand a radar entry to representation",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.radar.dismiss",
            "Take somebody off the radar",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.research.open",
            "Open a research case",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),
        new(
            "intelligence.research.link",
            "Attach something to a research case",
            "Intelligence",
            CommandActionKind.Invoke,
            Workspace: "intelligence"),

        // AI (M12). Ask, review, decide. A canonical change is never a palette
        // command away from a model request.
        new("ai.ask", "Ask AI for a brief", "AI", CommandActionKind.Invoke, Workspace: "ai"),
        new(
            "ai.approvals",
            "Review what AI is waiting on",
            "AI",
            CommandActionKind.Invoke,
            Workspace: "ai"),
        new("ai.runs", "Open my AI runs", "AI", CommandActionKind.Invoke, Workspace: "ai"),

        // Views and refresh.
        new(
            "view.save",
            "Save current view",
            "View",
            CommandActionKind.Invoke,
            Workspace: "saved-views"),
        new(
            "view.run",
            "Run selected view",
            "View",
            CommandActionKind.Invoke,
            Workspace: "saved-views"),
        new(
            "view.refresh",
            "Refresh",
            "View",
            CommandActionKind.Invoke,
            Gesture: CommandGesture.Function(CommandKey.F5)),

        // Sync and cache.
        new(
            "sync.now",
            "Synchronize now",
            "Sync",
            CommandActionKind.Invoke,
            Workspace: "sync",
            Gesture: CommandGesture.Function(CommandKey.F9)),
        new(
            "cache.reset",
            "Reset local cache",
            "Sync",
            CommandActionKind.Invoke,
            Workspace: "sync"),
    ];
}
