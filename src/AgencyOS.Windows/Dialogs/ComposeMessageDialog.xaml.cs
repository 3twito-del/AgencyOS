using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgencyOS.Contracts.Documents;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Composes an outbound message.
/// </summary>
/// <remarks>
/// <para>
/// The first surface in AgencyOS that leads to something irreversible outside the
/// database, and it is shaped around that. This dialog produces a draft. Sending is
/// a separate action, taken from the outbound list, behind its own confirmation.
/// Collapsing the two into one button would put the point of no return under a key
/// somebody presses on the way to somewhere else (ADR-0028).
/// </para>
/// <para>
/// There is no From field. The sending mailbox comes from the accounts the user
/// actually owns, and the server checks the ownership again on arrival, so knowing
/// another account's identifier buys nothing.
/// </para>
/// </remarks>
public sealed partial class ComposeMessageDialog : ContentDialog
{
    public ComposeMessageDialog(
        IReadOnlyList<CommunicationAccountResponse> accounts,
        IReadOnlyList<(Guid VersionId, string FileName)>? attachments = null,
        string? subject = null,
        Guid? inReplyToMessageId = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(accounts);

        InReplyToMessageId = inReplyToMessageId;

        foreach (CommunicationAccountResponse account in accounts)
        {
            AccountBox.Items.Add(new ComboBoxItem
            {
                Content = account.MailboxAddress,
                Tag = account.Id,
            });
        }

        if (AccountBox.Items.Count > 0)
        {
            AccountBox.SelectedIndex = 0;
        }

        if (subject is { Length: > 0 })
        {
            SubjectBox.Text = subject;
        }

        RecipientList.ItemsSource = _recipients;

        if (attachments is not null)
        {
            foreach ((Guid versionId, string fileName) in attachments)
            {
                _attachmentVersionIds.Add(versionId);
                _attachmentNames.Add(fileName);
            }

            AttachmentText.Text = _attachmentNames.Count == 0
                ? "No attachments"
                : "Attaching: " + string.Join(", ", _attachmentNames);
        }

        Update();
    }

    private readonly List<string> _attachmentNames = [];

    // Private, and exposed through methods rather than properties. The XAML
    // compiler generates type metadata for the public properties of a compiled
    // XAML class, and it cannot generate a setter for an init-only record - so a
    // public collection of contract records here fails the build rather than at
    // runtime, which is at least the right time to find out.
    private readonly ObservableCollection<RecipientRequest> _recipients = [];

    private readonly List<Guid> _attachmentVersionIds = [];

    /// <summary>The recipients, as entered.</summary>
    public RecipientRequest[] ToRecipients() => [.. _recipients];

    /// <summary>Document versions to attach, by version rather than by document.</summary>
    public Guid[] ToAttachmentVersionIds() => [.. _attachmentVersionIds];

    public Guid? InReplyToMessageId { get; }

    public Guid AccountId =>
        (AccountBox.SelectedItem as ComboBoxItem)?.Tag as Guid? ?? Guid.Empty;

    public string Subject => (SubjectBox.Text ?? string.Empty).Trim();

    public string Body => BodyBox.Text ?? string.Empty;

    private void OnAddRecipient(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string address = (AddressBox.Text ?? string.Empty).Trim();

        if (address.Length == 0)
        {
            return;
        }

        string role = (RoleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "To";

        _recipients.Add(new RecipientRequest(role, address));

        AddressBox.Text = string.Empty;

        Update();
    }

    private void OnRemoveRecipient(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (RecipientList.SelectedItem is RecipientRequest recipient)
        {
            _recipients.Remove(recipient);
        }

        Update();
    }

    private void OnChanged(object sender, object e) => Update();

    private void Update() =>
        IsPrimaryButtonEnabled =
            AccountId != Guid.Empty
            && Subject.Length > 0
            && _recipients.Count > 0;
}
