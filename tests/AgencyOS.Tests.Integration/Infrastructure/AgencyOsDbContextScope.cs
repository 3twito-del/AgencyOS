using AgencyOS.Domain.Communications;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Tests.Integration.Infrastructure;

/// <summary>
/// Direct access to the test database, for the assertions the API cannot make.
/// </summary>
/// <remarks>
/// <para>
/// Used sparingly, and only where the property under test is about what is
/// <em>stored</em> rather than about what is returned. An encrypted token is the
/// clearest case: the API is required never to expose it, so the only place to
/// check that the database does not hold plaintext is the database.
/// </para>
/// <para>
/// It is also how a crash is simulated. Moving a dispatch to
/// <c>SendRequested</c> and abandoning it is exactly the state a process killed
/// between the write and the provider's answer leaves behind, and no API route
/// produces it on purpose (ADR-0028).
/// </para>
/// </remarks>
public sealed class AgencyOsDbContextScope : IAsyncDisposable
{
    private readonly AgencyOsDbContext _context;

    public AgencyOsDbContextScope(AgencyOsTestFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _context = fixture.CreateDbContext();
    }

    /// <summary>The stored account row, credential and all.</summary>
    public async Task<CommunicationAccount> Accounts(Guid accountId) =>
        await _context.CommunicationAccounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == new CommunicationAccountId(accountId))
            .ConfigureAwait(false);

    /// <summary>The correlation value a reconciliation will search on.</summary>
    public async Task<string> ClientReferenceAsync(Guid dispatchId) =>
        (await _context.OutboundDispatches
            .AsNoTracking()
            .SingleAsync(x => x.Id == new OutboundDispatchId(dispatchId))
            .ConfigureAwait(false))
        .ClientReference;

    /// <summary>
    /// Leaves a dispatch in the state a process killed mid-send would leave it in.
    /// </summary>
    /// <remarks>
    /// Written with SQL rather than through the domain, because the domain has no
    /// operation for "advance to SendRequested and then stop existing". That is the
    /// point: the row has to be recoverable by a worker that knows nothing about
    /// how it got there.
    /// </remarks>
    public async Task SimulateCrashDuringSendAsync(Guid dispatchId) =>
        await _context.Database
            .ExecuteSqlRawAsync(
                """
                UPDATE outbound_dispatches
                   SET state = 4,
                       attempt_count = attempt_count + 1,
                       updated_at = now(),
                       lease_owner = NULL,
                       lease_expires_at = NULL,
                       next_attempt_at = NULL,
                       version = version + 1
                 WHERE id = {0}
                """,
                dispatchId)
            .ConfigureAwait(false);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync().ConfigureAwait(false);
}
