using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Documents;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The communications workspace: correspondence, mailboxes and what AgencyOS sent.
/// </summary>
/// <remarks>
/// <para>
/// Four surfaces over two different things. Messages and mailboxes are evidence:
/// mail that passed between real people, synchronized so it can be filed against
/// the business. Outbound is the opposite - it is AgencyOS acting, and every state
/// on it describes something the system did or does not know it did (ADR-0026,
/// ADR-0028).
/// </para>
/// <para>
/// Sending is two actions, not one. Composing writes a draft and sends nothing;
/// queueing hands it to the worker and is the point after which AgencyOS cannot
/// take the message back. The second is behind its own confirmation, which spells
/// that out.
/// </para>
/// <para>
/// An unknown outcome is never shown as a failure and never counted as one. It
/// means the provider did not answer, the message may already have gone, and the
/// only safe response is to look rather than to retry.
/// </para>
/// </remarks>
public sealed partial class CommunicationsPage : Page, IPaletteCommandTarget
{
    private const string RedirectUri = "http://localhost:5173/oauth/callback";

    private readonly MailboxListViewModel? _mailboxes;
    private readonly MessageListViewModel? _messages;
    private readonly MessageDetailViewModel? _messageDetail;
    private readonly OutboundListViewModel? _outbound;
    private readonly CommunicationCommandCenterViewModel? _desk;

    public CommunicationsPage()
    {
        InitializeComponent();

        MessageScopeText.Text = MessageListViewModel.SearchScopeNotice;

        if (AppServices.Api is not { } api)
        {
            return;
        }

        _mailboxes = new MailboxListViewModel(api);
        _mailboxes.PropertyChanged += (_, _) => RenderMailboxes();

        _messages = new MessageListViewModel(api);
        _messages.PropertyChanged += (_, _) => RenderMessages();

        _messageDetail = new MessageDetailViewModel(api);
        _messageDetail.PropertyChanged += (_, _) => RenderMessageDetail();

        _outbound = new OutboundListViewModel(api);
        _outbound.PropertyChanged += (_, _) => RenderOutbound();

        _desk = new CommunicationCommandCenterViewModel(api);
        _desk.PropertyChanged += (_, _) => RenderDesk();

        MailboxList.ItemsSource = _mailboxes.Accounts;
        MessageList.ItemsSource = _messages.Messages;
        ParticipantList.ItemsSource = _messageDetail.Participants;
        AttachmentList.ItemsSource = _messageDetail.Attachments;
        MessageLinkList.ItemsSource = _messageDetail.Links;
        OutboundList.ItemsSource = _outbound.Dispatches;
        DeskList.ItemsSource = _desk.UnknownOutcomes;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "go.mailboxes":
                SelectTab("Mailboxes");
                break;

            case "go.messages":
                SelectTab("Messages");
                break;

            case "go.messages.unlinked":
                UnlinkedBox.IsChecked = true;
                SelectTab("Messages");
                ApplyMessageFilters();
                break;

            case "go.outbound":
                SelectTab("Outbound");
                break;

            case "go.outbound.unknown":
                SelectComboTag(OutboundStateBox, "UnknownOutcome");
                SelectTab("Outbound");
                ApplyOutboundFilter();
                break;

            case "go.communications.command-center":
                SelectTab("Desk");
                break;

            case "mailbox.connect":
                _ = ConnectMailboxAsync();
                break;

            case "mailbox.disconnect":
                _ = DisconnectAsync();
                break;

            case "mailbox.visibility":
                _ = ChangeVisibilityAsync();
                break;

            case "message.link":
                _ = LinkMessageAsync();
                break;

            case "message.unlink":
                _ = UnlinkMessageAsync();
                break;

            case "message.participant.resolve":
                _ = ResolveParticipantAsync();
                break;

            case "attachment.ingest":
                _ = IngestAttachmentAsync();
                break;

            case "message.compose":
                _ = ComposeAsync(null);
                break;

            case "message.queue":
                _ = QueueAsync();
                break;

            case "message.cancel":
                _ = CancelSendAsync();
                break;

            default:
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_mailboxes is null || _messages is null || _outbound is null || _desk is null)
        {
            return;
        }

