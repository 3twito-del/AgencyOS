using System.Globalization;
using AgencyOS.Canonical.Detector.Detection;

namespace AgencyOS.Canonical.Detector.Output;

/// <summary>
/// Writes the observation evidence packet: the machine-readable half of the detector's
/// output, under the contract <see cref="ContractId"/>.
/// </summary>
/// <remarks>
/// <para>
/// The packet is <c>OBSERVED</c> evidence: an observation, not a candidate delta and not an
/// Accepted Publication Payload. A candidate delta (protocol section 6) needs Control
/// Room normalization and a permanent Delta ID, and the detector supplies neither. So
/// the packet's <c>observation</c> carries a temporary ID and
/// <c>delta_id: NOT_ALLOCATED</c>.
/// </para>
/// <para>
/// It differs from a payload (CANONICAL-PUBLISHER-CONTRACT.md section 3) in two ways: it
/// has a different contract identifier, and it has none of the payload's authority,
/// mode, staging or scope fields.
/// </para>
/// <para>
/// What the repository itself says, such as ledger statuses and the current-state status
/// line, is quoted under <c>repository_evidence</c>, and never restated as the detector's
/// own claim.
/// </para>
/// </remarks>
internal static class PacketWriter
{
    public const string ContractId = "agencyos-canonical-detector/v1";

    public static string Write(object outcome) => outcome switch
    {
        DetectionReport report => CanonicalJson.Serialize(Report(report)),
        DetectorFailure failure => CanonicalJson.Serialize(Failure(failure)),
        _ => throw new InvalidOperationException("Unknown detector outcome."),
    };

    private static JsonObject Report(DetectionReport report)
    {
        bool review = report.Result == Results.ReviewRequired;

        JsonObject packet = new()
        {
            ["contract"] = ContractId,
            ["result"] = report.Result,
            ["change_kind"] = report.ChangeKind,
            ["detected_utc"] = report.DetectedUtc,
            ["range"] = new JsonObject
            {
                ["branch"] = report.Range.Branch,
                ["baseline_sha"] = report.Range.BaselineSha,
                ["observed_sha"] = report.Range.ObservedSha,
                ["observed_resolved_from"] = report.Range.ObservedResolvedFrom,
            },
            ["ancestry"] = new JsonObject
            {
                ["observed_descends_from_baseline"] = Facts.Yes,
            },
            ["commits"] = List(report.Commits, x => new JsonObject
            {
                ["sha"] = x.Sha,
                ["parents"] = x.Parents.Cast<object>().ToList(),
                ["subject"] = x.Subject,
            }),
            ["changed_paths"] = List(report.ChangedPaths, x => new JsonObject
            {
                ["path"] = x.Path,
                ["change"] = x.Change,
                ["categories"] = x.Categories.Cast<object>().ToList(),
                ["canonical_relevant"] = x.CanonicalRelevant,
            }),
            ["flags"] = Map(report.Flags),
            ["product_code_changed"] = report.Flags.TryGetValue("product_code_changed", out string? product) ? product : Facts.No,
            ["canonical_artifact_changed"] = report.Flags.TryGetValue("canonical_artifact_changed", out string? canonical) ? canonical : Facts.No,
            ["refs"] = new JsonObject
            {
                ["current_branch"] = report.Refs.CurrentBranch,
                ["local_branch_head"] = report.Refs.LocalBranchHead,
                ["remote_tracking_head_as_of_last_fetch"] = report.Refs.RemoteTrackingHead,
                ["shallow_repository"] = report.Refs.ShallowRepository,
            },
            ["tags_at_range_commits"] = List(report.TagsAtRangeCommits, x => new JsonObject
            {
                ["name"] = x.Name,
                ["type"] = x.ObjectType,
                ["commit"] = x.Commit,
            }),
            ["tag_movement"] = Facts.Unknown,
            ["ci_state"] = Facts.Unknown,
            ["repository_evidence"] = new JsonObject
            {
                ["baseline"] = Snapshot(report.Baseline),
                ["observed"] = Snapshot(report.Observed),
            },
            ["machine_verifiable_observations"] = report.MachineVerifiableObservations.Cast<object>().ToList(),
            ["semantic_questions"] = report.SemanticQuestions.Cast<object>().ToList(),
            ["authority"] = new JsonObject
            {
                ["classification"] = report.AuthorityClassification,
                ["owner_authority_may_apply"] = report.OwnerTriggers.Count != 0,
                ["owner_triggers"] = report.OwnerTriggers.Select(x => (object)new JsonObject
                {
                    ["trigger"] = x.Trigger,
                    ["paths"] = x.Paths.Cast<object>().ToList(),
                }).ToList(),
                ["recommended_next_authority"] = review ? "CONTROL_ROOM" : "NONE",
            },
            ["conflicts"] = report.Conflicts.Select(x => (object)new JsonObject
            {
                ["kind"] = x.Kind,
                ["detail"] = x.Detail,
            }).ToList(),
            ["evidence_limitations"] = report.EvidenceLimitations.Cast<object>().ToList(),
            ["recommended_control_room_action"] = RecommendedAction(report),
        };

        if (review)
        {
            packet["observation"] = Observation(report);
        }

        return packet;
    }

