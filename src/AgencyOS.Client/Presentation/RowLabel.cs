using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// What a list row should say when it is read aloud.
/// </summary>
/// <remarks>
/// <para>
/// A templated <c>ListView</c> row whose template sets no accessible name falls
/// back to <c>ToString()</c> on the bound object, and a C# record's
/// <c>ToString()</c> prints every property it has. Audit 001 measured the result:
/// 115 rows across twelve surfaces announcing their whole record, one deal row
/// reaching 894 characters and six identifiers, beginning
/// <c>DealSummaryResponse { Id = 01a0…</c> (<c>AOS-R001-003</c>).
/// </para>
/// <para>
/// The rows render correctly on screen, so this is invisible to a sighted
/// reviewer and total for anybody using a screen reader.
/// </para>
/// <para>
/// <strong>One formatter rather than ninety-nine bindings.</strong> The client has
/// ninety-nine templated lists. Writing a bespoke accessible name into each would
/// be ninety-nine chances to disagree, and every list added later would start
/// wrong again. This reads the same fields a row displays — a name, what kind of
/// thing it is, where it stands — and writes them as a short phrase.
/// </para>
/// <para>
/// <strong>What it refuses to say.</strong> Identifiers are dropped: a
/// <see cref="Guid"/> is not information a person can act on, and six of them in
/// a row is the defect. Internal version counters are dropped for the same reason.
/// Domain tokens go through <see cref="DisplayLabel"/>, so a row says "Talent
/// employment" where the record holds <c>TalentEmployment</c>.
/// </para>
/// <para>
/// <strong>What it keeps.</strong> Status and kind are meaningful state and stay,
/// because a list of deals where every row announces only its name would hide the
/// thing the list is scanned for.
/// </para>
/// </remarks>
public static class RowLabel
{
    /// <summary>How long a row may speak before it stops being scannable.</summary>
    private const int MaximumLength = 160;

    /// <summary>
    /// The least of a title worth keeping when the roles have taken the rest.
    /// </summary>
    /// <remarks>
    /// Below this a truncated title is an ellipsis with a syllable in front of
    /// it. It is the floor a yielding value is never shortened past: a row whose
    /// other facts leave less keeps this much and runs past 160, rather than drop
    /// the value (D5, <see cref="Fragment"/>).
    /// </remarks>
    private const int MinimumHeadline = 12;

    /// <summary>Properties that carry the row's headline, best first.</summary>
    private static readonly string[] Headline =
    [
        "DisplayName", "Name", "Title", "Question", "Statement", "Proposition",
        "Claim", "Subject", "SubjectDisplayName", "Label", "MailboxAddress",
        "Address", "Summary", "Description", "Reference", "Code",

        // Last, and only where nothing above exists. Three shipped lists had no
        // headline property at all and announced their own type name, so every
        // row in them read identically: document versions, representation scopes
        // and the ledger's account rows (F-08).
        "DisplayFileName", "Area", "Action", "Memo",

        // Also last. A radar row shows the person it is about as its headline
        // and carries no other name property, so the announcement led with the
        // type name and then named the CompanyName in the context slot: the seen
        // row headed by a person and the spoken row by a company (F-09).
        "PersonDisplayName",
    ];

    /// <summary>Properties that say what kind of thing the row is, or where it stands.</summary>
    private static readonly string[] Qualifiers =
    [
        "Status", "Stage", "State", "Kind", "Type", "Direction", "Outcome",
        "Severity", "Discipline", "Role", "Priority",
    ];

    /// <summary>
    /// The row's business value, where the server has already written one.
    /// </summary>
    /// <remarks>
    /// A term row shows <c>Fee | 185,000.00 USD</c> and announced <c>Fee</c>.
    /// Both offer and contract terms carry <c>DisplayValue</c> — the value
    /// formatted once, on the server, by whatever rule the term's kind demands —
    /// and the visible column binds exactly that field. Announcing the same field
    /// gives the two channels one answer by construction rather than by a second
    /// formatter that could disagree (F-02).
    /// </remarks>
    private static readonly string[] Stated = ["DisplayValue"];

