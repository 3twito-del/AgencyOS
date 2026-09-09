using System.Data.Common;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AgencyOS.Tests.Integration.Scale;

/// <summary>
/// Counts the SQL commands one HTTP request actually executed.
/// </summary>
/// <remarks>
/// <para>
/// The instrument the M14 scale harness is built on. Wall-clock timing cannot be
/// asserted in hosted CI — a shared runner's numbers move for reasons that have
/// nothing to do with the code (§53) — but the <em>number</em> of round trips a
/// request makes is deterministic, and it is where the regressions that matter
/// actually show up.
/// </para>
/// <para>
/// An N+1 is not a slow query. It is a query count that grows with the size of the
/// result, and it stays invisible until the tenant is large enough to hurt. Counting
/// commands turns that into something a test can state exactly.
/// </para>
/// </remarks>
public sealed class QueryCounter : DbCommandInterceptor
{
    private int _count;

    /// <summary>Commands executed since the last <see cref="Reset"/>.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Whether this interceptor has ever been invoked.
    /// </summary>
    /// <remarks>
    /// Separate from the count on purpose. A harness whose instrument was never
    /// wired in would report zero queries for every endpoint and pass every
    /// N+1 assertion it makes — the most convincing kind of false green. Tests
    /// assert this before they assert anything else.
    /// </remarks>
    public bool EverObserved { get; private set; }

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    private void Record()
    {
        EverObserved = true;
        Interlocked.Increment(ref _count);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record();

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record();

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record();

        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record();

        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record();

        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record();

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// Adds the counter to the host's context without replacing its registration.
/// </summary>
/// <remarks>
/// Re-registering <c>AddDbContext</c> in the test host would mean restating the
/// connection string, the interceptors and the conventions the application
/// configures — a copy that would drift and would eventually make the harness
/// measure something the product does not do. This adds one interceptor to the
/// configuration the application already built.
/// </remarks>
public sealed class CountingOptionsConfiguration
    : IDbContextOptionsConfiguration<AgencyOsDbContext>
{
    private readonly QueryCounter _counter;

    public CountingOptionsConfiguration(QueryCounter counter) => _counter = counter;

    public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddInterceptors(_counter);
    }
}
