using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Proves an intelligence subject exists, and names it.
/// </summary>
/// <remarks>
/// <para>
/// One place for both, because they walk the same ten tables and a second copy
/// would drift. Existence is what the command handlers need; the label is what a
/// list needs so a subject reads as "Northgate Pictures" rather than as a GUID
/// (ADR-0030).
/// </para>
/// <para>
/// Labels are <strong>names only</strong>. Never a status, a figure, a stage or a
/// classification, because a subject label is rendered beside intelligence whose
/// permissions differ from the subject's own — and a label reading "Terminated"
/// or "$1.2m" would leak through the label.
/// </para>
/// </remarks>
public sealed class IntelligenceSubjectLabels : IIntelligenceSubjectValidator
{
    private readonly AgencyOsDbContext _context;

    public IntelligenceSubjectLabels(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// Every predicate carries the organization. The composite foreign key in the
    /// schema enforces the same thing, so a caller who somehow got past this still
    /// cannot write a cross-tenant subject; this exists so the refusal is a
    /// sentence rather than a constraint violation (ADR-0011, ADR-0030).
    /// </remarks>
    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        CancellationToken cancellationToken = default) => kind switch
    {
        IntelligenceSubjectKind.Person => _context.People.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.People.PersonId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Company => _context.Companies.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Companies.CompanyId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.TalentProfile => _context.TalentProfiles.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Talent.TalentProfileId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Project => _context.Projects.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.ProjectId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.SourceProperty => _context.SourceProperties.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.SourcePropertyId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Package => _context.Packages.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.PackageId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.ProjectRole => _context.ProjectRoles.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.ProjectRoleId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Opportunity => _context.Opportunities.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Opportunities.OpportunityId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Deal => _context.Deals.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Deals.DealId(subjectId),
            cancellationToken),

        IntelligenceSubjectKind.Contract => _context.Contracts.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Legal.ContractId(subjectId),
            cancellationToken),

        _ => Task.FromResult(false),
    };

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(IntelligenceSubjectKind Kind, Guid SubjectId), string>>
        DescribeAsync(
            OrganizationId organizationId,
            IReadOnlyCollection<(IntelligenceSubjectKind Kind, Guid SubjectId)> subjects,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        Dictionary<(IntelligenceSubjectKind, Guid), string> labels = [];

        if (subjects.Count == 0)
        {
            return labels;
        }

        // One query per kind present, rather than one per subject. A signal about
        // four records would otherwise be four round trips on every page.
        foreach (IGrouping<IntelligenceSubjectKind, Guid> group in subjects
            .GroupBy(x => x.Kind, x => x.SubjectId))
        {
            Guid[] ids = [.. group.Distinct()];

            foreach ((Guid id, string label) in await LabelsForAsync(
                organizationId, group.Key, ids, cancellationToken).ConfigureAwait(false))
            {
                labels[(group.Key, id)] = label;
            }
        }

        return labels;
    }

    /// <summary>
    /// Loads the names for one kind of subject.
    /// </summary>
    /// <remarks>
    /// The identifiers are wrapped into their own strongly-typed form before the
    /// query rather than compared against the underlying <c>Guid</c>. A predicate
    /// reaching through the wrapper cannot be translated to SQL and would fail at
    /// runtime rather than at compile time (ADR-0011).
    /// </remarks>
    private async Task<IReadOnlyList<(Guid Id, string Label)>> LabelsForAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case IntelligenceSubjectKind.Person:
            {
                Domain.People.PersonId[] keys = [.. ids.Select(x => new Domain.People.PersonId(x))];

                return Flatten(await _context.People
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.DisplayName))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Company:
            {
                Domain.Companies.CompanyId[] keys =
                    [.. ids.Select(x => new Domain.Companies.CompanyId(x))];

                return Flatten(await _context.Companies
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Project:
            {
                Domain.Projects.ProjectId[] keys =
                    [.. ids.Select(x => new Domain.Projects.ProjectId(x))];

                return Flatten(await _context.Projects
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.SourceProperty:
            {
                Domain.Projects.SourcePropertyId[] keys =
                    [.. ids.Select(x => new Domain.Projects.SourcePropertyId(x))];

                return Flatten(await _context.SourceProperties
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Package:
            {
                Domain.Projects.PackageId[] keys =
                    [.. ids.Select(x => new Domain.Projects.PackageId(x))];

                return Flatten(await _context.Packages
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Opportunity:
            {
                Domain.Opportunities.OpportunityId[] keys =
                    [.. ids.Select(x => new Domain.Opportunities.OpportunityId(x))];

                return Flatten(await _context.Opportunities
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Deal:
            {
                Domain.Deals.DealId[] keys = [.. ids.Select(x => new Domain.Deals.DealId(x))];

                return Flatten(await _context.Deals
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case IntelligenceSubjectKind.Contract:
            {
                Domain.Legal.ContractId[] keys =
                    [.. ids.Select(x => new Domain.Legal.ContractId(x))];

                return Flatten(await _context.Contracts
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            default:
                // A talent profile and a project role have no natural short name of
                // their own; they are labelled by kind rather than by inventing a
                // description, and the person or project beside them supplies the
                // context a reader needs.
                return [.. ids.Select(id => (id, kind.ToString()))];
        }
    }

    private static IReadOnlyList<(Guid Id, string Label)> Flatten(List<Labelled> rows) =>
        [.. rows.Select(x => (x.Id, x.Label))];

    /// <summary>One projected row. A named type, because EF cannot project a tuple.</summary>
    private sealed record Labelled(Guid Id, string Label);
}
