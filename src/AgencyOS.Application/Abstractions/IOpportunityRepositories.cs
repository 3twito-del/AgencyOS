using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M6 pursuit model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, as M2 through M5 do: a method that can
/// be called without a tenant is a cross-tenant read waiting to be written. The
/// database enforces the same containment through composite foreign keys
/// (ADR-0011).
/// </remarks>
public interface IOpportunityRepository
{
    /// <summary>Loads an opportunity with the subjects its commands reason about.</summary>
    Task<Opportunity?> FindAsync(
        OrganizationId organizationId,
        OpportunityId id,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms an opportunity exists in this tenant without loading its graph.</summary>
    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        OpportunityId id,
        CancellationToken cancellationToken = default);

    void Add(Opportunity opportunity);
}

public interface IOpportunityTargetRepository
{
    Task<OpportunityTarget?> FindAsync(
        OrganizationId organizationId,
        OpportunityTargetId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an open target for the same endpoint, so one is not added twice.
    /// </summary>
    /// <remarks>
    /// Two open targets for one studio on one pursuit means two agents working the
    /// same buyer without knowing about each other, which is the situation this
    /// part of the system exists to prevent. A partial unique index enforces the
    /// same thing.
    /// </remarks>
    Task<OpportunityTarget?> FindOpenForEndpointAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        Guid endpointId,
        CancellationToken cancellationToken = default);

    void Add(OpportunityTarget target);
}

public interface ISubmissionRepository
{
    Task<Submission?> FindAsync(
        OrganizationId organizationId,
        SubmissionId id,
        CancellationToken cancellationToken = default);

    void Add(Submission submission);
}

public interface IOpportunityPitchRepository
{
    Task<OpportunityPitch?> FindAsync(
        OrganizationId organizationId,
        OpportunityPitchId id,
        CancellationToken cancellationToken = default);

    void Add(OpportunityPitch pitch);

    /// <summary>Records which pursuit a task belongs to.</summary>
    void AddTaskLink(OpportunityTaskLink link);
}

/// <summary>
/// Confirms an opportunity subject points at something real, in this tenant.
/// </summary>
/// <remarks>
/// Unlike M5's package elements, subjects carry typed foreign keys, so the database
/// already refuses a cross-tenant reference. This exists to turn that refusal into
/// a sentence a person can act on, before the write is attempted.
/// </remarks>
public interface IOpportunitySubjectTargets
{
    Task RequireAsync(
        OrganizationId organizationId,
        OpportunitySubjectRef subject,
        CancellationToken cancellationToken = default);
}
