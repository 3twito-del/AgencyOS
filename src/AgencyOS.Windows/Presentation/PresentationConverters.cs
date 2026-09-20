using System.Globalization;
using Microsoft.UI.Xaml;
using System;
using AgencyOS.Client.Presentation;
using Microsoft.UI.Xaml.Data;

namespace AgencyOS.Windows.Presentation;

/// <summary>
/// Gives a list row the name a screen reader should announce.
/// </summary>
/// <remarks>
/// Bound once per template root as <c>AutomationProperties.Name</c>, so a row says
/// what it shows instead of reciting its record. The decision about what to say
/// lives in <see cref="RowLabel"/>, in the client assembly, where a test can reach
/// it without standing up a window.
/// </remarks>
public sealed partial class RowLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        RowLabel.For(value);

    /// <inheritdoc />
    /// <remarks>
    /// One way only. An accessible name is derived from a row and is never read
    /// back into one.
    /// </remarks>
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("An accessible name is not converted back into a row.");
}

/// <summary>
/// Writes a domain token as the words a person reads.
/// </summary>
/// <remarks>
/// <c>TalentEmployment</c> becomes "Talent employment". The value's identity is
/// unchanged — nothing is parsed back, and no enum was renamed to make this work.
/// </remarks>
public sealed partial class DisplayLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        DisplayLabel.For(value?.ToString());

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A display label is not converted back into a token.");
}

/// <summary>
/// Shows a control only when the bound text is actually there.
/// </summary>
/// <remarks>
/// Written for the detailed notes on an interaction, which most interactions do
/// not have. An empty expander that opens onto nothing is worse than no expander:
/// it promises there is more to read and then says nothing.
/// </remarks>
public sealed partial class PresentVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Visibility is not converted back into text.");
}

/// <summary>
/// Writes a date the one way this product writes dates.
/// </summary>
/// <remarks>
/// The blind-handoff retest of build 79 found the same representation start date
/// rendered twice on one tab, five lines apart, as "04/06/2026" and "06/04/2026" —
/// the summary formatted with the invariant short date and the rows left to the
/// default binding. Both were 2026-04-06, and an operator reading them would
/// conclude representation began two months later than it did.
/// <c>yyyy-MM-dd</c> is what the legal surfaces already use, and it cannot be read
/// two ways.
/// </remarks>
public sealed partial class IsoDateConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset moment =>
                moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime moment => moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A rendered date is not converted back.");
}
