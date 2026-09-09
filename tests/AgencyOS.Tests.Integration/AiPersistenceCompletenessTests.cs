using System.Reflection;
using AgencyOS.Domain.Ai;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That every persisted AI field is actually persisted.
/// </summary>
/// <remarks>
/// <para>
/// Written because of a real defect. M13 added <c>Residency</c> and
/// <c>ExecutionDevice</c> to <see cref="AgentRun"/> and did not map them, so EF
/// silently invented <c>Residency</c> and <c>ExecutionDevice</c> as column names
/// while every constraint, index and query in the migration referred to
/// <c>residency</c> and <c>execution_device</c>. It compiled, the model
/// validated, and the failure appeared only when the migration hit a real
/// database (ADR-0035).
/// </para>
/// <para>
/// That is the second time this class of mistake has cost a milestone: M11
/// shipped <c>SubjectId</c> mapped as ignored, and M12 shipped a subject arc that
/// was mapped and never populated. All three share a signature — the aggregate is
/// right, the schema is right, and nothing connects them — and all three were
/// invisible to a compiler and to every test that did not touch PostgreSQL.
/// </para>
/// <para>
/// So this is a structural test over EF's own model metadata rather than an
/// assertion per field. A new persisted property on an AI aggregate now fails
/// here the moment it exists unmapped, without anybody remembering to add a test
/// for it.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AiPersistenceCompletenessTests
{
    private readonly AgencyOsTestFixture _fixture;

    public AiPersistenceCompletenessTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>The aggregates that carry AI execution state.</summary>
    /// <remarks>
    /// Listed rather than discovered, so adding an aggregate to the namespace
    /// without adding it here is itself caught by
    /// <see cref="EveryAiAggregateIsUnderTest"/>.
    /// </remarks>
    private static readonly Type[] Aggregates =
    [
        typeof(AgentRun),
        typeof(AgentRunStep),
        typeof(AiToolRequest),
        typeof(AiApproval),
        typeof(AiProviderPolicy),
        typeof(AiContextLease),
    ];

    /// <summary>
    /// Every AI aggregate EF knows about is on the list above.
    /// </summary>
    /// <remarks>
    /// Closes the obvious hole: a completeness test that checks a list nobody
    /// updates is a completeness test about nothing.
    /// </remarks>
    [Fact]
    public void EveryAiAggregateIsUnderTest()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        List<Type> mapped =
        [
            .. context.Model.GetEntityTypes()
                .Select(x => x.ClrType)
                .Where(x => x.Namespace == typeof(AgentRun).Namespace),
        ];

        Assert.All(
            mapped,
            type => Assert.True(
                Aggregates.Contains(type),
                $"{type.Name} is an AI aggregate that this completeness test does not "
                    + "cover. Add it to the list."));
    }

    /// <summary>
    /// Every persisted property is mapped.
    /// </summary>
    /// <remarks>
    /// "Persisted" means a property the aggregate writes to: a getter with a
    /// setter, however private. A computed property has no setter and is
    /// deliberately not expected in the model — <c>IsTerminal</c> is a reading of
    /// state, not state.
    /// </remarks>
    [Fact]
    public void EveryPersistedPropertyIsMapped()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        List<string> missing = [];

        foreach (Type type in Aggregates)
        {
            IEntityType entity = context.Model.FindEntityType(type)
                ?? throw new InvalidOperationException($"{type.Name} is not mapped at all.");

            foreach (PropertyInfo property in PersistedProperties(type))
            {
                bool mapped = entity.FindProperty(property.Name) is not null
                    || entity.FindNavigation(property.Name) is not null
                    || entity.GetSkipNavigations().Any(x => x.Name == property.Name);

                if (!mapped)
                {
                    missing.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Every mapped column is named the way this repository names columns.
    /// </summary>
    /// <remarks>
    /// <strong>This is the test that would have caught the M13 defect.</strong> An
    /// unmapped property does not vanish — EF maps it with its CLR name, so the
    /// column arrives as <c>Residency</c> rather than <c>residency</c>. Asserting
    /// the convention catches "somebody forgot to configure this" without needing
    /// to know which property was forgotten.
    /// </remarks>
    [Fact]
    public void EveryColumnUsesTheRepositoryNamingConvention()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        List<string> offenders = [];

        foreach (Type type in Aggregates)
        {
            IEntityType entity = context.Model.FindEntityType(type)!;

            foreach (IProperty property in entity.GetProperties())
            {
                string? column = property.GetColumnName();

                if (column is null || IsSnakeCase(column))
                {
                    continue;
                }

                offenders.Add(
                    $"{type.Name}.{property.Name} maps to column '{column}'. Columns are "
                        + "snake_case; a PascalCase one means the property was never "
                        + "configured and EF named it for you.");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every aggregate that carries a version uses it as a concurrency token.
    /// </summary>
    /// <remarks>
    /// A <c>Version</c> column that is not a concurrency token is worse than none:
    /// the aggregate refuses a stale write and the database happily accepts two
    /// current ones (ADR-0014).
    /// </remarks>
    [Fact]
    public void EveryVersionIsAConcurrencyToken()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        foreach (Type type in Aggregates)
        {
            IEntityType entity = context.Model.FindEntityType(type)!;
            IProperty? version = entity.FindProperty("Version");

            if (version is null)
            {
                continue;
            }

            Assert.True(
                version.IsConcurrencyToken,
                $"{type.Name}.Version is stored but is not a concurrency token.");
        }
    }

    /// <summary>
    /// Aggregates another table takes a tenant-safe foreign key into.
    /// </summary>
    /// <remarks>
    /// Only these are referenced: M13's lease points at a run, and M12's approval
    /// points at a tool request. An approval, a run step and a provider policy are
    /// reached through their parent or their tenant, and an alternate key on them
    /// would be an index nothing uses.
    ///
    /// Listed rather than derived because EF cannot derive it: M12 and M13 declare
    /// their composite foreign keys in raw SQL, so the model does not know which
    /// tables are referenced. Adding a reference to an aggregate that lacks the key
    /// is therefore a mistake this list has to catch by being updated.
    /// </remarks>
    private static readonly Type[] TenantReferenced =
    [
        typeof(AgentRun),
        typeof(AiToolRequest),
    ];

    /// <summary>
    /// Every AI aggregate carries its tenant, and the referenced ones can be
    /// pointed at safely.
    /// </summary>
    /// <remarks>
    /// The composite <c>(organization_id, id)</c> alternate key is what lets
    /// another table take a foreign key that cannot cross organizations. Without
    /// it a child row could point at a parent in a different tenant and no
    /// constraint would notice (ADR-0011).
    /// </remarks>
    [Fact]
    public void EveryAiAggregateIsTenantQualified()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        foreach (Type type in Aggregates)
        {
            Assert.True(
                context.Model.FindEntityType(type)!.FindProperty("OrganizationId") is not null,
                $"{type.Name} carries no organization.");
        }

        foreach (Type type in TenantReferenced)
        {
            IEntityType entity = context.Model.FindEntityType(type)!;

            Assert.True(
                entity.GetKeys().Any(key =>
                    key.Properties.Count == 2
                    && key.Properties.Any(p => p.Name == "OrganizationId")
                    && key.Properties.Any(p => p.Name == "Id")),
                $"{type.Name} is referenced by a composite foreign key but has no "
                    + "(organization_id, id) alternate key to reference.");
        }
    }

    /// <summary>
    /// The M13 execution fields, named explicitly.
    /// </summary>
    /// <remarks>
    /// Redundant with the structural tests above and kept anyway. The structural
    /// tests say "nothing is unmapped"; this says "these specific things exist",
    /// which is what fails usefully if somebody deletes a field rather than
    /// forgetting to map one.
    /// </remarks>
    [Theory]
    [InlineData(typeof(AgentRun), "Residency", "residency")]
    [InlineData(typeof(AgentRun), "ExecutionDevice", "execution_device")]
    [InlineData(typeof(AiContextLease), "Id", "id")]
    [InlineData(typeof(AiContextLease), "OrganizationId", "organization_id")]
    [InlineData(typeof(AiContextLease), "UserId", "user_id")]
    [InlineData(typeof(AiContextLease), "AgentRunId", "ai_run_id")]
    [InlineData(typeof(AiContextLease), "SubjectKind", "subject_kind")]
    [InlineData(typeof(AiContextLease), "SubjectId", "subject_id")]
    [InlineData(typeof(AiContextLease), "Residency", "residency")]
    [InlineData(typeof(AiContextLease), "ContextFingerprint", "context_fingerprint")]
    [InlineData(typeof(AiContextLease), "ModelKey", "model_key")]
    [InlineData(typeof(AiContextLease), "PolicyVersion", "policy_version")]
    [InlineData(typeof(AiContextLease), "IssuedAt", "issued_at")]
    [InlineData(typeof(AiContextLease), "ExpiresAt", "expires_at")]
    [InlineData(typeof(AiContextLease), "State", "state")]
    [InlineData(typeof(AiContextLease), "ResolvedAt", "resolved_at")]
    [InlineData(typeof(AiContextLease), "Version", "version")]
    public void TheM13ExecutionFieldsAreMappedToTheirColumns(
        Type aggregate, string property, string column)
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        IProperty? mapped = context.Model.FindEntityType(aggregate)!.FindProperty(property);

        Assert.True(mapped is not null, $"{aggregate.Name}.{property} is not mapped.");
        Assert.Equal(column, mapped!.GetColumnName());
    }

    /// <summary>
    /// The model agrees with the database that was migrated into existence.
    /// </summary>
    /// <remarks>
    /// The last gap the tests above leave: a property can be mapped to a
    /// snake_case name that no migration ever created. Reading the real columns
    /// closes it, and is the check that would have failed fastest on the M13
    /// defect.
    /// </remarks>
    [Fact]
    public async Task EveryMappedColumnExistsInTheDatabase()
    {
        using AgencyOsDbContext context = _fixture.CreateDbContext();

        List<string> missing = [];

        foreach (Type type in Aggregates)
        {
            IEntityType entity = context.Model.FindEntityType(type)!;
            string table = entity.GetTableName()!;

            HashSet<string> actual = [];

            await using (System.Data.Common.DbCommand command =
                context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText =
                    "SELECT column_name FROM information_schema.columns "
                        + $"WHERE table_name = '{table}'";

                await context.Database.OpenConnectionAsync();

                await using System.Data.Common.DbDataReader reader =
                    await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    actual.Add(reader.GetString(0));
                }
            }

            await context.Database.CloseConnectionAsync();

            foreach (IProperty property in entity.GetProperties())
            {
                string column = property.GetColumnName()!;

                if (!actual.Contains(column))
                {
                    missing.Add($"{table}.{column} ({type.Name}.{property.Name})");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>Properties the aggregate writes to, and therefore stores.</summary>
    private static IEnumerable<PropertyInfo> PersistedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.CanRead && x.SetMethod is not null)
            .Where(x => x.GetIndexParameters().Length == 0);

    private static bool IsSnakeCase(string column) =>
        column.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_');
}
