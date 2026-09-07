using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Sync;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The change feed's ordering guarantee, under actual concurrency.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0013 claims that sequence order equals commit order because positions come
/// from a per-tenant counter row held under its row lock until commit. That claim
/// is the whole reason a client can trust its cursor, and a sequential test cannot
/// exercise it: with one writer at a time, any allocation scheme looks correct.
/// </para>
/// <para>
/// A bare <c>BIGSERIAL</c> would pass every other test in this suite and fail the
/// property here, because two transactions can take values 5 and 6 and commit in
/// the opposite order - leaving a client that read to 6 permanently unaware of 5.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ChangeFeedOrderingTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ChangeFeedOrderingTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Concurrent writers produce a dense, gapless sequence with no duplicates.
    /// </summary>
    /// <remarks>
    /// Every record created must appear exactly once, and the positions must form
    /// an unbroken run. A duplicate would mean two transactions were handed the
    /// same position; a gap would mean a client could skip a change forever.
    /// </remarks>
    [Fact]
    public async Task ConcurrentWrites_ProduceAGaplessSequenceWithNoDuplicates()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "feed-concurrent");
        Guid tenant = actor.Organization.Id.Value;

        const int Writers = 8;
        const int PerWriter = 3;

        // Separate clients, so the writes genuinely overlap rather than queueing
        // behind one connection.
        List<Task> writes = [];

        for (int writer = 0; writer < Writers; writer++)
        {
            int index = writer;

            writes.Add(Task.Run(async () =>
            {
                using HttpClient client = _fixture.CreateClient(actor.Subject);

                for (int item = 0; item < PerWriter; item++)
                {
                    using HttpResponseMessage response = await client.PostAsJsonAsync(
                        $"/api/v1/organizations/{tenant}/people",
                        new CreatePersonRequest($"Writer{index}", $"Item{item}"));

                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                }
            }));
        }

        await Task.WhenAll(writes);

        using HttpClient reader = _fixture.CreateClient(actor.Subject);

        List<ChangeEntryResponse> entries = await ReadEntireFeedAsync(reader, tenant);

        long[] sequences = [.. entries.Select(x => x.Sequence)];

        Assert.Equal(Writers * PerWriter, sequences.Length);

        // No position handed out twice.
        Assert.Equal(sequences.Length, sequences.Distinct().Count());

        // Strictly increasing and dense: 1..N with nothing missing.
        Assert.Equal(Enumerable.Range(1, sequences.Length).Select(x => (long)x).ToArray(), sequences);

        // Every record that was created is named exactly once.
        Assert.Equal(entries.Count, entries.Select(x => x.EntityId).Distinct().Count());
    }

    /// <summary>
    /// Paging the feed while writers are active never skips a change.
    /// </summary>
    /// <remarks>
    /// The reader advances its cursor page by page while writes continue. Anything
    /// committed below the cursor must already have been seen; that is precisely
    /// what the row-lock allocation buys and what a client's durability depends on.
    /// </remarks>
    [Fact]
    public async Task ReadingWhileWritingNeverSkipsAChange()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "feed-interleaved");
        Guid tenant = actor.Organization.Id.Value;

        const int Total = 20;

        using CancellationTokenSource writing = new();

        Task writer = Task.Run(async () =>
        {
            using HttpClient client = _fixture.CreateClient(actor.Subject);

            for (int index = 0; index < Total; index++)
            {
                using HttpResponseMessage response = await client.PostAsJsonAsync(
                    $"/api/v1/organizations/{tenant}/people",
                    new CreatePersonRequest("Interleaved", $"Person{index}"));

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
        });

        using HttpClient reader = _fixture.CreateClient(actor.Subject);

        List<long> observed = [];
        long cursor = 0;

        // Read concurrently with the writer, then drain once it has finished, so
        // the assertion covers both the racing window and the settled state.
        for (int attempt = 0; attempt < 60; attempt++)
        {
            SyncChangesResponse page = await ReadPageAsync(reader, tenant, cursor, take: 3);

            foreach (ChangeEntryResponse entry in page.Changes)
            {
                // A page must never hand back something at or below the cursor.
                Assert.True(
                    entry.Sequence > cursor,
                    $"Sequence {entry.Sequence} was returned for a cursor of {cursor}.");

                observed.Add(entry.Sequence);
            }

            cursor = page.Cursor;

            if (writer.IsCompleted && page.Changes.Count == 0)
            {
                break;
            }
        }

        await writer;

        // Drain anything committed after the loop's last read.
        while (true)
        {
            SyncChangesResponse page = await ReadPageAsync(reader, tenant, cursor, take: 10);

            if (page.Changes.Count == 0)
            {
                break;
            }

            observed.AddRange(page.Changes.Select(x => x.Sequence));
            cursor = page.Cursor;
        }

        Assert.Equal(Total, observed.Count);
        Assert.Equal(observed.Count, observed.Distinct().Count());
        long[] ordered = [.. observed.Order()];

        Assert.Equal(Enumerable.Range(1, Total).Select(x => (long)x).ToArray(), ordered);
    }

    // ------------------------------------------------------------- plumbing

    private static async Task<List<ChangeEntryResponse>> ReadEntireFeedAsync(HttpClient client, Guid tenant)
    {
        List<ChangeEntryResponse> entries = [];
        long cursor = 0;

        while (true)
        {
            SyncChangesResponse page = await ReadPageAsync(client, tenant, cursor, take: 5);

            if (page.Changes.Count == 0)
            {
                return entries;
            }

            entries.AddRange(page.Changes);
            cursor = page.Cursor;
        }
    }

    private static async Task<SyncChangesResponse> ReadPageAsync(
        HttpClient client,
        Guid tenant,
        long cursor,
        int take)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{tenant}/sync/changes?cursor={cursor}&take={take}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SyncChangesResponse>())!;
    }
}
