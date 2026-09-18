using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Puts somebody on a representation team, changes what they do, or takes them off.
/// </summary>
/// <remarks>
/// <c>AOS-R001-010</c>. Assigning somebody already on the team is how their role
/// changes, so they stay in the list rather than being hidden — hiding them would
/// make a role change unreachable.
/// </remarks>
public sealed partial class ChangeRepresentationTeamDialog : ContentDialog
{
    private readonly IReadOnlyList<OrganizationMemberResponse> _members;
    private readonly IReadOnlyList<RepresentationTeamMemberResponse> _team;

    /// <param name="members">Everybody in the organization.</param>
    /// <param name="team">Every assignment the server sent, current and historical.</param>
    public ChangeRepresentationTeamDialog(
        IReadOnlyList<OrganizationMemberResponse> members,
        IReadOnlyList<RepresentationTeamMemberResponse> team)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(team);

        InitializeComponent();

        // The hint under this field describes it (AOS-R002-011).
        AutomationProperties.GetDescribedBy(MemberBox).Add(MemberHint);

        _members = members;
        _team = team;

        IReadOnlyList<EntityChoice> working = RepresentationMaintenance.MembersToRemove(team);

        CurrentText.Text = working.Count == 0
            ? "Nobody is assigned to this relationship yet."
            : "Working it now: " + string.Join(", ", working.Select(x => x.Label)) + ".";

        RoleBox.ItemsSource = RepresentationMaintenance.Roles;
        RoleBox.SelectedIndex = 0;

        OccurredPicker.Date = DateTimeOffset.Now;
        ActionBox.SelectedIndex = 0;
    }

    /// <summary>Whether the operator is assigning rather than removing.</summary>
    public bool IsAssigning => SelectedTag(ActionBox) != "Remove";

    /// <summary>The person chosen, or null while none is.</summary>
    public EntityChoice? Chosen => MemberBox.SelectedItem as EntityChoice;

    public AssignRepresentationTeamMemberRequest ToAssignRequest(int expectedVersion) => new(
        Chosen?.Id ?? Guid.Empty,
        RoleBox.SelectedItem as string ?? "Agent",
        DateOnly.FromDateTime(OccurredPicker.Date.LocalDateTime.Date),
        expectedVersion);

    public RemoveRepresentationTeamMemberRequest ToRemoveRequest(int expectedVersion) => new(
        Chosen?.Id ?? Guid.Empty,
        DateOnly.FromDateTime(OccurredPicker.Date.LocalDateTime.Date),
        expectedVersion);

    private void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MemberBox is null)
        {
            return;
        }

        MemberBox.ItemsSource = IsAssigning
            ? RepresentationMaintenance.MembersToAssign(_members, _team)
            : RepresentationMaintenance.MembersToRemove(_team);

        MemberBox.SelectedItem = null;

        // A role is what an assignment is for. Removing somebody does not need one,
        // and offering it would suggest the removal could be partial.
        RoleBox.Visibility = IsAssigning ? Visibility.Visible : Visibility.Collapsed;

        MemberHint.Text = IsAssigning
            ? "Somebody already on the team can be chosen again to change what they do."
            : "Only people working it now can be taken off.";

        Validate();
    }

    private void OnMemberChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsAssigning && Chosen is { } chosen
            && RepresentationMaintenance.RoleOf(_team, chosen.Id) is { } role)
        {
            RoleBox.SelectedItem = role;
        }

        Validate();
    }

    private void Validate() => IsPrimaryButtonEnabled = Chosen is not null;

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
