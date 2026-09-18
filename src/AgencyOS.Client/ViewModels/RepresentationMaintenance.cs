using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// What a representation's scopes and team can be changed to, given what they are.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R001-010</c>. The server has always had the four commands — add a scope,
/// end one, assign somebody to the team, take them off — and the Talent workspace
/// has always shown the result. Nothing in the client called them, so an operator
/// could read who works a relationship and could not change it.
/// </para>
/// <para>
/// The choosing lives here rather than in the dialogs because it is the part with
/// rules: an area already represented cannot be added again, an area that is not
/// represented cannot be ended, and somebody already on the team is a role change
/// rather than a second assignment. Code-behind cannot be constructed off a UI
/// thread, and these rules are worth testing.
/// </para>
/// </remarks>
public static class RepresentationMaintenance
{
    /// <summary>The areas the domain recognises, in the order it declares them.</summary>
    /// <remarks>
    /// A closed list, mirrored from <c>RepresentationScopeArea</c>. The client does
    /// not reference the domain, and inventing an area here would produce a refusal
    /// the operator cannot act on.
    /// </remarks>
    public static IReadOnlyList<string> Areas { get; } =
    [
        "Film",
        "Television",
        "Literary",
        "Directing",
        "Acting",
        "Producing",
        "Digital",
        "Brand",
        "Speaking",
        "Music",
        "Books",
        "Theatre",
        "Other",
    ];

    /// <summary>What somebody can be on a representation team.</summary>
    /// <remarks>
    /// Mirrored from <c>RepresentationTeamRole</c>. Lead is first because assigning
    /// it is how the primary representative changes.
    /// </remarks>
    public static IReadOnlyList<string> Roles { get; } =
        ["Lead", "Agent", "Coordinator", "Assistant"];

    /// <summary>Areas not currently represented, which are the ones that can begin.</summary>
    public static IReadOnlyList<string> AreasThatCanBegin(
        IReadOnlyList<RepresentationScopeResponse> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        HashSet<string> current = Current(scopes);

        return [.. Areas.Where(x => !current.Contains(x))];
    }

    /// <summary>Areas currently represented, which are the ones that can end.</summary>
    /// <remarks>
    /// Read from the scopes the server sent rather than from <see cref="Areas"/>, so
    /// an area this client does not know about can still be ended.
    /// </remarks>
    public static IReadOnlyList<string> AreasThatCanEnd(
        IReadOnlyList<RepresentationScopeResponse> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return [.. scopes.Where(x => x.EndsOn is null).Select(x => x.Area).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Everybody in the organization who could work this relationship.</summary>
    /// <remarks>
    /// Members already on the team are kept: assigning one again is how their role
    /// changes, and hiding them would make a role change impossible to reach.
    /// </remarks>
    public static IReadOnlyList<EntityChoice> MembersToAssign(
        IReadOnlyList<OrganizationMemberResponse> members,
        IReadOnlyList<RepresentationTeamMemberResponse> team)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(team);

        Dictionary<Guid, string> onTeam = OnTeam(team);

        return
        [
            .. members.Select(x => new EntityChoice(
                x.UserId,
                onTeam.TryGetValue(x.UserId, out string? role)
                    ? $"{x.DisplayName} — {role} on this team"
                    : x.DisplayName)),
        ];
    }

    /// <summary>The people who are on the team now, and so can be taken off it.</summary>
    public static IReadOnlyList<EntityChoice> MembersToRemove(
        IReadOnlyList<RepresentationTeamMemberResponse> team)
    {
        ArgumentNullException.ThrowIfNull(team);

        return
        [
            .. team
                .Where(x => x.EndsOn is null)
                .Select(x => new EntityChoice(x.UserId, $"{x.DisplayName} — {x.Role}")),
        ];
    }

    /// <summary>What somebody does on this team now, or null when they are not on it.</summary>
    public static string? RoleOf(
        IReadOnlyList<RepresentationTeamMemberResponse> team,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(team);

        return OnTeam(team).TryGetValue(userId, out string? role) ? role : null;
    }

    private static HashSet<string> Current(IReadOnlyList<RepresentationScopeResponse> scopes) =>
        [.. scopes.Where(x => x.EndsOn is null).Select(x => x.Area)];

    private static Dictionary<Guid, string> OnTeam(
        IReadOnlyList<RepresentationTeamMemberResponse> team)
    {
        Dictionary<Guid, string> current = [];

        foreach (RepresentationTeamMemberResponse member in team.Where(x => x.EndsOn is null))
        {
            current[member.UserId] = member.Role;
        }

        return current;
    }
}
