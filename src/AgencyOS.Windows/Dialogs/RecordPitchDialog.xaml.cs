using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a pitch, and the interaction it is the commercial reading of.
/// </summary>
/// <remarks>
/// The user never creates an interaction first. Two steps is how one meeting ends
/// up in the system twice with the same participants and the same timestamp, so
/// this dialog produces both facts in one command (ADR-0020).
/// </remarks>
public sealed partial class RecordPitchDialog : ContentDialog
{
    /// <summary>Offered so that recording no material is an explicit choice.</summary>
    private static readonly EntityChoice Nothing = new(Guid.Empty, "Nothing was shown");

    private readonly OpportunityTargetResponse _target;

    /// <param name="target">Who the pitch went to.</param>
    /// <param name="materials">
    /// The materials belonging to the people this pursuit is about. Chosen by
    /// title, type and version rather than typed as an identifier
    /// (<c>AOS-R001-006</c>); "nothing shown" stays a real answer, because a pitch
    /// is often a conversation rather than a document.
    /// </param>
    public RecordPitchDialog(
        OpportunityTargetResponse target,
        IReadOnlyList<EntityChoice> materials)
    {
        ArgumentNullException.ThrowIfNull(target);

        ArgumentNullException.ThrowIfNull(materials);

        InitializeComponent();

        // "Nothing" is a first-class answer here, not an empty selection.
        MaterialBox.ItemsSource = new[] { Nothing }.Concat(materials).ToList();
        MaterialBox.SelectedItem = Nothing;

        _target = target;

        TargetText.Text = target.ContactDisplayName is { Length: > 0 } contact
            ? $"{target.DisplayName} - {contact}"
            : target.DisplayName;

        TypeBox.SelectedIndex = 0;
        KindBox.SelectedIndex = 0;
        OutcomeBox.SelectedIndex = 0;
        OccurredPicker.Date = DateTimeOffset.UtcNow;
    }

    public RecordPitchRequest ToRequest(int expectedVersion)
    {
        // The party pitched is the target itself: the contact when there is one,
        // otherwise the company or person. That is who was in the room.
        PitchParticipantRequest participant = _target.ContactPersonId is { } contact
            ? new PitchParticipantRequest("Person", contact, "Contact")
            : _target.CompanyId is { } company
                ? new PitchParticipantRequest("Company", company, "Buyer")
                : new PitchParticipantRequest("Person", _target.PersonId!.Value, "Buyer");

        SubmissionMaterialRequest[] materials =
            MaterialBox.SelectedItem is EntityChoice material && material.Id != Guid.Empty
                ? [new SubmissionMaterialRequest(material.Id)]
                : [];

        OpportunityFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new OpportunityFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), DateTimeOffset.UtcNow.AddDays(7))
                : null;

        return new RecordPitchRequest(
            SelectedTag(TypeBox) ?? "Meeting",
            SummaryBox.Text.Trim(),
            [participant],
            SelectedTag(KindBox) ?? "Formal",
            SelectedTag(OutcomeBox) ?? "NoDecision",
            expectedVersion,
            OccurredPicker.Date,
            materials,
            Empty(SummaryBox.Text),
            Empty(NotesBox.Text),
            DetailedNotes: null,
            followUp);
    }

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(SummaryBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
