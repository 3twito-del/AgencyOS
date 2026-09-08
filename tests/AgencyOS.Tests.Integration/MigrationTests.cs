using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>The M12 tables all arrive, in a database that had none.</summary>
    [Fact]
    public async Task Migrations_CreateTheAiRuntimeTables()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        string[] tables =
        [
            "ai_runs",
            "ai_run_steps",
            "ai_tool_requests",
            "ai_approvals",
            "ai_provider_policies",
        ];

        foreach (string table in tables)
        {
            object? result = await clean.ScalarAsync($"SELECT to_regclass('public.{table}')::text");
            Assert.True(result is not null and not DBNull, $"Table '{table}' was not created.");
        }
    }

    /// <summary>
    /// The constraints that hold when the application layer does not.
    /// </summary>
    /// <remarks>
    /// Two of these carry the milestone's load on their own.
    /// <c>ck_ai_tool_requests_effect</c> makes an external-effect request
    /// impossible to write at all, and <c>ck_ai_provider_policies_ceiling</c>
    /// leaves <c>Restricted</c> unreachable at the layer no future code path goes
    /// around (ADR-0031).
    /// </remarks>
    [Fact]
    public async Task Migrations_InstallTheAiRuntimeConstraints()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        string[] expected =
        [
            "ck_ai_runs_subject_arc",
            "ck_ai_runs_terminal",
            "ck_ai_runs_failure",
            "ck_ai_runs_counts",
            "ck_ai_approvals_decision",
            "ck_ai_approvals_window",
            "ck_ai_tool_requests_effect",
            "ck_ai_tool_requests_version",
            "ck_ai_provider_policies_ceiling",
            "fk_ai_runs_research_case_id",
            "fk_ai_runs_person_id",
            "fk_ai_runs_company_id",
            "fk_ai_runs_deal_id",
            "fk_ai_runs_contract_id",
        ];

        foreach (string constraint in expected)
        {
            object? found = await clean.ScalarAsync(
                $"SELECT conname FROM pg_constraint WHERE conname = '{constraint}'");

            Assert.Equal(constraint, found);
        }
    }

    /// <summary>The three triggers that make M12 history and decisions immutable.</summary>
    [Fact]
    public async Task Migrations_InstallTheAiRuntimeTriggers()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        string[] triggers =
        [
            "trg_ai_run_steps_immutable",
            "trg_ai_approvals_monotonic",
            "trg_ai_tool_requests_once",
        ];

        foreach (string trigger in triggers)
        {
            object? found = await clean.ScalarAsync(
                $"SELECT tgname FROM pg_trigger WHERE tgname = '{trigger}'");

            Assert.Equal(trigger, found);
        }
    }

    /// <summary>
    /// The expand path has a proven reverse.
    /// </summary>
    /// <remarks>
    /// Applied, rolled back to the previous migration, and re-applied. EF orders
    /// its own <c>DropTable</c> calls around relationships it knows about, and it
    /// knows about none of the foreign keys, checks and triggers M12 declares in
    /// SQL — so a <c>Down</c> that looks right can still fail halfway and leave a
    /// database that is neither version. M11 proved that the hard way.
    /// </remarks>
    [Fact]
    public async Task Migrations_RollBackAndReapplyTheAiRuntime()
    {
        await using TemporaryDatabase clean =
            await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "agencyos_migr");

        await clean.MigrateAsync();

        await using (AgencyOsDbContext down = clean.CreateDbContext())
        {
            IMigrator migrator = down.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
        }

        object? gone = await clean.ScalarAsync("SELECT to_regclass('public.ai_runs')::text");
        Assert.True(gone is null or DBNull, "ai_runs survived the rollback.");

        object? kept = await clean.ScalarAsync("SELECT to_regclass('public.research_cases')::text");
        Assert.True(kept is not null and not DBNull, "The rollback took M11 with it.");

        await clean.MigrateAsync();

        object? back = await clean.ScalarAsync("SELECT to_regclass('public.ai_runs')::text");
        Assert.True(back is not null and not DBNull, "ai_runs did not come back.");

        await using AgencyOsDbContext context = clean.CreateDbContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>The migration M12 rolls back to.</summary>
    private const string PreviousMigration = "20260908184322_Intelligence";
}
