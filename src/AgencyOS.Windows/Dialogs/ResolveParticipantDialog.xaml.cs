using System;
using System.Collections.Generic;
using AgencyOS.Contracts.Documents;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records who an email address belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is pre-selected when the address matches more than one record, and the
/// dialog says why. An address is not an identity: assistants send from their
/// principal's address, companies use one inbox for a department, and people change
/// employers without changing anything AgencyOS can see. A wrong identification
/// attributes somebody's correspondence to the wrong person and does it silently
/// (ADR-0026).
/// </para>
/// <para>
/// "Leave unidentified" is a real answer with its own button, not a cancel. An
/// address AgencyOS cannot place is a fact worth keeping as it is.
/// </para>
/// </remarks>
public sealed partial class ResolveParticipantDialog : ContentDialog
{
    public ResolveParticipantDialog(
        string address,
        string? displayName,
        IReadOnlyList<ParticipantSuggestionResponse> suggestions)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(suggestions);

        AddressText.Text = displayName is { Length: > 0 }
            ? $"{displayName} <{address}>"
            : address;

        SuggestionList.ItemsSource = suggestions;

        NoMatchBar.IsOpen = suggestions.Count == 0;
        AmbiguousBar.IsOpen = suggestions.Count > 1;

        // Selected only when there is exactly one match and the server itself said
        // it was unambiguous. Two matches means the operator chooses.
        if (suggestions.Count == 1 && suggestions[0].IsUnambiguous)
        {
            SuggestionList.SelectedIndex = 0;
        }

        Update();
    }

    /// <summary>
    /// The chosen record, or null when the address is to stay unidentified.
    /// </summary>
    /// <remarks>
    /// A method rather than a property: the XAML compiler generates type metadata
    /// for the public properties of a compiled XAML class, and it cannot generate a
    /// setter for an init-only record.
    /// </remarks>
    public ParticipantSuggestionResponse? Chosen() =>
        SuggestionList.SelectedItem as ParticipantSuggestionResponse;

    private void OnChanged(object sender, object e) => Update();

    private void Update() => IsPrimaryButtonEnabled = Chosen() is not null;
}
