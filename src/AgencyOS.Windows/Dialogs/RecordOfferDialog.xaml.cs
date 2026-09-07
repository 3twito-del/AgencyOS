using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AgencyOS.Contracts.Deals;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records an offer as made or received, with its commercial terms.
/// </summary>
/// <remarks>
/// <para>
/// The same dialog serves inbound, outbound and counters. Which it is comes from
/// the direction and from whether an offer is already on the table; a counter is a
/// new offer answering the standing one, and it never edits what the other side
/// put forward (ADR-0021).
/// </para>
/// <para>
/// The term vocabulary comes from the server's catalog rather than being
/// hard-coded here, so the editor cannot drift from what the server validates
/// against.
/// </para>
/// </remarks>
public sealed partial class RecordOfferDialog : ContentDialog
{
    private readonly string _direction;
    private readonly OfferResponse? _answering;
    private readonly Dictionary<string, DealTermDefinitionResponse> _catalog = [];

    public RecordOfferDialog(
        string direction,
        string counterparty,
        OfferResponse? standingOffer,
        IEnumerable<DealTermDefinitionResponse>? catalog)
    {
        InitializeComponent();

        _direction = direction;

        // A counter answers whatever is on the table. When nothing is, this is
        // simply the first offer in the thread.
        _answering = standingOffer;

        HeadlineText.Text = direction == "Inbound"
            ? $"What {counterparty} proposed"
            : $"What we proposed to {counterparty}";

        Title = direction == "Inbound" ? "Record inbound offer" : "Record outbound offer";

        CounterBar.IsOpen = standingOffer is not null;

        CommunicatedPicker.Date = DateTimeOffset.UtcNow;
        TermList.ItemsSource = Terms;

        foreach (DealTermDefinitionResponse definition in catalog ?? [])
        {
            _catalog[definition.Code] = definition;

            TermBox.Items.Add(new ComboBoxItem
            {
                Content = definition.DisplayName,
                Tag = definition.Code,
            });
        }

        if (TermBox.Items.Count > 0)
        {
            TermBox.SelectedIndex = 0;
        }
    }

    /// <summary>The terms staged for this offer.</summary>
    public ObservableCollection<StagedTerm> Terms { get; } = [];

    public RecordOfferRequest ToRequest(int expectedVersion)
    {
        DealFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new DealFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), DateTimeOffset.UtcNow.AddDays(3))
                : null;

        return new RecordOfferRequest(
            _direction,
            [.. Terms.Select(term => term.ToRequest())],
            expectedVersion,
            _answering?.Id,
            CommunicatedPicker.Date,
            ExpiryBox.IsChecked == true ? ExpiryPicker.Date : null,
            Empty(SummaryBox.Text),
            Empty(NotesBox.Text),
            followUp);
    }

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void OnExpiryToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ExpiryPicker.IsEnabled = ExpiryBox.IsChecked == true;

    private void OnTermChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedDefinition() is not { } definition)
        {
            return;
        }

        // A money term needs its currency and nothing else does, so the field is
        // shown only when it means something.
        CurrencyBox.IsEnabled = definition.ValueKind == "Money";

        TermHintText.Text = definition.ValueKind switch
        {
            "Money" => "An amount and its currency.",
            "Percentage" => definition.Maximum is { } max
                ? string.Create(CultureInfo.InvariantCulture, $"A percentage, up to {max}.")
                : "A percentage.",
            "Count" => "A whole number.",
            "Duration" => "A length, in the unit the term allows.",
            "Date" => "A date, as yyyy-mm-dd.",
            _ => "Free text.",
        };
    }

    private void OnAddTermClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (SelectedDefinition() is not { } definition || string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            return;
        }

        // One row per term code: comparison joins on the code, and two rows sharing
        // one would make the diff ambiguous.
        if (Terms.Any(term => term.Code == definition.Code))
        {
            return;
        }

        if (Build(definition, ValueBox.Text.Trim(), CurrencyBox.Text.Trim()) is not { } staged)
        {
            return;
        }

        Terms.Add(staged);
        ValueBox.Text = string.Empty;

        Validate();
    }

    private void OnRemoveTermClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (TermList.SelectedItem is StagedTerm selected)
        {
            Terms.Remove(selected);
            Validate();
        }
    }

    /// <summary>
    /// Turns what the user typed into a typed value.
    /// </summary>
    /// <remarks>
    /// Parsed with invariant culture and as <c>decimal</c>, never
    /// <c>double</c>: an amount that went through binary floating point on the way
    /// in would already be wrong before the server saw it.
    /// </remarks>
    private static StagedTerm? Build(DealTermDefinitionResponse definition, string value, string currency)
    {
        switch (definition.ValueKind)
        {
            case "Money":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
                {
                    return null;
                }

                return new StagedTerm(
                    definition.Code,
                    definition.DisplayName,
                    string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency.ToUpperInvariant()}"),
                    new TermValueRequest("Money", Amount: amount, Currency: currency.ToUpperInvariant()));

            case "Percentage":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal percentage))
                {
                    return null;
                }

                return new StagedTerm(
                    definition.Code,
                    definition.DisplayName,
                    string.Create(CultureInfo.InvariantCulture, $"{percentage:0.####}%"),
                    new TermValueRequest("Percentage", Number: percentage));

            case "Count":
            case "Integer":
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
                {
                    return null;
                }

                return new StagedTerm(
                    definition.Code,
                    definition.DisplayName,
                    whole.ToString(CultureInfo.InvariantCulture),
                    new TermValueRequest(
                        definition.ValueKind,
                        Whole: whole,
                        Unit: definition.AllowedUnits.Count > 0 ? definition.AllowedUnits[0] : null));

            case "Duration":
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long length)
                    || definition.AllowedUnits.Count == 0)
                {
                    return null;
                }

                return new StagedTerm(
                    definition.Code,
                    definition.DisplayName,
                    string.Create(CultureInfo.InvariantCulture, $"{length} {definition.AllowedUnits[0]}"),
                    new TermValueRequest(
                        "Duration", Whole: length, Unit: definition.AllowedUnits[0]));

            case "Date":
                if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly date))
                {
                    return null;
                }

                return new StagedTerm(
                    definition.Code, definition.DisplayName, date.ToString("O"),
                    new TermValueRequest("Date", Date: date));

            default:
                return new StagedTerm(
                    definition.Code, definition.DisplayName, value,
                    new TermValueRequest("Text", Text: value));
        }
    }

    private void Validate() =>
        IsPrimaryButtonEnabled = Terms.Count > 0 && !string.IsNullOrWhiteSpace(SummaryBox.Text);

    private DealTermDefinitionResponse? SelectedDefinition() =>
        (TermBox.SelectedItem as ComboBoxItem)?.Tag is string code
            && _catalog.TryGetValue(code, out DealTermDefinitionResponse? definition)
            ? definition
            : null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A term the user has staged, with what it will be sent as.
    /// </summary>
    /// <remarks>
    /// A plain class with read-only properties rather than a record: the XAML type
    /// generator emits assignments for init-only setters and then refuses to
    /// compile them, and this type exists only to be bound to a list.
    /// </remarks>
    public sealed class StagedTerm
    {
        public StagedTerm(string code, string displayName, string displayValue, TermValueRequest value)
        {
            Code = code;
            DisplayName = displayName;
            DisplayValue = displayValue;
            Value = value;
        }

        public string Code { get; }

        public string DisplayName { get; }

        public string DisplayValue { get; }

        public TermValueRequest Value { get; }

        public OfferTermRequest ToRequest() => new(Code, Value);
    }
}
