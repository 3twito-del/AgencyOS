using System;
using System.Collections.Generic;
using AgencyOS.Client;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Captures the minimum needed to create an external company.
/// </summary>
/// <remarks>
/// The create happens here rather than on the page, so a refusal can be answered
/// where it was caused: the dialog stays open with everything typed still in it,
/// and the reason is shown against the field it is about (<c>AOS-R002-010</c>,
/// <c>AOS-R002-011</c>).
/// </remarks>
public sealed partial class NewCompanyDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;
    private readonly RefusalSurface _refusal;

    /// <param name="api">Used to create the company, so a refusal can be answered here.</param>
    public NewCompanyDialog(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        InitializeComponent();

        _api = api;

        // Keyed by the name the server uses, because that is what a refusal says.
        _refusal = new RefusalSurface(
            ErrorBar,
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = NameBox,
                ["type"] = TypeBox,
                ["legalName"] = LegalNameBox,
                ["website"] = WebsiteBox,
                ["notes"] = NotesBox,
            });

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    /// <summary>The company the server created, or null while none has been.</summary>
    public CompanyDetailResponse? Created { get; private set; }

    /// <summary>A refusal this dialog could not answer, for the page to report.</summary>
    public AgencyOsApiException? Terminal { get; private set; }

    /// <summary>Builds the request from the entered values.</summary>
    public CreateCompanyRequest ToRequest() => new(
        NameBox.Text.Trim(),
        TypeBox.SelectedItem as string ?? "Other",
        Blank(LegalNameBox.Text),
        Blank(WebsiteBox.Text),
        Blank(NotesBox.Text));

    private async void OnPrimaryButtonClick(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();

        try
        {
            Created = await _api.CreateCompanyAsync(ToRequest()).ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            if (_refusal.Show(failure))
            {
                args.Cancel = true;
            }
            else
            {
                Terminal = failure;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>A changed entry retires the complaint that was about it.</summary>
    private void OnEntryChanged(object sender, TextChangedEventArgs e) => _refusal.Clear();

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
