using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Attaches existing work to a research case.
/// </summary>
/// <remarks>
/// Attaching never copies. The source, signal, thesis, prediction or task stays where
/// it lives and keeps its own classification, which is why a case can be Internal
/// while something attached to it is not — and why a reader sees only the parts they
/// are entitled to (§28).
/// </remarks>
public sealed partial class LinkResearchItemDialog : ContentDialog
{
    public LinkResearchItemDialog() => InitializeComponent();

    /// <summary>Source, Signal, Thesis, Prediction or Task.</summary>
    public string Kind => (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Source";

    public Guid LinkedId => Guid.TryParse(IdBox.Text, out Guid id) ? id : Guid.Empty;

    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Guid.TryParse(IdBox.Text, out _);
}
