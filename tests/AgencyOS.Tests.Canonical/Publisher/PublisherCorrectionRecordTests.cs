using System.Text;
using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Model;
using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Rewrites the output of one <c>cat-file commit</c> read, as a remote whose commit object
/// did not carry the expected record would. Every call is recorded, in order.
/// </summary>
internal sealed class CommitObjectRewritingGit(IPublisherGit inner, int occurrence, Func<string, string> rewrite) : IPublisherGit
{
    private int _reads;

    public List<string[]> Calls { get; } = [];

    public GitResult Run(params string[] arguments)
    {
        Calls.Add(arguments);
        GitResult result = inner.Run(arguments);

        if (arguments is ["cat-file", "commit", ..] && ++_reads == occurrence)
        {
            return result with { Output = Encoding.UTF8.GetBytes(rewrite(result.Text)) };
        }

        return result;
    }
}

/// <summary>
/// The descriptive-correction commit carries the exact accepted payload as its durable
/// record, verified in the remote commit object (contract section 7.3). H3D-002: the first
/// real correction's commit carried only a generic subject.
/// </summary>
public sealed class PublisherCorrectionRecordTests
{
    /// <summary>The operator's pre-push read of the local commit.</summary>
    private const int LocalRead = 1;

    /// <summary>The P4 read of the commit the remote reports.</summary>
    private const int RemoteRead = 2;

    private static byte[] Payload(PublisherFixture fixture)
    {
        PayloadBuilder payload = PublisherCorrectionTests.Correction(fixture);
        payload.Classes = ["MACHINE_VERIFIABLE_FACT", "CONTROL_ROOM"];
        payload.References = ["DECISION-20260101-001", "Adjudication of DELTA-20260101-001"];
        return payload.Build();
    }

    private static byte[] RemoteCommitObject(PublisherFixture fixture) =>
        Encoding.UTF8.GetBytes(fixture.Git(fixture.Origin, "cat-file", "commit", fixture.RemoteHead()));

    private static string ReplaceLine(string commitObject, string key, string value) =>
        string.Join('\n', commitObject.Split('\n').Select(x => x.StartsWith(key + ": ", StringComparison.Ordinal) ? $"{key}: {value}" : x));

    private static string DropLine(string commitObject, string key) =>
        string.Join('\n', commitObject.Split('\n').Where(x => !x.StartsWith(key + ": ", StringComparison.Ordinal)));

    [Fact]
    public void TheRemoteCorrectionCommitReconstructsTheExactPayload()
    {
        using PublisherFixture fixture = new();
        byte[] input = Payload(fixture);

        Receipt receipt = fixture.Publisher().Correct(input);

        Assert.True(receipt.Result == Results.CorrectionVerified, receipt.Render());
        Payload? recorded = CorrectionRecord.Reconstruct(RemoteCommitObject(fixture), out string reason);
        Assert.True(recorded is not null, reason);
        Assert.Equal(input, recorded.Bytes);
        Assert.Equal(PublisherFixture.Sha256(input), recorded.Sha256);
        Assert.Equal(receipt.PayloadSha256, recorded.Sha256);
        Assert.Equal(Modes.DescriptiveCorrection, recorded.Mode);
        Assert.Equal(["MACHINE_VERIFIABLE_FACT", "CONTROL_ROOM"], recorded.AuthorityClasses);
        Assert.Equal(["DECISION-20260101-001", "Adjudication of DELTA-20260101-001"], recorded.AdjudicationReferences);
    }

    [Fact]
    public void TheMessageIsTheFixedRecordAndNothingElse()
    {
        using PublisherFixture fixture = new();
        byte[] input = Payload(fixture);

        Assert.Equal(Results.CorrectionVerified, fixture.Publisher().Correct(input).Result);

        string message = fixture.Git(fixture.Origin, "log", "-1", "--format=%B", fixture.RemoteHead()).TrimEnd('\n');
        Assert.Equal(
            "Apply a descriptive canonical correction\n\n" +
            "AgencyOS-Correction-Contract: agencyos-canonical-publisher/v1.1\n" +
            $"AgencyOS-Correction-Payload-SHA256: {PublisherFixture.Sha256(input)}\n" +
            $"AgencyOS-Correction-Payload-Base64: {Convert.ToBase64String(input)}",
            message);
    }

    [Fact]
    public void TheCorrectionDiffIsOnlyTheAcceptedReplacement()
    {
        using PublisherFixture fixture = new();

        Assert.Equal(Results.CorrectionVerified, fixture.Publisher().Correct(Payload(fixture)).Result);

        string head = fixture.RemoteHead();
        Assert.Equal($"M\t{PublisherFixture.CurrentState}\n", fixture.Git(fixture.Origin, "diff", "--name-status", fixture.Start, head));
        Assert.Equal(
            PublisherFixture.BaseCurrentState.Replace(PublisherCorrectionTests.Before, PublisherCorrectionTests.After, StringComparison.Ordinal),
            fixture.Text(head, PublisherFixture.CurrentState));
    }

