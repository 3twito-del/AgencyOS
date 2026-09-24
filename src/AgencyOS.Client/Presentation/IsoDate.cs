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
/// The Windows <c>IsoDateConverter</c> writes what a row shows through this, and
/// <see cref="RowLabel"/> writes what the row announces through it too, so the two
/// channels cannot format one date two ways. It lives here rather than in the
/// converter so that a test can run it: a formatter only the WinUI assembly could
/// execute is one the parity gate would have to copy.
/// </para>
/// <para>
/// An instant is written in its own offset, not converted to local time. That is
/// what the converter has always done, and changing it here would move every date
/// the product already shows.
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
