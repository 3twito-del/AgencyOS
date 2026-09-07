using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <remarks>
/// Every query filters on the tenant as well as the identifier. The predicate is
/// not redundant with the composite foreign keys: those stop bad data being
/// written, this stops good data being read by the wrong tenant.
/// </remarks>
internal sealed class OpportunityRepository : IOpportunityRepository
{
    private readonly AgencyOsDbContext _context;

    public OpportunityRepository(AgencyOsDbContext context) => _context = context;

    public Task<Opportunity?> FindAsync(
        OrganizationId organizationId,
        OpportunityId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Opportunities
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        OpportunityId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Opportunities
            .AnyAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(Opportunity opportunity) => _context.Opportunities.Add(opportunity);
}

internal sealed class OpportunityTargetRepository : IOpportunityTargetRepository
{
    /// <summary>Stages a target can still move from, for the open-target query.</summary>
    private static readonly OpportunityTargetStage[] Open =
    [
        OpportunityTargetStage.Identified,
        OpportunityTargetStage.Approved,
        OpportunityTargetStage.Contacted,
        OpportunityTargetStage.Engaged,
        OpportunityTargetStage.Interested,
        OpportunityTargetStage.Advanced,
    ];

    private readonly AgencyOsDbContext _context;

    public OpportunityTargetRepository(AgencyOsDbContext context) => _context = context;

    public Task<OpportunityTarget?> FindAsync(
        OrganizationId organizationId,
        OpportunityTargetId id,
        CancellationToken cancellationToken = default)
    {
        return _context.OpportunityTargets
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public Task<OpportunityTarget?> FindOpenForEndpointAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        CompanyIdOrPerson keys = new(endpointId);

        return _context.OpportunityTargets
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.OpportunityId == opportunityId
                    && Open.Contains(x.Stage)
                    && (x.CompanyId == keys.Company || x.PersonId == keys.Person),
                cancellationToken);
    }

    public void Add(OpportunityTarget target) => _context.OpportunityTargets.Add(target);

    /// <summary>
    /// The endpoint identifier, typed both ways for the query.
    /// </summary>
    /// <remarks>
    /// A target is a company or a person, and the caller knows only the raw
    /// identifier. Comparing as both converted types lets EF translate the
    /// predicate; reaching through a nullable value-converted key's
    /// <c>.Value.Value</c> does not translate, and fails at runtime rather than at
    /// compile time.
    /// </remarks>
    private readonly record struct CompanyIdOrPerson(Guid Id)
    {
        public Domain.Companies.CompanyId? Company => new Domain.Companies.CompanyId(Id);

        public Domain.People.PersonId? Person => new Domain.People.PersonId(Id);
    }
}

internal sealed class SubmissionRepository : ISubmissionRepository
{
    private readonly AgencyOsDbContext _context;

    public SubmissionRepository(AgencyOsDbContext context) => _context = context;

    public Task<Submission?> FindAsync(
        OrganizationId organizationId,
        SubmissionId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Submissions
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(Submission submission) => _context.Submissions.Add(submission);
}

internal sealed class OpportunityPitchRepository : IOpportunityPitchRepository
{
    private readonly AgencyOsDbContext _context;

    public OpportunityPitchRepository(AgencyOsDbContext context) => _context = context;

    public Task<OpportunityPitch?> FindAsync(
        OrganizationId organizationId,
        OpportunityPitchId id,
        CancellationToken cancellationToken = default)
    {
        return _context.OpportunityPitches
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(OpportunityPitch pitch) => _context.OpportunityPitches.Add(pitch);

    public void AddTaskLink(OpportunityTaskLink link) => _context.OpportunityTaskLinks.Add(link);
}

/// <summary>
/// Confirms an opportunity subject points at something real, in this tenant.
/// </summary>
/// <remarks>
/// The typed foreign keys mean the database would refuse a bad reference anyway.
/// This turns that refusal into a sentence naming what is wrong, before the write
/// is attempted - the difference between "subject not found" and a constraint
/// violation nobody can act on.
/// </remarks>
internal sealed class OpportunitySubjectTargets : IOpportunitySubjectTargets
{
    private readonly AgencyOsDbContext _context;

    public OpportunitySubjectTargets(AgencyOsDbContext context) => _context = context;

    public async Task RequireAsync(
        OrganizationId organizationId,
        OpportunitySubjectRef subject,
        CancellationToken cancellationToken = default)
    {
        bool exists = subject.Kind switch
        {
            OpportunitySubjectKind.TalentProfile => await _context.TalentProfiles
                .AnyAsync(
                    x => x.Id == subject.TalentProfileId!.Value && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            OpportunitySubjectKind.Project => await _context.Projects
                .AnyAsync(
                    x => x.Id == subject.ProjectId!.Value && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            OpportunitySubjectKind.Package => await _context.Packages
                .AnyAsync(
                    x => x.Id == subject.PackageId!.Value && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            OpportunitySubjectKind.ProjectRole => await _context.ProjectRoles
                .AnyAsync(
                    x => x.Id == subject.ProjectRoleId!.Value && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            _ => false,
        };

        if (!exists)
        {
            throw new EntityNotFoundException(
                subject.Kind.ToString(),
                $"{subject.TargetId} is not a {subject.Kind} in this organization.");
        }
    }
}
