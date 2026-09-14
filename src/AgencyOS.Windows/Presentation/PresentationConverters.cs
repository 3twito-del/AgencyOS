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
