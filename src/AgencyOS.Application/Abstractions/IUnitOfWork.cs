namespace AgencyOS.Application.Abstractions;

/// <summary>Commits the work accumulated by a single command.</summary>
/// <remarks>
/// A command's business change and its audit record are written in one
/// transaction. They cannot diverge: either both are durable or neither is.
/// </remarks>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
