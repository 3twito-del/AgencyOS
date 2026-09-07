using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Domain.Deals;

/// <summary>
/// Joins an ordinary task to the negotiation it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The M6 shape, reused. A nullable <c>DealId</c> on <see cref="TaskItem"/> would
/// have been smaller today and worse every milestone after: each new linkable
/// thing adds another nullable column and another combination nothing validates.
/// A join table adds one row per link and stays the same size as the system grows
/// (ADR-0021).
/// </para>
/// <para>
/// A task belongs to at most one negotiation, enforced by a unique index. Follow
/// up on the offer, prepare the counter, review the terms, start the contract
/// process once terms are agreed - each is one task, on one deal.
/// </para>
/// </remarks>
public sealed class DealTaskLink
{
    private DealTaskLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The task. A task belongs to at most one negotiation.</summary>
    public TaskItemId TaskItemId { get; private set; }

    public DealId DealId { get; private set; }

    /// <summary>The offer it concerns, when it concerns one in particular.</summary>
    public OfferId? OfferId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static DealTaskLink Create(
        OrganizationId organizationId,
        TaskItemId taskItemId,
        DealId dealId,
        OfferId? offerId,
        DateTimeOffset now)
    {
        if (taskItemId.Value == Guid.Empty)
        {
            throw new DomainException("A task link must name a task.");
        }

        return new DealTaskLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            TaskItemId = taskItemId,
            DealId = dealId,
            OfferId = offerId,
            LinkedAt = now,
        };
    }
}
