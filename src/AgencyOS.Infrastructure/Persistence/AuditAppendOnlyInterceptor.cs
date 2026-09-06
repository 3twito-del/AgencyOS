using AgencyOS.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>
/// Raised when something attempts to modify or remove an audit record.
/// </summary>
public sealed class AuditTrailImmutableException : Exception
{
    public AuditTrailImmutableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Refuses any save that would modify or delete an <see cref="AuditEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The second of the three append-only defenses. The domain type has no mutator,
/// so ordinary code cannot express a change; this interceptor catches a change
/// made through the tracked object graph anyway - a detached entity re-attached
/// in the wrong state, a bulk operation, a future mapping mistake. The database
/// trigger is the third and catches everything that bypasses the application
/// entirely.
/// </para>
/// <para>
/// Enforcement is deliberately duplicated. <c>docs/07_SECURITY_AND_AUDIT.md</c>
/// treats audit as a data-integrity property, and a property worth having is
/// worth defending at more than one layer.
/// </para>
/// </remarks>
public sealed class AuditAppendOnlyInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new AuditTrailImmutableException(
                    $"The audit trail is append-only; attempted to {entry.State.ToString().ToLowerInvariant()} "
                        + $"audit event {entry.Entity.Id}.");
            }
        }
    }
}
