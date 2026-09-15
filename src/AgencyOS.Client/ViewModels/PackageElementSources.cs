using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// What each kind of package element is allowed to point at.
/// </summary>
/// <remarks>
/// <para>
/// A package element carries one identifier whose meaning changes with its kind,
/// and the server checks each kind against a different table — an attachment and
/// an open role against <em>this package's project</em>, a person or company
/// against the organization. Six kinds, six sources.
/// </para>
/// <para>
/// This is a named record rather than six loose parameters because the six travel
/// together and always come from the same two reads: the package's project, and
/// the organization's people and companies. It carries data and nothing else — no
/// endpoints, no callbacks, no display functions (§6).
/// </para>
/// </remarks>
/// <param name="Attachments">Attachments on the package's project.</param>
/// <param name="People">People this organization holds a record for.</param>
/// <param name="Companies">Companies it holds a record for.</param>
/// <param name="Roles">Roles on the package's project.</param>
/// <param name="Materials">Materials filed against that project.</param>
/// <param name="SourceProperties">Source properties behind that project.</param>
public sealed record PackageElementSources(
    IReadOnlyList<EntityChoice> Attachments,
    IReadOnlyList<EntityChoice> People,
    IReadOnlyList<EntityChoice> Companies,
    IReadOnlyList<EntityChoice> Roles,
    IReadOnlyList<EntityChoice> Materials,
    IReadOnlyList<EntityChoice> SourceProperties)
{
    /// <summary>
    /// Gathers the six from one project and the organization's parties.
    /// </summary>
    /// <param name="project">The package's own project, already read.</param>
    /// <param name="people">People this organization holds.</param>
    /// <param name="companies">Companies it holds.</param>
    /// <returns>The six sources, each already labelled for a person to read.</returns>
    public static PackageElementSources From(
        ProjectDetailResponse project,
        IReadOnlyList<PersonSummaryResponse> people,
        IReadOnlyList<CompanySummaryResponse> companies)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(companies);

        List<AttachmentResponse> attachments = [];

        foreach (ProjectRoleResponse role in project.Roles)
        {
            attachments.AddRange(role.Attachments);
        }

        // An open role is one nobody is attached to. Offering a filled role under
        // "open role" would invite an element the project's own state contradicts.
        List<ProjectRoleResponse> open =
            [.. project.Roles.Where(x => x.Attachments.Count == 0)];

        return new PackageElementSources(
            EntityChoice.ForAttachments(attachments),
            EntityChoice.ForPeople(people),
            EntityChoice.ForCompanies(companies),
            EntityChoice.ForProjectRoles(open),
            EntityChoice.ForProjectMaterials(project.Materials),
            EntityChoice.ForSourceProperties(project.SourceProperties));
    }
}
