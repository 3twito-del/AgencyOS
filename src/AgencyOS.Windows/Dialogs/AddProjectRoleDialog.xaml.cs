using System;
using System.Collections.Generic;
using AgencyOS.Client;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Captures a position on a project.
/// </summary>
/// <remarks>
/// <para>
/// Exclusivity is opt-in and the dialog says why. Claiming it wrongly blocks
/// legitimate data entry; omitting it wrongly only fails to catch a duplicate, so
/// the default fails open.
/// </para>
/// <para>
/// The add happens here rather than on the page, so a refusal can be answered where
/// it was caused (<c>AOS-R002-010</c>, <c>AOS-R002-011</c>). A stale version is one
/// of the refusals this dialog holds open: the operator reloads and tries again
/// without retyping the role.
/// </para>
/// </remarks>
public sealed partial class AddProjectRoleDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;
    private readonly Guid _projectId;
    private readonly int _expectedVersion;
    private readonly RefusalSurface _refusal;

    /// <param name="api">Used to add the role, so a refusal can be answered here.</param>
    /// <param name="projectId">The project the role belongs to.</param>
    /// <param name="expectedVersion">The version the operator was looking at.</param>
    public AddProjectRoleDialog(IAgencyOsApi api, Guid projectId, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(api);

        InitializeComponent();

        _api = api;
        _projectId = projectId;
        _expectedVersion = expectedVersion;

        // Keyed by the name the server uses, because that is what a refusal says.
        _refusal = new RefusalSurface(
            ErrorBar,
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = TypeBox,
                ["label"] = LabelBox,
                ["notes"] = NotesBox,
            });

        TypeBox.SelectedIndex = 0;

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    /// <summary>The role the server created, or null while none has been.</summary>
    public Guid? Created { get; private set; }

    /// <summary>A refusal this dialog could not answer, for the page to report.</summary>
    public AgencyOsApiException? Terminal { get; private set; }

    public CreateProjectRoleRequest ToRequest(int expectedVersion) => new(
        SelectedTag(TypeBox) ?? "Other",
        expectedVersion,
        Empty(LabelBox.Text),
        ExclusiveBox.IsChecked == true,
        Empty(NotesBox.Text));

    private async void OnPrimaryButtonClick(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();

        try
        {
            Created = await _api
                .CreateProjectRoleAsync(
                    _projectId,
                    ToRequest(_expectedVersion),
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);
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

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