    /// <summary>
    /// Money the row carries, in the order an operator reads the columns.
    /// </summary>
    /// <remarks>
    /// Curated rather than discovered, so the order a row speaks in is decided
    /// here and not by reflection's property ordering. Anything typed as money
    /// that is not named here is still announced, after these, so a new figure
    /// cannot go silent.
    /// </remarks>
    private static readonly string[] Monetary =
    [
        "Amount", "Total", "Entitled", "OriginalAmount",
        "Debits", "Credits", "Balance",
        "Allocated", "Collected", "Adjusted", "Unapplied", "Outstanding",
    ];

    /// <summary>A third field, when the row's own name is not enough to tell rows apart.</summary>
    private static readonly string[] Context =
    [
        "PrimaryCompanyName", "CompanyName",
        "OpportunityName", "ProjectTitle", "Publisher",
    ];

    /// <summary>Writes the phrase a row should announce.</summary>
    /// <param name="row">The bound item, or null.</param>
    /// <returns>A short phrase, never a record dump and never empty for a real row.</returns>
    public static string For(object? row)
    {
        if (row is null)
        {
            return string.Empty;
        }

        // A row that is already a string says itself. Several lists bind plain
        // strings - agent kinds, tool names - and those are already correct.
        if (row is string text)
        {
            return Shorten(text);
        }

        Type type = row.GetType();

        if (type.IsPrimitive || row is Guid or DateTimeOffset or DateTime or decimal)
        {
            return Shorten(Convert.ToString(row, CultureInfo.CurrentCulture) ?? string.Empty);
        }

        List<string> parts = [];

        string? headline = FirstValue(row, type, Headline);

        // A record that carries none of the known headline properties is not
        // something this can describe honestly, so it says the kind of thing it is
        // rather than inventing a name or falling back to ToString().
        parts.Add(headline ?? DisplayLabel.For(FriendlyTypeName(type)));

        // A target row carries two people - the counterparty contact and the
        // internal member - and this announced the second while the screen showed
        // the first, with neither channel saying which role it meant. Asking
        // PartyLine keeps the two channels reading one answer, and says the role.
        // The same slot holds the payer on a receivable, the debtor on an invoice,
        // the obligor on an obligation, the counterparty on a deal and whoever
        // acted on a history row, and it held every one of them as a bare name
        // or not at all (F-04, F-09, F-10).
        string? context = PartyLine.Who(row, headline) ?? FirstValue(row, type, Context);

        if (context is not null && !Same(context, headline))
        {
            parts.Add(context);
        }

        // A prediction row says the forecast, whose it is, how it came out and when
        // it resolves. It announced the statement, its owner, its status and the
        // raw outcome token - "Yes" - and never the probability, which is the one
        // thing a forecast is. Its outcome is said through ForecastLine instead of
        // the generic qualifier, so "Yes" is read out as "Happened" in both
        // channels (F-14). Only the statement yields to the budget: the first live
        // reading cut the status instead, because it sat on the shortened side.
        if (ForecastLine.IsPrediction(row))
        {
            List<string> essential = [.. parts.Skip(1)];

            if (Value(row, type, "Status") is { } status)
            {
                essential.Add(DisplayLabel.For(status));
            }

            essential.AddRange(ForecastLine.Essentials(row));

            return WithEssentials([parts[0]], essential);
        }

        // How much. Every quantitative row in the product announced what it was
        // and what state it was in, and never the figure - so a screen-reader
        // operator could scan the agreed terms of a negotiation and hear "Fee",
        // "Term", "Territory" without a single number, and scan receivables
        // without an amount. For a sighted operator the number is column one
        // (F-02).
        parts.AddRange(Values(row, type));

        foreach (string qualifier in Qualifiers)
        {
            if (Value(row, type, qualifier) is { } value && !Same(value, headline))
            {
                parts.Add(DisplayLabel.For(value));

                // Two qualifiers is the most a row can say without becoming a
                // sentence nobody listens to the end of.
                if (parts.Count >= 4)
                {
                    break;
                }
            }
        }

        // A task row says what it is about, who is accountable and when it is due,
        // because those are the questions asked of it. Nothing else on the row
        // carries them: the assignee is not in any of the vocabularies above, and a
        // screen reader that announced only "title, open, high" left a blind
        // operator unable to tell an assigned task from an unowned one - which is
        // what a sighted operator could not do either, before the rows themselves
        // were repaired. The subject joined them in build 83: the Command Center
        // showed "About X" and announced nothing of it, so the seen and the spoken
        // row disagreed about what the row contained.
        if (TaskLine.IsTask(row))
        {
            List<string> essential = [];

            if (TaskLine.About(row) is { } about)
            {
                essential.Add(about);
            }

            if (TaskLine.Who(row) is { } who)
            {
                essential.Add(who);
            }

            essential.Add(TaskLine.When(row));

            return WithEssentials(parts, essential);
        }

        return Shorten(string.Join(", ", parts));
    }

