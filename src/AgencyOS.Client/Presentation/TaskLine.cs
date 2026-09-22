using System.Globalization;
using System.Reflection;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// What a task row says about who owns it, when it is due, and what it concerns.
/// </summary>
/// <remarks>
/// <para>
/// Six surfaces list tasks and five of them decided independently what a row
/// should show. None showed an owner or a due date, and the one that showed a
/// person showed the wrong one: the Command Center rendered the task's
/// <em>subject</em> — the client it is about — unlabelled beneath the title, and a
/// blind operator read it as accountability.
/// </para>
/// <para>
/// So the wording lives here rather than in six templates. Layouts may differ
/// where the context differs; what a row <em>means</em> may not.
/// </para>
/// <para>
/// Rows are heterogeneous records from six modules, so the fields are found by
/// name, as <see cref="RowLabel"/> already does. That also absorbs the one
/// divergence — research tasks call it <c>AssignedToDisplayName</c> where the rest
/// call it <c>AssigneeDisplayName</c> — without renaming a published field.
/// </para>
/// </remarks>
public static class TaskLine
{
    /// <summary>
    /// How this product says that a named member is accountable for a task.
    /// </summary>
    /// <remarks>
    /// The same family as "Unassigned" and "Assigned, name unavailable", which the
    /// client already writes, so the three ownership states read as one vocabulary
    /// rather than three. Not "Owner", which is a different domain role here.
    /// </remarks>
    private const string Role = "Assigned to ";

    /// <summary>Names a projection uses for the assignee's display name.</summary>
    private static readonly string[] AssigneeNames =
        ["AssigneeDisplayName", "AssignedToDisplayName"];

    /// <summary>Names a projection uses for the assignee's identifier.</summary>
    private static readonly string[] AssigneeIds =
        ["AssigneeUserId", "AssignedToUserId"];

    /// <summary>
    /// Who is accountable, or null when this row cannot answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three projections — contract, opportunity and finance tasks — carry no
    /// assignment at all. They are not authoritative for ownership, so they say
    /// nothing rather than "Unassigned", which would be a claim they cannot
    /// support. A row that carries the identifier may say it: an identifier that
    /// is present and null is authoritative that nobody is accountable.
    /// </para>
    /// <para>
    /// <strong>The name is returned with its role, not bare.</strong> Build 82
    /// labelled the subject "About …" and left the assignee as a bare name beside
    /// it, which is the asymmetry the blind operator reported: the row proved a
    /// name could be labelled and then declined to label the other one. "Assigned
    /// to" is the product's own vocabulary for this — it is the family the two
    /// states below already belong to — and it is deliberately not "Owner", which
    /// in AgencyOS means the member responsible for a deal, target or opportunity
    /// rather than the member doing this piece of work.
    /// </para>
    /// </remarks>
    public static string? Who(object? row)
    {
        if (row is null)
        {
            return null;
        }

        Type type = row.GetType();

        string? name = Text(row, type, AssigneeNames);

        if (!string.IsNullOrWhiteSpace(name))
        {
            return Role + name.Trim();
        }

        // No name. Whether that means nobody, or only that this read did not
        // resolve one, is what the identifier decides.
        if (!Carries(row, type, AssigneeIds, out object? id))
        {
            return null;
        }

        return id is null ? "Unassigned" : "Assigned, name unavailable";
    }

    /// <summary>
    /// When it is due, said the one way this product writes dates.
    /// </summary>
    /// <remarks>
    /// An undated task says so. Silence would read as a rendering fault, and the
    /// operator cannot tell the two apart.
    /// </remarks>
    public static string When(object? row)
    {
        if (row is null)
        {
            return string.Empty;
        }

        return Value(row, row.GetType(), "DueAt") is DateTimeOffset due
            ? "Due " + due.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "No due date";
    }

    /// <summary>
    /// What the task concerns, labelled so it cannot be read as who owns it.
    /// </summary>
    /// <remarks>
    /// The subject is genuinely useful on a list that spans clients — it is how an
    /// operator tells one row from another. It was never the problem. Showing it
    /// as a bare name in the position a byline occupies was.
    /// </remarks>
    public static string? About(object? row)
    {
        if (row is null)
        {
            return null;
        }

        object? subject = Value(row, row.GetType(), "Subject");

        if (subject is null)
        {
            return null;
        }

        string? name = Value(subject, subject.GetType(), "Name") as string;

        return string.IsNullOrWhiteSpace(name) ? null : "About " + name;
    }

    /// <summary>Whether this row is a task at all, for callers that describe rows.</summary>
    public static bool IsTask(object? row) =>
        row is not null
        && (Carries(row, row.GetType(), AssigneeIds, out _)
            || Text(row, row.GetType(), AssigneeNames) is not null
            || row.GetType().GetProperty("DueAt") is not null);

    private static string? Text(object row, Type type, string[] names)
    {
        foreach (string name in names)
        {
            if (Value(row, type, name) is string found && !string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the row declares one of these properties at all, and what it holds.
    /// </summary>
    /// <remarks>
    /// Declaring it is what makes a projection authoritative for ownership. Holding
    /// null then means nobody is accountable; not declaring it at all means this
    /// row cannot say either way.
    /// </remarks>
    private static bool Carries(object row, Type type, string[] names, out object? value)
    {
        foreach (string name in names)
        {
            if (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } property)
            {
                value = property.GetValue(row);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? Value(object row, Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } property
            ? property.GetValue(row)
            : null;
}
