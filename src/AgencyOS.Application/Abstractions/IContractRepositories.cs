using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M8 legal model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, as M2 through M7 do. The database
/// enforces the same containment through composite foreign keys (ADR-0011).
/// </remarks>
public interface IContractRepository
{
    /// <summary>Loads a contract with the parties and signatures its commands reason about.</summary>
    Task<Contract?> FindAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms a contract exists in this tenant without loading its graph.</summary>
    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default);

    void Add(Contract contract);

    void AddRelationship(ContractRelationship relationship);
}

public interface IContractVersionRepository
{
    /// <summary>Loads a version with the terms its commands reason about.</summary>
    Task<ContractVersion?> FindAsync(
        OrganizationId organizationId,
        ContractVersionId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every version of a contract, in drafting order.
    /// </summary>
    /// <remarks>
    /// Loaded whole because the next version number and the supersession of the
    /// previous one are both facts about the sequence, and computing either from a
    /// partial view would let two people claim the same number.
    /// </remarks>
    Task<IReadOnlyList<ContractVersion>> ListForContractAsync(
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken = default);

    void Add(ContractVersion version);
}

public interface IRightsGrantRepository
{
    Task<RightsGrant?> FindAsync(
        OrganizationId organizationId,
        RightsGrantId id,
        CancellationToken cancellationToken = default);

    void Add(RightsGrant grant);
}

public interface IContractOptionRepository
{
    Task<ContractOption?> FindAsync(
        OrganizationId organizationId,
        ContractOptionId id,
        CancellationToken cancellationToken = default);

    void Add(ContractOption option);
}

public interface IObligationRepository
{
    Task<Obligation?> FindAsync(
        OrganizationId organizationId,
        ObligationId id,
        CancellationToken cancellationToken = default);

    void Add(Obligation obligation);
}

public interface INoticeRepository
{
    Task<NoticeRequirement?> FindRequirementAsync(
        OrganizationId organizationId,
        NoticeRequirementId id,
        CancellationToken cancellationToken = default);

    void AddRequirement(NoticeRequirement requirement);

    void AddRecord(NoticeRecord record);
}

/// <summary>Links ordinary tasks to the contract work they manage.</summary>
/// <remarks>
/// The M6 and M7 shape, reused. A task carries the obligation or option it is
/// about without <c>TaskItem</c> growing three more nullable columns.
/// </remarks>
public interface IContractTaskLinkRepository
{
    void Add(ContractTaskLink link);
}
