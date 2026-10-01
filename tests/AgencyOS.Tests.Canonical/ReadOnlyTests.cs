using AgencyOS.Canonical.Detector.Git;
using Xunit;

namespace AgencyOS.Tests.Canonical;

/// <summary>
/// The detector cannot change a repository: git refuses it anything but read-only
/// subcommands, and a run leaves every byte of the repository, including .git, as it was.
/// </summary>
public sealed class ReadOnlyTests
{
    [Theory]
    [InlineData("commit")]
    [InlineData("push")]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("tag")]
    [InlineData("checkout")]
    [InlineData("switch")]
    [InlineData("reset")]
    [InlineData("add")]
    [InlineData("rm")]
    [InlineData("merge")]
    [InlineData("rebase")]
    [InlineData("update-ref")]
    [InlineData("config")]
    [InlineData("gc")]
    [InlineData("branch")]
    public void WritingSubcommandsAreRefusedBeforeGitRuns(string subcommand)
    {
        Assert.DoesNotContain(subcommand, GitCli.ReadOnlySubcommands);
        Assert.Throws<InvalidOperationException>(() => new GitCli(Path.GetTempPath()).Run(subcommand));
    }

    [Fact]
    public void ARunChangesNoFileInTheRepository()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture
            .Write(DetectorScenarioTests.CurrentState, "Changed.\n")
            .Write("src/AgencyOS.Domain/Deal.cs", "class Deal { int z; }\n")
            .Commit("change");

        SortedDictionary<string, string> before = FixtureSnapshot.Take(fixture.Root);

        fixture.Detect(baseline, observed);
        fixture.Detect(baseline, baseline);
        fixture.Detect(observed, baseline);
        fixture.Detect(baseline, "local-head");
        fixture.Detect(baseline, "remote-tracking");
        fixture.Detect(baseline, observed, new DetectorScenarioTests.FailingGit(new GitCli(fixture.Root), "diff"));

        Assert.Equal(before, FixtureSnapshot.Take(fixture.Root));
    }

    [Fact]
    public void NoCanonicalArtifactIsWrittenDuringARun()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(DetectorScenarioTests.CurrentState, "Changed.\n").Commit("edit");
        string[] artifacts =
        [
            Path.Combine("docs", "control-room", "CURRENT-STATE.md"),
            Path.Combine("docs", "control-room", "CANONICAL-DELTAS.md"),
            Path.Combine("docs", "control-room", "DECISIONS.md"),
        ];

        SortedDictionary<string, string> before = FixtureSnapshot.Take(fixture.Root);
        fixture.Detect(baseline, observed);
        SortedDictionary<string, string> after = FixtureSnapshot.Take(fixture.Root);

        foreach (string artifact in artifacts)
        {
            Assert.Equal(before[artifact], after[artifact]);
        }

        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "docs", "control-room", "pending")));
    }
}
