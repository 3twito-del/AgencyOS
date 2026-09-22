using System;
using System.Collections.Generic;
using System.Globalization;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a promise a contract contains.
/// </summary>
/// <remarks>
/// <para>
/// The non-monetary half of M8. What a contract obliges somebody to <em>pay</em> is
/// a monetary obligation, which is a different aggregate on a different tab and
/// ends in a receivable. What it obliges somebody to <em>do</em> — deliver, render
/// services, give notice, carry insurance, accord a credit, stay off a competing
/// picture — is this, and it ends in somebody recording what became of it
/// (ADR-0022).
/// </para>
/// <para>
/// Reality Closure wave 5 found the product could record what became of an
/// obligation and could not record that one existed. The resolve dialog had been
/// delivered since M8 against objects no supported workflow could create.
/// </para>
/// <para>
/// Nothing here decides anything. The kind vocabulary is the domain's, the parties
/// are the contract's own, and whether the version still accepts an obligation is
/// the server's answer.
/// </para>
/// </remarks>
public sealed partial class RecordObligationDialog : ContentDialog
{
    private readonly Guid _contractVersionId;

    public RecordObligationDialog(
        string contractTitle,
        ContractVersionResponse version,
        IReadOnlyList<ContractPartyResponse> parties)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(parties);

        InitializeComponent();

        _contractVersionId = version.Id;

        HeadlineText.Text = contractTitle;

        VersionText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Read out of version {version.VersionNumber}, {version.Label}.");

        foreach (ContractPartyResponse party in parties)
        {
            ObligorBox.Items.Add(new ComboBoxItem { Content = party.DisplayName, Tag = party.Id });
            ObligeeBox.Items.Add(new ComboBoxItem { Content = party.DisplayName, Tag = party.Id });
        }

        UpdateReady();
    }

    public RecordObligationRequest ToRequest()
    {
        string dueKind = SelectedTag(DueKindBox) ?? "Absolute";

        return new RecordObligationRequest(
            _contractVersionId,
            SelectedGuid(ObligorBox),
            SelectedGuid(ObligeeBox),
            SelectedTag(KindBox) ?? "Other",
            (DescriptionBox.Text ?? string.Empty).Trim(),
            new DeadlineRuleRequest(
                dueKind,
                dueKind == "Absolute" && DuePicker.Date is { } due
                    ? DateOnly.FromDateTime(due.DateTime)
                    : null,
                Description: dueKind == "Unstructured" ? Empty(DueWordingBox.Text) : null),
            AnchorDate: null,
            ClauseReference: Empty(ClauseBox.Text),
            RelatedOptionId: null,
            RelatedRightsGrantId: null,
            Notes: Empty(NotesBox.Text));
    }

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDueDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdateReady();

    private void OnDueKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DuePicker is null)
        {
            return;
        }

        string kind = SelectedTag(DueKindBox) ?? "Absolute";

        DuePicker.Visibility = kind == "Absolute" ? Visibility.Visible : Visibility.Collapsed;
        DueWordingBox.Visibility = kind == "Unstructured" ? Visibility.Visible : Visibility.Collapsed;

        UpdateReady();
    }

    /// <summary>
    /// Whether the request can be built at all.
    /// </summary>
    /// <remarks>
    /// Only that the fields the request needs have been answered. Whether an
    /// obligor may also be the obligee, and whether this version still takes
    /// obligations, are the server's to decide.
    /// </remarks>
    private void UpdateReady()
    {
        if (DescriptionBox is null)
        {
            return;
        }

        string dueKind = SelectedTag(DueKindBox) ?? "Absolute";

        bool dueAnswered = dueKind switch
        {
            "Absolute" => DuePicker.Date is not null,
            "Unstructured" => !string.IsNullOrWhiteSpace(DueWordingBox.Text),
            _ => true,
        };

        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(DescriptionBox.Text)
            && SelectedGuid(ObligorBox) != Guid.Empty
            && SelectedGuid(ObligeeBox) != Guid.Empty
            && dueAnswered;
    }

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static Guid SelectedGuid(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag is Guid id ? id : Guid.Empty;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