    /// <summary>
    /// What was observed, and what the Control Room would have to do to make it a candidate
    /// delta. The detector does none of that normalization.
    /// </summary>
    private static JsonObject Observation(DetectionReport report) => new()
    {
        ["observation_id"] = report.ObservationId,
        ["lifecycle_state"] = Lifecycle.Observed,
        ["delta_id"] = Lifecycle.DeltaIdNotAllocated,
        ["handoff"] = "The Control Room inspects this evidence and decides whether a candidate delta is warranted. If it is, the Control Room allocates the permanent Delta ID and completes the protocol section 6 fields; only then does adjudication begin. The detector does none of this.",
        ["observed_scope"] = $"The {report.ChangedPaths.Items.Count} changed path(s) in {report.Range.BaselineSha}..{report.Range.ObservedSha}.",
        ["proposed_new_claim"] = "UNDETERMINED: the detector proposes no claim.",
        ["claimed_transition"] = "NONE: the detector claims no transition.",
        ["evidence_level"] = "source: repository evidence only.",
        ["authority_required"] = report.AuthorityClassification,
        ["questions_for_control_room"] = report.SemanticQuestions.Cast<object>().ToList(),
        ["boundaries"] = new List<object>
        {
            "This observation is not a candidate delta and not an adjudication.",
            "It does not accept, publish or seal anything.",
            "It does not establish that any stage, finding or decision is settled.",
            "It does not infer Owner approval.",
        },
    };

    private static JsonObject Failure(DetectorFailure failure) => new()
    {
        ["contract"] = ContractId,
        ["result"] = Results.DetectorFailure,
        ["failure_class"] = failure.FailureClass,
        ["detail"] = failure.Detail,
        ["detected_utc"] = failure.DetectedUtc,
        ["input"] = new JsonObject
        {
            ["branch"] = failure.Branch,
            ["baseline"] = failure.BaselineInput,
            ["observed"] = failure.ObservedInput,
        },
        ["facts"] = Map(failure.Facts),
        ["repository_mutated"] = false,
        ["recommended_control_room_action"] = "Investigate the detector failure. No observation was made and nothing was changed.",
    };

    private static JsonObject Snapshot(CanonicalSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new JsonObject { ["state"] = "NOT_READ" };
        }

        return new JsonObject
        {
            ["commit"] = snapshot.Commit,
            ["current_state_presence"] = snapshot.CurrentStatePresence,
            ["current_state_blob_sha256"] = snapshot.CurrentStateBlobSha256,
            ["current_state_status_line"] = snapshot.CurrentStateStatusLine,
            ["current_state_latest_delta_line"] = snapshot.LatestDeltaLine,
            ["ledger_statuses"] = List(snapshot.LedgerStatuses, x => new JsonObject
            {
                ["delta_id"] = x.DeltaId,
                ["status"] = x.Status,
            }),
            ["staging_entries"] = List(snapshot.StagingEntries, x => x),
        };
    }

    internal static string CurrentClaim(CanonicalSnapshot? baseline) =>
        baseline is null
            ? Facts.Unknown
            : string.Create(CultureInfo.InvariantCulture,
                $"quoted from the repository: CURRENT-STATE.md at {baseline.Commit[..12]} (blob SHA-256 {Short(baseline.CurrentStateBlobSha256)}): status line \"{baseline.CurrentStateStatusLine}\"; latest delta line \"{baseline.LatestDeltaLine}\".");

    internal static string RecommendedAction(DetectionReport report) =>
        report.Result == Results.ReviewRequired
            ? "Inspect the listed changes and decide whether they warrant a candidate delta, or dismiss this observation."
            : "None. No canonical-relevant change was observed.";

    private static string Short(string value) => value.Length == 64 ? value[..16] + "…" : value;

    private static JsonObject List<T>(KnownList<T> list, Func<T, object> item) =>
        list.Known
            ? new JsonObject { ["state"] = "KNOWN", ["items"] = list.Items.Select(item).ToList() }
            : new JsonObject { ["state"] = Facts.Unknown, ["reason"] = list.Reason };

    private static JsonObject Map(IReadOnlyDictionary<string, string> values)
    {
        JsonObject map = new();

        foreach (KeyValuePair<string, string> pair in values)
        {
            map[pair.Key] = pair.Value;
        }

        return map;
    }
}
