using System.Collections.ObjectModel;
using AgencyOS.Contracts.Documents;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// Puts the send states into words, without smoothing any of them over.
/// </summary>
/// <remarks>
/// <para>
/// The one that matters is <c>UnknownOutcome</c>. It renders as "Outcome unknown"
/// and never as "Failed", because the two demand opposite responses: a failure
/// invites another attempt, and an unknown outcome may already have put the
/// message in somebody's inbox. Rounding one to the other is how a client receives
/// the same commercial email twice (ADR-0028).
/// </para>
/// <para>
/// <c>Sent</c> means the provider confirmed it. It does not mean delivered, and
/// nothing in this build claims delivery: AgencyOS asked a mail server to send a
/// message and was told it did, which is a different fact from the message
/// arriving.
/// </para>
/// </remarks>
public static class OutboundFormatting
{
    /// <summary>The state, in the words a person should read.</summary>
    public static string State(string? state) => state switch
    {
        "Draft" => "Draft - nothing sent",
        "Queued" => "Queued to send",
        "ProviderDraftCreated" => "Prepared at provider",
        "SendRequested" => "Send in progress",
        "Sent" => "Sent - provider confirmed",
        "FailedRetryable" => "Failed - will retry",
        "FailedPermanent" => "Failed - stopped",
        "UnknownOutcome" => "Outcome unknown - check the mailbox",
        "Cancelled" => "Cancelled before sending",
        _ => state ?? string.Empty,
    };

    /// <summary>
    /// Why an unknown outcome is its own thing, for the surface that shows one.
    /// </summary>
    public static string UnknownOutcomeExplanation =>
        "The provider did not confirm the outcome. The message may or may not have "
        + "been sent. AgencyOS will not resend it on a guess; check the sent items "
        + "for this mailbox, or reconcile from the dispatch.";

    /// <summary>Whether a person has to do something about this dispatch.</summary>
    public static bool NeedsAttention(string? state) =>
        state is "UnknownOutcome" or "FailedPermanent";

    /// <summary>Whether cancelling is still possible.</summary>
    /// <remarks>
    /// Only before anything reached the provider. AgencyOS cannot unsend a message
    /// and does not offer a button that implies it can (ADR-0028).
    /// </remarks>
    public static bool CanCancel(string? state) =>
        state is "Draft" or "Queued";

    /// <summary>Whether the message can still be handed to the worker.</summary>
    public static bool CanQueue(string? state) => state is "Draft";

    /// <summary>What reconciliation concluded, including that it could not conclude.</summary>
    public static string Verdict(string? verdict) => verdict switch
    {
        "FoundSent" => "Found in sent items - the message went",
        "ProvenAbsent" => "Not sent, and the draft is still there",
        "Inconclusive" => "Could not tell",
        _ => "Not reconciled",
    };
}

