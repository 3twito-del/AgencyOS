using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Every versioned aggregate guards its writes in the database, not only in memory.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0014 gives every mutable record an explicit <c>version</c> integer and a
/// required <c>expectedVersion</c> on each guarded command. That check runs in the
/// domain, against the row as it was read, and it is exactly right for the case it
/// was written for: a client working from a stale copy is refused.
/// </para>
/// <para>
/// It cannot catch the other case. Two requests that both read version N are both
/// current when they check, and the check passes for both; only the database can
/// decide which one is still current when it writes. Without
/// <c>IsConcurrencyToken</c> the second write simply lands on top of the first.
/// </para>
/// <para>
/// That was not hypothetical. Before this test existed, an offer could be accepted
/// and countered at the same time — <c>DealTests.AcceptingWhileCountering</c>
/// reproduced it roughly one run in twelve — because thirty-three aggregates from
/// M2 through M9 declared <c>version</c> as an ordinary column. A test over the
/// model catches the next one at build time rather than one run in twelve.
/// </para>
/// <para>
/// This needs no database. It reads the model EF builds, which is why it runs in
/// milliseconds and cannot be flaky.
/// </para>
/// </remarks>
public sealed class ConcurrencyTokenTests
{
    /// <summary>
    /// Every entity carrying a <c>version</c> column declares it a concurrency
    /// token.
    /// </summary>
    [Fact]
    public void EveryVersionedEntity_GuardsItsWritesInTheDatabase()
    {
        List<string> unguarded = [];

        foreach (IEntityType entity in Model().GetEntityTypes())
        {
            IProperty? version = entity.FindProperty("Version");

            if (version is null)
            {
                continue;
            }

            if (!version.IsConcurrencyToken)
            {
                unguarded.Add(entity.ClrType.Name);
            }
        }

        Assert.True(
            unguarded.Count == 0,
            "These entities carry a version column that does not guard the write, so "
                + "two concurrent callers can both succeed and the second silently wins: "
                + string.Join(", ", unguarded.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The token is the explicit integer, not a storage-engine internal.
    /// </summary>
    /// <remarks>
    /// ADR-0014 rejected PostgreSQL's <c>xmin</c> deliberately: the token is part
    /// of the client contract, returned to the Windows client, cached, queued and
    /// sent back days later. Binding that to a storage internal would leak the
    /// schema into the client and would not survive a storage change.
    /// </remarks>
    [Fact]
    public void TheTokenIsTheExplicitVersionColumn()
    {
        foreach (IEntityType entity in Model().GetEntityTypes())
        {
            IProperty[] tokens = [.. entity.GetProperties().Where(x => x.IsConcurrencyToken)];

            if (tokens.Length == 0)
            {
                continue;
            }

            IProperty token = Assert.Single(tokens);

            Assert.Equal("Version", token.Name);
            Assert.Equal(typeof(int), token.ClrType);
            Assert.Equal("version", token.GetColumnName());
        }
    }

    /// <summary>
    /// The model is built without a connection, so this says nothing about a
    /// database and needs none.
    /// </summary>
    private static IModel Model()
    {
        DbContextOptions<AgencyOsDbContext> options =
            new DbContextOptionsBuilder<AgencyOsDbContext>()
                .UseNpgsql("Host=model-only;Database=model-only")
                .Options;

        using AgencyOsDbContext context = new(options, new SystemClock());

        return context.Model;
    }
}
