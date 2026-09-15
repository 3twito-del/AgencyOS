using System;
using System.Collections.Generic;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
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
    private readonly IReadOnlyList<EntityChoice> _people;
    private readonly IReadOnlyList<EntityChoice> _companies;

    /// <param name="roleType">The role's type, for the heading.</param>
    /// <param name="roleLabel">The role's label, when it has one.</param>
    /// <param name="people">Everyone this organization holds a record for.</param>
    /// <param name="companies">Every company it holds a record for.</param>
    /// <remarks>
    /// Both lists, because the party kind is chosen here rather than fixed by the
    /// role. Changing that choice clears the selection: a person chosen under
    /// "Person" is not a company, and carrying the value across would attach the
    /// wrong record (<c>AOS-R001-006</c>, §12).
    /// </remarks>
    public AttachToRoleDialog(
        string roleType,
        string? roleLabel,
        IReadOnlyList<PersonSummaryResponse> people,
        IReadOnlyList<CompanySummaryResponse> companies)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(companies);

        InitializeComponent();

        _people = EntityChoice.ForPeople(people);
        _companies = EntityChoice.ForCompanies(companies);

        RoleText.Text = string.IsNullOrWhiteSpace(roleLabel)
            ? roleType
            : $"{roleType} - {roleLabel}";

        PartyKindBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 1;
        StartsOnPicker.Date = DateTimeOffset.UtcNow;

        ApplyPartyKind();
    }

    /// <summary>The party the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => PartyBox.SelectedItem as EntityChoice;

    public AttachToRoleRequest ToRequest(int expectedVersion)
    {
        Guid? party = Chosen()?.Id;

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

    private void OnPartyKindChanged(object sender, SelectionChangedEventArgs e) => ApplyPartyKind();

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) => Validate();

    /// <summary>
    /// Points the picker at the chosen kind, and drops whatever was selected under
    /// the previous one.
    /// </summary>
    private void ApplyPartyKind()
    {
        if (PartyBox is null)
        {
            return;
        }

        bool isPerson = SelectedTag(PartyKindBox) != "Company";

        PartyBox.ItemsSource = isPerson ? _people : _companies;
        PartyBox.SelectedItem = null;
        PartyBox.Header = isPerson ? "Who" : "Which company";

        Validate();
    }

    private void Validate() => IsPrimaryButtonEnabled = Chosen() is not null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