    /// <summary>Writes the phrase a row announces under its template's profile.</summary>
    /// <param name="row">The bound item, or null.</param>
    /// <param name="profile">
    /// The id of the <see cref="RowProfile"/> the template names, or null for a row whose
    /// announcement is inferred.
    /// </param>
    /// <returns>
    /// The profile's fields, each in its role, and nothing the profile does not name.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A value that is absent is not said, rather than said as nothing. A row that
    /// would run past the budget shortens the one field its profile lets yield and
    /// keeps every other in full, as a task row keeps its owner and due date; a
    /// field that overflows is offered whole on the row's help text (D4).
    /// </para>
    /// <para>
    /// An unknown id falls back to the inferred phrase rather than failing a screen
    /// reader. The Windows parity tests hold every id a template names to a profile.
    /// </para>
    /// </remarks>
    public static string For(object? row, string? profile)
    {
        if (row is null)
        {
            return string.Empty;
        }

        return RowProfiles.Find(profile) is { } found
            ? Profiled(row, found)
            : For(row);
    }

    private sealed record Spoken(RowField Field, string Prefix, string Value)
    {
        public string Text => Prefix + Value;
    }

    private static string Profiled(object row, RowProfile profile)
    {
        List<Spoken> parts = [.. profile.Fields.Select(x => Say(row, x)).OfType<Spoken>()];

        string whole = string.Join(", ", parts.Select(x => x.Text));
        int yielding = parts.FindIndex(x => x.Field.Yields);

        if (whole.Length <= MaximumLength)
        {
            return Shorten(whole);
        }

        // The field this profile lets yield is absent from this row, so every part
        // that remains is a scan fact that may not. Shortening the whole would cut
        // the last of them - a payment with no reference lost its status and date
        // - so the row keeps them all and runs past 160 (D5). Nothing is said for
        // the absent value.
        if (yielding < 0)
        {
            return whole.Trim();
        }

        Spoken yields = parts[yielding];
        string rest = string.Join(", ", parts.Where((_, i) => i != yielding).Select(x => x.Text));

        // Two for the separator, where anything else is said.
        int taken = rest.Length + (rest.Length > 0 ? 2 : 0) + yields.Prefix.Length;

        parts[yielding] = yields with { Value = Fragment(yields.Value, taken) };

        return string.Join(", ", parts.Select(x => x.Text));
    }

    /// <summary>
    /// The yielding value of a row that does not fit, given what the rest of the row takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one budget rule for every value this lets yield — a profiled field, a
    /// task's title, a prediction's statement. The value is shortened to what the
    /// rest leaves of the 160, and never below <see cref="MinimumHeadline"/>.
    /// </para>
    /// <para>
    /// It used to be dropped below that, and the rest cut to fit. With legal names
    /// that erased what the row shows: a payment's reference, a receivable's contract,
    /// a task's title with its due date cut, a prediction's statement. Truth outranks
    /// the budget (owner decision D5): every other fact stays whole, the yielding value
    /// keeps its role and a fragment an operator can recognise, and the row runs past
    /// 160 by exactly what those require. There is no other cap. The whole value is on
    /// the row: on its help text where the profile marks it as overflowing, and as the
    /// visible value the row shows otherwise.
    /// </para>
    /// </remarks>
    private static string Fragment(string value, int taken) =>
        Shorten(value, Math.Max(MaximumLength - taken, MinimumHeadline));

