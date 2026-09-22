using System.Globalization;
using Microsoft.UI.Xaml;
using System;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
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

/// <summary>Who is accountable for a task row, or nothing when it cannot say.</summary>
/// <remarks>
/// Bound to the row itself rather than a field, because whether a projection may
/// speak about ownership depends on which fields it declares, not on their values.
/// The decision lives in <see cref="TaskLine"/>, where a test reaches it without a
/// window.
/// </remarks>
public sealed partial class TaskWhoConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        TaskLine.Who(value) ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A task's owner is not converted back into a row.");
}

/// <summary>When a task row is due, or that nobody set a date.</summary>
public sealed partial class TaskWhenConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        TaskLine.When(value);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A due date is not converted back into a row.");
}

/// <summary>What a task row concerns, labelled so it cannot read as who owns it.</summary>
public sealed partial class TaskAboutConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        TaskLine.About(value) ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A task's subject is not converted back into a row.");
}

/// <summary>
/// The counterparty individual on a target row, labelled as one.
/// </summary>
/// <remarks>
/// Bound to the row rather than to <c>ContactDisplayName</c>, so that the visible
/// caption and the announced row read the same answer out of
/// <see cref="TargetLine"/> and cannot drift into naming different people, which
/// is what they did before build 83.
/// </remarks>
public sealed partial class TargetContactConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        TargetLine.Contact(value) ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A target's contact is not converted back into a row.");
}

/// <summary>
/// Money, with the currency it is denominated in.
/// </summary>
/// <remarks>
/// <para>
/// Every Finance row bound <c>Something.Amount</c> — the raw decimal out of
/// <c>MoneyResponse</c>, discarding <c>Currency</c>. Reality Closure recorded the
/// result live: a receivable row reading <c>240000.0000 | 90000.0000 |
/// 150000.0000</c> two lines beneath a summary reading
/// <c>Outstanding: 140,000.00 GBP   1,255,000.00 USD</c>. On a screen holding two
/// currencies there was nothing in the row to say which one it was in (F-11).
/// </para>
/// <para>
/// The product already states the rule itself, in <c>RecordOfferDialog</c>:
/// <em>"Money always carries its currency; nothing here accepts a bare number."</em>
/// The dialogs obeyed it and the rows did not.
/// </para>
/// <para>
/// This binds the whole <c>MoneyResponse</c> and formats it through the same
/// <c>MoneyFormatting.Format</c> the summaries have always used, so a row and the
/// total above it cannot disagree about what a figure means. An absent amount
/// stays absent: it formats to nothing rather than to a zero nobody recorded.
/// </para>
/// </remarks>
public sealed partial class MoneyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        MoneyFormatting.Format(value as MoneyResponse);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A formatted amount is not parsed back into money.");
}

/// <summary>
/// A person named on a row, with the role they play in it.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the row and told which field to read, rather than bound to the field
/// itself, because a value converter given only a string cannot know what that
/// string is to the record — and the role is the part that was missing. The
/// markup names the field; <see cref="PartyLine"/> owns the words, and
/// <see cref="RowLabel"/> reads the same ones, so the caption an operator sees
/// and the phrase a screen reader announces cannot disagree about what a name
/// means.
/// </para>
/// <para>
/// Empty rather than the bare name when the field is not one of the roles
/// <see cref="PartyLine"/> knows. A caption that silently fell back to an
/// unlabelled name would look repaired and be exactly the defect.
/// </para>
/// </remarks>
public sealed partial class PartyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        PartyLine.For(value, parameter as string) ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("An attributed name is not converted back into a row.");
}
