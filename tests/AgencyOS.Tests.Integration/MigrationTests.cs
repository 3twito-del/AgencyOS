using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Verifies that the schema can be built from nothing, and that it arrives with
/// its integrity guarantees attached.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class MigrationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public MigrationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migrations_ApplyToACleanDatabase()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await using AgencyOsDbContext context = clean.CreateDbContext();
        await context.Database.MigrateAsync();

        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);

        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrations_CreateEveryExpectedTable()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        string[] tables =
        [
            "users",
            "organizations",
            "memberships",
            "audit_events",
            "release_policies",
            "system_initialization",
            "people",
            "companies",
            "professional_relationships",
            "interactions",
            "interaction_participants",
            "tasks",
        ];

        foreach (string table in tables)
        {
            // Cast to text: Npgsql cannot read the regclass type as an object.
            object? result = await clean.ScalarAsync($"SELECT to_regclass('public.{table}')::text");
            Assert.True(result is not null and not DBNull, $"Table '{table}' was not created.");
        }
    }

    /// <summary>
    /// The audit table must never exist without its guard. Both arrive in the same
    /// migration precisely so there is no window in which one is present and the
    /// other is not.
    /// </summary>
    [Fact]
    public async Task Migrations_InstallTheAuditAppendOnlyTriggers()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        object? rowTrigger = await clean.ScalarAsync(
            "SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_events'::regclass AND tgname = 'audit_events_append_only'");
        Assert.Equal("audit_events_append_only", rowTrigger);

        object? truncateTrigger = await clean.ScalarAsync(
            "SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_events'::regclass AND tgname = 'audit_events_no_truncate'");
        Assert.Equal("audit_events_no_truncate", truncateTrigger);
    }

    /// <summary>
    /// The initialization singleton is a database guarantee, not an application
    /// convention: the check constraint is what stops a second bootstrap row.
    /// </summary>
    [Fact]
    public async Task Migrations_ConstrainSystemInitializationToASingleRow()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        object? constraint = await clean.ScalarAsync(
            "SELECT conname FROM pg_constraint WHERE conname = 'ck_system_initialization_singleton'");

        Assert.Equal("ck_system_initialization_singleton", constraint);
    }

    /// <summary>
    /// The exclusive-arc and tenant-containment constraints are what make a
    /// cross-tenant or half-formed relationship impossible to write, so their
    /// presence is asserted rather than assumed (ADR-0011).
    /// </summary>
    [Fact]
    public async Task Migrations_InstallTheArcAndTenantConstraints()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        string[] expected =
        [
            "ck_relationships_from_exactly_one_endpoint",
            "ck_relationships_to_exactly_one_endpoint",
            "ck_relationships_ended_after_started",
            "ck_relationships_no_self_reference",
            "ck_interaction_participants_exactly_one_party",
            "ck_tasks_at_most_one_subject",
            "ck_tasks_completion_consistent",
            "fk_people_primary_company_same_tenant",
            "fk_relationships_from_person_same_tenant",
            "fk_relationships_to_company_same_tenant",
            "fk_interaction_participants_person_same_tenant",
            "fk_tasks_source_interaction_same_tenant",
        ];

        foreach (string constraint in expected)
        {
            object? found = await clean.ScalarAsync(
                $"SELECT conname FROM pg_constraint WHERE conname = '{constraint}'");

            Assert.Equal(constraint, found);
        }
    }

    /// <summary>Re-running migrations must be a no-op, not an error.</summary>
    [Fact]
    public async Task Migrations_AreIdempotent()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();
        await clean.MigrateAsync();

        await using AgencyOsDbContext context = clean.CreateDbContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
