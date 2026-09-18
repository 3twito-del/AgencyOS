using System;
using System.Collections.Generic;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Begins or ends representing one area of somebody's work.
/// </summary>
/// <remarks>
/// <c>AOS-R001-010</c>. The server has had both commands since M4 and the Talent
/// workspace has always shown the result; nothing in the client called them. Both
/// arms live in one dialog because an operator thinks of it as one question —
/// what do we represent — and the two answers share a date and a version.
/// </remarks>
public sealed partial class ChangeRepresentationScopeDialog : ContentDialog
{
    private readonly IReadOnlyList<RepresentationScopeResponse> _scopes;

    /// <param name="scopes">Every scope the server sent, current and historical.</param>
    public ChangeRepresentationScopeDialog(IReadOnlyList<RepresentationScopeResponse> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        InitializeComponent();

        // The hint under this field describes it (AOS-R002-011).
        AutomationProperties.GetDescribedBy(AreaBox).Add(AreaHint);

        _scopes = scopes;

        IReadOnlyList<string> current = RepresentationMaintenance.AreasThatCanEnd(scopes);

        CurrentText.Text = current.Count == 0
            ? "No area is represented yet."
            : "Represented now: " + string.Join(", ", current) + ".";

        OccurredPicker.Date = DateTimeOffset.Now;
        ActionBox.SelectedIndex = 0;
    }

    /// <summary>Whether the operator is beginning an area rather than ending one.</summary>
    public bool IsBeginning => SelectedTag(ActionBox) != "End";

    /// <summary>The area chosen, or empty while none is.</summary>
    public string Area => AreaBox.SelectedItem as string ?? string.Empty;

    public ChangeRepresentationScopeRequest ToRequest(int expectedVersion) => new(
        Area,
        DateOnly.FromDateTime(OccurredPicker.Date.LocalDateTime.Date),
        expectedVersion);

    private void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AreaBox is null)
        {
            return;
        }

        AreaBox.ItemsSource = IsBeginning
            ? RepresentationMaintenance.AreasThatCanBegin(_scopes)
            : RepresentationMaintenance.AreasThatCanEnd(_scopes);

        AreaBox.SelectedItem = null;

        AreaHint.Text = IsBeginning
            ? "Areas already represented are not offered; they are already true."
            : "Only areas represented now can end.";

        Validate();
    }

    private void OnAreaChanged(object sender, SelectionChangedEventArgs e) => Validate();

    private void Validate() => IsPrimaryButtonEnabled = Area.Length > 0;

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
