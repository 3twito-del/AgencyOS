using AgencyOS.Application.Documents;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Resolves short names for linked records, and proves they exist.
/// </summary>
/// <remarks>
/// <para>
/// One place for both, because they walk the same fourteen tables and a second
/// copy would drift. Existence is what the link validator needs; the label is what
/// a list needs so a link reads as "Northgate Pictures" rather than as a GUID
/// (ADR-0025).
/// </para>
/// <para>
/// Labels are <strong>names only</strong>. Never a figure, never a clause, never a
/// classification, because a link label is rendered beside documents whose own
/// permissions differ from the target's.
/// </para>
/// </remarks>
public sealed class LinkLabels : IDocumentLinkValidator
{
    private readonly AgencyOsDbContext _context;

    public LinkLabels(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        ExistsAsync(_context, organizationId, target, targetId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(DocumentLinkTarget Target, Guid TargetId), string>>
        DescribeAsync(
            OrganizationId organizationId,
            IReadOnlyCollection<(DocumentLinkTarget Target, Guid TargetId)> targets,
            CancellationToken cancellationToken = default) =>
        await DescribeAsync(_context, organizationId, targets, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Whether the record exists in this tenant.
    /// </summary>
    /// <remarks>
    /// Every predicate carries the organization. The composite foreign key in the
    /// schema enforces the same thing, so a caller who somehow got past this still
    /// cannot write a cross-tenant link; this exists so the refusal is a sentence
    /// rather than a constraint violation (ADR-0011, ADR-0025).
    /// </remarks>
    internal static Task<bool> ExistsAsync(
        AgencyOsDbContext context,
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken) => target switch
    {
        DocumentLinkTarget.Person => context.People.AnyAsync(
            x => x.OrganizationId == organizationId && x.Id == new Domain.People.PersonId(targetId),
            cancellationToken),

        DocumentLinkTarget.Company => context.Companies.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Companies.CompanyId(targetId),
            cancellationToken),

        DocumentLinkTarget.TalentProfile => context.TalentProfiles.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Talent.TalentProfileId(targetId),
            cancellationToken),

        DocumentLinkTarget.Material => context.Materials.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Talent.MaterialId(targetId),
            cancellationToken),

        DocumentLinkTarget.Project => context.Projects.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.ProjectId(targetId),
            cancellationToken),

