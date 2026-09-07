using System;
using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Adds an external party to a pursuit.</summary>
/// <remarks>
/// The contact field disables itself for a person target rather than accepting a
/// value the server would refuse. A form that lets somebody fill in a field which
/// cannot be saved wastes their time twice.
/// </remarks>
public sealed partial class AddOpportunityTargetDialog : ContentDialog
{
    public AddOpportunityTargetDialog()
    {
        InitializeComponent();

        KindBox.SelectedIndex = 0;
        NextActionPicker.Date = DateTimeOffset.UtcNow.AddDays(7);
    }

    public AddOpportunityTargetRequest ToRequest(int expectedVersion)
    {
        bool isCompany = SelectedTag(KindBox) != "Person";

        Guid? target = Guid.TryParse(TargetIdBox.Text.Trim(), out Guid parsed) ? parsed : null;

        Guid? contact = isCompany && Guid.TryParse(ContactIdBox.Text.Trim(), out Guid person)
            ? person
            : null;

        return new AddOpportunityTargetRequest(
            expectedVersion,
            isCompany ? target : null,
            isCompany ? null : target,
            contact,
            OwnerUserId: null,
            DateOnly.FromDateTime(NextActionPicker.Date.DateTime),
            Empty(NotesBox.Text));
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        bool isCompany = SelectedTag(KindBox) != "Person";

        if (ContactIdBox is not null)
        {
            ContactIdBox.IsEnabled = isCompany;
            ContactHint.Visibility = isCompany ? Visibility.Collapsed : Visibility.Visible;
        }

        Validate();
    }

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate() => IsPrimaryButtonEnabled = Guid.TryParse(TargetIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