/// <summary>
/// The connected mailboxes.
/// </summary>
/// <remarks>
/// <para>
/// This screen shows whether a credential is stored and when it expires. It never
/// shows a token, a fragment of one, or anything from which one could be
/// reconstructed, and the contract it binds to has no field that could carry one
/// (ADR-0027).
/// </para>
/// <para>
/// Visibility is displayed on every row, because a mailbox is somebody's
/// correspondence. A mailbox connected by one member does not become readable by
/// the rest of the agency because they share a tenant.
/// </para>
/// </remarks>
public sealed class MailboxListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public MailboxListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<CommunicationAccountResponse> Accounts { get; } = [];

    public override bool IsEmpty => _loaded && Accounts.Count == 0;

    /// <summary>Mailboxes that have stopped working and need a person.</summary>
    public int NeedingAttention => Accounts.Count(x =>
        x.State is "Disconnected" or "Failed" || x.LastSyncError is not null);

    /// <summary>Mailboxes whose contents only their owner can read.</summary>
    public int Private => Accounts.Count(x =>
        string.Equals(x.Visibility, "Private", StringComparison.Ordinal));

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<CommunicationAccountResponse> accounts = await _api
                .ListCommunicationAccountsAsync(token)
                .ConfigureAwait(true);

            Accounts.Clear();

            foreach (CommunicationAccountResponse account in accounts)
            {
                Accounts.Add(account);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(NeedingAttention));
            OnPropertyChanged(nameof(Private));
        }, cancellationToken);

    /// <summary>Completes a connection the user began in a browser.</summary>
    /// <remarks>
    /// The authorization code goes straight to the server, which exchanges it. The
    /// Windows client never holds a token and has nowhere to put one (ADR-0027).
    /// </remarks>
    public Task ConnectAsync(
        ConnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api.ConnectMailboxAsync(request, idempotencyKey, token).ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    /// <summary>Disconnects a mailbox, which destroys the stored credential.</summary>
    /// <remarks>
    /// The messages already synchronized stay. They are a record of what was said,
    /// and revoking access to a mailbox is not a reason to lose the correspondence
    /// a deal was negotiated in (ADR-0027).
    /// </remarks>
    public Task DisconnectAsync(
        Guid accountId,
        int expectedVersion,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .DisconnectMailboxAsync(
                    accountId,
                    new DisconnectMailboxRequest(expectedVersion),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    public Task ChangeVisibilityAsync(
        Guid accountId,
        string visibility,
        int expectedVersion,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .ChangeMailboxVisibilityAsync(
                    accountId,
                    new ChangeMailboxVisibilityRequest(visibility, expectedVersion),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CommunicationAccountResponse> accounts = await _api
            .ListCommunicationAccountsAsync(cancellationToken)
            .ConfigureAwait(true);

        Accounts.Clear();

        foreach (CommunicationAccountResponse account in accounts)
        {
            Accounts.Add(account);
        }

        OnPropertyChanged(nameof(NeedingAttention));
        OnPropertyChanged(nameof(Private));
    }
}

/// <summary>
/// Synchronized messages.
/// </summary>
/// <remarks>
/// Not an inbox. There is no unread count, no folder tree and no reply-all,
/// because Outlook already exists and does all of that better. What this list is
/// for is the question Outlook cannot answer: which of these bear on a deal, and
/// which are still unattached to anything (ADR-0026).
/// </remarks>
public sealed class MessageListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private Guid? _accountId;
    private string? _direction;
    private string? _linkedTarget;
    private Guid? _linkedTargetId;
    private bool _unlinkedOnly;
    private bool _withAttachmentsOnly;
    private string _search = string.Empty;

    public MessageListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<MessageSummaryResponse> Messages { get; } = [];

    public Guid? AccountId
    {
        get => _accountId;
        set => Set(ref _accountId, value);
    }

    /// <summary>Inbound or Outbound.</summary>
    public string? Direction
    {
        get => _direction;
        set => Set(ref _direction, value);
    }

    public string? LinkedTarget
    {
        get => _linkedTarget;
        set => Set(ref _linkedTarget, value);
    }

    public Guid? LinkedTargetId
    {
        get => _linkedTargetId;
        set => Set(ref _linkedTargetId, value);
    }

    /// <summary>The filing queue: correspondence attached to nothing yet.</summary>
    public bool UnlinkedOnly
    {
        get => _unlinkedOnly;
        set => Set(ref _unlinkedOnly, value);
    }

    public bool WithAttachmentsOnly
    {
        get => _withAttachmentsOnly;
        set => Set(ref _withAttachmentsOnly, value);
    }

    /// <summary>Matches subjects and participants. Not message bodies.</summary>
    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    /// <summary>
    /// What the search covers, said on the screen.
    /// </summary>
    /// <remarks>
    /// Bodies are not searched in M10. Getting cross-mailbox body search right
    /// means getting the authorization right first, and a leak between two agents
    /// correspondence is worse than a missing feature (ADR-0026).
    /// </remarks>
    public static string SearchScopeNotice =>
        "Searches subjects and participants. Message bodies are not searched in this build.";

    public override bool IsEmpty => _loaded && Messages.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many listed messages are attached to nothing.</summary>
    public int Unlinked => Messages.Count(x => x.LinkCount == 0);

    public int WithAttachments => Messages.Count(x => x.HasAttachments);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<MessageSummaryResponse> messages = await _api
                .ListMessagesAsync(
                    AccountId,
                    Direction,
                    LinkedTarget,
                    LinkedTargetId,
                    UnlinkedOnly,
                    WithAttachmentsOnly,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Messages.Clear();

            foreach (MessageSummaryResponse message in messages)
            {
                Messages.Add(message);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Unlinked));
            OnPropertyChanged(nameof(WithAttachments));
        }, cancellationToken);
}

