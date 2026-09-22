using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// Who is in this agency, and what each of them may do.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 could not review AgencyOS as anybody but its first owner, because
/// there was no way to add a second person: registering a user was reachable only
/// from first-run bootstrap, and membership was write-once with no read, no
/// change and no revoke (<c>AOS-R002-002</c>, <c>AOS-R002-006</c>).
/// </para>
/// <para>
/// This screen asks; the server decides. Buttons are enabled from what is
/// selected, never from a guess about what the caller may do — a refusal arriving
/// from the server is the authority, and it is shown rather than swallowed.
/// </para>
/// </remarks>
public sealed partial class OrganizationPage : Page
{
    private readonly List<MemberLine> _members = [];

    /// <summary>Creates the page.</summary>
    public OrganizationPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    /// <summary>One member, as the list shows them.</summary>
    /// <param name="MembershipId">The membership, for acting on it.</param>
    /// <param name="DisplayName">Their name.</param>
    /// <param name="Email">Their address.</param>
    /// <param name="Role">The role they hold.</param>
    /// <param name="Note">Why a row is worth a second look — "you", or the only owner.</param>
    /// <param name="IsSelf">Whether this is the signed-in person.</param>
    /// <param name="IsOnlyOwner">Whether ending this membership would leave no owner.</param>
    internal sealed record MemberLine(
        Guid MembershipId,
        string DisplayName,
        string Email,
        string Role,
        string Note,
        bool IsSelf,
        bool IsOnlyOwner);

    private async Task LoadAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        try
        {
            IReadOnlyList<OrganizationMemberResponse> members =
                await api.ListOrganizationMembersAsync().ConfigureAwait(true);

            int owners = members.Count(x => string.Equals(x.Role, "Owner", StringComparison.Ordinal));

            _members.Clear();
            _members.AddRange(members.Select(x => new MemberLine(
                x.MembershipId,
                x.DisplayName,
                x.Email,
                x.Role,
                Note(x, owners),
                x.IsSelf,
                string.Equals(x.Role, "Owner", StringComparison.Ordinal) && owners == 1)));

            MemberList.ItemsSource = null;
            MemberList.ItemsSource = _members;

            string people = _members.Count == 1 ? "person" : "people";
            string ownerWord = owners == 1 ? "owner" : "owners";

            SummaryText.Text =
                $"{_members.Count} {people}, {owners} {ownerWord}. "
                + "Ending a membership keeps the record of what they could do.";
        }
        catch (AgencyOsApiException failure)
        {
            // The rows that arrived last time stay on screen - they were true when
            // they arrived and throwing them away helps nobody - but the count
            // stops claiming to describe the membership, because this load did not
            // find out what it is. Only the list load clears it; a refused action
            // says nothing about who the members are.
            SummaryText.Text = SummaryAuthority.Unavailable;

            Refuse(failure);
        }
    }

    private static string Note(OrganizationMemberResponse member, int owners)
    {
        if (member.IsSelf)
        {
            return "you";
        }

        return string.Equals(member.Role, "Owner", StringComparison.Ordinal) && owners == 1
            ? "the only owner"
            : string.Empty;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool selected = MemberList.SelectedItem is MemberLine;

        ChangeRoleButton.IsEnabled = selected;
        RemoveButton.IsEnabled = selected;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnAddClick(object sender, RoutedEventArgs e) => _ = AddAsync();

    private void OnChangeRoleClick(object sender, RoutedEventArgs e) => _ = ChangeRoleAsync();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => _ = RemoveAsync();

    private async Task AddAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        AddMemberDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        AddMemberRequest request = dialog.ToRequest();

        try
        {
            AddMemberResponse added = await api
                .AddOrganizationMemberAsync(request, Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);

            Done(added.UserWasRegistered
                ? $"{request.DisplayName} can now sign in, as {Lower(request.Role)}."
                : $"{request.DisplayName} already had an AgencyOS account and is now "
                    + $"{Article(request.Role)} {Lower(request.Role)} here.");
        }
        catch (AgencyOsApiException failure)
        {
            Refuse(failure);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ChangeRoleAsync()
    {
        if (AppServices.Api is not { } api || MemberList.SelectedItem is not MemberLine member)
        {
            return;
        }

        ChangeMemberRoleDialog dialog =
            new(member.DisplayName, member.Role) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.Equals(dialog.SelectedRole, member.Role, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await api
                .ChangeOrganizationMemberRoleAsync(member.MembershipId, dialog.ToRequest())
                .ConfigureAwait(true);

            Done($"{member.DisplayName} is now {Article(dialog.SelectedRole)} "
                + $"{Lower(dialog.SelectedRole)}.");
        }
        catch (AgencyOsApiException failure)
        {
            Refuse(failure);
        }

        // Re-read whatever happened. A role change ends one membership and grants
        // another, so the identifier this page held is no longer the live one.
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RemoveAsync()
    {
        if (AppServices.Api is not { } api || MemberList.SelectedItem is not MemberLine member)
        {
            return;
        }

        // Ending access is not reversible from this screen, so it is asked twice.
        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = "End their access?",
            Content = $"{member.DisplayName} will not be able to sign in to this agency. "
                + "The record of what they could do is kept.",
            PrimaryButtonText = "End access",
            CloseButtonText = "Keep it",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await api.RevokeOrganizationMembershipAsync(member.MembershipId).ConfigureAwait(true);

            Done($"{member.DisplayName} no longer has access.");
        }
        catch (AgencyOsApiException failure)
        {
            Refuse(failure);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Shows what the server said, in the server's words.
    /// </summary>
    /// <remarks>
    /// The refusals this screen produces are written to be read — "This is the
    /// organization's only owner. Give somebody else the owner role first" — so
    /// replacing them with a generic message would throw away the useful part.
    /// </remarks>
    private void Refuse(AgencyOsApiException failure)
    {
        DoneBar.IsOpen = false;
        RefusalBar.Title = "That did not happen";
        RefusalBar.Message = failure.Detail ?? failure.Message;
        RefusalBar.IsOpen = true;
    }

    private void Done(string message)
    {
        RefusalBar.IsOpen = false;
        DoneBar.Title = message;
        DoneBar.IsOpen = true;
    }

    private static string Lower(string role) => role.ToLowerInvariant();

    private static string Article(string role) =>
        role.StartsWith('A') || role.StartsWith('O') ? "an" : "a";
}
