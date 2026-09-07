using System.Globalization;
using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.Deals;

/// <summary>
/// An ISO 4217 alphabetic currency code.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than a string, because the alternative is every layer deciding
/// for itself whether "usd", "US$" and "Dollars" are the same thing. Codes are
/// validated against the table below, so an unrecognised one is refused at the
/// boundary instead of stored and discovered later.
/// </para>
/// <para>
/// The table is a curated working set, not all of ISO 4217. It covers the
/// currencies a film and television representation agency plausibly transacts in,
/// and each entry carries its minor-unit count because rounding a JPY amount to
/// two places and a KWD amount to two places are both wrong. Adding a currency is
/// one row; refusing an unknown one is deliberate, because silently accepting
/// "XYZ" would produce money nobody can settle (ADR-0021).
/// </para>
/// </remarks>
public readonly record struct CurrencyCode
{
    /// <summary>
    /// The currencies this build knows, with how many decimal places each has.
    /// </summary>
    /// <remarks>
    /// Minor units come from ISO 4217 itself. The zero-decimal and three-decimal
    /// entries are the ones that matter: they are why this is a table rather than
    /// an assumption that money has two decimal places.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> Known =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // The currencies most agency business is denominated in.
            ["USD"] = 2,
            ["EUR"] = 2,
            ["GBP"] = 2,
            ["CAD"] = 2,
            ["AUD"] = 2,
            ["NZD"] = 2,
            ["CHF"] = 2,

            // Zero-decimal currencies. Rounding these to two places invents money.
            ["JPY"] = 0,
            ["KRW"] = 0,
            ["ISK"] = 0,
            ["CLP"] = 0,
            ["VND"] = 0,
            ["HUF"] = 2,

            // Asia-Pacific.
            ["CNY"] = 2,
            ["HKD"] = 2,
            ["SGD"] = 2,
            ["TWD"] = 2,
            ["MYR"] = 2,
            ["THB"] = 2,
            ["IDR"] = 2,
            ["PHP"] = 2,
            ["INR"] = 2,

            // Europe outside the euro.
            ["SEK"] = 2,
            ["NOK"] = 2,
            ["DKK"] = 2,
            ["PLN"] = 2,
            ["CZK"] = 2,
            ["RON"] = 2,
            ["BGN"] = 2,
            ["UAH"] = 2,
            ["TRY"] = 2,

            // Americas.
            ["MXN"] = 2,
            ["BRL"] = 2,
            ["ARS"] = 2,
            ["COP"] = 2,
            ["PEN"] = 2,

            // Middle East and Africa. The three-decimal currencies are here.
            ["ILS"] = 2,
            ["AED"] = 2,
            ["SAR"] = 2,
            ["QAR"] = 2,
            ["EGP"] = 2,
            ["ZAR"] = 2,
            ["NGN"] = 2,
            ["KES"] = 2,
            ["MAD"] = 2,
            ["KWD"] = 3,
            ["BHD"] = 3,
            ["OMR"] = 3,
            ["JOD"] = 3,
            ["TND"] = 3,
        };

    private CurrencyCode(string value) => Value = value;

    /// <summary>The three-letter code, upper case.</summary>
    public string Value { get; }

    /// <summary>How many decimal places amounts in this currency carry.</summary>
    public int MinorUnits => Known.TryGetValue(Value, out int units) ? units : 2;

    /// <summary>Every currency this build accepts.</summary>
    public static IReadOnlyCollection<string> All => (IReadOnlyCollection<string>)Known.Keys;

    /// <summary>Parses a code, refusing anything this build does not know.</summary>
    public static CurrencyCode Parse(string? value)
    {
        string trimmed = Ensure.NotBlankMax(value, nameof(value), 3).ToUpperInvariant();

        if (!Known.ContainsKey(trimmed))
        {
            throw new DomainException(
                $"'{trimmed}' is not a currency this build accepts. "
                + "Supported codes are ISO 4217 alphabetic codes from the AgencyOS currency table.");
        }

        return new CurrencyCode(trimmed);
    }

    /// <summary>Parses a code, or returns null when it is not one this build knows.</summary>
    public static CurrencyCode? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim().ToUpperInvariant();

        return Known.ContainsKey(trimmed) ? new CurrencyCode(trimmed) : null;
    }

    /// <summary>Whether this build accepts a code.</summary>
    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Known.ContainsKey(value.Trim().ToUpperInvariant());

    public override string ToString() => Value;
}

/// <summary>
/// An amount of money in a stated currency.
/// </summary>
/// <remarks>
/// <para>
/// Never a naked number. A compensation figure without its currency is a fact the
/// agency cannot act on, and the moment one is stored somebody assumes dollars.
/// </para>
/// <para>
/// The amount is <see cref="decimal"/>, and there is no constructor, conversion or
/// helper here that touches <see cref="double"/> or <see cref="float"/>. Binary
/// floating point is forbidden for money (CLAUDE.md section 5) because it cannot
/// represent a tenth exactly, and a deal that pays 0.1 of anything would drift.
/// </para>
/// <para>
/// Amounts are held to the currency's own minor units. Rounding is
/// <see cref="MidpointRounding.ToEven"/>, applied once at construction rather than
/// at display, so two components of a deal cannot disagree about what the same
/// figure is.
/// </para>
/// </remarks>
public readonly record struct Money
{
    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>The amount, held to the currency's minor units.</summary>
    public decimal Amount { get; }

    /// <summary>What the amount is denominated in.</summary>
    public CurrencyCode Currency { get; }

    /// <summary>Creates an amount, refusing negatives and over-precise figures.</summary>
    /// <remarks>
    /// Negative money is refused because M7 records what somebody is to be paid.
    /// A deduction is a different term, and expressing it as negative compensation
    /// would make every total silently wrong.
    /// </remarks>
    public static Money Create(decimal amount, CurrencyCode currency)
    {
        if (amount < 0m)
        {
            throw new DomainException("A money term must not be negative.");
        }

        // Guarded before rounding: rounding a figure with more precision than the
        // currency has would quietly change what somebody typed.
        if (decimal.Round(amount, currency.MinorUnits, MidpointRounding.ToEven) != amount)
        {
            throw new DomainException(
                $"{currency} carries {currency.MinorUnits} decimal place(s); "
                + $"{amount.ToString(CultureInfo.InvariantCulture)} has more.");
        }

        return new Money(amount, currency);
    }

    /// <summary>Creates an amount from a currency code that has not been parsed yet.</summary>
    public static Money Create(decimal amount, string? currency) =>
        Create(amount, CurrencyCode.Parse(currency));

    /// <summary>
    /// Renders the amount for a machine, never for a person.
    /// </summary>
    /// <remarks>
    /// Invariant culture and the full figure. "$500k" is a thing a screen may say;
    /// it is not a thing the system may store, because a value nobody can parse
    /// back is a value the next milestone cannot reconcile against a contract.
    /// </remarks>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Amount.ToString("F" + Currency.MinorUnits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {Currency}");
}