/// <summary>
/// One message: what it said, who was on it, and what it was filed against.
/// </summary>
/// <remarks>
/// <para>
/// The HTML shown here was sanitized once, on the way in. Script, styles, frames,
/// forms and external image references are gone before the row was written, so
/// nothing this screen renders can run, and opening a five-year-old message cannot
/// tell its sender that somebody looked at it (ADR-0026).
/// </para>
/// <para>
/// Linking a message to a deal records that the correspondence bears on it. It
/// does not advance the deal: an email saying "we accept" is evidence somebody
/// wrote that, and a person still has to record the acceptance (ADR-0026).
/// </para>
/// </remarks>
public sealed class MessageDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private MessageDetailResponse? _message;

    public MessageDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Guid MessageId { get; set; }

    public MessageDetailResponse? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(BodyText));
                OnPropertyChanged(nameof(SanitizedHtml));
                OnPropertyChanged(nameof(HasHtml));
                OnPropertyChanged(nameof(UnresolvedParticipants));
            }
        }
    }

    public ObservableCollection<ParticipantResponse> Participants { get; } = [];

    public ObservableCollection<MessageAttachmentResponse> Attachments { get; } = [];

    public ObservableCollection<MessageLinkResponse> Links { get; } = [];

    /// <summary>The rest of the conversation, oldest first.</summary>
    public ObservableCollection<MessageSummaryResponse> Thread { get; } = [];

    public override bool IsEmpty => _message is null;

    public string Subject => _message?.Message.Subject ?? "(no subject)";

    public string? BodyText => _message?.BodyText;

    public string? SanitizedHtml => _message?.SanitizedHtml;

    public bool HasHtml => !string.IsNullOrWhiteSpace(_message?.SanitizedHtml);

    /// <summary>
    /// Said beside the rendered message, so the treatment is not a surprise.
    /// </summary>
    public static string SanitizationNotice =>
        "Remote images and scripts were removed when this message was stored.";

    /// <summary>Addresses AgencyOS has not tied to anybody it knows.</summary>
    /// <remarks>
    /// Left unresolved rather than guessed. Two people share an address more often
    /// than a system expects - a shared assistant mailbox, a company address - and
    /// attributing correspondence to the wrong person is quiet and hard to undo
    /// (ADR-0026).
    /// </remarks>
    public int UnresolvedParticipants =>
        Participants.Count(x => x.PersonId is null && x.CompanyId is null);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            MessageDetailResponse detail = await _api
                .GetMessageAsync(MessageId, token)
                .ConfigureAwait(true);

            Apply(detail);

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);

    public Task LinkAsync(
        LinkMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api.LinkMessageAsync(MessageId, request, idempotencyKey, token).ConfigureAwait(true);

            Apply(await _api.GetMessageAsync(MessageId, token).ConfigureAwait(true));
        }, cancellationToken);

    public Task UnlinkAsync(Guid linkId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api.UnlinkMessageAsync(MessageId, linkId, token).ConfigureAwait(true);

            Apply(await _api.GetMessageAsync(MessageId, token).ConfigureAwait(true));
        }, cancellationToken);

    /// <summary>Records that an address is somebody AgencyOS knows.</summary>
    public Task ResolveParticipantAsync(
        Guid participantId,
        Guid? personId,
        Guid? companyId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .ResolveParticipantAsync(
                    MessageId,
                    participantId,
                    new ResolveParticipantRequest(personId, companyId),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            Apply(await _api.GetMessageAsync(MessageId, token).ConfigureAwait(true));
        }, cancellationToken);

    /// <summary>Asks who an address might be, without deciding.</summary>
    public async Task<IReadOnlyList<ParticipantSuggestionResponse>> SuggestAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ParticipantSuggestionResponse> suggestions = [];

        await RunAsync(async token =>
        {
            suggestions = await _api.SuggestParticipantsAsync(address, token).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);

        return suggestions;
    }

    /// <summary>Pulls an attachment into the canonical document store.</summary>
    /// <remarks>
    /// Explicit, because it is the moment the bytes become something AgencyOS
    /// holds and is answerable for. Until then the attachment is known to exist
    /// and nothing more (ADR-0024).
    /// </remarks>
    public Task IngestAttachmentAsync(
        Guid attachmentId,
        IngestAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .IngestAttachmentAsync(attachmentId, request, idempotencyKey, token)
                .ConfigureAwait(true);

            Apply(await _api.GetMessageAsync(MessageId, token).ConfigureAwait(true));
        }, cancellationToken);

    private void Apply(MessageDetailResponse detail)
    {
        Message = detail;

        Participants.Clear();

        foreach (ParticipantResponse participant in detail.Participants)
        {
            Participants.Add(participant);
        }

        Attachments.Clear();

        foreach (MessageAttachmentResponse attachment in detail.Attachments)
        {
            Attachments.Add(attachment);
        }

        Links.Clear();

        foreach (MessageLinkResponse link in detail.Links)
        {
            Links.Add(link);
        }

        Thread.Clear();

        foreach (MessageSummaryResponse message in detail.Thread.OrderBy(x => x.OccurredAt))
        {
            Thread.Add(message);
        }

        OnPropertyChanged(nameof(UnresolvedParticipants));
    }
}

