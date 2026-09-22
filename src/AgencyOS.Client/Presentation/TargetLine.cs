using System.Reflection;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// What a pipeline target row says about the person at the other end of it.
/// </summary>
/// <remarks>
/// <para>
/// A target carries up to three identities and the row showed one of them
/// unlabelled. <c>DisplayName</c> is the party being approached — a company, or a
/// person on a person target. <c>ContactDisplayName</c> is the individual dealt
/// with at a company target, resolved from the external person directory.
/// <c>OwnerDisplayName</c> is the internal member responsible, resolved from the
/// user directory. Three roles, two of them people, and the caption beneath the
/// company name said only a name.
/// </para>
/// <para>
/// <strong>The two channels named different people.</strong> The row displayed the
/// contact; <see cref="RowLabel"/> had no entry for <c>ContactDisplayName</c> and
/// announced <c>OwnerDisplayName</c> in the same position instead. A sighted
/// operator was told about the counterparty's casting director and a screen-reader
/// operator about an internal colleague, with nothing in either channel saying
/// which was which. That is why the wording lives in one place and both channels
/// read it from there.
/// </para>
/// <para>
/// <strong>Why "Contact:" and not "Contact".</strong> The bare-prefix form that
/// suits <see cref="TaskLine.About"/> does not work here, because "Contact" is
/// also a verb: "Contact Evander Quillon-Mbeki" reads as an instruction to ring
/// him. The colon is what the product already uses when it names a field in a
/// sentence — "Last contact: ", "Outstanding: " — and it cannot be read as an
/// imperative.
/// </para>
/// <para>
/// <strong>The words themselves now live in <see cref="PartyLine"/>.</strong> The
/// same two roles appear on receivables, invoices, obligations, mailboxes and
/// deals, and a second copy of "Owner: " here would be the drift this type was
/// written to prevent. What stays here is the part that is specific to a target:
/// which of its identities the row should name.
/// </para>
/// <para>
/// Rows are found by property name, as <see cref="TaskLine"/> and
/// <see cref="RowLabel"/> already do, so the read model and the response record
/// are both served without either being renamed.
/// </para>
/// </remarks>
public static class TargetLine
{
    /// <summary>
    /// The counterparty individual dealt with, or null when the row has none.
    /// </summary>
    /// <remarks>
    /// A person target has no contact — the domain refuses one, because the person
    /// approached is who you are dealing with — so this says nothing there rather
    /// than inventing a role the row does not hold.
    /// </remarks>
    public static string? Contact(object? row) =>
        PartyLine.For(row, "ContactDisplayName");

    /// <summary>
    /// The internal member responsible, or null when the row has none.
    /// </summary>
    /// <remarks>
    /// Labelled for the same reason the contact is. This row is the one place the
    /// two can be confused, because it is the only one that carries both.
    /// </remarks>
    public static string? Owner(object? row) =>
        PartyLine.For(row, "OwnerDisplayName");

    /// <summary>
    /// The one person this row should name, with the role they play.
    /// </summary>
    /// <remarks>
    /// A row names one person, as it did before: the contact where there is one,
    /// the responsible member otherwise. What changed is that it now says which.
    /// </remarks>
    public static string? Who(object? row) => Contact(row) ?? Owner(row);

    /// <summary>Whether the row is one this can describe at all.</summary>
    public static bool IsTarget(object? row) =>
        row is not null
        && row.GetType().GetProperty(
            "ContactDisplayName", BindingFlags.Public | BindingFlags.Instance) is not null;
}
