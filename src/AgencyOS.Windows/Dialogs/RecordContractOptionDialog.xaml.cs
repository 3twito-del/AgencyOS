using System;
using System.Collections.Generic;
using System.Globalization;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records an option a contract grants.
/// </summary>
/// <remarks>
/// <para>
/// A right one party holds against the other, with a deadline: a second season, a
/// sequel, an extension, a purchase. The deadline is the M8 rule shape, and its
/// passing is never enough on its own — an option past its date stays available
/// until a person records that it lapsed, because a clock noticing is not a legal
/// fact (ADR-0022).
/// </para>
/// <para>
/// Reality Closure wave 5 found the product could record an option's outcome and
/// could not record the option. The resolve dialog had been delivered since M8
/// against objects no supported workflow could create.
/// </para>
/// </remarks>
public sealed partial class RecordContractOptionDialog : ContentDialog
{
    private readonly Guid _contractVersionId;

    public RecordContractOptionDialog(
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
            HolderBox.Items.Add(new ComboBoxItem { Content = party.DisplayName, Tag = party.Id });
        }

        UpdateReady();
    }

    public RecordOptionRequest ToRequest()
    {
        string deadlineKind = SelectedTag(DeadlineKindBox) ?? "Absolute";

        return new RecordOptionRequest(
            _contractVersionId,
            SelectedTag(KindBox) ?? "Other",
            SelectedGuid(HolderBox),
            (SubjectBox.Text ?? string.Empty).Trim(),
            new DeadlineRuleRequest(
                deadlineKind,
                deadlineKind == "Absolute" && DeadlinePicker.Date is { } deadline
                    ? DateOnly.FromDateTime(deadline.DateTime)
                    : null,
                Description: deadlineKind == "Unstructured" ? Empty(DeadlineWordingBox.Text) : null),
            AnchorDate: null,
            WindowOpensOn: WindowPicker.Date is { } opens
                ? DateOnly.FromDateTime(opens.DateTime)
                : null,
            ClauseReference: Empty(ClauseBox.Text),
            ExerciseMethod: Empty(ExerciseBox.Text),
            ProjectId: null,
            SourcePropertyId: null,
            EconomicsTermId: null,
            Notes: Empty(NotesBox.Text));
    }

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDeadlineDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdateReady();

    private void OnDeadlineKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeadlinePicker is null)
        {
            return;
        }

        string kind = SelectedTag(DeadlineKindBox) ?? "Absolute";

        DeadlinePicker.Visibility = kind == "Absolute" ? Visibility.Visible : Visibility.Collapsed;
        DeadlineWordingBox.Visibility = kind == "Unstructured"
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateReady();
    }

    private void UpdateReady()
    {
        if (SubjectBox is null)
        {
            return;
        }

        string kind = SelectedTag(DeadlineKindBox) ?? "Absolute";

        bool deadlineAnswered = kind switch
        {
            "Absolute" => DeadlinePicker.Date is not null,
            "Unstructured" => !string.IsNullOrWhiteSpace(DeadlineWordingBox.Text),
            _ => true,
        };

        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(SubjectBox.Text)
            && SelectedGuid(HolderBox) != Guid.Empty
            && deadlineAnswered;
    }

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static Guid SelectedGuid(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag is Guid id ? id : Guid.Empty;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
