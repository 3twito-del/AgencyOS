using System.Text;
using AgencyOS.Canonical.Detector.Detection;

namespace AgencyOS.Canonical.Detector.Output;

/// <summary>
/// Writes the human-readable half of the output: a Canonical Alert in the shape of
/// CANONICAL-STATE-PROTOCOL.md section 7, or a plain result line when there is nothing
/// to adjudicate.
/// </summary>
/// <remarks>
/// An alert is a request for the Control Room to inspect and normalize an
/// <c>OBSERVED</c> item. It is never an adjudication (protocol section 7), and it never
/// presents the observation as a candidate delta. It is written only for
/// <c>REVIEW_REQUIRED</c>. A no-change result and a failure are
/// reported as what they are, so that no alert asks anyone to adjudicate nothing.
/// </remarks>
internal static class AlertWriter
{
    public static string Write(object outcome) => outcome switch
    {
        DetectionReport { Result: Results.ReviewRequired } report => Alert(report),
        DetectionReport report => NoChange(report),
        DetectorFailure failure => Failure(failure),
        _ => throw new InvalidOperationException("Unknown detector outcome."),
    };

    private static string Alert(DetectionReport report)
    {
        StringBuilder text = new();
        IReadOnlyList<ChangedPath> paths = report.ChangedPaths.Items;
        string categories = string.Join(", ", paths
            .Where(x => x.CanonicalRelevant)
            .SelectMany(x => x.Categories)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        text.Append("AGENCYOS CANONICAL ALERT\n\n");
        text.Append($"Delta / observation: {report.ObservationId} | Delta ID: {Lifecycle.DeltaIdNotAllocated} | lifecycle state: {Lifecycle.Observed}\n");
        text.Append($"Trigger: {report.Commits.Items.Count} commit(s) in {report.Range.BaselineSha}..{report.Range.ObservedSha} changed {paths.Count} path(s); canonical-relevant categories: {categories}\n");
        text.Append($"Current canonical state: {PacketWriter.CurrentClaim(report.Baseline)}\n");
        text.Append("Candidate change: UNDETERMINED. The detector observed the changes below; it does not state what they mean.\n");
        text.Append("Evidence anchors:\n");
        text.Append($"  - range {report.Range.BaselineSha}..{report.Range.ObservedSha} on {report.Range.Branch} (observed from {report.Range.ObservedResolvedFrom})\n");

        foreach (CommitInfo commit in report.Commits.Items)
        {
            text.Append($"  - commit {commit.Sha} {commit.Subject}\n");
        }

        foreach (ChangedPath path in paths)
        {
            text.Append($"  - {path.Change} {path.Path} [{string.Join(", ", path.Categories)}]\n");
        }

        text.Append("Conflicts:");

        if (report.Conflicts.Count == 0)
        {
            text.Append(" None detected mechanically.\n");
        }
        else
        {
            text.Append('\n');

            foreach (MechanicalConflict conflict in report.Conflicts)
            {
                text.Append($"  - {conflict.Kind}: {conflict.Detail}\n");
            }
        }

        text.Append($"Authority required: {report.AuthorityClassification}\n");

        if (report.OwnerTriggers.Count == 0)
        {
            text.Append("  owner_authority_may_apply: false\n");
        }
        else
        {
            text.Append("  owner_authority_may_apply: true (the Control Room decides whether Owner authority is required)\n");

            foreach (OwnerTrigger trigger in report.OwnerTriggers)
            {
                text.Append($"  - {trigger.Trigger}: {string.Join(", ", trigger.Paths)}\n");
            }
        }

        text.Append($"Product code changed: {report.Flags["product_code_changed"]}\n");
        text.Append($"Recommended Control Room action: {PacketWriter.RecommendedAction(report)}\n");

        if (report.SemanticQuestions.Count != 0)
        {
            text.Append("Questions for the Control Room:\n");

            foreach (string question in report.SemanticQuestions)
            {
                text.Append($"  - {question}\n");
            }
        }

        text.Append("Evidence limitations:\n");

        foreach (string limitation in report.EvidenceLimitations)
        {
            text.Append($"  - {limitation}\n");
        }

        return text.ToString();
    }

    private static string NoChange(DetectionReport report) =>
        $"AGENCYOS DETECTOR RESULT: {Results.NoRelevantChange} ({report.ChangeKind})\n" +
        $"Range: {report.Range.BaselineSha}..{report.Range.ObservedSha} on {report.Range.Branch}\n" +
        $"Changed paths: {report.ChangedPaths.Items.Count}; canonical-relevant by the path rule: 0\n" +
        "No adjudication is requested.\n";

    private static string Failure(DetectorFailure failure) =>
        $"AGENCYOS DETECTOR FAILURE: {failure.FailureClass}\n" +
        $"{failure.Detail}\n" +
        "No observation was made and nothing was changed.\n";
}