    /// <summary>One profiled field, in its role, or null where the row holds no value.</summary>
    private static Spoken? Say(object row, RowField field)
    {
        if (field.Kind == RowFieldKind.Party)
        {
            return PartyLine.For(row, field.Path) is { } party ? new Spoken(field, string.Empty, party) : null;
        }

        object? value = ReadPath(row, field.Path);

        string? written = field.Kind switch
        {
            RowFieldKind.Token => value is null ? null : DisplayLabel.For(value.ToString()),
            RowFieldKind.Money => value is null ? null : MoneyText(value),
            _ => Written(value),
        };

        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        written = written.Trim();

        if (field.Kind is RowFieldKind.Money or RowFieldKind.Suffixed)
        {
            // Money reads as the columns do, "240,000.00 GBP original", and a
            // beneficiary as whose money it is: "Client money".
            return new Spoken(field, string.Empty, field.Role is null ? written : written + " " + field.Role);
        }

        string? role = field.RoleFrom is { } source
            ? Written(ReadPath(row, source)) is { Length: > 0 } kind ? DisplayLabel.For(kind) : null
            : field.Role;

        return new Spoken(field, role is null ? string.Empty : role + ": ", written);
    }

    /// <summary>A value as the row shows it, in one form that cannot be read two ways.</summary>
    private static string? Written(object? value) => value switch
    {
        null => null,
        string text => text,
        DateOnly => IsoDate.Format(value),

        // To the second and in its own offset, as the row shows it: an instant
        // is not a day.
        DateTimeOffset moment => moment.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "yes" : "no",
        Enum item => item.ToString(),
        IFormattable number => number.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString(),
    };

    private static object? ReadPath(object row, string path)
    {
        object? current = row;

        foreach (string segment in path.Split('.'))
        {
            if (current?.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance) is not { } property
                || property.GetIndexParameters().Length > 0)
            {
                return null;
            }

            current = Read(current, property);
        }

