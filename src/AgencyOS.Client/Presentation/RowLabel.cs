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
    /// it, which tells an operator less than leaving it out does.
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
        "CounterpartyDisplayName", "PrimaryCompanyName", "CompanyName",
        "OwnerDisplayName", "OpportunityName", "ProjectTitle", "Publisher",
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
        // TargetLine keeps the two channels reading one answer, and says the role.
        string? context = TargetLine.IsTarget(row)
            ? TargetLine.Who(row)
            : FirstValue(row, type, Context);

        if (context is not null && !Same(context, headline))
        {
            parts.Add(context);
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

            return TaskRow(parts, essential);
        }

        return Shorten(string.Join(", ", parts));
    }

    /// <summary>
    /// A task row, with the operator's questions answered before the budget runs out.
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
    /// So the title yields instead. It is the part an operator can still recognise
    /// truncated, and the row keeps its bound: only if the roles alone exceed the
    /// budget - which needs improbably long names - does the total shorten.
    /// </para>
    /// </remarks>
    private static string TaskRow(List<string> parts, List<string> essential)
    {
        string tail = string.Join(", ", essential);

        // Two for the separator that joins the two halves.
        int budget = MaximumLength - tail.Length - 2;

        // Where the roles nearly fill the budget on their own there is no room
        // for a title worth reading. The row drops it rather than emit a phrase
        // that begins with a comma or overruns the bound it exists to keep.
        if (budget < MinimumHeadline)
        {
            return Shorten(tail);
        }

        return string.Concat(Shorten(string.Join(", ", parts), budget), ", ", tail);
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
