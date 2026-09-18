using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    /// <summary>Offered when no contact is named, so "nobody yet" is a real answer.</summary>
    private static readonly EntityChoice NoContact = new(Guid.Empty, "Nobody in particular yet");

    private readonly IReadOnlyList<EntityChoice> _people;
    private readonly IReadOnlyList<EntityChoice> _companies;

    /// <param name="people">Everyone this organization holds a record for.</param>
    /// <param name="companies">Every company it holds a record for.</param>
    public AddOpportunityTargetDialog(
        IReadOnlyList<PersonSummaryResponse> people,
        IReadOnlyList<CompanySummaryResponse> companies)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(companies);

        InitializeComponent();

        // The hint under this field describes it. Declaring that lets a screen
        // reader reach the explanation from the field, instead of the reader
        // having to find it by looking (AOS-R002-011).
        AutomationProperties.GetDescribedBy(ContactBox).Add(ContactHint);

        _people = EntityChoice.ForPeople(people);
        _companies = EntityChoice.ForCompanies(companies);

        ContactBox.ItemsSource = new[] { NoContact }.Concat(_people).ToList();
        ContactBox.SelectedItem = NoContact;

        KindBox.SelectedIndex = 0;
        NextActionPicker.Date = DateTimeOffset.UtcNow.AddDays(7);

        ApplyKind();
    }

    /// <summary>The target the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => TargetBox.SelectedItem as EntityChoice;

    public AddOpportunityTargetRequest ToRequest(int expectedVersion)
    {
        bool isCompany = SelectedTag(KindBox) != "Person";

        Guid? target = Chosen()?.Id;

        Guid? contact = isCompany && ContactBox.SelectedItem is EntityChoice person
            && person.Id != Guid.Empty
            ? person.Id
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

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ApplyKind();

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) => Validate();

    /// <summary>
    /// Points the target picker at companies or at people, and drops whatever was
    /// chosen under the other kind (§12).
    /// </summary>
    private void ApplyKind()
    {
        if (TargetBox is null)
        {
            return;
        }

        bool isCompany = SelectedTag(KindBox) != "Person";

        TargetBox.ItemsSource = isCompany ? _companies : _people;
        TargetBox.SelectedItem = null;
        TargetBox.Header = isCompany ? "Which company" : "Which person";

        // A contact belongs to a company target. For a person target the person is
        // the one being dealt with, so the field is not offered at all.
        ContactBox.IsEnabled = isCompany;
        ContactHint.Visibility = isCompany ? Visibility.Collapsed : Visibility.Visible;

        if (!isCompany)
        {
            ContactBox.SelectedItem = NoContact;
        }

        Validate();
    }

    private void Validate() => IsPrimaryButtonEnabled = Chosen() is not null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
