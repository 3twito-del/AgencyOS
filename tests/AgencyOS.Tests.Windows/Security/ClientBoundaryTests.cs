using Xunit;

namespace AgencyOS.Tests.Windows.Security;

/// <summary>
/// That the Windows client cannot reach a model provider, and holds no key to one.
/// </summary>
/// <remarks>
/// <para>
/// M12 put every provider credential behind the ModelGateway on the server, and
/// M13 introduced device-local execution — a model running on the workstation.
/// The two together invite exactly one mistake: giving the client a provider
/// credential so it can "just call the model itself". A key on a workstation is a
/// key on every workstation, revocable only by rotating it everywhere, and it
/// would let the client reach a provider without a lease, without a policy
/// evaluation, and without an audit record (§45, ADR-0033).
/// </para>
/// <para>
/// The device-local path needs no credential — the model is already on the
/// machine — so there is nothing to trade away by refusing this permanently.
/// </para>
/// <para>
/// These are structural checks over the project graph and its source. They hold
/// whatever the code does at runtime, which is the point: a credential cannot be
/// introduced by a code path nobody exercised in a test.
/// </para>
/// </remarks>
public sealed class ClientBoundaryTests
{
    private static readonly string[] ClientProjects =
        ["AgencyOS.Client", "AgencyOS.Windows", "AgencyOS.Windows.Platform"];

    /// <summary>
    /// The client cannot reference the assemblies that hold credentials or authority.
    /// </summary>
    /// <remarks>
    /// The strongest form of the rule, because it does not depend on anybody
    /// reading it. Infrastructure holds the ModelGateway and the provider
    /// credentials; Application holds the authorization decisions; Domain holds
    /// the invariants. A client that referenced any of them could call a business
    /// rule in-process and reach a different answer than the server would give,
    /// which is the failure CLAUDE.md §1 exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData("AgencyOS.Client")]
    [InlineData("AgencyOS.Windows")]
    [InlineData("AgencyOS.Windows.Platform")]
    public void NoClientProjectReferencesServerAuthority(string project)
    {
        string csproj = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", project, project + ".csproj"));

        string[] forbidden =
            ["AgencyOS.Infrastructure", "AgencyOS.Application", "AgencyOS.Domain", "AgencyOS.Api"];

        foreach (string assembly in forbidden)
        {
            Assert.DoesNotContain(assembly, csproj, StringComparison.Ordinal);
        }
    }

    /// <summary>No client source holds a provider credential.</summary>
    /// <remarks>
    /// The names a credential is given in practice. A match is not proof of a leak
    /// — it may be a comment — but it is a place a reviewer must look, and there
    /// are none today, so failing on the first one costs nothing and catches the
    /// paste that would otherwise ship.
    /// </remarks>
    [Theory]
    [InlineData("AgencyOS.Client")]
    [InlineData("AgencyOS.Windows")]
    [InlineData("AgencyOS.Windows.Platform")]
    public void NoClientSourceNamesAProviderCredential(string project)
    {
        string[] credentials =
        [
            "ApiKey", "api-key", "api_key", "x-api-key",
            "AnthropicKey", "OpenAiKey", "ProviderSecret", "ClientSecret",
        ];

        List<string> found = [];

        foreach (string file in Sources(project))
        {
            // DiagnosticSummary names these words in order to refuse them: its
            // whole purpose is withholding any value that looks like a secret. It
            // is the guard, not a leak, and excluding it by name is narrower than
            // weakening the pattern everywhere else.
            if (Path.GetFileName(file) == "DiagnosticSummary.cs")
            {
                continue;
            }

            string text = File.ReadAllText(file);

            found.AddRange(credentials
                .Where(c => text.Contains(c, StringComparison.OrdinalIgnoreCase))
                .Select(c => $"{Path.GetFileName(file)}: {c}"));
        }

        Assert.True(
            found.Count == 0,
            $"{project} names provider credential material: {string.Join(", ", found)}");
    }

    /// <summary>No client source addresses a model provider directly.</summary>
    /// <remarks>
    /// A client that could reach a provider endpoint would bypass the gateway even
    /// with no credential of its own — a user-supplied key would be enough. The
    /// only model the client may speak to is the one already on the machine, and
    /// that one has no address.
    /// </remarks>
    [Theory]
    [InlineData("AgencyOS.Client")]
    [InlineData("AgencyOS.Windows")]
    [InlineData("AgencyOS.Windows.Platform")]
    public void NoClientSourceAddressesAModelProvider(string project)
    {
        string[] providers =
        [
            "api.openai.com", "api.anthropic.com", "generativelanguage.googleapis.com",
            "openai.azure.com", "api.cohere.ai", "api.mistral.ai",
        ];

        List<string> found = [];

        foreach (string file in Sources(project))
        {
            string text = File.ReadAllText(file);

            found.AddRange(providers
                .Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{Path.GetFileName(file)}: {p}"));
        }

        Assert.True(
            found.Count == 0,
            $"{project} addresses a model provider directly: {string.Join(", ", found)}");
    }

    /// <summary>The scan is looking at real source.</summary>
    [Theory]
    [InlineData("AgencyOS.Client")]
    [InlineData("AgencyOS.Windows")]
    [InlineData("AgencyOS.Windows.Platform")]
    public void TheScanSeesTheRealSource(string project)
    {
        Assert.NotEmpty(Sources(project));
    }

    private static List<string> Sources(string project)
    {
        List<string> files = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", project), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(file);
        }

        return files;
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