/// <summary>
/// Composing a message AgencyOS will actually send.
/// </summary>
/// <remarks>
/// <para>
/// This is the first thing in AgencyOS that does something irreversible outside
/// the database, and the screen is shaped around that. Composing writes a draft
/// and sends nothing. Queueing is the point of no return, and it is a separate
/// action with its own confirmation, because after it AgencyOS cannot take the
/// message back (ADR-0028).
/// </para>
/// <para>
/// There is no From field. The sending mailbox is chosen from the accounts the
/// user actually owns, and the server checks the ownership again: a client that
/// knew another account's identifier still cannot send as them.
/// </para>
/// </remarks>
public sealed class ComposeMessageViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private Guid? _accountId;
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private Guid? _inReplyToMessageId;
    private Guid? _dispatchId;
    private string? _state;
    private int _version;

    public ComposeMessageViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>Mailboxes this user may send from.</summary>
    public ObservableCollection<CommunicationAccountResponse> Accounts { get; } = [];

    public ObservableCollection<RecipientRequest> Recipients { get; } = [];

    /// <summary>Document versions to attach, by version rather than by document.</summary>
    /// <remarks>
    /// A version, deliberately. "The contract" changes; the thing that was sent
    /// does not, and a year later the question is which draft went out (ADR-0024).
    /// </remarks>
    public ObservableCollection<Guid> AttachmentVersionIds { get; } = [];

    public Guid? AccountId
    {
        get => _accountId;
        set
        {
            if (Set(ref _accountId, value))
            {
                OnPropertyChanged(nameof(CanCompose));
            }
        }
    }

    public string Subject
    {
        get => _subject;
        set
        {
            if (Set(ref _subject, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanCompose));
            }
        }
    }

    public string Body
    {
        get => _body;
        set => Set(ref _body, value ?? string.Empty);
    }

    public Guid? InReplyToMessageId
    {
        get => _inReplyToMessageId;
        set => Set(ref _inReplyToMessageId, value);
    }

    /// <summary>The dispatch this compose produced, once it produced one.</summary>
    public Guid? DispatchId
    {
        get => _dispatchId;
        private set
        {
            if (Set(ref _dispatchId, value))
            {
                OnPropertyChanged(nameof(IsComposed));
                OnPropertyChanged(nameof(CanQueue));
            }
        }
    }

    /// <summary>The dispatch state, once there is a dispatch.</summary>
    public string? State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(CanQueue));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public string StateText => OutboundFormatting.State(_state);

    public override bool IsEmpty => false;

    public bool IsComposed => _dispatchId is not null;

    public bool CanCompose =>
        _accountId is not null
        && !string.IsNullOrWhiteSpace(_subject)
        && Recipients.Count > 0;

    /// <summary>The last moment at which nothing has left.</summary>
    public bool CanQueue => _dispatchId is not null && OutboundFormatting.CanQueue(_state);

    public bool CanCancel => _dispatchId is not null && OutboundFormatting.CanCancel(_state);

    /// <summary>Shown on the queue confirmation. It says what actually happens.</summary>
    public static string QueueWarning =>
        "Queueing hands this message to the send worker. AgencyOS cannot recall a "
        + "message once a provider has accepted it.";

    public Task LoadAccountsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<CommunicationAccountResponse> accounts = await _api
                .ListCommunicationAccountsAsync(token)
                .ConfigureAwait(true);

            Accounts.Clear();

            foreach (CommunicationAccountResponse account in accounts.Where(
                x => string.Equals(x.State, "Connected", StringComparison.Ordinal)))
            {
                Accounts.Add(account);
            }
        }, cancellationToken);

    public void AddRecipient(string role, string address, string? displayName = null)
    {
        Recipients.Add(new RecipientRequest(role, address, displayName));

        OnPropertyChanged(nameof(CanCompose));
    }

    public void RemoveRecipient(RecipientRequest recipient)
    {
        Recipients.Remove(recipient);

        OnPropertyChanged(nameof(CanCompose));
    }

    /// <summary>Writes the draft. Sends nothing.</summary>
    public Task ComposeAsync(
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ComposeMessageRequest request = new(
                AccountId ?? Guid.Empty,
                Subject.Trim(),
                Body,
                [.. Recipients],
                AttachmentVersionIds.Count == 0 ? null : [.. AttachmentVersionIds],
                InReplyToMessageId);

            ComposeMessageResponse response = await _api
                .ComposeMessageAsync(request, idempotencyKey, token)
                .ConfigureAwait(true);

            DispatchId = response.DispatchId;

            OutboundDispatchResponse dispatch = await _api
                .GetOutboundMessageAsync(response.DispatchId, token)
                .ConfigureAwait(true);

            State = dispatch.State;
            _version = dispatch.Version;
        }, cancellationToken);

    /// <summary>Hands the draft to the worker. The point of no return.</summary>
    public Task QueueAsync(
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .QueueMessageAsync(
                    DispatchId ?? Guid.Empty,
                    new QueueMessageRequest(_version),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            OutboundDispatchResponse dispatch = await _api
                .GetOutboundMessageAsync(DispatchId ?? Guid.Empty, token)
                .ConfigureAwait(true);

            State = dispatch.State;
            _version = dispatch.Version;
        }, cancellationToken);

    public Task CancelAsync(
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .CancelMessageAsync(
                    DispatchId ?? Guid.Empty,
                    new CancelMessageRequest(_version),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            OutboundDispatchResponse dispatch = await _api
                .GetOutboundMessageAsync(DispatchId ?? Guid.Empty, token)
                .ConfigureAwait(true);

            State = dispatch.State;
            _version = dispatch.Version;
        }, cancellationToken);
}

