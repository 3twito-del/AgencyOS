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

        if (FirstValue(row, type, Context) is { } context && !Same(context, headline))
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

        return Shorten(string.Join(", ", parts));
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

    private static string Shorten(string value)
    {
        string clean = value.Trim();

        if (clean.Length <= MaximumLength)
        {
            return clean;
        }

        int cut = clean.LastIndexOf(' ', MaximumLength - 1);

        return string.Concat(clean.AsSpan(0, cut > 40 ? cut : MaximumLength - 1), "…");
    }
}
