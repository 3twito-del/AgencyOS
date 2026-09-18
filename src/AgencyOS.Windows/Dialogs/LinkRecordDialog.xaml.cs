using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Links a document or a message to a record it bears on.
/// </summary>
/// <remarks>
/// <para>
/// One dialog for both, because it asks the same question: which record, and why.
/// The fourteen targets are the ones the database can actually enforce a foreign
/// key against, so a link cannot end up pointing at a record that was removed or
/// that belongs to another tenant (ADR-0025).
/// </para>
/// <para>
/// Twelve of the fourteen are chosen from this organization's own records
/// (<c>AOS-R001-006</c>). Two — a material and a contract version — belong to a
/// parent record and have no list of their own, so they still take an identifier
/// and the dialog says why. They are not removed: a link that was legal yesterday
/// stays legal.
/// </para>
/// <para>
/// The notice about access is not decoration. A link is the most natural place for
/// somebody to assume permission flows along it, and it does not: a privileged
/// document filed against a deal stays privileged.
/// </para>
/// </remarks>
public sealed partial class LinkRecordDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;

    /// <param name="api">Used to list whichever kind the operator picks.</param>
    /// <param name="headline">What is being linked, in the operator's words.</param>
    public LinkRecordDialog(IAgencyOsApi api, string headline)
    {
        ArgumentNullException.ThrowIfNull(api);

        InitializeComponent();

        _api = api;

        HeadlineText.Text = headline;

        foreach (string target in LinkTargets.All)
        {
            TargetBox.Items.Add(target);
        }
    }

    public string Target => TargetBox.SelectedItem as string ?? string.Empty;

    /// <summary>The record chosen, however this kind is chosen.</summary>
    public Guid TargetId =>
        RecordBox.Visibility == Visibility.Visible
            ? (RecordBox.SelectedItem as EntityChoice)?.Id ?? Guid.Empty
            : Guid.TryParse(TargetIdBox.Text, out Guid typed) ? typed : Guid.Empty;

    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnChanged(object sender, object e)
    {
        if (ReferenceEquals(sender, TargetBox))
        {
            _ = OfferRecordsAsync(Target);
        }

        Validate();
    }

    /// <summary>Offers the records of the chosen kind, or explains why it cannot.</summary>
    private async Task OfferRecordsAsync(string kind)
    {
        RecordBox.ItemsSource = null;
        RecordBox.SelectedItem = null;
        TargetIdBox.Text = string.Empty;

        if (kind.Length == 0)
        {
            RecordBox.Visibility = Visibility.Collapsed;
            TargetIdBox.Visibility = Visibility.Collapsed;
            RecordHint.Text = string.Empty;

            return;
        }

        if (LinkTargets.WhyNot(kind) is { } reason)
        {
            RecordBox.Visibility = Visibility.Collapsed;
            TargetIdBox.Visibility = Visibility.Visible;
            RecordHint.Text = $"There is no list to choose from here: {reason}.";

            Validate();

            return;
        }

        RecordBox.Visibility = Visibility.Visible;
        TargetIdBox.Visibility = Visibility.Collapsed;
        RecordHint.Text = string.Empty;
        RecordBusy.Visibility = Visibility.Visible;

        try
        {
            RecordBox.ItemsSource = await ChoicesAsync(kind).ConfigureAwait(true);

            if (RecordBox.ItemsSource is IReadOnlyList<EntityChoice> { Count: 0 })
            {
                RecordHint.Text = "This organization holds none of those yet.";
            }
        }
        catch (AgencyOsApiException failure)
        {
            // A caller who may not read a kind is told so, rather than shown an
            // empty list that looks like an empty organization (AOS-R002-024).
            RecordHint.Text = failure.Detail ?? failure.Message;
        }
        finally
        {
            RecordBusy.Visibility = Visibility.Collapsed;

            Validate();
        }
    }

    /// <summary>This organization's records of one kind, as choices.</summary>
    private async Task<IReadOnlyList<EntityChoice>> ChoicesAsync(string kind) => kind switch
    {
        "Person" => EntityChoice.ForPeople(await _api.ListPeopleAsync().ConfigureAwait(true)),
        "Company" => EntityChoice.ForCompanies(await _api.ListCompaniesAsync().ConfigureAwait(true)),
        "TalentProfile" => EntityChoice.ForTalent(await _api.ListTalentAsync().ConfigureAwait(true)),
        "Project" => EntityChoice.ForProjects(await _api.ListProjectsAsync().ConfigureAwait(true)),
        "Package" => EntityChoice.ForPackages(await _api.ListPackagesAsync().ConfigureAwait(true)),
        "Opportunity" =>
            EntityChoice.ForOpportunities(await _api.ListOpportunitiesAsync().ConfigureAwait(true)),
        "Submission" =>
            EntityChoice.ForSubmissions(await _api.ListSubmissionsAsync().ConfigureAwait(true)),
        "Deal" => EntityChoice.ForDeals(await _api.ListDealsAsync().ConfigureAwait(true)),
        "Offer" => EntityChoice.ForOffers(await _api.ListOffersAsync().ConfigureAwait(true)),
        "Contract" => EntityChoice.ForContracts(await _api.ListContractsAsync().ConfigureAwait(true)),
        "Invoice" => EntityChoice.ForInvoices(await _api.ListInvoicesAsync().ConfigureAwait(true)),
        "Payment" => EntityChoice.ForPayments(await _api.ListPaymentsAsync().ConfigureAwait(true)),
        _ => [],
    };

    private void Validate() =>
        IsPrimaryButtonEnabled = TargetBox.SelectedItem is not null && TargetId != Guid.Empty;
}