/// <summary>
/// Everything AgencyOS has tried to send.
/// </summary>
/// <remarks>
/// The unknown outcomes sort to the top and are counted separately from the
/// failures, because they are a different problem: a failure needs a decision
/// about whether to try again, and an unknown outcome needs somebody to go and
/// look at a mailbox (ADR-0028).
/// </remarks>
public sealed class OutboundListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _state;

    public OutboundListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<OutboundDispatchResponse> Dispatches { get; } = [];

    public string? State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public override bool IsEmpty => _loaded && Dispatches.Count == 0;
    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>Dispatches whose outcome nobody knows. Never counted as failures.</summary>
    public int UnknownOutcomes => Dispatches.Count(x => x.State == "UnknownOutcome");

    public int Failed => Dispatches.Count(x => x.State == "FailedPermanent");

    public int Sent => Dispatches.Count(x => x.State == "Sent");

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<OutboundDispatchResponse> dispatches = await _api
                .ListOutboundMessagesAsync(State, limit: null, token)
                .ConfigureAwait(true);

            Dispatches.Clear();

            // Attention first, then most recent. A desk that has to scroll to find
            // the message nobody knows the fate of will not find it.
            foreach (OutboundDispatchResponse dispatch in dispatches
                .OrderByDescending(x => x.NeedsAttention)
                .ThenByDescending(x => x.UpdatedAt))
            {
                Dispatches.Add(dispatch);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(UnknownOutcomes));
            OnPropertyChanged(nameof(Failed));
            OnPropertyChanged(nameof(Sent));
        }, cancellationToken);
}

