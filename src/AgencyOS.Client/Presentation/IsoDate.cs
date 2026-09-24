using System.Globalization;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// A date, written the one way this product writes dates.
/// </summary>
/// <remarks>
/// <para>
/// <c>yyyy-MM-dd</c>, because it cannot be read two ways. The blind-handoff retest
/// of build 79 found one representation start date rendered twice on one tab as
/// "04/06/2026" and "06/04/2026".
/// </para>
/// <para>
/// The Windows <c>IsoDateConverter</c> writes what a row shows through this. It lives
/// here rather than in the converter so that a test can run it: a formatter only the
/// WinUI assembly could execute is one the operational parity gate would have to copy.
/// A profiled <see cref="RowLabel"/> writes a row's calendar dates through it too, so
/// the date a row shows and the one it announces are one string.
/// </para>
/// <para>
/// It writes a calendar date and nothing else. Given an instant it writes that
/// instant's calendar day in the instant's own offset, not converted to local time,
/// and drops the time and the offset: that is what the converter has always shown,
/// and changing it here would move every date the product already shows. An instant
/// a profiled row shows raw, to the second and with its offset, is announced by
/// <see cref="RowLabel"/>'s own profiled formatting, not by this.
/// </para>
/// </remarks>
public static class IsoDate
{
    /// <summary>The date, or nothing where the value is not a date.</summary>
    public static string Format(object? value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}