        DocumentLinkTarget.Package => context.Packages.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Projects.PackageId(targetId),
            cancellationToken),

        DocumentLinkTarget.Opportunity => context.Opportunities.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Opportunities.OpportunityId(targetId),
            cancellationToken),

        DocumentLinkTarget.Submission => context.Submissions.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Opportunities.SubmissionId(targetId),
            cancellationToken),

        DocumentLinkTarget.Deal => context.Deals.AnyAsync(
            x => x.OrganizationId == organizationId && x.Id == new Domain.Deals.DealId(targetId),
            cancellationToken),

        DocumentLinkTarget.Offer => context.Offers.AnyAsync(
            x => x.OrganizationId == organizationId && x.Id == new Domain.Deals.OfferId(targetId),
            cancellationToken),

        DocumentLinkTarget.Contract => context.Contracts.AnyAsync(
            x => x.OrganizationId == organizationId && x.Id == new Domain.Legal.ContractId(targetId),
            cancellationToken),

        DocumentLinkTarget.ContractVersion => context.ContractVersions.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Legal.ContractVersionId(targetId),
            cancellationToken),

        DocumentLinkTarget.Invoice => context.Invoices.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Finance.InvoiceId(targetId),
            cancellationToken),

        DocumentLinkTarget.Payment => context.Payments.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.Id == new Domain.Finance.PaymentId(targetId),
            cancellationToken),

        _ => Task.FromResult(false),
    };

    /// <summary>Loads a short name for each target, batched by kind.</summary>
    internal static async Task<IReadOnlyDictionary<(DocumentLinkTarget Target, Guid TargetId), string>>
        DescribeAsync(
            AgencyOsDbContext context,
            OrganizationId organizationId,
            IReadOnlyCollection<(DocumentLinkTarget Target, Guid TargetId)> targets,
            CancellationToken cancellationToken)
    {
        Dictionary<(DocumentLinkTarget, Guid), string> labels = [];

        if (targets.Count == 0)
        {
            return labels;
        }

        // One query per kind present, rather than one per link. A document attached
        // to twelve records would otherwise be twelve round trips on every page.
        foreach (IGrouping<DocumentLinkTarget, Guid> group in targets
            .GroupBy(x => x.Target, x => x.TargetId))
        {
            Guid[] ids = [.. group.Distinct()];

            foreach ((Guid id, string label) in await LabelsForAsync(
                context, organizationId, group.Key, ids, cancellationToken).ConfigureAwait(false))
            {
                labels[(group.Key, id)] = label;
            }
        }

        return labels;
    }

    /// <summary>
    /// Loads the names for one kind of target.
    /// </summary>
    /// <remarks>
    /// The identifiers are wrapped into their own strongly-typed form before the
    /// query, rather than compared against the underlying <c>Guid</c>. A predicate
    /// reaching through the wrapper cannot be translated to SQL, and the query
    /// would have failed at runtime rather than at compile time (ADR-0011).
    /// </remarks>
    private static async Task<IReadOnlyList<(Guid Id, string Label)>> LabelsForAsync(
        AgencyOsDbContext context,
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        switch (target)
        {
            case DocumentLinkTarget.Person:
            {
                Domain.People.PersonId[] keys = [.. ids.Select(x => new Domain.People.PersonId(x))];

                return Flatten(await context.People
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.DisplayName))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Company:
            {
                Domain.Companies.CompanyId[] keys =
                    [.. ids.Select(x => new Domain.Companies.CompanyId(x))];

                return Flatten(await context.Companies
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Material:
            {
                Domain.Talent.MaterialId[] keys =
                    [.. ids.Select(x => new Domain.Talent.MaterialId(x))];

                return Flatten(await context.Materials
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Project:
            {
                Domain.Projects.ProjectId[] keys =
                    [.. ids.Select(x => new Domain.Projects.ProjectId(x))];

                return Flatten(await context.Projects
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Package:
            {
                Domain.Projects.PackageId[] keys =
                    [.. ids.Select(x => new Domain.Projects.PackageId(x))];

                return Flatten(await context.Packages
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Opportunity:
            {
                Domain.Opportunities.OpportunityId[] keys =
                    [.. ids.Select(x => new Domain.Opportunities.OpportunityId(x))];

                return Flatten(await context.Opportunities
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Deal:
            {
                Domain.Deals.DealId[] keys = [.. ids.Select(x => new Domain.Deals.DealId(x))];

                return Flatten(await context.Deals
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Name))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Contract:
            {
                Domain.Legal.ContractId[] keys =
                    [.. ids.Select(x => new Domain.Legal.ContractId(x))];

                return Flatten(await context.Contracts
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Title))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            case DocumentLinkTarget.Invoice:
            {
                Domain.Finance.InvoiceId[] keys =
                    [.. ids.Select(x => new Domain.Finance.InvoiceId(x))];

                // The reference only. An invoice label never carries its total: it
                // is shown beside documents guarded by different permissions from
                // the invoice itself (ADR-0023, ADR-0025).
                return Flatten(await context.Invoices
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                    .Select(x => new Labelled(x.Id.Value, x.Reference ?? "Invoice"))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            default:
                // Targets with no natural short name - a contract version, a
                // submission, an offer, a payment, a talent profile - are labelled
                // by kind rather than by inventing a description of them.
                return [.. ids.Select(id => (id, target.ToString()))];
        }
    }

    private static IReadOnlyList<(Guid Id, string Label)> Flatten(List<Labelled> rows) =>
        [.. rows.Select(x => (x.Id, x.Label))];

    /// <summary>One projected row. A named type, because EF cannot project a tuple.</summary>
    private sealed record Labelled(Guid Id, string Label);
}
