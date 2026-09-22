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

    /// <summary>Properties that carry the row's headline, best first.</summary>
    private static readonly string[] Headline =
    [
        "DisplayName", "Name", "Title", "Question", "Statement", "Proposition",
        "Claim", "Subject", "SubjectDisplayName", "Label", "MailboxAddress",
        "Address", "Summary", "Description", "Reference", "Code",
    ];

    /// <summary>Properties that say what kind of thing the row is, or where it stands.</summary>
    private static readonly string[] Qualifiers =
    [
        "Status", "Stage", "State", "Kind", "Type", "Direction", "Outcome",
        "Severity", "Discipline", "Role", "Priority",
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

        if (tail.Length >= MaximumLength)
        {
            return Shorten(tail);
        }

        string head = string.Join(", ", parts);

        // Two for the separator that joins the two halves.
        int budget = MaximumLength - tail.Length - 2;

        return string.Concat(Shorten(head, budget), ", ", tail);
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
