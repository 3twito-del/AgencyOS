using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records an observed claim, with the evidence for it.
/// </summary>
/// <remarks>
/// <para>
/// The source picker is not optional and the primary button stays disabled until one
/// is chosen. That is the milestone's central rule expressed as an interface: a claim
/// with no provenance is a rumour with a database row, and the cheapest place to
/// refuse it is before it is written (§1).
/// </para>
/// <para>
/// The dialog asks for the claim and for what the operator thinks of it, and asks
/// separately. Confidence is theirs; the claim is what somebody said happened; and
/// verification — whether other evidence agrees — is a later act by whoever finds
/// that evidence.
/// </para>
/// </remarks>
public sealed partial class RecordSignalDialog : ContentDialog
{
    /// <param name="sources">
    /// The sources this operator may cite. Already narrowed by the server to what
    /// they can read, so a classification they lack never appears in the list.
    /// </param>
    public RecordSignalDialog(IReadOnlyList<IntelligenceSourceResponse> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        InitializeComponent();

        SourceBox.ItemsSource = sources.ToList();

        if (sources.Count > 0)
        {
            SourceBox.SelectedIndex = 0;
        }

        ObservedPicker.Date = DateTimeOffset.Now;
        UpdatePrimary();
    }

    /// <summary>What the operator filled in, as the API expects it.</summary>
    public RecordSignalRequest ToRequest() =>
        new(
            Text(TitleBox),
            Text(ClaimBox),
            Selection(KindBox) ?? "Other",
            Selection(SensitivityBox) ?? "Internal",
            [
                new SignalEvidenceRequest(
                    Chosen()?.Id ?? Guid.Empty,
                    Selection(RoleBox) ?? "Primary",
                    Optional(ExcerptBox),
                    Optional(LocatorBox)),
            ],
            Subjects: null,
            OccurredPicker.Date,
            ObservedPicker.Date,
            Selection(ConfidenceBox) ?? "Unstated",
            Optional(NotesBox));

    /// <summary>The source the operator chose, or null while none is chosen.</summary>
    public IntelligenceSourceResponse? Chosen() =>
        SourceBox.SelectedItem as IntelligenceSourceResponse;

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdatePrimary();

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e) => UpdatePrimary();

    private void UpdatePrimary() =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(TitleBox.Text)
            && !string.IsNullOrWhiteSpace(ClaimBox.Text)
            && Chosen() is not null;

    private static string Text(TextBox box) => (box.Text ?? string.Empty).Trim();

    private static string? Optional(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private static string? Selection(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
