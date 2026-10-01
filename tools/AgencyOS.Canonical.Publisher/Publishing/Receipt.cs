using System.Text;

namespace AgencyOS.Canonical.Publisher.Publishing;

/// <summary>The contract's failure classes (section 9).</summary>
internal static class FailureClasses
{
    public const string PreconditionDrift = "PRECONDITION_DRIFT";
    public const string AuthorityMissing = "AUTHORITY_MISSING";
    public const string SealAuthorizationInvalid = "SEAL_AUTHORIZATION_INVALID";
    public const string SemanticChangeRequiresNewDelta = "SEMANTIC_CHANGE_REQUIRES_NEW_DELTA";
    public const string ScopeViolation = "SCOPE_VIOLATION";
    public const string ProductBoundaryViolation = "PRODUCT_BOUNDARY_VIOLATION";
    public const string RemoteBasisMismatch = "REMOTE_BASIS_MISMATCH";
    public const string RemoteMovedBeforeSeal = "REMOTE_MOVED_BEFORE_SEAL";
    public const string SubstantiveMutationDuringSeal = "SUBSTANTIVE_MUTATION_DURING_SEAL";
    public const string FinalReadbackMismatch = "FINAL_READBACK_MISMATCH";
}

/// <summary>The contract's result values (section 11).</summary>
internal static class Results
{
    public const string PublishedVerified = "PUBLISHED_VERIFIED";
    public const string BasisVerifiedAwaitingSeal = "BASIS_VERIFIED_AWAITING_SEAL";
    public const string CorrectionVerified = "CORRECTION_VERIFIED";
    public const string StoppedPrecondition = "STOPPED_PRECONDITION";
    public const string StoppedAuthority = "STOPPED_AUTHORITY";
    public const string StoppedScope = "STOPPED_SCOPE";
    public const string FailedSealValidation = "FAILED_SEAL_VALIDATION";
    public const string FailedRemoteVerification = "FAILED_REMOTE_VERIFICATION";

    /// <summary>The section 11 mapping from failure class to result, exactly.</summary>
    public static string For(string failureClass) => failureClass switch
    {
        FailureClasses.PreconditionDrift or FailureClasses.SemanticChangeRequiresNewDelta => StoppedPrecondition,
        FailureClasses.AuthorityMissing or FailureClasses.SealAuthorizationInvalid => StoppedAuthority,
        FailureClasses.ScopeViolation or FailureClasses.ProductBoundaryViolation => StoppedScope,
        FailureClasses.SubstantiveMutationDuringSeal => FailedSealValidation,
        FailureClasses.RemoteBasisMismatch or FailureClasses.RemoteMovedBeforeSeal or FailureClasses.FinalReadbackMismatch => FailedRemoteVerification,
        _ => throw new InvalidOperationException($"'{failureClass}' is not a contract failure class."),
    };
}

/// <summary>A stop. Every failure stops the run, and none is retried.</summary>
internal sealed class PublisherFailure(string failureClass, string detail) : Exception(detail)
{
    public string FailureClass { get; } = failureClass;
}

/// <summary>
/// The Publisher Receipt (contract section 11). It is an execution receipt: evidence for
/// the next cycle, never authority. It is written to standard output only.
/// </summary>
internal sealed class Receipt
{
    public const string None = "None";

    public string DeltaId { get; set; } = None;
    public string Mode { get; set; } = None;
    public string Result { get; set; } = None;
    public string FailureClass { get; set; } = None;
    public string Branch { get; set; } = None;
    public string StartingSha { get; set; } = None;
    public string SemanticBasisSha { get; set; } = None;
    public string FinalPublicationBasisSha { get; set; } = None;
    public string BasisReadbackUtc { get; set; } = None;
    public string StagedCurrentStateSha256 { get; set; } = None;
    public string PayloadSha256 { get; set; } = None;
    public string DeltaSubstantiveSha256 { get; set; } = None;
    public string BindingSealAuthorization { get; set; } = None;
    public List<string> EarlierAuthorizationRecords { get; } = [];
    public string SealingSha { get; set; } = None;
    public string SealReadbackUtc { get; set; } = None;
    public List<string> ChangedPaths { get; } = [];
    public List<string> DecisionReceiptsAffected { get; } = [];
    public string ProductCodeChanged { get; set; } = "NO";
    public List<string> UnresolvedWarnings { get; } = [];
    public string NextRequiredAction { get; set; } = None;

    public void Fail(PublisherFailure failure)
    {
        FailureClass = failure.FailureClass;
        Result = Results.For(failure.FailureClass);
        UnresolvedWarnings.Add(failure.Message);
        NextRequiredAction = "Control Room: inspect this failure and its evidence. The publisher does not retry.";
    }

    public string Render()
    {
        StringBuilder text = new();
        text.Append("AGENCYOS PUBLISHER RECEIPT\n\n");

        void Line(string key, string value) => text.Append(key).Append(": ").Append(value).Append('\n');

        void List(string key, List<string> values)
        {
            if (values.Count == 0)
            {
                Line(key, None);
                return;
            }

            text.Append(key).Append(":\n");

            foreach (string value in values)
            {
                text.Append("  - ").Append(value.Replace("\n", " ", StringComparison.Ordinal)).Append('\n');
            }
        }

        Line("Delta ID", DeltaId);
        Line("Mode", Mode);
        Line("Result", Result);
        Line("Failure class", FailureClass);
        Line("Branch", Branch);
        Line("Starting SHA", StartingSha);
        Line("Semantic-basis SHA", SemanticBasisSha);
        Line("Final publication-basis SHA", FinalPublicationBasisSha);
        Line("Basis readback UTC", BasisReadbackUtc);
        Line("Staged CURRENT-STATE SHA-256", StagedCurrentStateSha256);
        Line("Payload SHA-256", PayloadSha256);
        Line("Delta substantive SHA-256", DeltaSubstantiveSha256);
        Line("Binding seal authorization", BindingSealAuthorization);
        List("Earlier authorization records", EarlierAuthorizationRecords);
        Line("Sealing SHA", SealingSha);
        Line("Seal readback UTC", SealReadbackUtc);
        List("Changed paths", ChangedPaths);
        List("Decision receipts affected", DecisionReceiptsAffected);
        Line("Product code changed", ProductCodeChanged);
        List("Unresolved warnings", UnresolvedWarnings);
        Line("Next required authority/action", NextRequiredAction);
        return text.ToString();
    }
}
