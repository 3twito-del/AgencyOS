using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Captures the terms needed to turn a pursuit into a representation.
/// </summary>
/// <remarks>
/// Deliberately a dialog rather than a button. Signing a client sets a start date,
/// a lead and the scopes represented; doing it in one click would make the most
/// consequential command in the milestone the easiest to trigger by accident.
/// </remarks>
public sealed partial class ConvertProspectDialog : ContentDialog
{
    public ConvertProspectDialog(string displayName)
    {
        InitializeComponent();

        SubjectText.Text = $"Sign {displayName} as a client.";
        StartPicker.Date = DateTimeOffset.UtcNow;
    }

    /// <summary>When representation takes effect.</summary>
    public DateOnly StartsOn => StartPicker.Date is { } date
        ? DateOnly.FromDateTime(date.UtcDateTime)
        : DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Areas the agency will represent. Never empty when the dialog signs.</summary>
    public IReadOnlyList<string> Scopes
    {
        get
        {
            List<string> scopes = [];

            Add(scopes, FilmBox, "Film");
            Add(scopes, TelevisionBox, "Television");
            Add(scopes, LiteraryBox, "Literary");
            Add(scopes, ActingBox, "Acting");
            Add(scopes, DirectingBox, "Directing");
            Add(scopes, ProducingBox, "Producing");
            Add(scopes, TheatreBox, "Theatre");
            Add(scopes, BooksBox, "Books");

            return scopes;
        }
    }

    /// <summary>Exclusivity, or null when it has not been established.</summary>
    /// <remarks>
    /// Three-state on purpose: "we have not agreed this yet" is a real answer, and
    /// a two-state checkbox would assert something nobody checked.
    /// </remarks>
    public bool? IsExclusive => ExclusiveBox.IsChecked;

    public string? Territory =>
        string.IsNullOrWhiteSpace(TerritoryBox.Text) ? null : TerritoryBox.Text.Trim();

    private void OnRequiredChanged(object sender, object e) =>
        IsPrimaryButtonEnabled = StartPicker.Date is not null && Scopes.Count > 0;

    private static void Add(List<string> scopes, CheckBox box, string area)
    {
        if (box.IsChecked == true)
        {
            scopes.Add(area);
        }
    }
}
