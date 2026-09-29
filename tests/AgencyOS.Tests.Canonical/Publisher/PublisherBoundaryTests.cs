using System.Reflection;
using System.Text;
using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// The publisher's git write boundary, its independence from the detector, and the
/// exact-byte discipline under line-ending conversion.
/// </summary>
public sealed class PublisherBoundaryTests
{
    public static TheoryData<string[]> Refused => new()
    {
        new[] { "push", "--force", "origin", new string('a', 40) + ":refs/heads/main" },
        new[] { "push", "--porcelain", "origin", "+" + new string('a', 40) + ":refs/heads/main" },
        new[] { "push", "--porcelain", "--force-with-lease", "origin", new string('a', 40) + ":refs/heads/main" },
        new[] { "push", "--porcelain", "-f", "origin", new string('a', 40) + ":refs/heads/main" },
        new[] { "push", "--porcelain", "origin", ":refs/heads/main" },
        new[] { "push", "--porcelain", "origin", "HEAD:refs/heads/main" },
        new[] { "push", "--porcelain", "origin", new string('a', 40) + ":refs/tags/v1" },
        new[] { "push", "--mirror", "origin" },
        new[] { "push", "--porcelain", "--delete", "origin", "main" },
        new[] { "commit", "--amend", "-q", "-m", "x" },
        new[] { "commit", "-q", "--allow-empty", "-m", "x" },
        new[] { "add", "-f", "--", "x" },
        new[] { "fetch", "origin", "+refs/heads/main:refs/heads/main" },
        new[] { "fetch", "--prune", "origin" },
        new[] { "reset", "--hard" },
        new[] { "checkout", "main" },
        new[] { "switch", "main" },
        new[] { "rebase", "origin/main" },
        new[] { "merge", "origin/main" },
        new[] { "tag", "v1" },
        new[] { "branch", "-D", "main" },
        new[] { "config", "user.name", "x" },
        new[] { "update-ref", "refs/heads/main", new string('a', 40) },
        new[] { "gc" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void TheGitBoundaryRefusesEveryForceResetRewriteAndConfigShape(string[] arguments)
    {
        Assert.Throws<InvalidOperationException>(() => GitCommandPolicy.Validate(arguments));
    }

    [Fact]
    public void TheGitBoundaryAcceptsOnlyTheShapesTheContractNeeds()
    {
        GitCommandPolicy.Validate(["push", "--porcelain", "origin", new string('a', 40) + ":refs/heads/main"]);
        GitCommandPolicy.Validate(["commit", "-q", "-m", "Publish DELTA-20260102-001"]);
        GitCommandPolicy.Validate(["add", "-A", "--", "."]);
        GitCommandPolicy.Validate(["fetch", "--no-tags", "--quiet", "origin"]);
    }

    [Fact]
    public void ThePublisherAndTheDetectorShareNoAssemblyAndNoProductReference()
    {
        Assembly publisher = typeof(GitCommandPolicy).Assembly;

        Assert.DoesNotContain(publisher.GetReferencedAssemblies(), x => x.Name!.StartsWith("AgencyOS", StringComparison.Ordinal));
        Assert.Empty(publisher.GetExportedTypes());
    }

    [Fact]
    public void DigestsAreOverGitBlobsNotWorkingTreeFilesUnderAutoCrlf()
    {
        using PublisherFixture fixture = new(operatorAutoCrlf: true);
        byte[] workingTree = FixtureSnapshot.Bytes(Path.Combine(fixture.Operator, "docs", "control-room", "CURRENT-STATE.md"));
        PayloadBuilder payload = new(fixture);

        Assert.Contains((byte)'\r', workingTree);
        Assert.NotEqual(payload.Prior, PublisherFixture.Sha256(workingTree));

        string basis = PublisherFlowTests.StageDefault(fixture, payload);
        Receipt receipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Equal(Encoding.UTF8.GetBytes(payload.NextText), fixture.Blob(fixture.RemoteHead(), PublisherFixture.CurrentState));
        Assert.DoesNotContain((byte)'\r', fixture.Blob(fixture.RemoteHead(), PublisherFixture.Deltas));
    }
}
