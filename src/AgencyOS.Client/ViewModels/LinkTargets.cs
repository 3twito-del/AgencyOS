using System;
using System.Collections.Generic;
using System.Linq;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The record kinds a document or message can be filed against, and which of them
/// an operator can choose from a list.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R001-006</c>. <c>LinkRecordDialog</c> asked for a typed identifier for
/// all fourteen kinds. Twelve of them have a list this organization can be asked
/// for; two are children of another record and have no list of their own, so
/// choosing one means choosing its parent first.
/// </para>
/// <para>
/// The fourteen are the kinds the database can enforce a foreign key against
/// (ADR-0025), so this list is not a menu somebody chose — it is what a link can
/// legally point at. Kinds are not removed here for being awkward to pick.
/// </para>
/// </remarks>
public static class LinkTargets
{
    /// <summary>Kinds an operator can choose from a list of this organization's records.</summary>
    public static IReadOnlyList<string> Pickable { get; } =
    [
        "Person",
        "Company",
        "TalentProfile",
        "Project",
        "Package",
        "Opportunity",
        "Submission",
        "Deal",
        "Offer",
        "Contract",
        "Invoice",
        "Payment",
    ];

    /// <summary>
    /// Kinds that belong to another record, and have no list of their own.
    /// </summary>
    /// <remarks>
    /// A material belongs to a person and is listed per person; a contract version
    /// belongs to a contract and is read from that contract. Choosing either means
    /// choosing the parent first, which is a shape this dialog does not have and
    /// which is recorded as an open decision rather than invented here.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> RequiresAParent { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Material"] = "a material belongs to a person, and is listed per person",
            ["ContractVersion"] = "a version belongs to a contract, and is read from that contract",
        };

    /// <summary>Every kind a link can point at.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. Pickable.Concat(RequiresAParent.Keys).Order(StringComparer.Ordinal)];

    /// <summary>Whether this kind can be chosen from a list.</summary>
    public static bool CanPick(string kind) => Pickable.Contains(kind, StringComparer.Ordinal);

    /// <summary>Why a kind cannot be chosen from a list, when it cannot.</summary>
    public static string? WhyNot(string kind) =>
        RequiresAParent.TryGetValue(kind, out string? reason) ? reason : null;
}
