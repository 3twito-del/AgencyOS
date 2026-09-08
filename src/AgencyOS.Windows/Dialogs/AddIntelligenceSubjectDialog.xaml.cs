using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Names a record that a piece of intelligence is about.
/// </summary>
/// <remarks>
/// The same dialog serves signals, theses, predictions, watchlists and research
/// cases, because all five name subjects the same way and through the same table.
/// One screen rather than five keeps the vocabulary identical everywhere — a
/// watchlist "entry" and a signal "subject" are the same thing (§59).
/// </remarks>
public sealed partial class AddIntelligenceSubjectDialog : ContentDialog
{
    /// <param name="headline">What the subject is being added to.</param>
    public AddIntelligenceSubjectDialog(string headline)
    {
        InitializeComponent();

        HeadlineText.Text = headline;
    }

    /// <summary>Person, Company, Project and the rest.</summary>
    public string Kind => (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Person";

    /// <summary>The record identifier. Only read once the button is enabled.</summary>
    public Guid SubjectId =>
        Guid.TryParse(IdBox.Text, out Guid id) ? id : Guid.Empty;

    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Guid.TryParse(IdBox.Text, out _);
}