        await _mailboxes.LoadAsync().ConfigureAwait(true);
        await _messages.LoadAsync().ConfigureAwait(true);
        await _outbound.LoadAsync().ConfigureAwait(true);
        await _desk.LoadAsync().ConfigureAwait(true);

        PopulateAccountFilter();
    }

    // ------------------------------------------------------------- filtering

    private void OnMessageFilterChanged(object sender, RoutedEventArgs e) => ApplyMessageFilters();

    private void OnOutboundFilterChanged(object sender, RoutedEventArgs e) => ApplyOutboundFilter();

    private void OnMessageSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_messages is not null)
        {
            _messages.Search = sender.Text ?? string.Empty;
            _ = _messages.LoadAsync();
        }
    }

    private void ApplyMessageFilters()
    {
        if (_messages is null)
        {
            return;
        }

        _messages.AccountId = (MessageAccountBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        _messages.Direction = SelectedTag(DirectionBox);
        _messages.UnlinkedOnly = UnlinkedBox.IsChecked == true;
        _messages.WithAttachmentsOnly = AttachmentsBox.IsChecked == true;

        _ = _messages.LoadAsync();
    }

    private void ApplyOutboundFilter()
    {
        if (_outbound is null)
        {
            return;
        }

        _outbound.State = SelectedTag(OutboundStateBox);

        _ = _outbound.LoadAsync();
    }

    /// <summary>
    /// The selected item's tag, or null when it is the "any" entry.
    /// </summary>
    /// <remarks>
    /// Named apart from <c>FrameworkElement.Tag</c>, which a page inherits. A
    /// helper called <c>Tag</c> compiles and hides it, and the next person to
    /// write <c>Tag = something</c> on the page gets a puzzle.
    /// </remarks>
    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null;

    private static void SelectComboTag(ComboBox box, string tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem entry && entry.Tag as string == tag)
            {
                box.SelectedItem = entry;
                return;
            }
        }
    }

    private void SelectTab(string header)
    {
        foreach (object item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && tab.Header as string == header)
            {
                Tabs.SelectedItem = tab;
                return;
            }
        }
    }

    private void PopulateAccountFilter()
    {
        if (_mailboxes is null || MessageAccountBox.Items.Count > 0)
        {
            return;
        }

        foreach (CommunicationAccountResponse account in _mailboxes.Accounts)
        {
            MessageAccountBox.Items.Add(new ComboBoxItem
            {
                Content = account.MailboxAddress,
                Tag = account.Id,
            });
        }
    }

    // ------------------------------------------------------------- selection

    private void OnMailboxSelected(object sender, SelectionChangedEventArgs e)
    {
        bool selected = MailboxList.SelectedItem is CommunicationAccountResponse;

        DisconnectButton.IsEnabled = selected;
        VisibilityButton.IsEnabled = selected;
    }

    private void OnMessageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_messageDetail is null || MessageList.SelectedItem is not MessageSummaryResponse selected)
        {
            return;
        }

        _messageDetail.MessageId = selected.Id;
        _ = _messageDetail.LoadAsync();
    }

    private void OnParticipantSelected(object sender, SelectionChangedEventArgs e) =>
        ResolveButton.IsEnabled = ParticipantList.SelectedItem is ParticipantResponse;

    private void OnAttachmentSelected(object sender, SelectionChangedEventArgs e) =>
        IngestButton.IsEnabled =
            AttachmentList.SelectedItem is MessageAttachmentResponse { HoldsContent: false };

    private void OnMessageLinkSelected(object sender, SelectionChangedEventArgs e) =>
        UnlinkMessageButton.IsEnabled = MessageLinkList.SelectedItem is MessageLinkResponse;

    private void OnOutboundSelected(object sender, SelectionChangedEventArgs e)
    {
        OutboundDispatchResponse? dispatch = OutboundList.SelectedItem as OutboundDispatchResponse;

        QueueButton.IsEnabled = OutboundFormatting.CanQueue(dispatch?.State);
        CancelSendButton.IsEnabled = OutboundFormatting.CanCancel(dispatch?.State);

        if (dispatch is null)
        {
            OutboundNotice.IsOpen = false;
            return;
        }

        // An unknown outcome gets the whole explanation rather than a status word.
        // It is the one state where doing the obvious thing is wrong (ADR-0028).
        if (dispatch.State == "UnknownOutcome")
        {
            OutboundNotice.Severity = InfoBarSeverity.Warning;
            OutboundNotice.Title = "Outcome unknown";
            OutboundNotice.Message = OutboundFormatting.UnknownOutcomeExplanation
                + "  " + OutboundFormatting.Verdict(dispatch.LastVerdict) + ".";
            OutboundNotice.IsOpen = true;
            return;
        }

        OutboundNotice.Severity = InfoBarSeverity.Informational;
        OutboundNotice.Title = OutboundFormatting.State(dispatch.State);
        OutboundNotice.Message = dispatch.LastError ?? string.Empty;
        OutboundNotice.IsOpen = true;
    }

    // -------------------------------------------------------------- commands

    private void OnConnectMailbox(object sender, RoutedEventArgs e) => _ = ConnectMailboxAsync();

    private void OnDisconnect(object sender, RoutedEventArgs e) => _ = DisconnectAsync();

    private void OnChangeVisibility(object sender, RoutedEventArgs e) => _ = ChangeVisibilityAsync();

    private void OnLinkMessage(object sender, RoutedEventArgs e) => _ = LinkMessageAsync();

    private void OnUnlinkMessage(object sender, RoutedEventArgs e) => _ = UnlinkMessageAsync();

    private void OnResolveParticipant(object sender, RoutedEventArgs e) =>
        _ = ResolveParticipantAsync();

    private void OnIngestAttachment(object sender, RoutedEventArgs e) => _ = IngestAttachmentAsync();

    private void OnComposeReply(object sender, RoutedEventArgs e) =>
        _ = ComposeAsync(MessageList.SelectedItem as MessageSummaryResponse);

    private void OnCompose(object sender, RoutedEventArgs e) => _ = ComposeAsync(null);

    private void OnQueue(object sender, RoutedEventArgs e) => _ = QueueAsync();

    private void OnCancelSend(object sender, RoutedEventArgs e) => _ = CancelSendAsync();

    private async Task ConnectMailboxAsync()
    {
        if (AppServices.Api is not { } api || _mailboxes is null)
        {
            return;
        }

        IReadOnlyList<CommunicationProviderResponse> providers = [];

        await Guarded(async () =>
        {
            providers = await api
                .ListCommunicationProvidersAsync(RedirectUri)
                .ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (providers.Count == 0)
        {
            Notice(
                "No provider is configured",
                "An administrator has to register the application with the mail provider before a mailbox can be connected. The steps are in ADR-0027.");
            return;
        }

        ConnectMailboxDialog dialog = new(providers, RedirectUri) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _mailboxes.ConnectAsync(
                new ConnectMailboxRequest(
                    dialog.Provider,
                    dialog.AuthorizationCode,
                    dialog.RedirectUri,
                    dialog.MailboxVisibility),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);
    }

    private async Task DisconnectAsync()
    {
        if (_mailboxes is null
            || MailboxList.SelectedItem is not CommunicationAccountResponse account)
        {
            Error("Select a mailbox first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Disconnect this mailbox",
            account.MailboxAddress,
            "Disconnecting destroys the stored credentials, and AgencyOS stops synchronizing and stops being able to send from this mailbox. The messages already synchronized stay: they are a record of what was said.",
            "Disconnect")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _mailboxes.DisconnectAsync(
                account.Id, account.Version, Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);
    }

    private async Task ChangeVisibilityAsync()
    {
        if (_mailboxes is null
            || MailboxList.SelectedItem is not CommunicationAccountResponse account)
        {
            Error("Select a mailbox first.");
            return;
        }

        MailboxVisibilityDialog dialog = new(account.MailboxAddress, account.Visibility)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _mailboxes.ChangeVisibilityAsync(
                account.Id, dialog.MailboxVisibility, account.Version, Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);
    }

    private async Task LinkMessageAsync()
    {
        if (_messageDetail?.Message is not { } message)
        {
            Error("Select a message first.");
            return;
        }

        if (AppServices.Api is not { } api)
        {
            return;
        }

        LinkRecordDialog dialog = new(api, $"File \"{message.Message.Subject}\" against a record")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Filing correspondence records that it bears on the deal. It does not
        // advance the deal: an email saying "we accept" is evidence somebody wrote
        // that, and accepting is still a command a person issues (ADR-0026).
        await Guarded(() => _messageDetail.LinkAsync(
                new LinkMessageRequest(dialog.Target, dialog.TargetId, dialog.Note),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        Notice(
            "Filed",
            "The message is recorded as bearing on that record. Nothing about the record itself changed.");
    }

    private async Task UnlinkMessageAsync()
    {
        if (_messageDetail is null || MessageLinkList.SelectedItem is not MessageLinkResponse link)
        {
            Error("Select a link first.");
            return;
        }

        await Guarded(() => _messageDetail.UnlinkAsync(link.Id)).ConfigureAwait(true);
    }

    private async Task ResolveParticipantAsync()
    {
        if (_messageDetail is null
            || ParticipantList.SelectedItem is not ParticipantResponse participant)
        {
            Error("Select a participant first.");
            return;
        }

        IReadOnlyList<ParticipantSuggestionResponse> suggestions = await _messageDetail
            .SuggestAsync(participant.Address)
            .ConfigureAwait(true);

        ResolveParticipantDialog dialog = new(
            participant.Address, participant.DisplayName, suggestions)
        {
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.None)
        {
            return;
        }

        // Secondary is "leave unidentified", which withdraws any identification
        // rather than cancelling. An address AgencyOS cannot place is a fact.
        (Guid? personId, Guid? companyId) = result == ContentDialogResult.Primary
            ? (dialog.Chosen()?.PersonId, dialog.Chosen()?.CompanyId)
            : (null, null);

        await Guarded(() => _messageDetail.ResolveParticipantAsync(
                participant.Id, personId, companyId, Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);
    }

    private async Task IngestAttachmentAsync()
    {
        if (_messageDetail is null
            || AttachmentList.SelectedItem is not MessageAttachmentResponse attachment)
        {
            Error("Select an attachment first.");
            return;
        }

        IngestAttachmentDialog dialog = new(
            attachment.FileName,
            DocumentFormatting.FormatSize(attachment.ByteLength))
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _messageDetail.IngestAttachmentAsync(
                attachment.Id,
                new IngestAttachmentRequest(
                    dialog.Kind, dialog.Sensitivity, dialog.DocumentTitle),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);
    }

    private async Task ComposeAsync(MessageSummaryResponse? replyTo)
    {
        if (AppServices.Api is not { } api || _mailboxes is null || _outbound is null)
        {
            return;
        }

        List<CommunicationAccountResponse> sendable =
        [
            .. _mailboxes.Accounts.Where(x =>
                string.Equals(x.State, "Connected", StringComparison.Ordinal)),
        ];

        if (sendable.Count == 0)
        {
            Notice(
                "No mailbox to send from",
                "Connect a mailbox first. AgencyOS sends from a real mailbox somebody owns, never from an address it invents.");
            return;
        }

        ComposeMessageDialog dialog = new(
            sendable,
            attachments: null,
            replyTo?.Subject is { Length: > 0 } subject ? "Re: " + subject : null,
            replyTo?.Id)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(async () =>
        {
            await api
                .ComposeMessageAsync(
                    new ComposeMessageRequest(
                        dialog.AccountId,
                        dialog.Subject,
                        dialog.Body,
                        dialog.ToRecipients(),
                        dialog.ToAttachmentVersionIds() is { Length: > 0 } attachments
                            ? attachments
                            : null,
                        dialog.InReplyToMessageId),
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);

            await _outbound.LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        SelectTab("Outbound");

        Notice(
            "Draft saved",
            "Nothing has been sent. Select it under Outbound and choose \"Send this draft\" to hand it to the send worker.");
    }

    /// <summary>
    /// Hands a draft to the send worker.
    /// </summary>
    /// <remarks>
    /// The point of no return, behind its own confirmation. After this AgencyOS
    /// cannot recall the message, and the confirmation says exactly that rather
    /// than asking "are you sure" (ADR-0028).
    /// </remarks>
    private async Task QueueAsync()
    {
        if (AppServices.Api is not { } api || _outbound is null
            || OutboundList.SelectedItem is not OutboundDispatchResponse dispatch)
        {
            Error("Select a draft first.");
            return;
        }

        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = "Send this message",
            Content = ComposeMessageViewModel.QueueWarning
                + $"\n\nSubject: {dispatch.Subject}\nFrom: {dispatch.MailboxAddress}\nRecipients: {dispatch.Recipients.Count}",
            PrimaryButtonText = "Send",
            CloseButtonText = "Not yet",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(async () =>
        {
            await api
                .QueueMessageAsync(
                    dispatch.Id,
                    new QueueMessageRequest(dispatch.Version),
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);

            await _outbound.LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task CancelSendAsync()
    {
        if (AppServices.Api is not { } api || _outbound is null
            || OutboundList.SelectedItem is not OutboundDispatchResponse dispatch)
        {
            Error("Select a message first.");
            return;
        }

        if (!OutboundFormatting.CanCancel(dispatch.State))
        {
            Error("This message has already reached the provider. AgencyOS cannot unsend it.");
            return;
        }

        await Guarded(async () =>
        {
            await api
                .CancelMessageAsync(
                    dispatch.Id,
                    new CancelMessageRequest(dispatch.Version),
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);

            await _outbound.LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    // --------------------------------------------------------------- surface

    private async Task Guarded(Func<Task> action)
    {
        try
        {
            ErrorBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            Error(failure.Detail ?? failure.Message);
        }
    }

    private void Error(string message)
    {
        ErrorBar.Title = "That did not happen";
        ErrorBar.Message = message;
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.IsOpen = true;
    }

    private void Notice(string title, string message)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.Severity = InfoBarSeverity.Informational;
        ErrorBar.IsOpen = true;
    }

    private void RenderMailboxes()
    {
        if (_mailboxes is null)
        {
            return;
        }

        Busy.Visibility = _mailboxes.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        if (_mailboxes.HasError)
        {
            Error(_mailboxes.ErrorMessage ?? string.Empty);
        }

        MailboxEmpty.IsOpen = _mailboxes.IsEmpty;
    }

    private void RenderMessages()
    {
        if (_messages is null)
        {
            return;
        }

        MessageEmpty.IsOpen = SummaryAuthority.Knows(_messages) && _messages.IsEmpty;

        if (_messages.HasError)
        {
            Error(_messages.ErrorMessage ?? string.Empty);
        }

        MessageSummary.Text = SummaryAuthority.Of(
            () => string.Create(
                CultureInfo.InvariantCulture,
                $"{_messages.Messages.Count} messages   {_messages.Unlinked} unfiled   "
                + $"{_messages.WithAttachments} with attachments"),
            _messages);
    }

    private void RenderMessageDetail()
    {
        if (_messageDetail is null)
        {
            return;
        }

        if (_messageDetail.HasError)
        {
            Error(_messageDetail.ErrorMessage ?? string.Empty);
        }

        if (_messageDetail.Message is not { } detail)
        {
            MessageSubject.Text = "Select a message";
            MessageDates.Text = string.Empty;
            MessageBody.Text = string.Empty;
            SanitizedBar.IsOpen = false;
            LinkMessageButton.IsEnabled = false;
            ReplyButton.IsEnabled = false;
            return;
        }

        MessageSubject.Text = _messageDetail.Subject;

        // Three dates kept apart: when it was sent or received, and when AgencyOS
        // first saw it. Merging them would invent a history nobody observed.
        MessageDates.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{detail.Message.Direction}   occurred {detail.Message.OccurredAt:u}   "
            + $"synchronized {detail.Message.SynchronizedAt:u}");

        // The plain text, deliberately. The sanitized HTML exists and is safe to
        // render, but this build shows text rather than shipping a WebView whose
        // navigation and script surface would have to be locked down first
        // (ADR-0026).
        MessageBody.Text = _messageDetail.BodyText
            ?? _messageDetail.SanitizedHtml
            ?? string.Empty;

        SanitizedBar.IsOpen = _messageDetail.HasHtml;
        SanitizedBar.Message = MessageDetailViewModel.SanitizationNotice;

        LinkMessageButton.IsEnabled = true;
        ReplyButton.IsEnabled = true;
    }

    private void RenderOutbound()
    {
        if (_outbound is null)
        {
            return;
        }

        OutboundEmpty.IsOpen = SummaryAuthority.Knows(_outbound) && _outbound.IsEmpty;

        if (_outbound.HasError)
        {
            Error(_outbound.ErrorMessage ?? string.Empty);
        }

        // Counted apart, always. An unknown outcome is not a failure.
        OutboundSummary.Text = SummaryAuthority.Of(
            () => string.Create(
                CultureInfo.InvariantCulture,
                $"{_outbound.Dispatches.Count} dispatches   {_outbound.Sent} sent   "
                + $"{_outbound.UnknownOutcomes} with an unknown outcome   {_outbound.Failed} failed"),
            _outbound);
    }

    private void RenderDesk()
    {
        if (_desk is null)
        {
            return;
        }

        // Wave 2 gated the sentence below and left these three counts ungated, so
        // the same panel could say "Whether anything needs attention is
        // unavailable" directly beneath "0 unknown outcomes   0 failed sends".
        // Both halves answer the same question and both need the same authority.
        DeskSummary.Text = SummaryAuthority.Of(
            () => string.Create(
                CultureInfo.InvariantCulture,
                $"{_desk.UnknownOutcomeCount} unknown outcomes   {_desk.FailedSendCount} failed sends   "
                + $"{_desk.DisconnectedAccountCount} mailboxes needing attention"),
            _desk);

        // An all-clear is a business claim, so it waits for the same authority.
        DeskClear.IsOpen = SummaryAuthority.Knows(_desk) && !_desk.NeedsAttention;

        AttentionBar.IsOpen = SummaryAuthority.Knows(_desk) && _desk.UnknownOutcomeCount > 0;
        AttentionBar.Message = OutboundFormatting.UnknownOutcomeExplanation;

        // Not a total, so it does not use SummaryAuthority.Of: the unavailable
        // wording has to match the claim, and "Totals unavailable." would be
        // answering a question nobody asked. The authority test is the same one.
        SummaryText.Text = !SummaryAuthority.Knows(_desk)
            ? "Whether anything needs attention is unavailable."
            : _desk.NeedsAttention
                ? "Something needs a person. See the Desk tab."
                : "Nothing needs attention.";
    }
}
