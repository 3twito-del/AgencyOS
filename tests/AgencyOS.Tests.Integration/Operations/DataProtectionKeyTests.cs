using AgencyOS.Application.Abstractions;
using AgencyOS.Infrastructure.DependencyInjection;
using AgencyOS.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration.Operations;

/// <summary>
/// That a restart does not throw away the keys protecting stored credentials.
/// </summary>
/// <remarks>
/// <para>
/// The Data Protection key ring is production state, and until M15 nothing tested
/// that it behaved like state. It protects exactly one thing —
/// <c>ISecretProtector</c> is used only by Communications, for mailbox credentials
/// and tokens — so the consequence of losing it can be stated precisely rather
/// than dramatically: <strong>stored mailbox credentials stop decrypting and each
/// mailbox must be re-authorized. No canonical business data is affected.</strong>
/// People, deals, contracts, finance, documents and the audit trail do not depend
/// on it (ADR-0039).
/// </para>
/// <para>
/// The failure mode worth testing is silent. Without a configured key path, ASP.NET
/// Core falls back to whatever the host offers, which under a service account with
/// no profile is an in-memory key ring: every restart issues new keys, every stored
/// credential stops decrypting, and nothing in the logs names the cause. M15 makes
/// the API refuse to start in that configuration on a ring that permits real data;
/// this proves the configured path actually works.
/// </para>
/// </remarks>
public sealed class DataProtectionKeyTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "agencyos-keys", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A secret protected before a restart is readable after one.
    /// </summary>
    /// <remarks>
    /// The restart is modelled by disposing the whole service provider and building
    /// a new one against the same directory, which is what a service restart does
    /// to this component.
    /// </remarks>
    [Fact]
    public void KeysSurviveARestart()
    {
        string keyPath = Path.Combine(_workspace, "keys");
        const string Secret = "a mailbox refresh token";

        string ciphertext;

        using (ServiceProvider before = Build(keyPath))
        {
            ciphertext = before.GetRequiredService<ISecretProtector>().Protect(Secret);
        }

        Assert.NotEqual(Secret, ciphertext);

        using ServiceProvider after = Build(keyPath);

        Assert.Equal(Secret, after.GetRequiredService<ISecretProtector>().Unprotect(ciphertext));
    }

    /// <summary>
    /// A different key ring cannot read what the first one wrote.
    /// </summary>
    /// <remarks>
    /// The other half, and the one that says what losing the keys costs. It also
    /// demonstrates why the key ring belongs in the backup: restoring a database
    /// without it returns every mailbox row and no ability to use one.
    /// </remarks>
    [Fact]
    public void ADifferentKeyRingCannotRead()
    {
        string ciphertext;

        using (ServiceProvider original = Build(Path.Combine(_workspace, "original")))
        {
            ciphertext = original.GetRequiredService<ISecretProtector>()
                .Protect("a mailbox refresh token");
        }

        using ServiceProvider replacement = Build(Path.Combine(_workspace, "replacement"));

        Assert.Throws<SecretProtectionException>(
            () => replacement.GetRequiredService<ISecretProtector>().Unprotect(ciphertext));
    }

    /// <summary>
    /// A tampered ciphertext is refused rather than half-read.
    /// </summary>
    [Fact]
    public void ATamperedSecretIsRefused()
    {
        string keyPath = Path.Combine(_workspace, "tamper");

        using ServiceProvider provider = Build(keyPath);
        ISecretProtector protector = provider.GetRequiredService<ISecretProtector>();

        string ciphertext = protector.Protect("a mailbox refresh token");
        string tampered = ciphertext[..^2] + (ciphertext[^1] == 'A' ? "BB" : "AA");

        Assert.Throws<SecretProtectionException>(() => protector.Unprotect(tampered));
    }

    /// <summary>The key ring is written where it was told to write it.</summary>
    /// <remarks>
    /// Asserted because the backup depends on it. A key ring that quietly lived
    /// somewhere else would be excluded from every backup taken, and nobody would
    /// discover that until a restore.
    /// </remarks>
    [Fact]
    public void TheKeyRingIsWrittenToTheConfiguredPath()
    {
        string keyPath = Path.Combine(_workspace, "located");

        using ServiceProvider provider = Build(keyPath);

        provider.GetRequiredService<ISecretProtector>().Protect("a mailbox refresh token");

        Assert.True(Directory.Exists(keyPath));
        Assert.NotEmpty(Directory.GetFiles(keyPath, "*.xml"));
    }

    private ServiceProvider Build(string keyPath)
    {
        ServiceCollection services = new();

        services.AddAgencyOSContentStorage(
            Path.Combine(_workspace, "blobs"), keyPath);

        // Registered here rather than by calling AddAgencyOSInfrastructure, which
        // would need a connection string and bring EF along for a test about key
        // files. This is the same implementation the host resolves.
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddLogging();

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }
}
