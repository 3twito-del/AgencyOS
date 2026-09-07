using System;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Records who is attached to a role.</summary>
/// <remarks>
/// The dialog states, in the surface itself, that an attachment is a claim about
/// the world rather than an intention. That distinction is the whole reason
/// Attachment and PackageElement are separate concepts, and it is easiest to lose
/// at the point somebody is typing a name in (ADR-0019).
/// </remarks>
public sealed partial class AttachToRoleDialog : ContentDialog
{
    public AttachToRoleDialog(string roleType, string? roleLabel)
    {
        InitializeComponent();

        RoleText.Text = string.IsNullOrWhiteSpace(roleLabel)
            ? roleType
            : $"{roleType} - {roleLabel}";

        PartyKindBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 1;
        StartsOnPicker.Date = DateTimeOffset.UtcNow;
    }

    public AttachToRoleRequest ToRequest(int expectedVersion)
    {
        Guid? party = Guid.TryParse(PartyIdBox.Text.Trim(), out Guid parsed) ? parsed : null;

        bool isPerson = SelectedTag(PartyKindBox) != "Company";

        return new AttachToRoleRequest(
            SelectedTag(StatusBox) ?? "Attached",
            DateOnly.FromDateTime(StartsOnPicker.Date.DateTime),
            expectedVersion,
            isPerson ? party : null,
            isPerson ? null : party,
            EndsOn: null,
            Empty(SourceBox.Text),
            Empty(NotesBox.Text));
    }

    private void OnPartyKindChanged(object sender, SelectionChangedEventArgs e) => Validate();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate() =>
        IsPrimaryButtonEnabled = Guid.TryParse(PartyIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
