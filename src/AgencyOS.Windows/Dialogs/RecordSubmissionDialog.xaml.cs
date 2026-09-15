using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
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

        // "Nothing" is a first-class answer here, not an empty selection.
        MaterialBox.ItemsSource = new[] { Nothing }.Concat(materials).ToList();
        MaterialBox.SelectedItem = Nothing;

        TargetText.Text = $"To {targetName}";

        ChannelBox.SelectedIndex = 0;
        SentPicker.Date = DateTimeOffset.UtcNow;
        ResponsePicker.Date = DateTimeOffset.UtcNow.AddDays(14);
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
            SentPicker.Date,
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
