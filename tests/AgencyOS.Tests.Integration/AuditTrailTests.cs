using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Verifies that consequential changes are recorded, and that the record cannot
/// afterwards be altered.
/// </summary>
/// <remarks>
/// The immutability tests attack the trail from three directions, matching the
/// three defenses: through the tracked object graph, through raw SQL row
/// operations, and through TRUNCATE. An audit trail that can be edited is not
/// evidence of anything.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AuditTrailTests
{
    /// <summary>PostgreSQL SQLSTATE for feature_not_supported, raised by the guard trigger.</summary>
    private const string FeatureNotSupported = "0A000";

    private readonly AgencyOsTestFixture _fixture;

    public AuditTrailTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The required verification scenario: a successful consequential mutation
    /// creates the correct audit record.
    /// </summary>
    [Fact]
    public async Task SuccessfulMutation_WritesAnAuditRecordDescribingIt()
    {
        (string subject, User actor, _) = await SeedActorAsync(AgencyRole.Administrator);

        using HttpClient client = _fixture.CreateClient(subject);
        string name = $"Audited {Guid.NewGuid():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest(name, null, "Agency"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        OrganizationResponse created = (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        AuditEvent? record = await context.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.EntityType == nameof(Organization)
                && x.EntityId == created.Id.ToString()
                && x.Action == AuditAction.OrganizationCreated);

        Assert.NotNull(record);

        // Actor, authority and client provenance, per docs/07_SECURITY_AND_AUDIT.md.
        Assert.Equal(actor.Id, record.ActorUserId);
        Assert.Equal(subject, record.ActorSubject);
        Assert.Equal(Permission.OrganizationsCreate, record.Permission);
        Assert.Equal(new OrganizationId(created.Id), record.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(record.CorrelationId));

        Assert.Equal(AgencyOsTestFixture.Platform, record.ClientPlatform);
        Assert.Equal(AgencyOsTestFixture.Channel, record.ClientChannel);
        Assert.Equal(AgencyOsTestFixture.LatestVersion, record.ClientVersion);

        Assert.NotNull(record.SemanticDelta);
        Assert.Contains(name, record.SemanticDelta, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creating an organization also records the ownership it confers, so the
    /// authority a user ends up holding is explainable from the trail alone.
    /// </summary>
    [Fact]
    public async Task CreatingAnOrganization_AlsoRecordsTheOwnershipGrant()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Administrator);

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"Owned {Guid.NewGuid():N}", null, "Agency"));

        OrganizationResponse created = (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        bool granted = await context.AuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.OrganizationId == new OrganizationId(created.Id)
                && x.Action == AuditAction.MembershipGranted);

        Assert.True(granted, "The ownership grant should have been audited alongside the creation.");
    }

    /// <summary>A refused mutation must not leave a business change behind.</summary>
    [Fact]
    public async Task RefusedMutation_WritesNoOrganization()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Observer);

        string name = $"Never Created {Guid.NewGuid():N}";

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest(name, null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();
        Assert.False(await context.Organizations.AsNoTracking().AnyAsync(x => x.Name == name));
    }

    /// <summary>
    /// The required verification scenario, first defense: the save interceptor
    /// refuses a modified audit entry.
    /// </summary>
    [Fact]
    public async Task Interceptor_RefusesToUpdateAnAuditRecord()
    {
        AuditEventId id = await EnsureAuditRecordAsync();

        await using AgencyOsDbContext context = _fixture.CreateGuardedDbContext();

        AuditEvent tracked = await context.AuditEvents.SingleAsync(x => x.Id == id);
        context.Entry(tracked).State = EntityState.Modified;

        await Assert.ThrowsAsync<AuditTrailImmutableException>(() => context.SaveChangesAsync());
    }

    /// <summary>The save interceptor refuses a removed audit entry.</summary>
    [Fact]
    public async Task Interceptor_RefusesToDeleteAnAuditRecord()
    {
        AuditEventId id = await EnsureAuditRecordAsync();

        await using AgencyOsDbContext context = _fixture.CreateGuardedDbContext();

        AuditEvent tracked = await context.AuditEvents.SingleAsync(x => x.Id == id);
        context.AuditEvents.Remove(tracked);

        await Assert.ThrowsAsync<AuditTrailImmutableException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// The required verification scenario, last defense: the database refuses an
    /// UPDATE that never went near the application.
    /// </summary>
    [Fact]
    public async Task Database_RefusesADirectUpdateOfAnAuditRecord()
    {
        AuditEventId id = await EnsureAuditRecordAsync();

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("UPDATE audit_events SET action = 'tampered' WHERE id = @id", id.Value));

        Assert.Equal(FeatureNotSupported, error.SqlState);
        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);

        // And the row is untouched.
        await using AgencyOsDbContext context = _fixture.CreateDbContext();
        AuditEvent unchanged = await context.AuditEvents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.NotEqual("tampered", unchanged.Action);
    }

    /// <summary>The database refuses a direct DELETE.</summary>
    [Fact]
    public async Task Database_RefusesADirectDeleteOfAnAuditRecord()
    {
        AuditEventId id = await EnsureAuditRecordAsync();

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("DELETE FROM audit_events WHERE id = @id", id.Value));

        Assert.Equal(FeatureNotSupported, error.SqlState);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(x => x.Id == id));
    }

    /// <summary>
    /// TRUNCATE does not fire row-level triggers, so it is refused by a separate
    /// statement-level trigger. Without it the whole trail could be erased by one
    /// statement that every row-level defense would have permitted.
    /// </summary>
    [Fact]
    public async Task Database_RefusesToTruncateTheAuditTable()
    {
        await EnsureAuditRecordAsync();

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("TRUNCATE TABLE audit_events", id: null));

        Assert.Equal(FeatureNotSupported, error.SqlState);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync());
    }

    private async Task ExecuteAsync(string sql, Guid? id)
    {
        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        if (id.HasValue)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<AuditEventId> EnsureAuditRecordAsync()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Administrator);

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"Immutable {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        OrganizationResponse created = (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        AuditEvent record = await context.AuditEvents
            .AsNoTracking()
            .FirstAsync(x => x.EntityId == created.Id.ToString() && x.Action == AuditAction.OrganizationCreated);

        return record.Id;
    }

    private async Task<(string Subject, User User, Organization Organization)> SeedActorAsync(AgencyRole role)
    {
        string subject = $"audit-{Guid.NewGuid():N}";

        User user = await _fixture.SeedUserAsync(subject, $"Auditor {role}");
        Organization organization = await _fixture.SeedOrganizationAsync($"Audit Org {Guid.NewGuid():N}", user.Id);

        await _fixture.SeedMembershipAsync(organization.Id, user.Id, role, user.Id);

        return (subject, user, organization);
    }
}