        return current;
    }

    /// <summary>
    /// A row whose essential answers are kept before the budget runs out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These three were appended last and so were cut first. A blind operator
    /// measured the result on build 82: "the longer the task, the less an assistive
    /// user is told about it" - a title long enough to reach the limit took the
    /// owner and the due date with it, leaving the one row shape whose whole
    /// purpose is to say who and when saying neither.
    /// </para>
    /// <para>
    /// So the title yields instead, and only the title: it is the part an operator
    /// can still recognise truncated. What follows it - the priority and status a
    /// task row shows - and the essential answers stay whole. Where they leave the
    /// title less than its minimum, the row keeps that minimum and runs past 160
    /// rather than drop the title or cut an answer (D5; see <see cref="Fragment"/>).
    /// </para>
    /// <para>
    /// A prediction row has the same shape: a statement long enough to fill the
    /// budget would otherwise take the forecast and the date with it (F-14).
    /// </para>
    /// </remarks>
    private static string WithEssentials(List<string> parts, List<string> essential)
    {
        string whole = string.Join(", ", parts.Concat(essential));

        if (whole.Length <= MaximumLength)
        {
            return whole;
        }

        string rest = string.Join(", ", parts.Skip(1).Concat(essential));

        // Two for the separator that joins the title to the rest.
        return rest.Length == 0
            ? Shorten(parts[0])
            : string.Concat(Fragment(parts[0], rest.Length + 2), ", ", rest);
    }

    /// <summary>
    /// What the row is worth, said the way the row shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two shapes. A term carries <c>DisplayValue</c>, already formatted by the
    /// server for that term's kind, and the visible column binds the same field —
    /// so money, percentages, dates and counts all arrive correct without this
    /// formatter knowing anything about them.
    /// </para>
    /// <para>
    /// Money is typed, so it is found by its type rather than by its name and
    /// cannot be confused with an unrelated decimal. Each figure keeps the label
    /// of the field it came from, because a row announcing three bare sums tells
    /// an operator how much of something without saying of what. The first one
    /// speaks bare where the field is simply the row's own <c>Amount</c>.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Values(object row, Type type)
    {
        if (FirstValue(row, type, Stated) is { } written)
        {
            yield return written;

            // A term states its value once. Anything else on it is not a figure.
            yield break;
        }

        foreach (PropertyInfo property in Monetary
            .Select(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .Concat(Readable(type))
            .Where(x => x is not null && IsMoney(x.PropertyType))
            .Distinct()
            .Cast<PropertyInfo>())
        {
            if (Read(row, property) is not { } money)
            {
                // An absent amount stays absent. A contingent bonus nobody can
                // value yet is not worth zero, and saying so would put a figure
                // in the operator's head that nobody recorded.
                continue;
            }

            if (MoneyText(money) is not { } text)
            {
                continue;
            }

            yield return string.Equals(property.Name, "Amount", StringComparison.Ordinal)
                ? text
                : text + " " + DisplayLabel.For(property.Name).ToLowerInvariant();
        }
    }

    /// <summary>Whether a property holds money rather than a bare number.</summary>
    /// <remarks>
    /// By shape, not by name. <c>MoneyResponse</c> is the only pair of an amount
    /// and the currency it is denominated in, and matching on it means an
    /// unrelated decimal called <c>Amount</c> can never be read out as a sum.
    /// </remarks>
    private static bool IsMoney(Type type) =>
        type.Name.Equals("MoneyResponse", StringComparison.Ordinal);

    /// <summary>An amount with its currency, or nothing where either is missing.</summary>
    private static string? MoneyText(object money)
    {
        Type type = money.GetType();

        if (type.GetProperty("Amount")?.GetValue(money) is not decimal amount
            || type.GetProperty("Currency")?.GetValue(money) is not string currency
            || string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{amount:N2} {currency}");
    }

    private static string? FirstValue(object row, Type type, string[] names)
    {
        foreach (string name in names)
        {
            if (Value(row, type, name) is { } value)
            {
                return value;
            }
        }

        // One level of nesting, because the detail responses wrap their summary:
        // PersonDetailResponse carries a PersonSummaryResponse that holds the name.
        foreach (PropertyInfo property in Readable(type))
        {
            if (!IsComposite(property.PropertyType))
            {
                continue;
            }

            object? nested = Read(row, property);

            if (nested is null)
            {
                continue;
            }

            if (FirstValue(nested, nested.GetType(), names) is { } value)
            {
                return value;
            }

            // One level only.
            break;
        }

        return null;
    }

    private static string? Value(object row, Type type, string name)
    {
        PropertyInfo? property = type.GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance);

        if (property is null || property.GetIndexParameters().Length > 0)
        {
            return null;
        }

        // Identifiers and version counters are deliberately unreadable aloud.
        if (IsIdentity(property))
        {
            return null;
        }

        object? value = Read(row, property);

        string? written = value switch
        {
            null => null,
            string s => s,
            bool flag => flag ? name : null,
            Enum item => item.ToString(),
            _ => Convert.ToString(value, CultureInfo.CurrentCulture),
        };

        return string.IsNullOrWhiteSpace(written) ? null : written.Trim();
    }

    private static object? Read(object row, PropertyInfo property)
    {
        try
        {
            return property.GetValue(row);
        }
        catch (TargetInvocationException)
        {
            // A computed property that throws is not worth failing a screen
            // reader over.
            return null;
        }
    }

    private static IEnumerable<PropertyInfo> Readable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.GetIndexParameters().Length == 0);

    /// <summary>Whether a property is an identifier or a concurrency counter.</summary>
    private static bool IsIdentity(PropertyInfo property) =>
        property.PropertyType == typeof(Guid)
        || property.PropertyType == typeof(Guid?)
        || property.Name.EndsWith("Id", StringComparison.Ordinal)
        || string.Equals(property.Name, "Version", StringComparison.Ordinal);

    private static bool IsComposite(Type type) =>
        !type.IsPrimitive
        && type != typeof(string)
        && type != typeof(Guid)
        && type != typeof(decimal)
        && type != typeof(DateTimeOffset)
        && type != typeof(DateTime)
        && !type.IsEnum
        && !typeof(IEnumerable).IsAssignableFrom(type);

    private static bool Same(string value, string? other) =>
        other is not null && string.Equals(value, other, StringComparison.Ordinal);

    /// <summary>The record's name without the transport suffix.</summary>
    private static string FriendlyTypeName(Type type)
    {
        string name = type.Name;

        foreach (string suffix in (string[])["Response", "Model", "ViewModel", "Summary"])
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
            }
        }

        return name;
    }

    private static string Shorten(string value) => Shorten(value, MaximumLength);

    private static string Shorten(string value, int limit)
    {
        string clean = value.Trim();

        if (limit < 2)
        {
            return string.Empty;
        }

        if (clean.Length <= limit)
        {
            return clean;
        }

        int cut = clean.LastIndexOf(' ', limit - 1);

        // A word boundary is preferred, but only where enough of the text survives
        // to be worth reading; otherwise the cut is taken where the budget falls.
        int keep = cut > limit / 4 ? cut : limit - 1;

        return string.Concat(clean.AsSpan(0, keep), "…");
    }
}
