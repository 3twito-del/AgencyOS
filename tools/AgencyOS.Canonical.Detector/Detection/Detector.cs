using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgencyOS.Canonical.Detector.Git;

namespace AgencyOS.Canonical.Detector.Detection;

/// <summary>Where the observed commit comes from.</summary>
internal static class ObservedSources
{
    public const string ExplicitSha = "EXPLICIT_SHA";
    public const string LocalHead = "LOCAL_HEAD";
    public const string RemoteTracking = "REMOTE_TRACKING";

    /// <summary>Command-line spellings of the two convenience sources.</summary>
    public const string LocalHeadToken = "local-head";
    public const string RemoteTrackingToken = "remote-tracking";
}

internal sealed record DetectorRequest(string Branch, string Baseline, string Observed, string DetectedUtc);

/// <summary>
/// Observes <c>baseline..observed</c> and reports what changed, where, and which
/// authority must look at it. It never says what a change means.
/// </summary>
/// <remarks>
/// <para>
/// The boundary is the protocol's (CANONICAL-STATE-PROTOCOL.md sections 3 and 10):
/// the detector may establish machine-verifiable facts and ask semantic questions. It
/// may not answer one. Its output is <c>OBSERVED</c> evidence, not a candidate delta: it
/// allocates no Delta ID, it writes nothing, and it never calls the publisher.
/// </para>
/// <para>
/// A fact it cannot establish is <c>UNKNOWN</c>, never <c>false</c> and never an empty
/// list (CLAUDE.md, operator-context coherence).
/// </para>
/// </remarks>
internal sealed partial class Detector
{
    private readonly IGitReader _git;

    public Detector(IGitReader git)
    {
        _git = git;
    }

    public object Detect(DetectorRequest request)
    {
        try
        {
            return DetectCore(request);
        }
        catch (DetectorFailureException failure)
        {
            return failure.ToFailure(request);
        }
    }

    private object DetectCore(DetectorRequest request)
    {
        GitResult workTree = _git.Run("rev-parse", "--is-inside-work-tree");

        if (!workTree.Succeeded || workTree.Text.Trim() != "true")
        {
            throw Fail(FailureClasses.UnsupportedRepositoryState, "The path is not inside a git work tree.");
        }

        if (!FullSha().IsMatch(request.Baseline))
        {
            throw Fail(FailureClasses.InvalidInput, "The baseline must be a full 40-character lowercase commit SHA.");
        }

        string shallow = ReadShallow();
        string baseline = ResolveCommit(request.Baseline, FailureClasses.UnknownBaselineCommit, "baseline");
        (string observed, string resolvedFrom) = ResolveObserved(request);
        DetectorRange range = new(request.Branch, baseline, observed, resolvedFrom);
        RefEvidence refs = ReadRefs(request.Branch, shallow);

        if (baseline == observed)
        {
            return new DetectionReport(
                Results.NoRelevantChange,
                ChangeKinds.NoGitChange,
                request.DetectedUtc,
                range,
                KnownList<CommitInfo>.Of([]),
                KnownList<ChangedPath>.Of([]),
                new SortedDictionary<string, string>(StringComparer.Ordinal),
                refs,
                KnownList<TagAtCommit>.Of([]),
                null,
                null,
                [$"The baseline and the observed commit are the same commit, {baseline}."],
                [],
                Authority.MachineVerifiableFactOnly,
                [],
                [],
                Limitations(shallow, tagsKnown: true));
        }

        GitResult ancestry = _git.Run("merge-base", "--is-ancestor", baseline, observed);

        if (ancestry.ExitCode == 1)
        {
            GitResult reverse = _git.Run("merge-base", "--is-ancestor", observed, baseline);
            GitResult mergeBase = _git.Run("merge-base", baseline, observed);

            throw Fail(
                FailureClasses.ObservedNotDescendant,
                "The observed commit does not descend from the baseline. The detector compares only a baseline with its descendants.",
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["baseline_sha"] = baseline,
                    ["observed_sha"] = observed,
                    ["observed_descends_from_baseline"] = Facts.No,
                    ["baseline_descends_from_observed"] = reverse.ExitCode switch
                    {
                        0 => Facts.Yes,
                        1 => Facts.No,
                        _ => Facts.Unknown,
                    },
                    ["merge_base"] = mergeBase.Succeeded ? mergeBase.Text.Trim() : Facts.Unknown,
                });
        }

