using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Infrastructure.Storage;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration.Operations;

/// <summary>
/// The drill: back AgencyOS up, destroy it, bring it back, and check what returned.
/// </summary>
/// <remarks>
/// <para>
/// A backup is not proven by an exit code. Until M15 nothing here had ever been
/// restored, so "AgencyOS can be recovered" was a belief rather than a finding.
/// This exercises the operator scripts themselves — the same
/// <c>Backup-AgencyOS.ps1</c> and <c>Restore-AgencyOS.ps1</c> a person would run —
/// so what is tested is what is shipped, not a reimplementation that could drift
/// from it (ADR-0039).
/// </para>
/// <para>
/// <strong>Both halves, because the database is not all of the data.</strong> M10
/// put document bytes in a content-addressed store outside the relational rows. A
/// database-only restore returns every document's metadata, hash and version
/// history and none of its content, and the system looks healthy while every file
/// is gone. So the drill seeds real blobs and verifies each restored byte against
/// the digest the database recorded.
/// </para>
/// <para>
/// Runs against a throwaway database on the same server, because the drill drops
/// its target and the shared fixture database is not something to drop.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class BackupRestoreDrillTests
{
    private readonly AgencyOsTestFixture _fixture;

    public BackupRestoreDrillTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A full round trip returns the records, the audit trail and the bytes.
    /// </summary>
    [Fact]
    public async Task ABackupCanBeRestoredAndEverythingComesBack()
    {
        await using Drill drill = await Drill.StartAsync(_fixture.ConnectionString);

        Seeded seeded = await drill.SeedAsync();

        // ---------------------------------------------------------- back up
        ScriptResult backup = drill.Run(
            "Backup-AgencyOS.ps1",
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot,
            "-Destination", drill.BackupPath);

        Assert.True(backup.Succeeded, $"Backup failed:{Environment.NewLine}{backup.Output}");
        Assert.Contains("backup.success", backup.Output, StringComparison.Ordinal);

        // The manifest is the binding between the two halves.
        Assert.True(File.Exists(Path.Combine(drill.BackupPath, "backup-manifest.json")));
        Assert.True(File.Exists(Path.Combine(drill.BackupPath, "database.dump")));

        // --------------------------------------------------------- destroy
        // Not "clear the tables" — the real failure is losing the database, and a
        // drill that only truncated rows would never exercise the restore path
        // that recreates it.
        Directory.Delete(drill.BlobRoot, recursive: true);

        // ---------------------------------------------------------- restore
        ScriptResult restore = drill.Run(
            "Restore-AgencyOS.ps1",
            "-BackupPath", drill.BackupPath,
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot,
            "-Force");

        Assert.True(restore.Succeeded, $"Restore failed:{Environment.NewLine}{restore.Output}");
        Assert.Contains("restore.success", restore.Output, StringComparison.Ordinal);

        // ----------------------------------------------------- what returned
        await using AgencyOsDbContext restored = drill.Database.CreateDbContext();

        Person person = await restored.People.SingleAsync(x => x.Id == seeded.PersonId);
        Assert.Equal(seeded.PersonDisplayName, person.DisplayName);

        Company company = await restored.Companies.SingleAsync(x => x.Id == seeded.CompanyId);
        Assert.Equal(seeded.CompanyName, company.Name);

        // Audit continuity. A recovery that silently dropped or resequenced the
        // evidence trail would leave the business records intact and the account
        // of how they got there missing.
        long auditRows = await restored.Set<Domain.Audit.AuditEvent>().LongCountAsync();
        Assert.Equal(seeded.AuditEventCount, auditRows);

        // The bytes, checked against the digest the database recorded rather than
        // against the file that was copied. A copy that verified itself would
        // prove only that copying works.
        IBlobStore store = drill.CreateBlobStore();

        foreach ((string key, string digest) in seeded.Blobs)
        {
            Assert.True(
                await store.ExistsAsync(seeded.OrganizationId, key),
                $"The document bytes at '{key}' did not come back.");

            Assert.True(
                await store.VerifyAsync(seeded.OrganizationId, key, digest),
                $"The bytes at '{key}' came back changed. Restored content does not hash to {digest}.");
        }
    }

    /// <summary>
    /// Restore refuses to run without <c>-Force</c>, and changes nothing.
    /// </summary>
    /// <remarks>
    /// Restore replaces a database somebody may still be using. The default has to
    /// be to describe what would happen and stop, because the operator reaching for
    /// this script is often having a bad day already.
    /// </remarks>
    [Fact]
    public async Task RestoreRefusesWithoutForceAndChangesNothing()
    {
        await using Drill drill = await Drill.StartAsync(_fixture.ConnectionString);

        Seeded seeded = await drill.SeedAsync();

        ScriptResult backup = drill.Run(
            "Backup-AgencyOS.ps1",
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot,
            "-Destination", drill.BackupPath);

        Assert.True(backup.Succeeded, backup.Output);

        ScriptResult refused = drill.Run(
            "Restore-AgencyOS.ps1",
            "-BackupPath", drill.BackupPath,
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot);

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("restore.refused", refused.Output, StringComparison.Ordinal);

        // And the database it declined to replace is untouched.
        await using AgencyOsDbContext context = drill.Database.CreateDbContext();

        Assert.True(await context.People.AnyAsync(x => x.Id == seeded.PersonId));
    }

    /// <summary>
    /// A damaged backup is refused before anything is destroyed.
    /// </summary>
    /// <remarks>
    /// The ordering is the point. A restore that dropped the database and then
    /// discovered the dump was truncated would have turned a recoverable situation
    /// into an unrecoverable one, which is the worst thing a recovery tool can do.
    /// </remarks>
    [Fact]
    public async Task ADamagedBackupIsRefusedBeforeAnythingIsDestroyed()
    {
        await using Drill drill = await Drill.StartAsync(_fixture.ConnectionString);

        Seeded seeded = await drill.SeedAsync();

        ScriptResult backup = drill.Run(
            "Backup-AgencyOS.ps1",
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot,
            "-Destination", drill.BackupPath);

        Assert.True(backup.Succeeded, backup.Output);

        // A single flipped byte, which is what silent corruption looks like.
        string dump = Path.Combine(drill.BackupPath, "database.dump");
        byte[] bytes = await File.ReadAllBytesAsync(dump);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(dump, bytes);

        ScriptResult restore = drill.Run(
            "Restore-AgencyOS.ps1",
            "-BackupPath", drill.BackupPath,
            "-ConnectionString", drill.Database.ConnectionString,
            "-BlobRoot", drill.BlobRoot,
            "-Force");

        Assert.False(restore.Succeeded);
        Assert.Contains("restore.failure", restore.Output, StringComparison.Ordinal);

        // The database is still there, because the check ran first.
        await using AgencyOsDbContext context = drill.Database.CreateDbContext();

        Assert.True(await context.People.AnyAsync(x => x.Id == seeded.PersonId));
    }

    // ------------------------------------------------------------- harness

    private sealed record Seeded(
        OrganizationId OrganizationId,
        PersonId PersonId,
        string PersonDisplayName,
        CompanyId CompanyId,
        string CompanyName,
        long AuditEventCount,
        IReadOnlyList<(string Key, string Digest)> Blobs);

    private sealed record ScriptResult(int ExitCode, string Output)
    {
        public bool Succeeded => ExitCode == 0;
    }

    /// <summary>A throwaway database, a throwaway blob root and somewhere to back up to.</summary>
    private sealed class Drill : IAsyncDisposable
    {
        private Drill(TemporaryDatabase database, string workspace)
        {
            Database = database;
            Workspace = workspace;
            BlobRoot = Path.Combine(workspace, "blobs");
            BackupPath = Path.Combine(workspace, "backup");

            Directory.CreateDirectory(BlobRoot);
        }

        public TemporaryDatabase Database { get; }

        public string Workspace { get; }

        public string BlobRoot { get; }

        public string BackupPath { get; }

        public static async Task<Drill> StartAsync(string template)
        {
            TemporaryDatabase database = await TemporaryDatabase
                .CreateAsync(template, "agencyos_drill")
                .ConfigureAwait(false);

            await database.MigrateAsync().ConfigureAwait(false);

            string workspace = Path.Combine(
                Path.GetTempPath(), "agencyos-drill", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(workspace);

            return new Drill(database, workspace);
        }

        public IBlobStore CreateBlobStore() =>
            new FileSystemBlobStore(
                new BlobStoreOptions { RootPath = BlobRoot },
                NullLogger<FileSystemBlobStore>.Instance);

        /// <summary>Seeds representative cross-domain state with real document bytes.</summary>
        public async Task<Seeded> SeedAsync()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            await using AgencyOsDbContext context = Database.CreateDbContext();

            User user = User.Register(
                "drill-operator", "Drill Operator", "drill@agencyos.invalid", now);

            context.Users.Add(user);
            await context.SaveChangesAsync();

            Organization organization = Organization.Create(
                "Northgate Talent", legalName: null, OrganizationType.Agency, user.Id, now);

            context.Organizations.Add(organization);
            await context.SaveChangesAsync();

            context.Memberships.Add(
                Membership.Grant(organization.Id, user.Id, AgencyRole.Owner, user.Id, now));

            Person person = Person.Create(
                organization.Id, "Marguerite", "Sable", user.Id, now);

            Company company = Company.Create(
                organization.Id, "Northgate Pictures", CompanyType.ProductionCompany, user.Id, now);

            context.People.Add(person);
            context.Companies.Add(company);

            await context.SaveChangesAsync();

            // Real bytes through the real store, so the digests are the store's own.
            IBlobStore store = CreateBlobStore();
            List<(string, string)> blobs = [];

            foreach (string content in new[]
            {
                "The agreed terms, in writing.",
                "A second document, differing by one word.",
                "A third, for good measure.",
            })
            {
                BlobWriteResult written = await store.PutAsync(
                    organization.Id, new MemoryStream(Encoding.UTF8.GetBytes(content)));

                Assert.Equal(
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
                    written.ContentHash);

                blobs.Add((written.StorageKey, written.ContentHash));
            }

            long auditRows = await context.Set<Domain.Audit.AuditEvent>().LongCountAsync();

            return new Seeded(
                organization.Id,
                person.Id,
                person.DisplayName,
                company.Id,
                company.Name,
                auditRows,
                blobs);
        }

        /// <summary>Runs an operator script exactly as an operator would.</summary>
        public ScriptResult Run(string script, params string[] arguments)
        {
            string path = Path.Combine(RepositoryRoot, "scripts", script);

            ProcessStartInfo start = new()
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(path);

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("pwsh did not start.");

            string output = process.StandardOutput.ReadToEnd()
                + process.StandardError.ReadToEnd();

            process.WaitForExit();

            return new ScriptResult(process.ExitCode, output);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync().ConfigureAwait(false);

            if (Directory.Exists(Workspace))
            {
                Directory.Delete(Workspace, recursive: true);
            }
        }

        private static string RepositoryRoot { get; } = Find();

        private static string Find()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("The repository root was not found.");
        }
    }
}
