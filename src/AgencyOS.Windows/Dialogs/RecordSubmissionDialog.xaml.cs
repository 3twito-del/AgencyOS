using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Records that material went to a target.</summary>
/// <remarks>
/// The surface says twice, in different words, that AgencyOS is recording rather
/// than sending: once in the title of the notice and once in the snapshot hint.
/// This is the screen where somebody would most easily assume the software just
/// emailed a studio, and it must not be possible to think that (ADR-0020).
/// </remarks>
public sealed partial class RecordSubmissionDialog : ContentDialog
{
    /// <summary>Offered so that attaching no material is an explicit choice.</summary>
    private static readonly EntityChoice Nothing = new(Guid.Empty, "Nothing attached");

    /// <param name="targetName">Who it went to.</param>
    /// <param name="materials">
    /// The materials belonging to the people this pursuit is about, chosen by
    /// title, type and version (<c>AOS-R001-006</c>). Optional, as it was before:
    /// "nothing attached" remains a real answer.
    /// </param>
    public RecordSubmissionDialog(string targetName, IReadOnlyList<EntityChoice> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        InitializeComponent();

        // The hint under this field describes it. Declaring that lets a screen
        // reader reach the explanation from the field, instead of the reader
        // having to find it by looking (AOS-R002-011).
        AutomationProperties.GetDescribedBy(MaterialBox).Add(SnapshotHint);

        // "Nothing" is a first-class answer here, not an empty selection.
        MaterialBox.ItemsSource = new[] { Nothing }.Concat(materials).ToList();
        MaterialBox.SelectedItem = Nothing;

        TargetText.Text = $"To {targetName}";

        ChannelBox.SelectedIndex = 0;
        // Local, because that is what the pickers show and what the operator is
        // answering about. The API boundary makes it canonical UTC (AOS-R002-001).
        (DateTimeOffset date, TimeSpan timeOfDay) = LocalInstant.Now();

        SentPicker.Date = date;
        SentTimePicker.Time = timeOfDay;

        // A calendar date, not an instant: it stays a date picker.
        ResponsePicker.Date = date.AddDays(14);
    }

    public RecordSubmissionRequest ToRequest(int expectedVersion)
    {
        SubmissionMaterialRequest[] materials =
            MaterialBox.SelectedItem is EntityChoice material && material.Id != Guid.Empty
                ? [new SubmissionMaterialRequest(material.Id)]
                : [];

        OpportunityFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new OpportunityFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), ResponsePicker.Date)
                : null;

        return new RecordSubmissionRequest(
            SelectedTag(ChannelBox) ?? "Email",
            expectedVersion,
            LocalInstant.From(SentPicker.Date, SentTimePicker.Time),
            materials,
            Empty(SubjectBox.Text),
            Empty(NotesBox.Text),
            DateOnly.FromDateTime(ResponsePicker.Date.DateTime),
            ExternalReference: null,
            followUp);
    }

    private void OnMaterialChanged(object sender, SelectionChangedEventArgs e)
    {
        // Nothing to validate: a submission with no material is a legitimate
        // record of a phone call in which something was described.
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
