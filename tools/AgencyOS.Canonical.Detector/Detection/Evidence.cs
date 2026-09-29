namespace AgencyOS.Canonical.Detector.Detection;

/// <summary>Values the detector writes when a fact is not a plain value.</summary>
internal static class Facts
{
    public const string Yes = "YES";
    public const string No = "NO";
    public const string Unknown = "UNKNOWN";
    public const string NotPresentLocally = "NOT_PRESENT_LOCALLY";
    public const string Detached = "DETACHED";
    public const string Present = "PRESENT";
    public const string NotPresent = "NOT_PRESENT";

    public static string YesNo(bool value) => value ? Yes : No;
}

/// <summary>
/// A list the detector either established or could not. An unknown list is never
/// written as an empty one: "no tags" and "tags could not be read" are different facts.
/// </summary>
internal sealed record KnownList<T>(bool Known, IReadOnlyList<T> Items, string Reason)
{
    public static KnownList<T> Of(IReadOnlyList<T> items) => new(true, items, string.Empty);

    public static KnownList<T> UnknownBecause(string reason) => new(false, [], reason);
}

internal sealed record CommitInfo(string Sha, IReadOnlyList<string> Parents, string Subject);

internal sealed record ChangedPath(string Path, string Change, IReadOnlyList<string> Categories, bool CanonicalRelevant);

internal sealed record TagAtCommit(string Name, string ObjectType, string Commit);

internal sealed record OwnerTrigger(string Trigger, IReadOnlyList<string> Paths);

internal sealed record MechanicalConflict(string Kind, string Detail);

internal sealed record LedgerStatus(string DeltaId, string Status);

/// <summary>What the Control Room artifacts said at one commit, read from git objects.</summary>
internal sealed record CanonicalSnapshot(
    string Commit,
    string CurrentStatePresence,
    string CurrentStateBlobSha256,
    string CurrentStateStatusLine,
    string LatestDeltaLine,
    KnownList<LedgerStatus> LedgerStatuses,
    KnownList<string> StagingEntries);

internal sealed record RefEvidence(
    string CurrentBranch,
    string LocalBranchHead,
    string RemoteTrackingHead,
    string ShallowRepository);

/// <summary>The request, after convenience values have been resolved to SHAs.</summary>
internal sealed record DetectorRange(string Branch, string BaselineSha, string ObservedSha, string ObservedResolvedFrom);

/// <summary>A successful observation: no relevant change, or a change for review.</summary>
internal sealed record DetectionReport(
    string Result,
    string ChangeKind,
    string DetectedUtc,
    DetectorRange Range,
    KnownList<CommitInfo> Commits,
    KnownList<ChangedPath> ChangedPaths,
    IReadOnlyDictionary<string, string> Flags,
    RefEvidence Refs,
    KnownList<TagAtCommit> TagsAtRangeCommits,
    CanonicalSnapshot? Baseline,
    CanonicalSnapshot? Observed,
    IReadOnlyList<string> MachineVerifiableObservations,
    IReadOnlyList<string> SemanticQuestions,
    string AuthorityClassification,
    IReadOnlyList<OwnerTrigger> OwnerTriggers,
    IReadOnlyList<MechanicalConflict> Conflicts,
    IReadOnlyList<string> EvidenceLimitations)
{
    public string ObservationId => $"OBS-{Range.BaselineSha[..12]}-{Range.ObservedSha[..12]}";
}

/// <summary>The detector could not make an observation. Nothing was changed.</summary>
internal sealed record DetectorFailure(
    string FailureClass,
    string Detail,
    string DetectedUtc,
    string Branch,
    string BaselineInput,
    string ObservedInput,
    IReadOnlyDictionary<string, string> Facts);

internal static class Results
{
    public const string NoRelevantChange = "NO_RELEVANT_CHANGE";
    public const string ReviewRequired = "REVIEW_REQUIRED";
    public const string DetectorFailure = "DETECTOR_FAILURE";
}

internal static class ChangeKinds
{
    public const string NoGitChange = "NO_GIT_CHANGE";
    public const string NotCanonicalRelevant = "GIT_CHANGE_NOT_CANONICAL_RELEVANT";
    public const string CanonicalRelevant = "CANONICAL_RELEVANT_CHANGE";
}

internal static class Authority
{
    public const string MachineVerifiableFactOnly = "MACHINE_VERIFIABLE_FACT_ONLY";
    public const string ControlRoomAdjudicationRequired = "CONTROL_ROOM_ADJUDICATION_REQUIRED";
}

/// <summary>
/// The detector's only lifecycle claim about its own output.
/// </summary>
/// <remarks>
/// Raw detector evidence is <c>OBSERVED</c> (protocol section 2). It is not a candidate
/// delta: that needs Control Room normalization and a permanent Delta ID (protocol
/// section 6), and the detector does neither.
/// </remarks>
internal static class Lifecycle
{
    public const string Observed = "OBSERVED";
    public const string DeltaIdNotAllocated = "NOT_ALLOCATED";
}

internal static class FailureClasses
{
    public const string InvalidInput = "INVALID_INPUT";
    public const string UnknownBaselineCommit = "UNKNOWN_BASELINE_COMMIT";
    public const string UnknownObservedCommit = "UNKNOWN_OBSERVED_COMMIT";
    public const string ObservedNotDescendant = "OBSERVED_NOT_DESCENDANT";
    public const string DirtyTreeForLocalHead = "DIRTY_TREE_FOR_LOCAL_HEAD";
    public const string ReadFailure = "READ_FAILURE";
    public const string UnsupportedRepositoryState = "UNSUPPORTED_REPOSITORY_STATE";
}
