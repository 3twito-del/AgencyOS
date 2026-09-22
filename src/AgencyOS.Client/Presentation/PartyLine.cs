using System.Reflection;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// Which role a person named on a row is playing.
/// </summary>
/// <remarks>
/// <para>
/// A row that names somebody and does not say what they are to the record leaves
/// the reader to guess from position, and position is not a statement. The same
/// caption slot beneath a headline holds the counterparty on one surface, the
/// internal member responsible on another and whoever performed an action on a
/// third; a bare name in it is read as accountability wherever it appears.
/// </para>
/// <para>
/// <strong>One vocabulary, both channels.</strong> <see cref="TargetLine"/> proved
/// the shape on pipeline targets, where the screen showed the contact and
/// <see cref="RowLabel"/> announced the owner in the same position. The wording
/// lives here so the seen row and the spoken row cannot name different roles, and
/// the markup names only the field it is binding, never the words.
/// </para>
/// <para>
/// <strong>Narrow by construction.</strong> Only fields that are on this list are
/// labelled. An identity whose role is the only one its row could hold - the
/// person a radar review is about, the prospect on the prospect list - is not
/// here and stays bare, because a label on it would be noise rather than meaning.
/// </para>
/// <para>
/// <strong>Why <c>ActorDisplayName</c> is not "Actor".</strong> In an agency for
/// performers that word is a discipline: <c>AddProjectRoleDialog</c> and the
/// talent filters both offer "Actor" meaning somebody who acts. A history row
/// reading "Actor: Ravensworth" would assert a profession the record never
/// claimed. "By" is the byline said out loud, and carries no second meaning.
/// </para>
/// </remarks>
public static class PartyLine
{
    /// <summary>
    /// The product's own words for these roles, in the order a row should prefer.
    /// </summary>
    /// <remarks>
    /// Taken from the dialogs that set the fields - <c>RecordPaymentDialog</c>
    /// heads its box "Payer", <c>RecordObligationDialog</c> asks "Who must do it"
    /// of the obligor - so a row names a role with the same word the operator
    /// chose it by. Ordered counterparty-first because where a row carries both an
    /// external party and an internal one, the external party is what the row is
    /// about.
    /// </remarks>
    private static readonly (string Property, string Role)[] Vocabulary =
    [
        ("ContactDisplayName", "Contact"),
        ("CounterpartyDisplayName", "Counterparty"),
        ("PayerDisplayName", "Payer"),
        ("PayeeDisplayName", "Payee"),
        ("DebtorDisplayName", "Debtor"),
        ("ObligorDisplayName", "Obligor"),
        ("PersonDisplayName", "Person"),
        ("OwnerDisplayName", "Owner"),
        ("ActorDisplayName", "By"),
    ];

    /// <summary>The roles this knows, for tests and for the converter to check.</summary>
    public static IReadOnlyList<string> Fields { get; } =
        [.. Vocabulary.Select(x => x.Property)];

    /// <summary>
    /// A named field on a row, with the role it holds, or null where it is empty.
    /// </summary>
    /// <param name="row">The bound item.</param>
    /// <param name="property">The field the caller is showing.</param>
    /// <remarks>
    /// Null rather than a bare name when the field is unknown to the vocabulary:
    /// a caller asking for a role this cannot state should get silence, not an
    /// unlabelled name that looks like the repair worked.
    /// </remarks>
    public static string? For(object? row, string? property)
    {
        if (row is null || string.IsNullOrWhiteSpace(property))
        {
            return null;
        }

        foreach ((string field, string role) in Vocabulary)
        {
            if (string.Equals(field, property, StringComparison.Ordinal))
            {
                return Read(row, field) is { } name ? role + ": " + name : null;
            }
        }

        return null;
    }

    /// <summary>
    /// The one person a row should name, with the role they play.
    /// </summary>
    /// <remarks>
    /// A row names one person, as it did before. What changed is that it says
    /// which. The first role present wins, so a receivable says its payer and a
    /// target row its contact rather than the internal member behind them.
    /// </remarks>
    public static string? Who(object? row, string? excluding = null)
    {
        if (row is null)
        {
            return null;
        }

        foreach ((string field, string role) in Vocabulary)
        {
            if (Read(row, field) is not { } name)
            {
                continue;
            }

            // The row already leads with this person. Saying the same name twice
            // tells an operator nothing and costs the row a field that would.
            if (string.Equals(name, excluding, StringComparison.Ordinal))
            {
                continue;
            }

            return role + ": " + name;
        }

        return null;
    }

    /// <summary>Whether the row carries any identity this can attribute.</summary>
    public static bool Knows(object? row) => Who(row) is not null;

    /// <summary>The role word for a field, or null where it is not one of these.</summary>
    public static string? Role(string? property)
    {
        foreach ((string field, string role) in Vocabulary)
        {
            if (string.Equals(field, property, StringComparison.Ordinal))
            {
                return role;
            }
        }

        return null;
    }

    private static string? Read(object row, string property)
    {
        if (row.GetType().GetProperty(
            property, BindingFlags.Public | BindingFlags.Instance) is not { } found)
        {
            return null;
        }

        return found.GetValue(row) as string is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}
