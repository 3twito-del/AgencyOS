using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Opens a contract against a negotiation whose terms are agreed.
/// </summary>
/// <remarks>
/// <para>
/// The accepted offer is never inferred silently. A deal carries exactly one, so
/// there is nothing for an operator to choose - but it is the baseline every later
/// draft is reconciled against, so the one that will be used is shown before the
/// contract is opened, and a deal with no agreed terms is refused with its reason
/// rather than papered against nothing (ADR-0022). Repair Wave 003B replaced two
/// typed identifiers with this; what is asked has not changed, only how it is
/// answered (AOS-R001-006).
/// </para>
/// <para>
/// Choosing Amendment surfaces a note rather than changing the form: an amendment
/// is a separate executed instrument that is linked to the paper it changes, not a
/// later drafting version of it.
/// </para>
/// </remarks>
public sealed partial class CreateContractDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;

    private Guid _acceptedOfferId;

    /// <param name="api">Used to read the chosen deal's accepted offer.</param>
    /// <param name="deals">The negotiations this organization is running.</param>
    public CreateContractDialog(
        IAgencyOsApi api,
        IReadOnlyList<DealSummaryResponse> deals,
        IReadOnlyList<OrganizationMemberResponse> members)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(deals);
        ArgumentNullException.ThrowIfNull(members);

        InitializeComponent();

        _api = api;
        DealBox.ItemsSource = EntityChoice.ForDeals(deals);

        // Preselect whoever is signed in, proved by the directory's own IsSelf
        // rather than guessed; they can still choose somebody else.
        OwnerPicker(members);
    }

    /// <summary>The negotiation the operator chose, or null while none is chosen.</summary>
    public EntityChoice? ChosenDeal() => DealBox.SelectedItem as EntityChoice;

    /// <summary>The accepted offer the chosen deal carries, or empty when it has none.</summary>
    public Guid AcceptedOfferId => _acceptedOfferId;

    public CreateContractRequest ToRequest() =>
        new(
            ChosenDeal()?.Id ?? Guid.Empty,
            _acceptedOfferId,
            TitleBox.Text.Trim(),
            SelectedTag(KindBox) ?? "LongForm",
            Chosen(OwnerIdBox),
            Empty(ReferenceBox.Text),
            Empty(SummaryBox.Text),
            Empty(AnalysisBox.Text),
            Empty(StrategyBox.Text),
            SelectedTag(PrivilegeBox) ?? "Ordinary");

    private void OnRequiredChanged(object sender, object e) => UpdateReady();

    private void OnDealChanged(object sender, SelectionChangedEventArgs e) => _ = LoadOfferAsync();

    /// <summary>Reads the chosen deal's accepted offer and says which one it is.</summary>
    /// <remarks>
    /// A deal with nothing accepted is refused here rather than at the server: the
    /// operator is told the terms are not agreed yet, which is the actual reason,
    /// instead of meeting a validation failure about an empty identifier.
    /// </remarks>
    private async Task LoadOfferAsync()
    {
        _acceptedOfferId = Guid.Empty;
        OfferBar.IsOpen = false;

        if (ChosenDeal() is not { } deal)
        {
            UpdateReady();

            return;
        }

        OfferBusy.Visibility = Visibility.Visible;
        UpdateReady();

        try
        {
            DealDetailResponse detail = await _api.GetDealAsync(deal.Id).ConfigureAwait(true);

            if (detail.AcceptedOffer is { } offer)
            {
                _acceptedOfferId = offer.Id;

                OfferBar.Severity = InfoBarSeverity.Informational;
                OfferBar.Title = "Accepted offer";
                OfferBar.Message =
                    $"Offer {offer.Sequence.ToString(CultureInfo.CurrentCulture)}, "
                        + offer.Direction.ToLowerInvariant()
                        + ". This is the agreement every later draft is reconciled against.";
            }
            else
            {
                OfferBar.Severity = InfoBarSeverity.Warning;
                OfferBar.Title = "Nothing has been accepted on this deal";
                OfferBar.Message =
                    "A contract papers terms that are already agreed. Accept an offer on the "
                        + "negotiation first, then open the contract against it.";
            }

            OfferBar.IsOpen = true;
        }
        catch (AgencyOsApiException failure)
        {
            OfferBar.Severity = InfoBarSeverity.Error;
            OfferBar.Title = "That deal could not be read";
            // The server's own explanation, not the title of the problem
            // (AOS-R002-024).
            OfferBar.Message = failure.Detail ?? failure.Message;
            OfferBar.IsOpen = true;
        }
        finally
        {
            OfferBusy.Visibility = Visibility.Collapsed;
            UpdateReady();
        }
    }

    private void UpdateReady()
    {
        if (AmendmentBar is not null)
        {
            AmendmentBar.IsOpen = SelectedTag(KindBox) == "Amendment";
        }

        IsPrimaryButtonEnabled =
            ChosenDeal() is not null
            && _acceptedOfferId != Guid.Empty
            && Chosen(OwnerIdBox) != Guid.Empty
            && !string.IsNullOrWhiteSpace(TitleBox.Text);
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    /// <summary>
    /// Offers the organization's people, with whoever is signed in preselected.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-006</c>. The directory says which member is the caller through
    /// <c>IsSelf</c>, so the default is proved rather than assumed — and it is only
    /// a default: every other member stays selectable.
    /// </remarks>
    private void OwnerPicker(IReadOnlyList<OrganizationMemberResponse> members)
    {
        IReadOnlyList<EntityChoice> choices = EntityChoice.ForMembers(members);

        OwnerIdBox.ItemsSource = choices;

        if (members.FirstOrDefault(x => x.IsSelf) is { } self)
        {
            OwnerIdBox.SelectedItem = choices.FirstOrDefault(x => x.Id == self.UserId);
        }
    }

    /// <summary>The member chosen in a picker, or empty while none is.</summary>
    private static Guid Chosen(ComboBox box) =>
        (box.SelectedItem as EntityChoice)?.Id ?? Guid.Empty;

}
