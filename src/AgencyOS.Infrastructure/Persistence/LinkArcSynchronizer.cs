using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>
/// Fills the typed column behind every exclusive-arc link before it is saved.
/// </summary>
/// <remarks>
/// <para>
/// A document link and a communication link each carry one <c>Target</c>
/// discriminator and one <c>TargetId</c> in the domain, and fourteen nullable
/// typed columns in the schema — one per target, each with a composite foreign key
/// into <c>(organization_id, id)</c> on the target's own table. That is what gives
/// a link real referential integrity and real tenant integrity, instead of an
/// untyped pair that cheerfully points at a deleted or foreign-tenant row
/// (ADR-0025).
/// </para>
/// <para>
/// Doing the translation here rather than at each call site is deliberate. There
/// are a dozen places a link is created, every one of them compiles perfectly
/// without setting the column, and the failure would be a check-constraint
/// violation at some unrelated later save — or, if a stale value survived, a link
/// quietly pointing at two records at once.
/// </para>
/// </remarks>
internal static class LinkArcSynchronizer
{
    internal static void Apply(ChangeTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        foreach (EntityEntry<DocumentLink> entry in tracker.Entries<DocumentLink>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Write(entry, entry.Entity.Target, entry.Entity.TargetId);
            }
        }

        foreach (EntityEntry<CommunicationLink> entry in tracker.Entries<CommunicationLink>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Write(entry, entry.Entity.Target, entry.Entity.TargetId);
            }
        }
    }

    /// <summary>Sets the one matching column and nulls the other thirteen.</summary>
    private static void Write<TEntity>(
        EntityEntry<TEntity> entry,
        DocumentLinkTarget target,
        Guid targetId)
        where TEntity : class
    {
        foreach ((DocumentLinkTarget candidate, string column, _) in M10Links.Targets)
        {
            entry.Property<Guid?>(column).CurrentValue = candidate == target ? targetId : null;
        }
    }
}
