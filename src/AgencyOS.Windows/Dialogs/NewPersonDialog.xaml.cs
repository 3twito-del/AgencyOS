using System;
using System.Collections.Generic;
using AgencyOS.Client;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Captures the minimum needed to create a person.
/// </summary>
/// <remarks>
/// <para>
/// Only the first name is required, matching the domain. Names that do not split
/// into given and family parts are common in this industry, and demanding a last
/// name would make the system wrong about real people.
/// </para>
/// <para>
/// The create happens here rather than on the page, so that a refusal can be
/// answered where it was caused: the dialog stays open with everything typed still
/// in it, the reason is shown against the field it is about, and the operator can
/// correct and try again (<c>AOS-R002-010</c>, <c>AOS-R002-011</c>). The pattern is
/// <c>ConnectCompanyDialog</c>'s, which has always done this.
/// </para>
/// </remarks>
public sealed partial class NewPersonDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;
    private readonly RefusalSurface _refusal;

    /// <param name="api">Used to create the person, so a refusal can be answered here.</param>
    public NewPersonDialog(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        InitializeComponent();

        _api = api;

        // Keyed by the name the server uses, because that is what a refusal says.
        _refusal = new RefusalSurface(
            ErrorBar,
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
            {
                ["firstName"] = FirstNameBox,
                ["lastName"] = LastNameBox,
                ["title"] = TitleBox,
                ["email"] = EmailBox,
                ["phone"] = PhoneBox,
                ["notes"] = NotesBox,
            });

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    /// <summary>The person the server created, or null while none has been.</summary>
    public PersonDetailResponse? Created { get; private set; }

    /// <summary>A refusal this dialog could not answer, for the page to report.</summary>
    /// <remarks>
    /// A session that is gone or a server that failed cannot be corrected by editing
    /// the form. Holding the dialog open over one would be pretending.
    /// </remarks>
    public AgencyOsApiException? Terminal { get; private set; }

    /// <summary>Builds the request from the entered values.</summary>
    public CreatePersonRequest ToRequest() => new(
        FirstNameBox.Text.Trim(),
        Blank(LastNameBox.Text),
        DisplayName: null,
        MiddleName: null,
        PreferredName: null,
        PrimaryCompanyId: null,
        Blank(TitleBox.Text),
        Blank(EmailBox.Text),
        Blank(PhoneBox.Text),
        Blank(NotesBox.Text));

    private async void OnPrimaryButtonClick(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();

        try
        {
            Created = await _api.CreatePersonAsync(ToRequest()).ConfigureAwait(true);
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
