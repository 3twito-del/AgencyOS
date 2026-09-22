using System;
using System.Collections.Generic;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Puts a party on a contract.
/// </summary>
/// <remarks>
/// <para>
/// The first step of the legal chain, and the one that was missing. Signatures are
/// recorded against parties, so a contract with none can never be executed: the
/// signature dialog filters to required signatories who have not signed and finds
/// nobody to offer. Reality Closure classified that as F-13 — capability the
/// domain, the API and the client all had, with no route an operator could take.
/// </para>
/// <para>
/// <strong>Nothing here decides anything.</strong> The role vocabulary is the
/// domain's own, the three ways to name a party are the three the domain accepts,
/// and every rule — that a party is named exactly one way, that the contract must
/// still accept parties — is enforced on the server. This collects what the
/// request needs and lets the refusal speak for itself.
/// </para>
/// </remarks>
public sealed partial class AddContractPartyDialog : ContentDialog
{
    public AddContractPartyDialog(
        string contractTitle,
        IReadOnlyList<EntityChoice> people,
        IReadOnlyList<EntityChoice> companies)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(companies);

        InitializeComponent();

        HeadlineText.Text = contractTitle;
        PersonBox.ItemsSource = people;
        CompanyBox.ItemsSource = companies;

        RoleBox.SelectedIndex = 0;
        KindBox.SelectedIndex = 0;

        UpdateReady();
    }

    /// <summary>What the operator chose, as the request the server expects.</summary>
    public AddContractPartyRequest ToRequest(int expectedVersion)
    {
        string kind = SelectedTag(KindBox) ?? "Person";

        return new AddContractPartyRequest(
            SelectedTag(RoleBox) ?? "Other",
            expectedVersion,
            PersonId: kind == "Person" ? Chosen(PersonBox) : null,
            CompanyId: kind == "Company" ? Chosen(CompanyBox) : null,
            ExternalName: kind == "External" ? Text(ExternalBox.Text) : null,
            Provenance: kind == "External" ? Text(ProvenanceBox.Text) : null,
            IsRequiredSignatory: SignatoryBox.IsChecked == true);
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        string kind = SelectedTag(KindBox) ?? "Person";

        PersonBox.Visibility = kind == "Person" ? Visibility.Visible : Visibility.Collapsed;
        CompanyBox.Visibility = kind == "Company" ? Visibility.Visible : Visibility.Collapsed;
        ExternalBox.Visibility = kind == "External" ? Visibility.Visible : Visibility.Collapsed;
        ProvenanceBox.Visibility = ExternalBox.Visibility;

        UpdateReady();
    }

    private void OnChanged(object sender, RoutedEventArgs e) => UpdateReady();

    private void OnChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    /// <summary>
    /// Whether the request can be built at all.
    /// </summary>
    /// <remarks>
    /// Only that one field has been answered — not whether the domain will accept
    /// it. Deciding here whether a role is legal for this contract would put a
    /// second copy of the rule in the client, where it would drift.
    /// </remarks>
    private void UpdateReady()
    {
        string kind = SelectedTag(KindBox) ?? "Person";

        IsPrimaryButtonEnabled = SelectedTag(RoleBox) is { Length: > 0 } && kind switch
        {
            "Person" => Chosen(PersonBox) is not null,
            "Company" => Chosen(CompanyBox) is not null,
            _ => !string.IsNullOrWhiteSpace(ExternalBox.Text),
        };
    }

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static Guid? Chosen(ComboBox box) =>
        (box.SelectedItem as EntityChoice)?.Id;

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
