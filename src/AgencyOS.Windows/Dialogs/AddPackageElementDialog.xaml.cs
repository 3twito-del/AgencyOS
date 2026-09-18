using System;
using System.Collections.Generic;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Adds one item to a package.</summary>
/// <remarks>
/// The dialog names the distinction it is easiest to lose while typing: an
/// attached party is a fact about the project, everything else is the agency's own
/// working list (ADR-0019).
/// </remarks>
public sealed partial class AddPackageElementDialog : ContentDialog
{
    private readonly PackageElementSources _sources;

    /// <param name="sources">
    /// The records each kind of element can point at, gathered by the page from the
    /// package's own project and from this organization's people and companies. A
    /// package element names something that already exists, so the operator chooses
    /// it rather than typing an identifier (<c>AOS-R001-006</c>).
    /// </param>
    public AddPackageElementDialog(PackageElementSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;

        InitializeComponent();

        // The hint under this field describes it. Declaring that lets a screen
        // reader reach the explanation from the field, instead of the reader
        // having to find it by looking (AOS-R002-011).
        AutomationProperties.GetDescribedBy(TargetBox).Add(TargetHint);

        KindBox.SelectedIndex = 1;
        ApplyKind();
    }

    public AddPackageElementRequest ToRequest(int expectedVersion) => new(
        SelectedTag(KindBox) ?? "ProposedPerson",
        Chosen()?.Id ?? Guid.Empty,
        expectedVersion,
        Empty(NoteBox.Text));

    /// <summary>The record the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => TargetBox.SelectedItem as EntityChoice;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ApplyKind();

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) => Validate();

    /// <summary>
    /// Points the picker at the records this kind of element can name, and drops
    /// whatever was chosen under the previous kind.
    /// </summary>
    /// <remarks>
    /// Six kinds, six explicit arms. The server checks each one against a different
    /// table and against this package's own project, so a person chosen while the
    /// kind was "proposed person" is not an open role and must not survive the
    /// change (§12).
    /// </remarks>
    private void ApplyKind()
    {
        if (TargetBox is null)
        {
            return;
        }

        TargetBox.SelectedItem = null;

        (IReadOnlyList<EntityChoice> items, string header, string hint) =
            SelectedTag(KindBox) switch
            {
                "AttachedParty" => (
                    _sources.Attachments,
                    "Which attachment",
                    "Somebody already attached to a role on this project."),
                "ProposedCompany" => (
                    _sources.Companies,
                    "Which company",
                    "A company being proposed. Proposing is not attaching."),
                "OpenRole" => (
                    _sources.Roles,
                    "Which role",
                    "A role on this project that nobody fills yet."),
                "Material" => (
                    _sources.Materials,
                    "Which material",
                    "A material already filed against this project."),
                "SourceProperty" => (
                    _sources.SourceProperties,
                    "Which source property",
                    "The underlying work this project comes from."),
                _ => (
                    _sources.People,
                    "Who",
                    "Somebody being proposed. Proposing is not attaching."),
            };

        TargetBox.ItemsSource = items;
        TargetBox.Header = header;
        TargetHint.Text = items.Count == 0
            ? hint + " There are none on this project yet."
            : hint;

        Validate();
    }

    private void Validate() => IsPrimaryButtonEnabled = Chosen() is not null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