    [Fact]
    public void VerificationReadsTheCommitTheRemoteReportsAfterThePush()
    {
        using PublisherFixture fixture = new();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), occurrence: 0, x => x);

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.Result == Results.CorrectionVerified, receipt.Render());
        int push = git.Calls.FindIndex(x => x[0] == "push");
        int remoteRead = git.Calls.FindLastIndex(x => x is ["cat-file", "commit", ..]);
        Assert.True(push >= 0 && remoteRead > push, "the record must be read again after the push");
        Assert.Equal($"refs/remotes/origin/{PublisherFixture.Branch}", git.Calls[remoteRead][2]);
        Assert.Contains(git.Calls.GetRange(push, remoteRead - push), x => x[0] == "ls-remote");
    }

    [Fact]
    public void ARemoteCommitWithoutTheRecordIsNotVerified()
    {
        using PublisherFixture fixture = new();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), RemoteRead, x => DropLine(x, CorrectionRecord.Base64Key));

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.NotEqual(Results.CorrectionVerified, receipt.Result);
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("not a durable correction record", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedBase64InTheRemoteRecordIsRejected()
    {
        using PublisherFixture fixture = new();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), RemoteRead, x => ReplaceLine(x, CorrectionRecord.Base64Key, "not*base64!"));

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("Base64 is malformed", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordedBytesThatHashDifferentlyAreRejected()
    {
        using PublisherFixture fixture = new();
        byte[] other = Encoding.UTF8.GetBytes("{\"contract\":\"something else\"}");
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), RemoteRead, x => ReplaceLine(x, CorrectionRecord.Base64Key, Convert.ToBase64String(other)));

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("not the recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void AConsistentRecordOfADifferentPayloadIsRejected()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder different = PublisherCorrectionTests.Correction(fixture);
        different.References = ["DECISION-20260101-001"];
        byte[] other = different.Build();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), RemoteRead, x =>
            ReplaceLine(ReplaceLine(x, CorrectionRecord.Base64Key, Convert.ToBase64String(other)), CorrectionRecord.Sha256Key, PublisherFixture.Sha256(other)));

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("not the payload", StringComparison.Ordinal));
    }

    [Fact]
    public void ALocalCommitWhoseMessageLostTheRecordIsNeverPushed()
    {
        using PublisherFixture fixture = new();
        string hook = Path.Combine(fixture.Operator, ".git", "hooks", "commit-msg");
        File.WriteAllText(hook, "#!/bin/sh\nprintf 'Apply a descriptive canonical correction\\n' > \"$1\"\n");

        Receipt receipt = fixture.Publisher().Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Equal(fixture.Start, fixture.RemoteHead());
        Assert.NotEqual(fixture.Start, fixture.OperatorHead());
    }

    [Fact]
    public void ALocallyTamperedRecordIsNeverPushed()
    {
        using PublisherFixture fixture = new();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), LocalRead, x => DropLine(x, CorrectionRecord.Sha256Key));

        Receipt receipt = fixture.Publisher(git).Correct(Payload(fixture));

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Equal(fixture.Start, fixture.RemoteHead());
        Assert.DoesNotContain(git.Calls, x => x[0] == "push");
    }

    [Fact]
    public void RunningTheSameCorrectionAgainIsRefusedWithoutMutation()
    {
        using PublisherFixture fixture = new();
        byte[] input = Payload(fixture);
        Assert.Equal(Results.CorrectionVerified, fixture.Publisher().Correct(input).Result);
        string head = fixture.RemoteHead();

        Receipt again = fixture.Publisher().Correct(input);

        Assert.True(again.FailureClass == FailureClasses.PreconditionDrift, again.Render());
        Assert.Equal(head, fixture.RemoteHead());
        Assert.Equal(head, fixture.OperatorHead());
        Assert.Equal(string.Empty, fixture.Status());
    }

    /// <summary>
    /// An otherwise valid correction whose canonical payload is exactly <paramref name="size"/>
    /// bytes: the replacement text is padded with ASCII, one byte per character.
    /// </summary>
    private static byte[] SizedCorrection(PublisherFixture fixture, int size)
    {
        byte[] unpadded = PublisherCorrectionTests.Correction(fixture).Build();
        string after = PublisherCorrectionTests.After + new string('x', size - unpadded.Length);
        byte[] sized = PublisherCorrectionTests.Correction(fixture, after: after).Build();
        Assert.Equal(size, sized.Length);
        return sized;
    }

    [Fact]
    public void ACorrectionPayloadOneByteOverTheBoundIsRefusedBeforeAnyWrite()
    {
        Assert.Equal(18_000, CorrectionRecord.MaxPayloadBytes);
        using PublisherFixture fixture = new();
        CommitObjectRewritingGit git = new(fixture.PublisherGit(), occurrence: 0, x => x);

        Receipt receipt = fixture.Publisher(git).Correct(SizedCorrection(fixture, CorrectionRecord.MaxPayloadBytes + 1));

        Assert.True(receipt.FailureClass == FailureClasses.PreconditionDrift, receipt.Render());
        Assert.Equal(Results.StoppedPrecondition, receipt.Result);
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("18001 bytes", StringComparison.Ordinal) && x.Contains("durable record", StringComparison.Ordinal));
        Assert.DoesNotContain(git.Calls, x => x[0] is "add" or "commit" or "push");
        Assert.Equal(fixture.Start, fixture.RemoteHead());
        Assert.Equal(fixture.Start, fixture.OperatorHead());
        Assert.Equal(string.Empty, fixture.Status());
    }

    [Fact]
    public void ACorrectionPayloadExactlyAtTheBoundIsAcceptedAndRecordedWhole()
    {
        using PublisherFixture fixture = new();
        byte[] input = SizedCorrection(fixture, CorrectionRecord.MaxPayloadBytes);

        Receipt receipt = fixture.Publisher().Correct(input);

        Assert.True(receipt.Result == Results.CorrectionVerified, receipt.Render());
        Payload? recorded = CorrectionRecord.Reconstruct(RemoteCommitObject(fixture), out string reason);
        Assert.True(recorded is not null, reason);
        Assert.Equal(CorrectionRecord.MaxPayloadBytes, recorded.Bytes.Length);
        Assert.Equal(input, recorded.Bytes);
    }

    [Fact]
    public void TheBoundDoesNotApplyToSemanticStaging()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture) { EntryText = PayloadBuilder.Entry(scope: "the fixture transition " + new string('x', CorrectionRecord.MaxPayloadBytes)) };
        byte[] input = payload.Build();
        Assert.True(input.Length > CorrectionRecord.MaxPayloadBytes);

        Receipt receipt = fixture.Publisher().Stage(input);

        Assert.True(receipt.Result == Results.BasisVerifiedAwaitingSeal, receipt.Render());
        Assert.Equal(input, fixture.Blob(fixture.RemoteHead(), $"docs/control-room/pending/{PayloadBuilder.DeltaId}/PUBLICATION-PAYLOAD.json"));
    }

    [Theory]
    [InlineData("missing subject")]
    [InlineData("extra line")]
    [InlineData("wrong contract")]
    [InlineData("embedded whitespace")]
    [InlineData("not descriptive")]
    public void ReconstructionIsStrict(string defect)
    {
        using PublisherFixture fixture = new();
        byte[] input = Payload(fixture);
        byte[] stage = new PayloadBuilder(fixture).Build();
        string good = "tree 0000000000000000000000000000000000000000\nauthor A <a@b> 0 +0000\ncommitter A <a@b> 0 +0000\n\n" +
            $"{CorrectionRecord.Subject}\n\n{CorrectionRecord.ContractKey}: agencyos-canonical-publisher/v1.1\n" +
            $"{CorrectionRecord.Sha256Key}: {PublisherFixture.Sha256(input)}\n{CorrectionRecord.Base64Key}: {Convert.ToBase64String(input)}\n";
        Assert.NotNull(CorrectionRecord.Reconstruct(Encoding.UTF8.GetBytes(good), out _));

        string bad = defect switch
        {
            "missing subject" => good.Replace(CorrectionRecord.Subject + "\n", "Something else\n", StringComparison.Ordinal),
            "extra line" => good + "Signed-off-by: someone\n",
            "wrong contract" => ReplaceLine(good, CorrectionRecord.ContractKey, "agencyos-canonical-publisher/v1.2"),
            "embedded whitespace" => ReplaceLine(good, CorrectionRecord.Base64Key, Convert.ToBase64String(input).Insert(4, " ")),
            _ => ReplaceLine(ReplaceLine(good, CorrectionRecord.Base64Key, Convert.ToBase64String(stage)), CorrectionRecord.Sha256Key, PublisherFixture.Sha256(stage)),
        };

        Assert.Null(CorrectionRecord.Reconstruct(Encoding.UTF8.GetBytes(bad), out string reason));
        Assert.NotEqual(string.Empty, reason);
    }
}
