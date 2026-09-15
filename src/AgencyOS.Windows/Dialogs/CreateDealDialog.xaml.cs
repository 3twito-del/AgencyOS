using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Opens a negotiation against a market conversation.</summary>
/// <remarks>
/// The opportunity and target are both required, so the lineage from client or
/// project through pursuit and target to agreed terms is never broken. The server
/// refuses a target that belongs to a different pursuit, and the database refuses
/// it too (ADR-0021).
/// </remarks>
public sealed partial class CreateDealDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;

    /// <param name="api">Used to fetch the chosen opportunity's targets.</param>
    /// <param name="opportunities">The pursuits this organization is running.</param>
    /// <remarks>
    /// Audit 002 classified both fields as derivable from context on the reading
    /// that a deal is opened from an opportunity. It is not — <c>deal.create</c> is
    /// a Deals-workspace command and nothing is selected when it runs. So the
    /// opportunity is chosen here, and the target is derived from that choice
    /// (<c>AOS-R001-006</c>).
    /// </remarks>
    public CreateDealDialog(
        IAgencyOsApi api,
        IReadOnlyList<OpportunitySummaryResponse> opportunities)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(opportunities);

        InitializeComponent();

        _api = api;
        OpportunityBox.ItemsSource = EntityChoice.ForOpportunities(opportunities);
    }

    /// <summary>The pursuit the operator chose, or null while none is chosen.</summary>
    public EntityChoice? ChosenOpportunity() => OpportunityBox.SelectedItem as EntityChoice;

    /// <summary>The target the operator chose, or null while none is chosen.</summary>
    public EntityChoice? ChosenTarget() => TargetBox.SelectedItem as EntityChoice;

    public CreateDealRequest ToRequest() =>
        new(
            ChosenOpportunity()?.Id ?? Guid.Empty,
            ChosenTarget()?.Id ?? Guid.Empty,
            NameBox.Text.Trim(),
            SelectedTag(KindBox) ?? "Other",
            Guid.Parse(OwnerIdBox.Text.Trim()),
            OpenedOn: null,
            Empty(ReferenceBox.Text),
            Empty(SummaryBox.Text),
            Empty(StrategyBox.Text));

    private void OnRequiredChanged(object sender, object e) => Validate();

    private void OnOpportunityChanged(object sender, SelectionChangedEventArgs e) =>
        _ = LoadTargetsAsync();

    /// <summary>
    /// Fetches the chosen pursuit's targets, dropping any target chosen under the
    /// previous one.
    /// </summary>
    /// <remarks>
    /// The drop is the point. A target belongs to exactly one opportunity, and
    /// carrying a selection across would send the server a pair it refuses — which
    /// the operator would meet as a refusal rather than as a cleared field (§12).
    /// </remarks>
    private async Task LoadTargetsAsync()
    {
        TargetBox.SelectedItem = null;

        if (ChosenOpportunity() is not { } opportunity)
        {
            TargetBox.ItemsSource = Array.Empty<EntityChoice>();
            Validate();

            return;
        }

        TargetBusy.Visibility = Visibility.Visible;
        Validate();

        try
        {
            OpportunityDetailResponse detail = await _api
                .GetOpportunityAsync(opportunity.Id).ConfigureAwait(true);

            TargetBox.ItemsSource = EntityChoice.ForTargets(detail.Targets);
        }
        catch (AgencyOsApiException)
        {
            TargetBox.ItemsSource = Array.Empty<EntityChoice>();
        }
        finally
        {
            TargetBusy.Visibility = Visibility.Collapsed;
            Validate();
        }
    }

    private void Validate() =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(NameBox.Text)
            && ChosenOpportunity() is not null
            && ChosenTarget() is not null
            && Guid.TryParse(OwnerIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