/// <summary>
/// The communications desk: what needs a person, and nothing else.
/// </summary>
/// <remarks>
/// Not an inbox clone, and nothing here is ranked, summarized or triaged. M10
/// records communications; deciding which of them matters is a judgment, and this
/// build does not make judgments (ADR-0026).
/// </remarks>
public sealed class CommunicationCommandCenterViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private CommunicationCommandCenterResponse? _center;

    public CommunicationCommandCenterViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<OutboundDispatchResponse> UnknownOutcomes { get; } = [];

    public ObservableCollection<OutboundDispatchResponse> FailedSends { get; } = [];

    public ObservableCollection<CommunicationAccountResponse> AccountsNeedingAttention { get; } = [];

    public override bool IsEmpty =>
        _center is not null
        && UnknownOutcomes.Count == 0
        && FailedSends.Count == 0
        && AccountsNeedingAttention.Count == 0;

    /// <summary>Whether the desk has been read. This view model keeps the projection itself rather than a flag, so holding one is what having loaded means.</summary>
    public bool HasLoaded => _center is not null;

    public int UnknownOutcomeCount => _center?.UnknownOutcomeCount ?? 0;

    public int FailedSendCount => _center?.FailedSendCount ?? 0;

    public int DisconnectedAccountCount => _center?.DisconnectedAccountCount ?? 0;

    /// <summary>Whether anything at all needs a person right now.</summary>
    public bool NeedsAttention =>
        UnknownOutcomeCount > 0 || FailedSendCount > 0 || DisconnectedAccountCount > 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            CommunicationCommandCenterResponse center = await _api
                .GetCommunicationCommandCenterAsync(token)
                .ConfigureAwait(true);

            _center = center;

            UnknownOutcomes.Clear();

            foreach (OutboundDispatchResponse dispatch in center.UnknownOutcomes)
            {
                UnknownOutcomes.Add(dispatch);
            }

            FailedSends.Clear();

            foreach (OutboundDispatchResponse dispatch in center.FailedSends)
            {
                FailedSends.Add(dispatch);
            }

            AccountsNeedingAttention.Clear();

            foreach (CommunicationAccountResponse account in center.AccountsNeedingAttention)
            {
                AccountsNeedingAttention.Add(account);
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(UnknownOutcomeCount));
            OnPropertyChanged(nameof(FailedSendCount));
            OnPropertyChanged(nameof(DisconnectedAccountCount));
            OnPropertyChanged(nameof(NeedsAttention));
        }, cancellationToken);
}