        if (!ancestry.Succeeded)
        {
            throw Fail(FailureClasses.ReadFailure, "git could not establish the ancestry of the two commits.");
        }

        KnownList<CommitInfo> commits = ReadCommits(baseline, observed);
        KnownList<ChangedPath> paths = ReadChangedPaths(baseline, observed);
        KnownList<TagAtCommit> tags = ReadTags(commits);
        CanonicalSnapshot before = ReadSnapshot(baseline);
        CanonicalSnapshot after = ReadSnapshot(observed);

        IReadOnlyList<ChangedPath> changed = paths.Items;
        bool relevant = changed.Any(x => x.CanonicalRelevant);
        IReadOnlyDictionary<string, string> flags = Flags(changed);
        List<OwnerTrigger> triggers = OwnerTriggers(changed);
        List<MechanicalConflict> conflicts = Conflicts(changed, before, after);

        return new DetectionReport(
            relevant ? Results.ReviewRequired : Results.NoRelevantChange,
            relevant ? ChangeKinds.CanonicalRelevant : ChangeKinds.NotCanonicalRelevant,
            request.DetectedUtc,
            range,
            commits,
            paths,
            flags,
            refs,
            tags,
            before,
            after,
            Observations(range, commits, changed, flags, tags),
            relevant ? SemanticQuestions(changed, flags) : [],
            relevant ? Authority.ControlRoomAdjudicationRequired : Authority.MachineVerifiableFactOnly,
            relevant ? triggers : [],
            conflicts,
            Limitations(shallow, tags.Known));
    }

    private string ReadShallow()
    {
        GitResult shallow = _git.Run("rev-parse", "--is-shallow-repository");

        return shallow.Succeeded
            ? Facts.YesNo(shallow.Text.Trim() == "true")
            : Facts.Unknown;
    }

    private string ResolveCommit(string sha, string failureClass, string role)
    {
        GitResult resolved = _git.Run("rev-parse", "--verify", "--quiet", sha + "^{commit}");

        if (!resolved.Succeeded)
        {
            throw Fail(failureClass, $"The {role} commit {sha} is not present in this repository.");
        }

        return resolved.Text.Trim();
    }

    private (string Sha, string ResolvedFrom) ResolveObserved(DetectorRequest request)
    {
        if (request.Observed == ObservedSources.LocalHeadToken)
        {
            GitResult status = _git.Run("status", "--porcelain", "--untracked-files=normal");

            if (!status.Succeeded)
            {
                throw Fail(FailureClasses.ReadFailure, "git could not report the working tree status.");
            }

            if (status.Text.Length != 0)
            {
                throw Fail(
                    FailureClasses.DirtyTreeForLocalHead,
                    "local-head needs a clean working tree, so that the observed commit is what the checkout contains. Pass an explicit SHA instead.");
            }

            GitResult branch = _git.Run("symbolic-ref", "--short", "-q", "HEAD");

            if (!branch.Succeeded || branch.Text.Trim() != request.Branch)
            {
                throw Fail(
                    FailureClasses.UnsupportedRepositoryState,
                    $"local-head needs the checkout to be on {request.Branch}.");
            }

            return (ResolveRef("refs/heads/" + request.Branch), ObservedSources.LocalHead);
        }

        if (request.Observed == ObservedSources.RemoteTrackingToken)
        {
            return (ResolveRef("refs/remotes/origin/" + request.Branch), ObservedSources.RemoteTracking);
        }

        if (!FullSha().IsMatch(request.Observed))
        {
            throw Fail(
                FailureClasses.InvalidInput,
                "The observed commit must be a full 40-character lowercase SHA, local-head or remote-tracking.");
        }

        return (ResolveCommit(request.Observed, FailureClasses.UnknownObservedCommit, "observed"), ObservedSources.ExplicitSha);
    }

    private string ResolveRef(string name)
    {
        GitResult resolved = _git.Run("rev-parse", "--verify", "--quiet", name + "^{commit}");

        if (!resolved.Succeeded)
        {
            throw Fail(FailureClasses.UnknownObservedCommit, $"{name} is not present in this repository.");
        }

        return resolved.Text.Trim();
    }

    private RefEvidence ReadRefs(string branch, string shallow)
    {
        GitResult current = _git.Run("symbolic-ref", "--short", "-q", "HEAD");

        string currentBranch = current.ExitCode switch
        {
            0 => current.Text.Trim(),
            1 => Facts.Detached,
            _ => Facts.Unknown,
        };

        return new RefEvidence(
            currentBranch,
            OptionalRef("refs/heads/" + branch),
            OptionalRef("refs/remotes/origin/" + branch),
            shallow);
    }

    private string OptionalRef(string name)
    {
        GitResult resolved = _git.Run("rev-parse", "--verify", "--quiet", name + "^{commit}");

        return resolved.ExitCode switch
        {
            0 => resolved.Text.Trim(),
            1 => Facts.NotPresentLocally,
            _ => Facts.Unknown,
        };
    }

    private KnownList<CommitInfo> ReadCommits(string baseline, string observed)
    {
        GitResult list = _git.Run(
            "rev-list", "--reverse", "--topo-order", "--no-commit-header",
            "--format=%H%x1f%P%x1f%s", baseline + ".." + observed);

        if (!list.Succeeded)
        {
            throw Fail(FailureClasses.ReadFailure, "git could not list the commits in the range.");
        }

        List<CommitInfo> commits = [];

        foreach (string line in list.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\x1f');

            if (parts.Length != 3)
            {
                throw Fail(FailureClasses.ReadFailure, "git returned a commit line the detector cannot parse.");
            }

            commits.Add(new CommitInfo(
                parts[0],
                parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                parts[2]));
        }

        return KnownList<CommitInfo>.Of(commits);
    }

    private KnownList<ChangedPath> ReadChangedPaths(string baseline, string observed)
    {
        GitResult diff = _git.Run(
            "diff", "--name-status", "-z", "--no-renames", "--no-ext-diff", "--no-textconv",
            baseline, observed, "--");

        if (!diff.Succeeded)
        {
            throw Fail(FailureClasses.ReadFailure, "git could not list the changed paths.");
        }

        string[] fields = diff.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length % 2 != 0)
        {
            throw Fail(FailureClasses.ReadFailure, "git returned a path list the detector cannot parse.");
        }

        List<ChangedPath> paths = [];

        for (int i = 0; i < fields.Length; i += 2)
        {
            string change = fields[i] switch
            {
                "A" => "added",
                "M" => "modified",
                "D" => "deleted",
                "T" => "type_changed",
                _ => throw Fail(FailureClasses.ReadFailure, $"git reported a change type the detector does not know: {fields[i]}."),
            };

            IReadOnlyList<string> categories = PathClassifier.Classify(fields[i + 1]);
            paths.Add(new ChangedPath(fields[i + 1], change, categories, PathClassifier.IsCanonicalRelevant(categories)));
        }

        paths.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return KnownList<ChangedPath>.Of(paths);
    }

    private KnownList<TagAtCommit> ReadTags(KnownList<CommitInfo> commits)
    {
        GitResult refs = _git.Run(
            "for-each-ref", "refs/tags",
            "--format=%(refname:strip=2)%1f%(objecttype)%1f%(objectname)%1f%(*objectname)");

        if (!refs.Succeeded)
        {
            return KnownList<TagAtCommit>.UnknownBecause("git could not list the local tags.");
        }

        HashSet<string> inRange = commits.Items.Select(x => x.Sha).ToHashSet(StringComparer.Ordinal);
        List<TagAtCommit> tags = [];

        foreach (string line in refs.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\x1f');

            if (parts.Length != 4)
            {
                return KnownList<TagAtCommit>.UnknownBecause("git returned a tag line the detector cannot parse.");
            }

            string commit = parts[1] == "tag" ? parts[3] : parts[2];

            if (inRange.Contains(commit))
            {
                tags.Add(new TagAtCommit(parts[0], parts[1] == "tag" ? "annotated" : "lightweight", commit));
            }
        }

        tags.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return KnownList<TagAtCommit>.Of(tags);
    }

    private CanonicalSnapshot ReadSnapshot(string commit)
    {
        string presence = Presence(commit, PathClassifier.CurrentStatePath);
        string blobSha = Facts.Unknown;
        string statusLine = Facts.Unknown;
        string latestDelta = Facts.Unknown;

        if (presence == Facts.Present)
        {
            GitResult blob = _git.Run("cat-file", "blob", commit + ":" + PathClassifier.CurrentStatePath);

            if (blob.Succeeded)
            {
                blobSha = Convert.ToHexStringLower(SHA256.HashData(blob.Output));
                statusLine = FirstLineStartingWith(blob.Text, "**Status:**") ?? Facts.NotPresent;
                latestDelta = FirstLineUnderHeading(blob.Text, "## Latest published delta")
                    ?? FirstLineUnderHeading(blob.Text, "## Latest accepted delta")
                    ?? Facts.NotPresent;
            }
        }
        else if (presence == Facts.NotPresent)
        {
            blobSha = Facts.NotPresent;
            statusLine = Facts.NotPresent;
            latestDelta = Facts.NotPresent;
        }

        return new CanonicalSnapshot(
            commit,
            presence,
            blobSha,
            statusLine,
            latestDelta,
            ReadLedger(commit),
            ReadStaging(commit));
    }

    private string Presence(string commit, string path)
    {
        GitResult entry = _git.Run("ls-tree", commit, "--", path);

        if (!entry.Succeeded)
        {
            return Facts.Unknown;
        }

        return entry.Text.Length == 0 ? Facts.NotPresent : Facts.Present;
    }

    private KnownList<LedgerStatus> ReadLedger(string commit)
    {
        string presence = Presence(commit, PathClassifier.DeltaLedgerPath);

        if (presence == Facts.NotPresent)
        {
            return KnownList<LedgerStatus>.Of([]);
        }

        GitResult blob = presence == Facts.Present
            ? _git.Run("cat-file", "blob", commit + ":" + PathClassifier.DeltaLedgerPath)
            : new GitResult(-1, [], string.Empty);

        if (!blob.Succeeded)
        {
            return KnownList<LedgerStatus>.UnknownBecause("The delta ledger could not be read at this commit.");
        }

        List<LedgerStatus> statuses = [];
        bool inEntries = false;
        string? current = null;

        foreach (string raw in blob.Text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line == "# Entries")
            {
                inEntries = true;
                continue;
            }

            if (!inEntries)
            {
                continue;
            }

            if (line.StartsWith("## DELTA-", StringComparison.Ordinal))
            {
                current = line[3..].Trim();
                continue;
            }

            if (current is not null && line.StartsWith("Status:", StringComparison.Ordinal))
            {
                statuses.Add(new LedgerStatus(current, line["Status:".Length..].Trim()));
                current = null;
            }
        }

        statuses.Sort((a, b) => string.CompareOrdinal(a.DeltaId, b.DeltaId));
        return KnownList<LedgerStatus>.Of(statuses);
    }

    private KnownList<string> ReadStaging(string commit)
    {
        GitResult entries = _git.Run("ls-tree", "--name-only", commit, "--", PathClassifier.StagingPrefix);

        if (!entries.Succeeded)
        {
            return KnownList<string>.UnknownBecause("The staging directory could not be listed at this commit.");
        }

        List<string> names = [.. entries.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        names.Sort(StringComparer.Ordinal);
        return KnownList<string>.Of(names);
    }

    private static string? FirstLineStartingWith(string text, string prefix) =>
        text.Split('\n')
            .Select(x => x.TrimEnd('\r'))
            .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));

    private static string? FirstLineUnderHeading(string text, string heading)
    {
        string[] lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r') != heading)
            {
                continue;
            }

            for (int j = i + 1; j < lines.Length; j++)
            {
                string line = lines[j].TrimEnd('\r');

                if (line.StartsWith('#'))
                {
                    return null;
                }

                if (line.Length != 0)
                {
                    return line;
                }
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> Flags(IReadOnlyList<ChangedPath> paths)
    {
        bool Any(string category) => paths.Any(x => x.Categories.Contains(category));

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["adr_changed"] = Facts.YesNo(Any(PathClassifier.Adr)),
            ["all_changed_paths_under_docs"] = Facts.YesNo(paths.All(x => x.Path.StartsWith("docs/", StringComparison.Ordinal))),
            ["api_contract_source_changed"] = Facts.YesNo(Any(PathClassifier.ApiContract)),
            ["build_configuration_changed"] = Facts.YesNo(Any(PathClassifier.BuildConfiguration)),
            ["canonical_artifact_changed"] = Facts.YesNo(Any(PathClassifier.ControlRoom)),
            ["ci_changed"] = Facts.YesNo(Any(PathClassifier.ContinuousIntegration)),
            ["closure_record_changed"] = Facts.YesNo(Any(PathClassifier.ClosureRecord)),
            ["configuration_changed"] = Facts.YesNo(Any(PathClassifier.Configuration)),
            ["control_room_staging_changed"] = Facts.YesNo(Any(PathClassifier.ControlRoomStaging)),
            ["domain_source_changed"] = Facts.YesNo(Any(PathClassifier.Domain)),
            ["formal_specification_changed"] = Facts.YesNo(Any(PathClassifier.FormalSpecification)),
            ["governance_changed"] = Facts.YesNo(Any(PathClassifier.Governance)),
            ["product_code_changed"] = Facts.YesNo(Any(PathClassifier.ProductSource)),
            ["release_record_changed"] = Facts.YesNo(Any(PathClassifier.ReleaseRecord)),
            ["review_record_changed"] = Facts.YesNo(Any(PathClassifier.ReviewRecord)),
            ["schema_migration_changed"] = Facts.YesNo(Any(PathClassifier.SchemaMigration)),
            ["scripts_changed"] = Facts.YesNo(Any(PathClassifier.Scripts)),
            ["tests_changed"] = Facts.YesNo(Any(PathClassifier.Tests)),
            ["tooling_changed"] = Facts.YesNo(Any(PathClassifier.Tooling)),
            ["unclassified_changed"] = Facts.YesNo(Any(PathClassifier.Unclassified)),
        };
    }

    /// <summary>
    /// Mechanical reasons an Owner decision might be needed. The detector does not decide
    /// that one is: it names the trigger and the paths, and the Control Room decides.
    /// </summary>
    private static List<OwnerTrigger> OwnerTriggers(IReadOnlyList<ChangedPath> paths)
    {
        List<OwnerTrigger> triggers = [];

        void Add(string trigger, Func<ChangedPath, bool> match)
        {
            List<string> hits = [.. paths.Where(match).Select(x => x.Path)];

            if (hits.Count != 0)
            {
                triggers.Add(new OwnerTrigger(trigger, hits));
            }
        }

        Add("product source changed (src/)", x => x.Categories.Contains(PathClassifier.ProductSource));
        Add("schema migration changed", x => x.Categories.Contains(PathClassifier.SchemaMigration));
        Add("API contract source changed", x => x.Categories.Contains(PathClassifier.ApiContract));
        Add("domain source changed", x => x.Categories.Contains(PathClassifier.Domain));
        Add("pinned technology or build configuration changed",
            x => x.Categories.Contains(PathClassifier.BuildConfiguration) || x.Categories.Contains(PathClassifier.Configuration));
        Add("decision ledger changed", x => x.Path == PathClassifier.DecisionLedgerPath);
        Add("closure record changed", x => x.Categories.Contains(PathClassifier.ClosureRecord));
        Add("release record changed", x => x.Categories.Contains(PathClassifier.ReleaseRecord));
        Add("governance file changed", x => x.Categories.Contains(PathClassifier.Governance));

        return triggers;
    }

    private static readonly Dictionary<string, int> StatusOrder = new(StringComparer.Ordinal)
    {
        ["PENDING_ADJUDICATION"] = 0,
        ["ACCEPTED"] = 1,
        ["REJECTED"] = 1,
        ["PUBLISHED"] = 2,
    };

    /// <summary>
    /// Disagreements a program can see between the ledger's rules and the repository.
    /// Each is a question for the Control Room, not a finding.
    /// </summary>
    private static List<MechanicalConflict> Conflicts(
        IReadOnlyList<ChangedPath> paths, CanonicalSnapshot before, CanonicalSnapshot after)
    {
        List<MechanicalConflict> conflicts = [];
        bool currentStateChanged = paths.Any(x => x.Path == PathClassifier.CurrentStatePath);
        bool ledgerChanged = paths.Any(x => x.Path == PathClassifier.DeltaLedgerPath);

        if (currentStateChanged && !ledgerChanged)
        {
            conflicts.Add(new MechanicalConflict(
                "CURRENT_STATE_CHANGED_WITHOUT_LEDGER_CHANGE",
                "CURRENT-STATE.md changed in this range and CANONICAL-DELTAS.md did not. That is expected for a descriptive correction and unexpected for a state transition; which it is needs adjudication."));
        }

        if (before.LedgerStatuses.Known && after.LedgerStatuses.Known)
        {
            Dictionary<string, string> later = after.LedgerStatuses.Items.ToDictionary(x => x.DeltaId, x => x.Status, StringComparer.Ordinal);

            foreach (LedgerStatus earlier in before.LedgerStatuses.Items)
            {
                if (!later.TryGetValue(earlier.DeltaId, out string? status))
                {
                    conflicts.Add(new MechanicalConflict(
                        "LEDGER_ENTRY_REMOVED",
                        $"{earlier.DeltaId} is in the ledger at the baseline and not at the observed commit. The ledger is append-only."));
                }
                else if (StatusOrder.TryGetValue(earlier.Status, out int from)
                    && StatusOrder.TryGetValue(status, out int to)
                    && (to < from || (to == from && earlier.Status != status)))
                {
                    conflicts.Add(new MechanicalConflict(
                        "LEDGER_STATUS_NOT_FORWARD",
                        $"{earlier.DeltaId} changed status between the baseline and the observed commit in a direction the ledger's lifecycle does not permit. Both statuses are quoted under repository_evidence."));
                }
            }
        }

        return conflicts;
    }

    private static List<string> Observations(
        DetectorRange range,
        KnownList<CommitInfo> commits,
        IReadOnlyList<ChangedPath> paths,
        IReadOnlyDictionary<string, string> flags,
        KnownList<TagAtCommit> tags)
    {
        List<string> observations =
        [
            $"{range.ObservedSha} descends from {range.BaselineSha}.",
            $"{commits.Items.Count} commit(s) are in the range.",
            $"{paths.Count} path(s) changed: {paths.Count(x => x.Change == "added")} added, {paths.Count(x => x.Change == "modified")} modified, {paths.Count(x => x.Change == "deleted")} deleted, {paths.Count(x => x.Change == "type_changed")} type-changed.",
            $"Every changed path is under docs/: {flags["all_changed_paths_under_docs"]}.",
            $"Product source (src/) changed: {flags["product_code_changed"]}.",
            $"A Control Room artifact changed: {flags["canonical_artifact_changed"]}.",
            $"{paths.Count(x => x.CanonicalRelevant)} changed path(s) are canonical-relevant by the path rule.",
        ];

        if (tags.Known)
        {
            observations.Add(tags.Items.Count == 0
                ? "No local tag points at a commit in the range."
                : $"Local tags pointing at commits in the range: {string.Join(", ", tags.Items.Select(x => $"{x.Name} -> {x.Commit}"))}.");
        }

        return observations;
    }

    private static List<string> SemanticQuestions(IReadOnlyList<ChangedPath> paths, IReadOnlyDictionary<string, string> flags)
    {
        List<string> questions = [];

        string PathsIn(string category) => string.Join(", ", paths.Where(x => x.Categories.Contains(category)).Select(x => x.Path));

        if (flags["canonical_artifact_changed"] == Facts.Yes)
        {
            questions.Add($"Do the changes to {PathsIn(PathClassifier.ControlRoom)} alter any canonical claim, and if so, which delta and which authority carry that change?");
        }

        if (paths.Any(x => x.Path == PathClassifier.CurrentStatePath))
        {
            questions.Add("Does the change to CURRENT-STATE.md complete a delta under the publication protocol, or is it a descriptive correction?");
        }

        if (flags["control_room_staging_changed"] == Facts.Yes)
        {
            questions.Add($"Do the staging changes at {PathsIn(PathClassifier.ControlRoomStaging)} match the expected lifecycle of an accepted transition?");
        }

        if (flags["product_code_changed"] == Facts.Yes)
        {
            questions.Add($"Are the product source changes ({PathsIn(PathClassifier.ProductSource)}) within an authorized scope, and do they bear on the released product identity recorded in CURRENT-STATE.md?");
        }

        if (flags["tests_changed"] == Facts.Yes)
        {
            questions.Add($"Do the test changes ({PathsIn(PathClassifier.Tests)}) alter evidence that a canonical record relies on?");
        }

        if (flags["ci_changed"] == Facts.Yes || flags["scripts_changed"] == Facts.Yes
            || flags["tooling_changed"] == Facts.Yes || flags["build_configuration_changed"] == Facts.Yes
            || flags["configuration_changed"] == Facts.Yes || flags["formal_specification_changed"] == Facts.Yes)
        {
            questions.Add("Do the CI, script, tooling, build, configuration or specification changes alter a gate or a pinned baseline that canonical evidence relies on?");
        }

        if (flags["release_record_changed"] == Facts.Yes || flags["review_record_changed"] == Facts.Yes || flags["adr_changed"] == Facts.Yes)
        {
            questions.Add("Do the changes to release, review, closure or ADR records alter the specialised authority those records carry?");
        }

        if (flags["governance_changed"] == Facts.Yes)
        {
            questions.Add($"Do the governance changes ({PathsIn(PathClassifier.Governance)}) alter an authority boundary?");
        }

        if (flags["unclassified_changed"] == Facts.Yes)
        {
            questions.Add($"What governs the paths the detector could not classify ({PathsIn(PathClassifier.Unclassified)})?");
        }

        return questions;
    }

    private static List<string> Limitations(string shallow, bool tagsKnown)
    {
        List<string> limitations =
        [
            "CI and Nightly results were not collected. V1 reads the local repository only, so their state is UNKNOWN.",
            "Remote-tracking refs are as of the last fetch. The detector does not fetch.",
            "Tag movement is UNKNOWN: the detector has no earlier snapshot of the tags. Tags pointing at commits in the range are listed.",
            "Path categories are a path rule. They say where a change landed, not what it means.",
        ];

        if (!tagsKnown)
        {
            limitations.Add("The local tags could not be read, so the tags at commits in the range are UNKNOWN.");
        }

        if (shallow == Facts.Yes)
        {
            limitations.Add("The repository is shallow. History outside the fetched depth is unavailable.");
        }
        else if (shallow == Facts.Unknown)
        {
            limitations.Add("Whether the repository is shallow is UNKNOWN.");
        }

        return limitations;
    }

    private static DetectorFailureException Fail(
        string failureClass, string detail, IReadOnlyDictionary<string, string>? facts = null) =>
        new(failureClass, detail, facts ?? new SortedDictionary<string, string>(StringComparer.Ordinal));

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullSha();

    private sealed class DetectorFailureException : Exception
    {
        public DetectorFailureException(string failureClass, string detail, IReadOnlyDictionary<string, string> facts)
            : base(detail)
        {
            FailureClass = failureClass;
            Facts = facts;
        }

        public string FailureClass { get; }

        public IReadOnlyDictionary<string, string> Facts { get; }

        public DetectorFailure ToFailure(DetectorRequest request) =>
            new(FailureClass, Message, request.DetectedUtc, request.Branch, request.Baseline, request.Observed, Facts);
    }
}
