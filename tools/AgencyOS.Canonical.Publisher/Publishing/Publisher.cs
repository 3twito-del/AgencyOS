using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Ledger;
using AgencyOS.Canonical.Publisher.Model;

namespace AgencyOS.Canonical.Publisher.Publishing;

/// <summary>Seams a test uses to interfere with a run, as a crash or a concurrent actor would.</summary>
internal sealed class PublisherHooks
{
    /// <summary>Runs after P5 writes the sealed files and before the seal is validated.</summary>
    public Action<string>? AfterSealMaterialised { get; init; }
}

/// <summary>
/// The Canonical State Publisher (CANONICAL-PUBLISHER-CONTRACT.md). It executes an
/// already-adjudicated transition and has no semantic decision authority.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point first derives the recovery state (contract section 10) from the
/// repository alone, then continues only along the route the contract permits from that
/// state. A state that matches no row is <c>PRECONDITION_DRIFT</c>. Nothing about the
/// workflow is stored anywhere except in commits, files and ledger records.
/// </para>
/// <para>
/// All semantic text comes verbatim from the Accepted Publication Payload. The
/// publisher derives only hashes, commit SHAs, readback times, the next <c>SA-n</c>
/// record number and the paths the contract fixes.
/// </para>
/// </remarks>
internal sealed partial class Publisher
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly Repository _repo;
    private readonly Func<string> _clock;
    private readonly PublisherHooks _hooks;
    private readonly Receipt _receipt = new();

    public Publisher(IPublisherGit git, string root, Func<string>? clock = null, PublisherHooks? hooks = null)
    {
        _repo = new Repository(git, root);
        _clock = clock ?? (() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        _hooks = hooks ?? new PublisherHooks();
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>R0/R1/R2, and a replacement basis: stage, validate, publish and read back the semantic basis.</summary>
    public Receipt Stage(byte[] payloadBytes) => Run(() => StageCore(Payload.Parse(payloadBytes)));

    /// <summary>R2/R3/R4 with a durable seal authorization input: P4A, then P5–P7.</summary>
    public Receipt Authorize(byte[] authorizationBytes) => Run(() => AuthorizeCore(Authorization.Parse(authorizationBytes)));

    /// <summary>Derives the state and continues from R1–R7 without new input.</summary>
    public Receipt Resume(string branch, string deltaId) => Run(() => ResumeCore(branch, deltaId));

    /// <summary>A Control Room-classified descriptive correction (contract section 7.3).</summary>
    public Receipt Correct(byte[] payloadBytes) => Run(() => CorrectCore(Payload.Parse(payloadBytes)));

    private Receipt Run(Action body)
    {
        try
        {
            body();
        }
        catch (PublisherFailure failure)
        {
            _receipt.Fail(failure);
        }
        catch (LedgerFormatException ex)
        {
            _receipt.Fail(new PublisherFailure(FailureClasses.PreconditionDrift, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _receipt.Fail(new PublisherFailure(FailureClasses.PreconditionDrift, "Refused: " + ex.Message));
        }

        _receipt.ProductCodeChanged = _receipt.ChangedPaths.Any(x => x.StartsWith("src/", StringComparison.Ordinal)) ? "YES" : "NO";
        return _receipt;
    }

    // ------------------------------------------------------------------ stage

    private void StageCore(Payload payload)
    {
        if (payload.IsDescriptive)
        {
            throw Drift("A DESCRIPTIVE_CORRECTION payload is run with 'correct', not 'stage'.");
        }

        Describe(payload);
        Observation seen = _repo.Observe(payload.Branch);
        _receipt.StartingSha = seen.Remote;
        string id = payload.DeltaId;

        if (!seen.Clean)
        {
            throw Drift("The working tree is not clean. The publisher never discards changes; inspect and resolve them first.");
        }

        if (seen.Local == seen.Remote)
        {
            string head = seen.Remote;

            if (head == payload.ExpectedStartSha)
            {
                BuildSemanticBasis(payload, head);
                return;
            }

            if (_repo.Parent(head) == payload.ExpectedStartSha && IsSemanticBasis(head, id)
                && _repo.Blob(head, CanonicalPaths.StagedPayload(id)).AsSpan().SequenceEqual(payload.Bytes))
            {
                VerifySemanticBasis(payload, head, payload.ExpectedStartSha); // R2
                return;
            }

            throw Drift($"The remote head {head} is neither expected_start_sha {payload.ExpectedStartSha} nor this payload's semantic basis. Use 'resume' for a transition past its hold.");
        }

        if (_repo.Parent(seen.Local) == seen.Remote && seen.Remote == payload.ExpectedStartSha
            && IsSemanticBasis(seen.Local, id) && _repo.Blob(seen.Local, CanonicalPaths.StagedPayload(id)).AsSpan().SequenceEqual(payload.Bytes))
        {
            // R1: the semantic basis was committed but never pushed.
            bool replacement = _repo.TryBlob(seen.Remote, CanonicalPaths.StagedPayload(id)) is not null;
            PreFlight(payload, seen.Remote, replacement);
            ValidateSemanticBasis(payload, seen.Remote, replacement, seen.Local);
            PublishSemanticBasis(payload, seen.Local, seen.Remote);
            VerifySemanticBasis(payload, seen.Local, seen.Remote);
            return;
        }

        throw Drift($"The local head {seen.Local} and the remote head {seen.Remote} match no recovery state for this payload.");
    }

    /// <summary>P0 → P1 → P2 → P3 → P4, ending at the hold.</summary>
    private void BuildSemanticBasis(Payload payload, string head)
    {
        string id = payload.DeltaId;
        bool replacement = _repo.TryBlob(head, CanonicalPaths.StagedPayload(id)) is not null;

        PreFlight(payload, head, replacement);

        // P1: the exact accepted bytes, and nothing else. CURRENT-STATE.md is not touched.
        foreach (KeyValuePair<string, byte[]> file in SemanticBasisFiles(payload, head))
        {
            _repo.Write(file.Key, file.Value);
        }

        _repo.StageAll();
        ValidateSemanticBasis(payload, head, replacement, Repository.Index);

        string basis = _repo.Commit($"Stage {id} for canonical publication");

        if (_repo.Parent(basis) != head || _repo.Changes(basis, Repository.Index).Count != 0)
        {
            throw Drift("The semantic-basis commit does not hold exactly the validated index.");
        }

        PublishSemanticBasis(payload, basis, head);
        VerifySemanticBasis(payload, basis, head);
    }

    /// <summary>P0 (contract section 6).</summary>
    private void PreFlight(Payload payload, string head, bool replacement)
    {
        string id = payload.DeltaId;

        foreach (string pending in _repo.PendingDirectories(head))
        {
            if (pending != CanonicalPaths.PendingDirectory(id))
            {
                throw Drift($"Another transition is pending ({pending}). V1 allows one pending transition at a time.");
            }
        }

        ParsedLedger deltas = _repo.Ledger(head, CanonicalPaths.Deltas, LedgerKind.Deltas);
        ParsedLedger decisions = _repo.Ledger(head, CanonicalPaths.Decisions, LedgerKind.Decisions);
        ValidateAllEntries(deltas, decisions, requireSealFor: null);

        if (Repository.Sha256(_repo.Blob(head, CanonicalPaths.CurrentState)) != payload.PriorSha256)
        {
            throw Drift("current_state.prior_sha256 does not match the active CURRENT-STATE.md at expected_start_sha.");
        }

        CheckStaticScope(payload, WrittenPaths(payload));
        CheckAuthorityPresence(payload);

        LedgerEntry? existing = deltas.Entry(id);

        if (payload.Mode == Modes.PublishNewDelta && (existing is not null || replacement))
        {
            throw Drift($"{id} already exists or is already staged; PUBLISH_NEW_DELTA adds a new delta.");
        }

        if (payload.Mode == Modes.AdvanceExistingDelta)
        {
            if (existing is null)
            {
                throw Drift($"{id} does not exist; ADVANCE_EXISTING_DELTA advances an existing delta.");
            }

            if (existing.HeaderValue(LedgerKind.Status) != "ACCEPTED")
            {
                throw Drift($"{id} is {existing.HeaderValue(LedgerKind.Status)}; only an ACCEPTED delta can be published.");
            }
        }
    }

    private static IReadOnlyList<string> WrittenPaths(Payload payload)
    {
        List<string> paths = [CanonicalPaths.Deltas, CanonicalPaths.CurrentState];

        if (payload.NewDecisions.Count != 0 || payload.StatusChanges.Count != 0 || payload.ReceiptsToSeal.Count != 0)
        {
            paths.Add(CanonicalPaths.Decisions);
        }

        paths.AddRange(payload.ProvenanceProse.Select(x => x.Path));
        return [.. paths.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The semantic-basis files, computed from the start tree and the payload's exact text.</summary>
    private Dictionary<string, byte[]> SemanticBasisFiles(Payload payload, string head)
    {
        string id = payload.DeltaId;
        string deltas = _repo.Text(head, CanonicalPaths.Deltas);
        string decisions = _repo.Text(head, CanonicalPaths.Decisions);
        ParsedLedger decisionLedger = ParsedLedger.Parse(decisions, LedgerKind.Decisions);

        deltas = payload.Mode == Modes.PublishNewDelta
            ? LedgerRules.AppendEntry(deltas, LedgerKind.Deltas, payload.DeltaEntryText)
            : LedgerRules.ReplaceEntry(deltas, LedgerKind.Deltas, id, payload.DeltaEntryText);

        string updatedDecisions = decisions;

        foreach (NewDecision decision in payload.NewDecisions)
        {
            if (decisionLedger.Entry(decision.Id) is null)
            {
                updatedDecisions = LedgerRules.AppendEntry(updatedDecisions, LedgerKind.Decisions, decision.EntryText);
            }
        }

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [CanonicalPaths.Deltas] = Utf8.GetBytes(deltas),
            [CanonicalPaths.StagedCurrentState(id)] = Utf8.GetBytes(payload.NextText),
            [CanonicalPaths.StagedPayload(id)] = payload.Bytes,
        };

        if (updatedDecisions != decisions)
        {
            files[CanonicalPaths.Decisions] = Utf8.GetBytes(updatedDecisions);
        }

        return files;
    }

    /// <summary>P2: every check of contract section 7.1, against the index or a commit.</summary>
    private void ValidateSemanticBasis(Payload payload, string head, bool replacement, string source)
    {
        string id = payload.DeltaId;
        IReadOnlyList<Change> changes = _repo.Changes(head, source);

        // 1. Scope.
        HashSet<string> allowed = new(payload.AllowedPaths, StringComparer.Ordinal)
        {
            CanonicalPaths.StagedCurrentState(id),
            CanonicalPaths.StagedPayload(id),
        };

        foreach (Change change in changes)
        {
            if (change.Path.StartsWith("src/", StringComparison.Ordinal))
            {
                throw new PublisherFailure(FailureClasses.ProductBoundaryViolation, $"The semantic basis changes product source: {change.Path}.");
            }

            if (!allowed.Contains(change.Path) || MatchesForbidden(payload, change.Path))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation, $"The semantic basis changes a path outside paths.allowed: {change.Path}.");
            }
        }

        string sourceRevision = source == Repository.Index ? Repository.Index : source;
        List<string> staged = source == Repository.Index
            ? [.. new[] { CanonicalPaths.StagedCurrentState(id), CanonicalPaths.StagedPayload(id) }.Where(x => _repo.Exists(Repository.Index, x))]
            : [.. _repo.FilesUnder(source, CanonicalPaths.PendingDirectory(id))];

        if (staged.Count != 2 || (source != Repository.Index && staged.Except(allowed).Any()))
        {
            throw new PublisherFailure(FailureClasses.ScopeViolation, $"{CanonicalPaths.PendingDirectory(id)} must hold exactly the two staging files.");
        }

        // 2. Active state untouched.
        byte[] active = _repo.Blob(sourceRevision, CanonicalPaths.CurrentState);

        if (Repository.Sha256(active) != payload.PriorSha256 || changes.Any(x => x.Path == CanonicalPaths.CurrentState))
        {
            throw Drift("The semantic basis changes the active CURRENT-STATE.md.");
        }

        // 3. Staged state.
        byte[] next = _repo.Blob(sourceRevision, CanonicalPaths.StagedCurrentState(id));

        if (Repository.Sha256(next) != payload.NextSha256 || !next.AsSpan().SequenceEqual(Utf8.GetBytes(payload.NextText)))
        {
            throw Drift("CURRENT-STATE.next.md is not exactly current_state.next_text with digest current_state.next_sha256.");
        }

        if (!_repo.Blob(sourceRevision, CanonicalPaths.StagedPayload(id)).AsSpan().SequenceEqual(payload.Bytes))
        {
            throw Drift("PUBLICATION-PAYLOAD.json is not exactly the accepted payload.");
        }

        CheckClaims(payload, Utf8.GetString(active), payload.NextText);

        ParsedLedger deltasAtHead = _repo.Ledger(head, CanonicalPaths.Deltas, LedgerKind.Deltas);
        ParsedLedger decisionsAtHead = _repo.Ledger(head, CanonicalPaths.Decisions, LedgerKind.Decisions);
        ParsedLedger deltas = _repo.Ledger(sourceRevision, CanonicalPaths.Deltas, LedgerKind.Deltas);
        ParsedLedger decisions = _repo.Ledger(sourceRevision, CanonicalPaths.Decisions, LedgerKind.Decisions);
        ValidateAllEntries(deltas, decisions, requireSealFor: id);

        foreach (string cited in LedgerRules.CitedIds(payload.NextText))
        {
            if (deltas.Entry(cited) is null && decisions.Entry(cited) is null)
            {
                throw Drift($"CURRENT-STATE.next.md cites {cited}, which does not exist.");
            }
        }

        // 4. Delta identity.
        LedgerEntry entry = deltas.Entry(id) ?? throw Drift($"The semantic basis has no entry {id}.");

        if (entry.Text != payload.DeltaEntryText)
        {
            throw Drift($"{id} in the semantic basis is not exactly delta.entry_text.");
        }

        if (entry.HeaderValue(LedgerKind.Status) != "ACCEPTED" || entry.HeaderValue(LedgerKind.Published) != LedgerRules.Pending
            || entry.HeaderValue(LedgerKind.PublicationReceipt) != LedgerRules.Pending)
        {
            throw Drift($"{id} must be ACCEPTED with Published and Publication receipt Pending; nothing may claim PUBLISHED before the seal.");
        }

        string adjudication = entry.BlockContent(LedgerKind.Adjudication);

        if (adjudication.Length == 0 || adjudication == LedgerRules.Pending)
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, $"{id} has no populated Adjudication.");
        }

        string digest = entry.InvariantDigest();

        if (digest != payload.DeltaSubstantiveSha256)
        {
            throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta,
                $"{id}'s publication-invariant digest is {digest}, not delta.substantive_sha256 {payload.DeltaSubstantiveSha256}.");
        }

        LedgerEntry? previous = deltasAtHead.Entry(id);

        if (replacement && previous?.InvariantDigest() != digest)
        {
            throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta,
                $"{id}'s substantive content differs from the previous semantic basis. That is not a re-authorization: it needs a new correction delta.");
        }

        _receipt.DeltaSubstantiveSha256 = digest;

        // 5. Other delta entries.
        foreach (LedgerEntry earlier in deltasAtHead.Entries.Where(x => x.Id != id))
        {
            if (deltas.Entry(earlier.Id)?.Text != earlier.Text)
            {
                throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta,
                    $"The semantic basis changes the earlier delta {earlier.Id}: {FirstDifference(earlier.Text, deltas.Entry(earlier.Id)?.Text)}.");
            }
        }

        if (!Utf8.GetBytes(Expected(payload, head)[CanonicalPaths.Deltas]).AsSpan().SequenceEqual(_repo.Blob(sourceRevision, CanonicalPaths.Deltas)))
        {
            throw Drift("CANONICAL-DELTAS.md in the semantic basis is not exactly the start ledger with delta.entry_text in place.");
        }

        // 6. Decisions.
        foreach (LedgerEntry earlier in decisionsAtHead.Entries.Where(x => payload.NewDecisions.All(n => n.Id != x.Id)))
        {
            if (decisions.Entry(earlier.Id)?.Text != earlier.Text)
            {
                throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta,
                    $"The semantic basis changes the earlier decision {earlier.Id}: {FirstDifference(earlier.Text, decisions.Entry(earlier.Id)?.Text)}.");
            }
        }

        foreach (NewDecision added in payload.NewDecisions)
        {
            LedgerEntry decision = decisions.Entry(added.Id) ?? throw Drift($"The semantic basis has no decision {added.Id}.");

            if (decision.Text != added.EntryText)
            {
                throw Drift($"{added.Id} in the semantic basis is not exactly its entry_text.");
            }

            LedgerRules.ValidateNewDecision(decision);
            LedgerEntry? before = decisionsAtHead.Entry(added.Id);

            if (replacement ? before?.Text != added.EntryText : before is not null)
            {
                throw replacement
                    ? new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta, $"{added.Id} differs from the previous semantic basis.")
                    : Drift($"{added.Id} already exists; it is not a new decision.");
            }
        }

        if (!Utf8.GetBytes(Expected(payload, head)[CanonicalPaths.Decisions]).AsSpan().SequenceEqual(_repo.Blob(sourceRevision, CanonicalPaths.Decisions)))
        {
            throw Drift("DECISIONS.md in the semantic basis is not exactly the start ledger with the new entries appended.");
        }

        // 7. Authorization history.
        IReadOnlyList<SealRecord> records = LedgerRules.SealRecords(entry);
        IReadOnlyList<SealRecord> previousRecords = previous?.Field(LedgerKind.SealAuthorizations) is null ? [] : LedgerRules.SealRecords(previous);

        if (!(replacement ? records.SequenceEqual(previousRecords) : records.Count == 0))
        {
            throw new PublisherFailure(FailureClasses.SealAuthorizationInvalid,
                $"{id}'s authorization history differs from the previous basis, or a record was added in a semantic basis.");
        }

        _receipt.EarlierAuthorizationRecords.Clear();
        _receipt.EarlierAuthorizationRecords.AddRange(records.Select(x => $"{x.Record} (semantic basis {x.SemanticBasis})"));

        // 8. Correction chain.
        foreach (string cited in LedgerRules.CitedIds(entry.BlockContent(LedgerKind.Supersedes))
            .Concat(payload.NewDecisions.SelectMany(x => LedgerRules.CitedIds(decisions.Entry(x.Id)!.BlockContent(LedgerKind.Supersedes)))))
        {
            if (deltas.Entry(cited) is null && decisions.Entry(cited) is null)
            {
                throw Drift($"A Supersedes field names {cited}, which does not exist.");
            }
        }

        foreach (StatusChange change in payload.StatusChanges)
        {
            CheckStatusChange(payload, decisions, change);
        }

        // 9. Authority.
        CheckAuthorityReferences(payload, sourceRevision, deltas, decisions);

        // Receipts to seal, and provenance prose.
        foreach (ReceiptToSeal receipt in payload.ReceiptsToSeal)
        {
            CheckReceiptToSeal(payload, sourceRevision, head, decisions, receipt);
        }

        foreach (TextReplacement prose in payload.ProvenanceProse)
        {
            CheckProvenance(prose, _repo.Text(sourceRevision, prose.Path));
        }

        // 11. Forbidden implications, by exact phrase.
        CheckForbiddenImplications(payload, entry, Utf8.GetString(active), decisions);
    }

    /// <summary>The start tree's ledgers with the payload's entries in place, as text.</summary>
    private Dictionary<string, string> Expected(Payload payload, string head)
    {
        Dictionary<string, byte[]> files = SemanticBasisFiles(payload, head);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CanonicalPaths.Deltas] = Utf8.GetString(files[CanonicalPaths.Deltas]),
            [CanonicalPaths.Decisions] = files.TryGetValue(CanonicalPaths.Decisions, out byte[]? decisions)
                ? Utf8.GetString(decisions)
                : _repo.Text(head, CanonicalPaths.Decisions),
        };
    }

    /// <summary>P3: one commit, one normal fast-forward push.</summary>
    private void PublishSemanticBasis(Payload payload, string basis, string expectedRemote)
    {
        _repo.Fetch();

        if (_repo.RemoteHead(payload.Branch) != expectedRemote)
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, $"The remote moved before the semantic basis could be pushed; it is no longer {expectedRemote}.");
        }

        if (!_repo.Push(basis, payload.Branch))
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, "The remote refused the semantic basis as a fast-forward.");
        }
    }

    /// <summary>P4: the semantic basis is what the remote holds, read back from the remote. Then the hold.</summary>
    private void VerifySemanticBasis(Payload payload, string basis, string head)
    {
        string id = payload.DeltaId;
        string remote = RemoteReadback(payload.Branch, basis, FailureClasses.RemoteBasisMismatch);

        if (Repository.Sha256(_repo.Blob(remote, CanonicalPaths.StagedCurrentState(id))) != payload.NextSha256
            || !_repo.Blob(remote, CanonicalPaths.StagedPayload(id)).AsSpan().SequenceEqual(payload.Bytes)
            || _repo.Ledger(remote, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)?.InvariantDigest() != payload.DeltaSubstantiveSha256)
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, "The remote semantic basis does not hold the validated staging bytes.");
        }

        if (payload.NextText.Contains(basis, StringComparison.Ordinal))
        {
            throw Drift("CURRENT-STATE.next.md contains its own semantic-basis SHA.");
        }

        _receipt.SemanticBasisSha = basis;
        _receipt.StagedCurrentStateSha256 = payload.NextSha256;
        _receipt.PayloadSha256 = payload.Sha256;
        _receipt.DeltaSubstantiveSha256 = payload.DeltaSubstantiveSha256;
        Record(_repo.Changes(head, basis));
        _receipt.Result = Results.BasisVerifiedAwaitingSeal;
        _receipt.NextRequiredAction =
            $"{RequiredAuthority(payload).First()} (as the transition requires): record a durable seal authorization binding semantic basis {basis}, then run 'authorize'.";
    }

    // ------------------------------------------------------------------ authorize

    private void AuthorizeCore(Authorization authorization)
    {
        string branch = _repo.CurrentBranch();
        Observation seen = _repo.Observe(branch);
        string id = authorization.DeltaId;
        _receipt.DeltaId = id;
        _receipt.Branch = branch;
        _receipt.StartingSha = seen.Remote;

        if (!seen.Clean)
        {
            throw Drift("The working tree is not clean. The publisher never discards changes; inspect and resolve them first.");
        }

        string head = seen.Remote;

        if (seen.Local == head)
        {
            if (IsAuthorizationCommit(head, id))
            {
                // R4: the record is already durable. It must be this authorization.
                RequireRecordIs(LastRecord(head, id), authorization, _repo.Parent(head)!);
                ContinueFromAuthorizationCommit(head, branch);
                return;
            }

            if (IsSemanticBasis(head, id))
            {
                // R2: the hold. Record the authorization (P4A), then seal.
                Payload payload = StagedPayload(head, id);
                RequireBranch(payload, branch);
                CheckAuthorizationAgainstBasis(authorization, head, payload);
                VerifySemanticBasis(payload, head, _repo.Parent(head)!);
                string commit = RecordAuthorization(payload, head, authorization);
                PushAuthorization(payload, commit, head);
                return;
            }

            RequireBasisNotOvertaken(authorization, head);

            if (authorization.SemanticBasis == head && _repo.PendingDirectories(head).Count == 1)
            {
                throw new PublisherFailure(FailureClasses.SealAuthorizationInvalid,
                    $"The authorization names {id}, but {head} stages {_repo.PendingDirectories(head)[0]}.");
            }

            throw Drift($"The remote head {head} is not a semantic basis or authorization commit for {id}.");
        }

        if (_repo.Parent(seen.Local) == head && IsSemanticBasis(head, id) && IsAuthorizationCommit(seen.Local, id))
        {
            // R3: the record was committed but never pushed.
            RequireRecordIs(LastRecord(seen.Local, id), authorization, head);
            Payload payload = StagedPayload(head, id);
            RequireBranch(payload, branch);
            PushAuthorization(payload, seen.Local, head);
            return;
        }

        RequireBasisNotOvertaken(authorization, head);
        throw Drift($"The local head {seen.Local} and the remote head {head} match no recovery state for {id}. Use 'resume'.");
    }

    /// <summary>
    /// An authorization names one semantic basis. If the remote has moved past it with
    /// anything but its own authorization commit, the basis is invalidated (contract
    /// section 4) and this authorization can never bind.
    /// </summary>
    private void RequireBasisNotOvertaken(Authorization authorization, string head)
    {
        string ownLifecycle = IsSealCommit(head, authorization.DeltaId) ? _repo.Parent(head)! : head;

        if (IsAuthorizationCommit(ownLifecycle, authorization.DeltaId) && _repo.Parent(ownLifecycle) == authorization.SemanticBasis)
        {
            return;
        }

        if (authorization.SemanticBasis != head && _repo.Resolve(authorization.SemanticBasis) == authorization.SemanticBasis
            && IsSemanticBasis(authorization.SemanticBasis, authorization.DeltaId) && _repo.IsAncestor(authorization.SemanticBasis, head))
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch,
                $"The remote moved past semantic basis {authorization.SemanticBasis}; this authorization no longer binds. A replacement basis must be staged and authorized.");
        }
    }

    /// <summary>The authorization must bind exactly this semantic basis and its bytes.</summary>
    private void CheckAuthorizationAgainstBasis(Authorization authorization, string basis, Payload payload)
    {
        string id = payload.DeltaId;

        if (authorization.DeltaId != id)
        {
            throw Invalid($"The authorization names {authorization.DeltaId}, not {id}.");
        }

        if (authorization.SemanticBasis != basis)
        {
            throw Invalid($"The authorization names semantic basis {authorization.SemanticBasis}, not the verified basis {basis}.");
        }

        if (authorization.NextSha256 != Repository.Sha256(_repo.Blob(basis, CanonicalPaths.StagedCurrentState(id))))
        {
            throw Invalid("The authorization's CURRENT-STATE.next.md digest is not the staged blob's.");
        }

        if (authorization.PayloadSha256 != Repository.Sha256(_repo.Blob(basis, CanonicalPaths.StagedPayload(id))))
        {
            throw Invalid("The authorization's payload digest is not the staged payload blob's.");
        }

        if (!RequiredAuthority(payload).Contains(authorization.Authority))
        {
            throw Invalid($"The transition requires {string.Join(" or ", RequiredAuthority(payload))}; the authorization is by {authorization.Authority}.");
        }

        if (!ReferenceResolves(basis, authorization.Reference))
        {
            throw Invalid($"The authorization's reference '{authorization.Reference}' is not a durable reference that resolves.");
        }
    }

    /// <summary>P4A: append exactly one record, and change nothing else.</summary>
    private string RecordAuthorization(Payload payload, string basis, Authorization authorization)
    {
        string id = payload.DeltaId;
        string ledger = _repo.Text(basis, CanonicalPaths.Deltas);
        LedgerEntry entry = ParsedLedger.Parse(ledger, LedgerKind.Deltas).Entry(id)!;
        int number = LedgerRules.SealRecords(entry).Count + 1;

        SealRecord record = new(
            "SA-" + number.ToString(CultureInfo.InvariantCulture), "AUTHORIZE_SEAL_ONLY", authorization.Authority, id, basis,
            authorization.NextSha256, authorization.PayloadSha256, authorization.AuthorizedUtc, authorization.Reference,
            authorization.RecordedBy);

        string updated = LedgerRules.AppendSealRecord(ledger, id, record);

        if (ParsedLedger.Parse(updated, LedgerKind.Deltas).Entry(id)!.InvariantDigest() != entry.InvariantDigest())
        {
            throw Invalid("Appending the record would change the delta's publication-invariant digest.");
        }

        _repo.Write(CanonicalPaths.Deltas, Utf8.GetBytes(updated));
        _repo.StageAll();

        IReadOnlyList<Change> changes = _repo.Changes(basis, Repository.Index);

        if (changes.Count != 1 || changes[0].Path != CanonicalPaths.Deltas
            || !_repo.Blob(Repository.Index, CanonicalPaths.Deltas).AsSpan().SequenceEqual(Utf8.GetBytes(updated)))
        {
            throw Invalid("The authorization commit would change more than the appended record.");
        }

        return _repo.Commit($"Record seal authorization {record.Record} for {id}");
    }

    private void PushAuthorization(Payload payload, string commit, string basis)
    {
        _repo.Fetch();

        if (_repo.RemoteHead(payload.Branch) != basis)
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, $"The remote moved from semantic basis {basis} before the authorization could be pushed.");
        }

        if (!_repo.Push(commit, payload.Branch))
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, "The remote refused the authorization commit as a fast-forward.");
        }

        ContinueFromAuthorizationCommit(commit, payload.Branch);
    }

    /// <summary>R4 and after P4A: read back, apply the binding rule, then seal.</summary>
    private void ContinueFromAuthorizationCommit(string commit, string branch)
    {
        string basis = _repo.Parent(commit)!;
        string id = PendingDelta(basis);
        Payload payload = StagedPayload(basis, id);
        RequireBranch(payload, branch);
        Describe(payload);

        RemoteReadback(branch, commit, FailureClasses.RemoteBasisMismatch);
        SealRecord record = Bind(commit, payload);
        string readback = _clock();

        _receipt.SemanticBasisSha = basis;
        _receipt.FinalPublicationBasisSha = commit;
        _receipt.BasisReadbackUtc = readback;
        _receipt.StagedCurrentStateSha256 = payload.NextSha256;
        _receipt.PayloadSha256 = payload.Sha256;
        _receipt.DeltaSubstantiveSha256 = payload.DeltaSubstantiveSha256;
        _receipt.BindingSealAuthorization = $"{record.Record}; {record.Authority}; {record.Authorized}; {record.Reference}";
        _receipt.EarlierAuthorizationRecords.Clear();
        _receipt.EarlierAuthorizationRecords.AddRange(Records(commit, id).SkipLast(1).Select(x => $"{x.Record} (semantic basis {x.SemanticBasis})"));

        Seal(payload, commit, readback);
    }

    /// <summary>
    /// The binding rule (contract section 5.3). The last record must bind the exact
    /// semantic basis and bytes being promoted. Any failure is SEAL_AUTHORIZATION_INVALID.
    /// </summary>
    private SealRecord Bind(string commit, Payload payload)
    {
        string id = payload.DeltaId;
        string basis = _repo.Parent(commit) ?? throw Invalid("The authorization commit has no parent.");

        if (!IsAuthorizationCommit(commit, id))
        {
            throw Invalid($"{commit} is not an authorization commit for {id}: it must change only the appended record.");
        }

        SealRecord record = LastRecord(commit, id);
        LedgerEntry entry = _repo.Ledger(commit, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)!;

        if (record.Delta != id)
        {
            throw Invalid($"The last record names {record.Delta}, not {id}.");
        }

        if (!RequiredAuthority(payload).Contains(record.Authority))
        {
            throw Invalid($"The last record is by {record.Authority}; the transition requires {string.Join(" or ", RequiredAuthority(payload))}.");
        }

        if (record.SemanticBasis != basis)
        {
            throw Invalid($"The last record names {record.SemanticBasis}, not the authorization commit's parent {basis}.");
        }

        if (!IsSemanticBasis(basis, id) || !_repo.IsAncestor(basis, _repo.RemoteHead(payload.Branch)))
        {
            throw Invalid($"{basis} is not a semantic basis the remote holds.");
        }

        string next = Repository.Sha256(_repo.Blob(basis, CanonicalPaths.StagedCurrentState(id)));
        string staged = Repository.Sha256(_repo.Blob(basis, CanonicalPaths.StagedPayload(id)));

        if (record.NextSha256 != next || next != Repository.Sha256(_repo.Blob(commit, CanonicalPaths.StagedCurrentState(id))))
        {
            throw Invalid("The record's CURRENT-STATE.next.md digest is not the staged blob's.");
        }

        if (record.PayloadSha256 != staged || staged != Repository.Sha256(_repo.Blob(commit, CanonicalPaths.StagedPayload(id))))
        {
            throw Invalid("The record's payload digest is not the staged payload blob's.");
        }

        if (record.Scope != "AUTHORIZE_SEAL_ONLY" || !ReferenceResolves(commit, record.Reference))
        {
            throw Invalid($"The record's scope or reference is not valid: '{record.Reference}'.");
        }

        if (_repo.RemoteHead(payload.Branch) != commit)
        {
            throw Invalid($"The remote head is no longer the authorization commit {commit}.");
        }

        if (entry.InvariantDigest() != payload.DeltaSubstantiveSha256)
        {
            throw Invalid($"{id}'s publication-invariant digest changed between the semantic basis and the authorization.");
        }

        return record;
    }

    // ------------------------------------------------------------------ seal

    /// <summary>P5 → P6 → P7.</summary>
    private void Seal(Payload payload, string finalBasis, string readbackUtc)
    {
        Dictionary<string, byte[]?> expected = SealFiles(payload, finalBasis, readbackUtc);

        foreach (KeyValuePair<string, byte[]?> file in expected)
        {
            if (file.Value is null)
            {
                _repo.Delete(file.Key);
            }
            else
            {
                _repo.Write(file.Key, file.Value);
            }
        }

        _hooks.AfterSealMaterialised?.Invoke(_repo.Root);
        _repo.StageAll();
        ValidateSeal(payload, finalBasis, Repository.Index, expected);
        Bind(finalBasis, payload);

        string seal = _repo.Commit($"Publish {payload.DeltaId}");
        PushSeal(payload, seal, finalBasis, expected);
    }

    /// <summary>
    /// The section 7.2 whitelist, computed from the final basis: the exact bytes every
    /// changed path must hold after the seal (<c>null</c> = deleted).
    /// </summary>
    private Dictionary<string, byte[]?> SealFiles(Payload payload, string finalBasis, string readbackUtc)
    {
        string id = payload.DeltaId;
        string branch = payload.Branch;
        string receipt = $"{finalBasis} on origin/{branch}; remote readback verified {readbackUtc}";
        Dictionary<string, string> texts = new(StringComparer.Ordinal);

        string deltas = _repo.Text(finalBasis, CanonicalPaths.Deltas);
        deltas = LedgerRules.SetHeader(deltas, LedgerKind.Deltas, id, LedgerKind.Status, "ACCEPTED", "PUBLISHED");
        deltas = LedgerRules.SetHeader(deltas, LedgerKind.Deltas, id, LedgerKind.Published, LedgerRules.Pending, readbackUtc);
        deltas = LedgerRules.SetHeader(deltas, LedgerKind.Deltas, id, LedgerKind.PublicationReceipt, LedgerRules.Pending, receipt);
        texts[CanonicalPaths.Deltas] = deltas;

        if (payload.ReceiptsToSeal.Count != 0 || payload.StatusChanges.Count != 0)
        {
            string decisions = _repo.Text(finalBasis, CanonicalPaths.Decisions);
            _receipt.DecisionReceiptsAffected.Clear();

            foreach (ReceiptToSeal target in payload.ReceiptsToSeal)
            {
                string contentBearing = target.ContentBearing == "SEMANTIC_BASIS"
                    ? FirstSemanticBasisContaining(finalBasis, id, target.Id)
                    : target.ContentBearing;
                decisions = LedgerRules.SetHeader(decisions, LedgerKind.Decisions, target.Id, LedgerKind.PublicationReceipt, LedgerRules.Pending,
                    $"{contentBearing} on origin/{branch}; remote readback verified {readbackUtc}");
                _receipt.DecisionReceiptsAffected.Add($"{target.Id}: {contentBearing}");
            }

            foreach (StatusChange change in payload.StatusChanges)
            {
                decisions = LedgerRules.SetHeader(decisions, LedgerKind.Decisions, change.Id, LedgerKind.Status, "ACTIVE", change.Status);
            }

            texts[CanonicalPaths.Decisions] = decisions;
        }

        foreach (TextReplacement prose in payload.ProvenanceProse)
        {
            string text = texts.TryGetValue(prose.Path, out string? edited) ? edited : _repo.Text(finalBasis, prose.Path);
            CheckProvenance(prose, text);
            texts[prose.Path] = text.Replace(prose.Before, prose.After, StringComparison.Ordinal);
        }

        Dictionary<string, byte[]?> files = new(StringComparer.Ordinal)
        {
            [CanonicalPaths.CurrentState] = _repo.Blob(finalBasis, CanonicalPaths.StagedCurrentState(id)),
            [CanonicalPaths.StagedCurrentState(id)] = null,
            [CanonicalPaths.StagedPayload(id)] = null,
        };

        foreach (KeyValuePair<string, string> text in texts)
        {
            files[text.Key] = Utf8.GetBytes(text.Value);
        }

        return files;
    }

    /// <summary>
    /// Every byte the seal changes must be one the whitelist names, with exactly the
    /// whitelisted value. Anything else is SUBSTANTIVE_MUTATION_DURING_SEAL, and the run
    /// stops before committing, leaving the index for inspection.
    /// </summary>
    private void ValidateSeal(Payload payload, string finalBasis, string source, Dictionary<string, byte[]?> expected)
    {
        string id = payload.DeltaId;
        IReadOnlyList<Change> changes = _repo.Changes(finalBasis, source);
        HashSet<string> changed = [.. changes.Select(x => x.Path)];
        HashSet<string> whitelisted = [.. expected.Where(x => x.Value is null || !x.Value.AsSpan().SequenceEqual(_repo.TryBlob(finalBasis, x.Key) ?? [])).Select(x => x.Key)];

        if (!changed.SetEquals(whitelisted))
        {
            throw Mutation($"The seal changes {string.Join(", ", changed.Except(whitelisted))} and misses {string.Join(", ", whitelisted.Except(changed))}.");
        }

        foreach (KeyValuePair<string, byte[]?> file in expected)
        {
            byte[]? actual = _repo.TryBlob(source, file.Key);

            if (file.Value is null ? actual is not null : actual is null || !actual.AsSpan().SequenceEqual(file.Value))
            {
                throw Mutation($"{file.Key} does not hold exactly the whitelisted bytes.");
            }
        }

        if (source == Repository.Index)
        {
            GitResult status = _repo.Git("status", "--porcelain", "--untracked-files=all");

            if (status.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Length < 2 || x[1] != ' '))
            {
                throw Mutation("The working tree holds changes outside the staged seal.");
            }
        }

        LedgerEntry sealedEntry = _repo.Ledger(source, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)!;

        if (sealedEntry.InvariantDigest() != payload.DeltaSubstantiveSha256)
        {
            throw Mutation($"{id}'s publication-invariant digest changed during the seal.");
        }

        if (Repository.Sha256(_repo.Blob(source, CanonicalPaths.CurrentState)) != LastRecord(finalBasis, id).NextSha256)
        {
            throw Mutation("The promoted CURRENT-STATE.md is not the bound CURRENT-STATE.next.md.");
        }
    }

    /// <summary>P6 and P7.</summary>
    private void PushSeal(Payload payload, string seal, string finalBasis, Dictionary<string, byte[]?> expected)
    {
        _repo.Fetch();

        if (_repo.RemoteHead(payload.Branch) != finalBasis)
        {
            throw new PublisherFailure(FailureClasses.RemoteMovedBeforeSeal, $"The remote moved from the final publication basis {finalBasis}; the latest record no longer binds.");
        }

        if (!_repo.Push(seal, payload.Branch))
        {
            throw new PublisherFailure(FailureClasses.RemoteMovedBeforeSeal, "The remote refused the seal as a fast-forward.");
        }

        FinalReadback(payload, seal, finalBasis, expected);
    }

    /// <summary>P7: only when this passes is the transition PUBLISHED.</summary>
    private void FinalReadback(Payload payload, string seal, string finalBasis, Dictionary<string, byte[]?> expected)
    {
        string remote = RemoteReadback(payload.Branch, seal, FailureClasses.FinalReadbackMismatch);

        foreach (KeyValuePair<string, byte[]?> file in expected)
        {
            byte[]? actual = _repo.TryBlob(remote, file.Key);

            if (file.Value is null ? actual is not null : actual is null || !actual.AsSpan().SequenceEqual(file.Value))
            {
                throw new PublisherFailure(FailureClasses.FinalReadbackMismatch, $"{file.Key} on the remote is not the sealed bytes.");
            }
        }

        if (_repo.FilesUnder(remote, CanonicalPaths.PendingDirectory(payload.DeltaId)).Count != 0)
        {
            throw new PublisherFailure(FailureClasses.FinalReadbackMismatch, "The staging directory is still on the remote.");
        }

        _receipt.SealingSha = seal;
        _receipt.SealReadbackUtc = _clock();
        Record(_repo.Changes(finalBasis, seal));
        _receipt.Result = Results.PublishedVerified;
        _receipt.NextRequiredAction = "None. The transition is PUBLISHED.";
    }

    // ------------------------------------------------------------------ resume

    private void ResumeCore(string branch, string id)
    {
        if (!LedgerKind.DeltaId().IsMatch(id))
        {
            throw Drift($"'{id}' is not a Delta ID.");
        }

        _receipt.DeltaId = id;
        _receipt.Branch = branch;
        Observation seen = _repo.Observe(branch);
        _receipt.StartingSha = seen.Remote;

        if (!seen.Clean)
        {
            throw Drift("The working tree is not clean. The publisher never discards changes; inspect and resolve them first.");
        }

        string head = seen.Remote;

        if (seen.Local == head)
        {
            if (IsSealCommit(head, id))
            {
                // R6/R7: re-run P7, which only reads.
                string finalBasis = _repo.Parent(head)!;
                Payload published = StagedPayload(_repo.Parent(finalBasis)!, id);
                Describe(published);
                LedgerEntry entry = _repo.Ledger(head, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)!;
                string readback = entry.HeaderValue(LedgerKind.Published)!;

                if (entry.HeaderValue(LedgerKind.PublicationReceipt) != $"{finalBasis} on origin/{branch}; remote readback verified {readback}")
                {
                    throw Drift($"{id} is PUBLISHED with a receipt that does not name its final publication basis {finalBasis}.");
                }

                Dictionary<string, byte[]?> expected = SealFiles(published, finalBasis, readback);
                ValidateSeal(published, finalBasis, head, expected);
                _receipt.FinalPublicationBasisSha = finalBasis;
                _receipt.SemanticBasisSha = _repo.Parent(finalBasis)!;
                _receipt.BasisReadbackUtc = readback;
                FinalReadback(published, head, finalBasis, expected);
                _receipt.UnresolvedWarnings.Add("Already published. This run re-read the remote and changed nothing.");
                return;
            }

            if (IsAuthorizationCommit(head, id))
            {
                ContinueFromAuthorizationCommit(head, branch); // R4
                return;
            }

            if (IsSemanticBasis(head, id))
            {
                Payload payload = StagedPayload(head, id); // R2
                RequireBranch(payload, branch);
                Describe(payload);
                VerifySemanticBasis(payload, head, _repo.Parent(head)!);
                return;
            }

            ThrowIfOvertaken(head, id);
            throw Drift($"No staged transition for {id} is at {head}. An unstaged transition starts with 'stage' and its accepted payload.");
        }

        string local = seen.Local;

        if (_repo.Parent(local) == head)
        {
            if (IsAuthorizationCommit(head, id) && LooksSealed(local, id))
            {
                // R5: the seal was committed but never pushed.
                Payload payload = StagedPayload(_repo.Parent(head)!, id);
                RequireBranch(payload, branch);
                Describe(payload);
                string readback = _repo.Ledger(local, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)!.HeaderValue(LedgerKind.Published)!;
                Dictionary<string, byte[]?> expected = SealFiles(payload, head, readback);
                ValidateSeal(payload, head, local, expected);
                Bind(head, payload);
                _receipt.SemanticBasisSha = _repo.Parent(head)!;
                _receipt.FinalPublicationBasisSha = head;
                _receipt.BasisReadbackUtc = readback;
                PushSeal(payload, local, head, expected);
                return;
            }

            if (IsSemanticBasis(head, id) && IsAuthorizationCommit(local, id))
            {
                Payload payload = StagedPayload(head, id); // R3
                RequireBranch(payload, branch);
                Describe(payload);
                PushAuthorization(payload, local, head);
                return;
            }

            if (IsSemanticBasis(local, id))
            {
                Payload payload = StagedPayload(local, id); // R1
                RequireBranch(payload, branch);
                Describe(payload);

                if (payload.ExpectedStartSha != head)
                {
                    throw Drift($"The local semantic basis was built on {payload.ExpectedStartSha}, but the remote is {head}.");
                }

                bool replacement = _repo.TryBlob(head, CanonicalPaths.StagedPayload(id)) is not null;
                PreFlight(payload, head, replacement);
                ValidateSemanticBasis(payload, head, replacement, local);
                PublishSemanticBasis(payload, local, head);
                VerifySemanticBasis(payload, local, head);
                return;
            }
        }

        ThrowIfOvertaken(head, id);
        throw Drift($"The local head {local} and the remote head {head} match no recovery state for {id}.");
    }

    /// <summary>
    /// A pending transition the remote has moved past (contract sections 4 and 9). If other
    /// commits sit on top of a final authorization basis, that is REMOTE_MOVED_BEFORE_SEAL;
    /// on top of a semantic basis not yet authorized, REMOTE_BASIS_MISMATCH. Either way the
    /// basis needs a replacement and a new authorization.
    /// </summary>
    private void ThrowIfOvertaken(string head, string id)
    {
        if (_repo.FilesUnder(head, CanonicalPaths.PendingDirectory(id)).Count == 0)
        {
            return;
        }

        GitResult history = _repo.Git("rev-list", "--first-parent", "--max-count=200", head);

        foreach (string commit in history.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            if (IsAuthorizationCommit(commit, id))
            {
                throw new PublisherFailure(FailureClasses.RemoteMovedBeforeSeal,
                    $"The remote moved past the final publication basis {commit} before the seal. The latest record no longer binds; a replacement basis and a new authorization are needed.");
            }

            if (IsSemanticBasis(commit, id))
            {
                throw new PublisherFailure(FailureClasses.RemoteBasisMismatch,
                    $"The remote moved past semantic basis {commit} before it was authorized. A replacement basis must be staged and authorized.");
            }
        }
    }

    // ------------------------------------------------------------------ descriptive correction

    private void CorrectCore(Payload payload)
    {
        if (!payload.IsDescriptive)
        {
            throw Drift("'correct' runs only a payload the Control Room classified as DESCRIPTIVE_CORRECTION.");
        }

        _receipt.Mode = payload.Mode;
        _receipt.Branch = payload.Branch;
        _receipt.PayloadSha256 = payload.Sha256;
        Observation seen = _repo.Observe(payload.Branch);
        _receipt.StartingSha = seen.Remote;

        if (seen.Local != seen.Remote || seen.Remote != payload.ExpectedStartSha || !seen.Clean)
        {
            throw Drift($"A descriptive correction starts from a clean checkout at expected_start_sha {payload.ExpectedStartSha}; the remote is {seen.Remote} and the local head {seen.Local}.");
        }

        string head = seen.Remote;

        // P0.
        ParsedLedger deltas = _repo.Ledger(head, CanonicalPaths.Deltas, LedgerKind.Deltas);
        ParsedLedger decisions = _repo.Ledger(head, CanonicalPaths.Decisions, LedgerKind.Decisions);
        ValidateAllEntries(deltas, decisions, requireSealFor: null);

        if (Repository.Sha256(_repo.Blob(head, CanonicalPaths.CurrentState)) != payload.PriorSha256)
        {
            throw Drift("current_state.prior_sha256 does not match the active CURRENT-STATE.md.");
        }

        CheckStaticScope(payload, [.. payload.Corrections.Select(x => x.Path).Distinct(StringComparer.Ordinal)]);
        CheckAuthorityPresence(payload);

        foreach (string pending in _repo.PendingDirectories(head))
        {
            _receipt.UnresolvedWarnings.Add($"This correction lands on top of the pending transition {pending}; it invalidates that basis, which now needs a replacement semantic basis and a new authorization.");
        }

        // P1: only the exact pre-adjudicated replacements.
        Dictionary<string, string> texts = new(StringComparer.Ordinal);

        foreach (TextReplacement correction in payload.Corrections)
        {
            string text = texts.TryGetValue(correction.Path, out string? edited) ? edited : _repo.Text(head, correction.Path);

            if (Occurrences(text, correction.Before) != 1)
            {
                throw Drift($"The text to correct occurs {Occurrences(text, correction.Before)} time(s) in {correction.Path}, not exactly once.");
            }

            texts[correction.Path] = text.Replace(correction.Before, correction.After, StringComparison.Ordinal);
        }

        foreach (KeyValuePair<string, string> text in texts)
        {
            _repo.Write(text.Key, Utf8.GetBytes(text.Value));
        }

        _repo.StageAll();

        // P2 (contract section 7.3).
        IReadOnlyList<Change> changes = _repo.Changes(head, Repository.Index);

        foreach (Change change in changes)
        {
            if (change.Path.StartsWith("src/", StringComparison.Ordinal))
            {
                throw new PublisherFailure(FailureClasses.ProductBoundaryViolation, $"The correction changes product source: {change.Path}.");
            }

            if (!payload.AllowedPaths.Contains(change.Path) || MatchesForbidden(payload, change.Path) || !texts.ContainsKey(change.Path))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation, $"The correction changes a path outside paths.allowed: {change.Path}.");
            }

            if (change.Path.StartsWith(CanonicalPaths.PendingRoot, StringComparison.Ordinal))
            {
                throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta, "The correction changes staging under pending/, so it is not descriptive.");
            }
        }

        ParsedLedger correctedDeltas = _repo.Ledger(Repository.Index, CanonicalPaths.Deltas, LedgerKind.Deltas);
        ParsedLedger correctedDecisions = _repo.Ledger(Repository.Index, CanonicalPaths.Decisions, LedgerKind.Decisions);

        if (!SameEntries(deltas, correctedDeltas) || !SameEntries(decisions, correctedDecisions))
        {
            throw new PublisherFailure(FailureClasses.SemanticChangeRequiresNewDelta,
                "The correction adds or changes a ledger entry or a lifecycle value, so it is not descriptive and needs a delta.");
        }

        if (Repository.Sha256(_repo.Blob(Repository.Index, CanonicalPaths.CurrentState)) != payload.NextSha256)
        {
            throw Drift("The corrected CURRENT-STATE.md does not have the digest current_state.next_sha256.");
        }

        // P3 and P4.
        string commit = _repo.Commit("Apply a descriptive correction");
        _repo.Fetch();

        if (_repo.RemoteHead(payload.Branch) != head)
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, "The remote moved before the correction could be pushed.");
        }

        if (!_repo.Push(commit, payload.Branch))
        {
            throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, "The remote refused the correction as a fast-forward.");
        }

        string remote = RemoteReadback(payload.Branch, commit, FailureClasses.RemoteBasisMismatch);

        foreach (KeyValuePair<string, string> text in texts)
        {
            if (!_repo.Blob(remote, text.Key).AsSpan().SequenceEqual(Utf8.GetBytes(text.Value)))
            {
                throw new PublisherFailure(FailureClasses.RemoteBasisMismatch, $"{text.Key} on the remote is not the corrected bytes.");
            }
        }

        _receipt.SemanticBasisSha = commit;
        Record(changes);
        _receipt.Result = Results.CorrectionVerified;
        _receipt.NextRequiredAction = _repo.PendingDirectories(head).Count == 0
            ? "None."
            : "Control Room: the pending transition needs a replacement semantic basis and a new seal authorization.";
    }

    // ------------------------------------------------------------------ state predicates

    /// <summary>A commit that stages <paramref name="id"/>'s payload, new or changed from its parent.</summary>
    private bool IsSemanticBasis(string commit, string id)
    {
        byte[]? staged = _repo.TryBlob(commit, CanonicalPaths.StagedPayload(id));
        string? parent = _repo.Parent(commit);

        if (staged is null || parent is null)
        {
            return false;
        }

        byte[]? before = _repo.TryBlob(parent, CanonicalPaths.StagedPayload(id));
        return before is null || !before.AsSpan().SequenceEqual(staged);
    }

    /// <summary>A direct child of a semantic basis that appends exactly one record naming it.</summary>
    private bool IsAuthorizationCommit(string commit, string id)
    {
        string? basis = _repo.Parent(commit);

        if (basis is null || !IsSemanticBasis(basis, id))
        {
            return false;
        }

        IReadOnlyList<Change> changes = _repo.Changes(basis, commit);

        if (changes.Count != 1 || changes[0].Path != CanonicalPaths.Deltas)
        {
            return false;
        }

        try
        {
            IReadOnlyList<SealRecord> before = Records(basis, id);
            IReadOnlyList<SealRecord> after = Records(commit, id);
            return after.Count == before.Count + 1 && after.Take(before.Count).SequenceEqual(before) && after[^1].SemanticBasis == basis
                && _repo.Text(commit, CanonicalPaths.Deltas) == LedgerRules.AppendSealRecord(_repo.Text(basis, CanonicalPaths.Deltas), id, after[^1]);
        }
        catch (Exception ex) when (ex is LedgerFormatException or PublisherFailure)
        {
            return false;
        }
    }

    private bool IsSealCommit(string commit, string id)
    {
        string? finalBasis = _repo.Parent(commit);

        if (finalBasis is null || !IsAuthorizationCommit(finalBasis, id))
        {
            return false;
        }

        return LooksSealed(commit, id);
    }

    private bool LooksSealed(string commit, string id) =>
        _repo.Ledger(commit, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)?.HeaderValue(LedgerKind.Status) == "PUBLISHED"
        && _repo.FilesUnder(commit, CanonicalPaths.PendingDirectory(id)).Count == 0;

    private IReadOnlyList<SealRecord> Records(string commit, string id)
    {
        LedgerEntry entry = _repo.Ledger(commit, CanonicalPaths.Deltas, LedgerKind.Deltas).Entry(id)
            ?? throw Drift($"{id} does not exist at {commit}.");
        return LedgerRules.SealRecords(entry);
    }

    private SealRecord LastRecord(string commit, string id) =>
        Records(commit, id) is { Count: > 0 } records ? records[^1] : throw Invalid($"{id} has no authorization record at {commit}.");

    private string PendingDelta(string commit)
    {
        IReadOnlyList<string> pending = _repo.PendingDirectories(commit);
        return pending.Count == 1 ? pending[0][CanonicalPaths.PendingRoot.Length..] : throw Drift($"{commit} does not stage exactly one transition.");
    }

    private Payload StagedPayload(string commit, string id) =>
        Payload.Parse(_repo.Blob(commit, CanonicalPaths.StagedPayload(id)));

    private static void RequireRecordIs(SealRecord record, Authorization authorization, string basis)
    {
        if (record.Delta != authorization.DeltaId || record.SemanticBasis != authorization.SemanticBasis || record.SemanticBasis != basis
            || record.Authority != authorization.Authority || record.NextSha256 != authorization.NextSha256
            || record.PayloadSha256 != authorization.PayloadSha256 || record.Authorized != authorization.AuthorizedUtc
            || record.Reference != authorization.Reference || record.RecordedBy != authorization.RecordedBy)
        {
            throw Invalid($"The recorded {record.Record} is not this authorization.");
        }
    }

    // ------------------------------------------------------------------ mechanical checks

    private static void ValidateAllEntries(ParsedLedger deltas, ParsedLedger decisions, string? requireSealFor)
    {
        foreach (LedgerEntry entry in deltas.Entries)
        {
            LedgerRules.ValidateDelta(entry, entry.Id == requireSealFor);
        }

        foreach (LedgerEntry entry in decisions.Entries)
        {
            LedgerRules.ValidateDecision(entry);
        }
    }

    private static bool SameEntries(ParsedLedger before, ParsedLedger after) =>
        before.Entries.Count == after.Entries.Count
        && before.Entries.All(x => after.Entry(x.Id)?.Text == x.Text);

    /// <summary>A path the payload forbids, or one in the V1.1 hard-forbidden set, which no authority overrides.</summary>
    private static bool MatchesForbidden(Payload payload, string path) =>
        Payload.IsHardForbidden(path)
        || payload.ForbiddenPaths.Any(x => x.EndsWith('/') ? path.StartsWith(x, StringComparison.Ordinal) : path == x);

    private static void CheckStaticScope(Payload payload, IReadOnlyList<string> written)
    {
        if (payload.ProductCodeChange || payload.AllowedPaths.Concat(written).Any(x => x.StartsWith("src/", StringComparison.Ordinal)))
        {
            throw new PublisherFailure(FailureClasses.ProductBoundaryViolation, "The payload expects a product change or names product source. The publisher never changes product code.");
        }

        CheckHardForbidden(payload, [.. payload.AllowedPaths.Concat(written)]);

        foreach (string path in written)
        {
            if (!payload.AllowedPaths.Contains(path))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation, $"The transition writes {path}, which paths.allowed does not name.");
            }
        }

        foreach (string path in payload.AllowedPaths)
        {
            if (payload.ForbiddenPaths.Any(x => x.EndsWith('/') ? path.StartsWith(x, StringComparison.Ordinal) : path == x)
                || path.StartsWith(CanonicalPaths.PendingRoot, StringComparison.Ordinal))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation, $"paths.allowed names {path}, which the payload forbids or which is reserved for staging.");
            }
        }
    }

    /// <summary>
    /// The V1.1 hard-forbidden set (contract section 3.2). No authority, CONTROL_ROOM or
    /// OWNER, overrides it through this implementation, and there is no exception field.
    /// </summary>
    private static void CheckHardForbidden(Payload payload, IReadOnlyList<string> paths)
    {
        foreach (string mandatory in Payload.MandatoryForbiddenPaths)
        {
            if (!payload.ForbiddenPaths.Contains(mandatory))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation, $"paths.forbidden omits the mandatory {mandatory}; Publisher V1.1 has no exception to it.");
            }
        }

        foreach (string path in paths)
        {
            if (Payload.IsHardForbidden(path))
            {
                throw new PublisherFailure(FailureClasses.ScopeViolation,
                    $"{path} is hard-forbidden to Publisher V1.1. A transition that needs it requires a separately governed publication or a future contract revision.");
            }
        }
    }

    private static void CheckAuthorityPresence(Payload payload)
    {
        bool semantic = payload.AuthorityClasses.Any(x => x is "CONTROL_ROOM" or "OWNER");

        if (semantic && payload.AdjudicationReferences.Count == 0)
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, "The payload claims CONTROL_ROOM or OWNER authority but gives no durable adjudication reference.");
        }

        if ((payload.SchemaChange || payload.ApiChange || payload.DomainExpansion) && !payload.OwnerDecisionApplies)
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, "A schema, API or domain change requires authority.owner_decision_applies.");
        }

        if (payload.OwnerDecisionApplies && (payload.GoverningDecisionIds.Count == 0 || !payload.AuthorityClasses.Contains("OWNER")))
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, "owner_decision_applies requires OWNER authority and governing OWNER Decision IDs.");
        }
    }

    private void CheckAuthorityReferences(Payload payload, string revision, ParsedLedger deltas, ParsedLedger decisions)
    {
        foreach (string reference in payload.AdjudicationReferences)
        {
            if (!ReferenceResolves(revision, reference, deltas, decisions))
            {
                throw new PublisherFailure(FailureClasses.AuthorityMissing, $"The adjudication reference '{reference}' is not a durable reference that resolves.");
            }
        }

        foreach (string governing in payload.GoverningDecisionIds)
        {
            if (decisions.Entry(governing) is null)
            {
                throw new PublisherFailure(FailureClasses.AuthorityMissing, $"The governing decision {governing} does not exist.");
            }
        }

        if (payload.OwnerDecisionApplies && !payload.GoverningDecisionIds.Any(x => decisions.Entry(x)?.HeaderValue("Authority") == "OWNER"))
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, "owner_decision_applies, but no governing decision has OWNER authority.");
        }
    }

    private bool ReferenceResolves(string revision, string reference) =>
        ReferenceResolves(revision, reference,
            _repo.Ledger(revision, CanonicalPaths.Deltas, LedgerKind.Deltas),
            _repo.Ledger(revision, CanonicalPaths.Decisions, LedgerKind.Decisions));

    /// <summary>The three durable reference forms of contract section 5.2. Nothing else resolves.</summary>
    private bool ReferenceResolves(string revision, string reference, ParsedLedger deltas, ParsedLedger decisions)
    {
        if (LedgerKind.DecisionId().IsMatch(reference))
        {
            return decisions.Entry(reference) is not null;
        }

        Match pinned = PinnedPath().Match(reference);

        if (pinned.Success)
        {
            return _repo.ObjectExists(pinned.Groups[2].Value, pinned.Groups[1].Value);
        }

        Match adjudication = AdjudicationOf().Match(reference);

        if (adjudication.Success)
        {
            string content = deltas.Entry(adjudication.Groups[1].Value)?.BlockContent(LedgerKind.Adjudication) ?? string.Empty;
            return content.Length != 0 && content != LedgerRules.Pending;
        }

        return false;
    }

    /// <summary>
    /// The class a seal authorization must carry (contract section 5.2): <c>OWNER</c> when
    /// the transition requires Owner authority, otherwise <c>CONTROL_ROOM</c>. That includes a
    /// transition accepted as machine-verifiable fact only: no promotion bypasses the seal gate.
    /// </summary>
    private static IReadOnlyList<string> RequiredAuthority(Payload payload) =>
        payload.OwnerDecisionApplies || payload.AuthorityClasses.Contains("OWNER") ? ["OWNER"] : ["CONTROL_ROOM"];

    private static void CheckStatusChange(Payload payload, ParsedLedger decisions, StatusChange change)
    {
        LedgerEntry target = decisions.Entry(change.Id) ?? throw Drift($"decisions.status_changes names {change.Id}, which does not exist.");
        Match status = StatusChangePattern().Match(change.Status);

        if (target.HeaderValue(LedgerKind.Status) != "ACTIVE" || !status.Success)
        {
            throw Drift($"{change.Id} can only move from ACTIVE to 'SUPERSEDED by …' or 'REVOKED by …'.");
        }

        string by = status.Groups[1].Value;
        LedgerEntry? successor = payload.NewDecisions.Any(x => x.Id == by) ? decisions.Entry(by) : null;

        if (successor is null || !LedgerRules.CitedIds(successor.BlockContent(LedgerKind.Supersedes)).Contains(change.Id))
        {
            throw Drift($"{change.Id} can only change status through a new decision in this transition that names it in Supersedes.");
        }

        if (target.HeaderValue("Authority") == "OWNER" && successor.HeaderValue("Authority") != "OWNER")
        {
            throw new PublisherFailure(FailureClasses.AuthorityMissing, $"{change.Id} is an OWNER decision; only a later OWNER decision may supersede or revoke it.");
        }
    }

    private void CheckReceiptToSeal(Payload payload, string revision, string head, ParsedLedger decisions, ReceiptToSeal target)
    {
        LedgerEntry decision = decisions.Entry(target.Id) ?? throw Drift($"decisions.receipts_to_seal names {target.Id}, which does not exist.");

        if (decision.HeaderValue(LedgerKind.PublicationReceipt) != LedgerRules.Pending)
        {
            throw Drift($"{target.Id}'s publication receipt is already sealed.");
        }

        if (target.ContentBearing == "SEMANTIC_BASIS")
        {
            if (payload.NewDecisions.All(x => x.Id != target.Id))
            {
                throw Drift($"{target.Id} is not added by this transition, so its content-bearing commit is not the semantic basis.");
            }

            return;
        }

        if (!Payload.FullSha().IsMatch(target.ContentBearing) || _repo.Resolve(target.ContentBearing) != target.ContentBearing
            || !_repo.IsAncestor(target.ContentBearing, head))
        {
            throw Drift($"{target.Id}'s content-bearing commit {target.ContentBearing} is not an ancestor of the start.");
        }

        string? parent = _repo.Parent(target.ContentBearing);
        string? atCommit = _repo.Ledger(target.ContentBearing, CanonicalPaths.Decisions, LedgerKind.Decisions).Entry(target.Id)?.Text;
        string? atParent = parent is null ? null : _repo.Ledger(parent, CanonicalPaths.Decisions, LedgerKind.Decisions).Entry(target.Id)?.Text;

        if (atCommit != decision.Text || atParent == decision.Text)
        {
            throw Drift($"{target.ContentBearing} is not the first commit that holds {target.Id} in its current form.");
        }
    }

    private string FirstSemanticBasisContaining(string finalBasis, string deltaId, string decisionId)
    {
        string wanted = _repo.Ledger(finalBasis, CanonicalPaths.Decisions, LedgerKind.Decisions).Entry(decisionId)!.Text;

        foreach (string commit in _repo.CommitsTouching(finalBasis, CanonicalPaths.StagedPayload(deltaId)))
        {
            if (IsSemanticBasis(commit, deltaId)
                && _repo.Ledger(commit, CanonicalPaths.Decisions, LedgerKind.Decisions).Entry(decisionId)?.Text == wanted)
            {
                return commit;
            }
        }

        throw Drift($"No semantic basis of {deltaId} holds {decisionId} in its current form.");
    }

    /// <summary>A provenance replacement's text must occur exactly once, outside every ledger entry.</summary>
    private static void CheckProvenance(TextReplacement prose, string text)
    {
        int count = Occurrences(text, prose.Before);

        if (count != 1)
        {
            throw Drift($"The provenance prose to replace occurs {count} time(s) in {prose.Path}, not exactly once.");
        }

        if (prose.Path == CanonicalPaths.CurrentState || prose.Path.StartsWith(CanonicalPaths.PendingRoot, StringComparison.Ordinal))
        {
            throw new PublisherFailure(FailureClasses.ScopeViolation, $"Provenance prose cannot target {prose.Path}.");
        }

        LedgerKind? kind = prose.Path == CanonicalPaths.Deltas ? LedgerKind.Deltas : prose.Path == CanonicalPaths.Decisions ? LedgerKind.Decisions : null;

        if (kind is not null)
        {
            ParsedLedger ledger = ParsedLedger.Parse(text, kind);
            int index = text.IndexOf(prose.Before, StringComparison.Ordinal);
            int firstLine = text[..index].Count(x => x == '\n');
            int lastLine = firstLine + prose.Before.Count(x => x == '\n');

            if (ledger.Entries.Any(x => lastLine >= x.FirstLine && firstLine <= x.LastLine))
            {
                throw Drift($"The provenance prose in {prose.Path} lies inside a ledger entry. Provenance prose is outside every entry.");
            }
        }
    }

    private static void CheckClaims(Payload payload, string prior, string next)
    {
        foreach (Claim claim in payload.Unchanged)
        {
            if (!(Section(prior, claim.Section)?.Contains(claim.Text, StringComparison.Ordinal) ?? false)
                || !(Section(next, claim.Section)?.Contains(claim.Text, StringComparison.Ordinal) ?? false))
            {
                throw Drift($"The unchanged claim in section '{claim.Section}' is not in both the active and the staged current state.");
            }
        }

        foreach (Claim claim in payload.Changes)
        {
            if (!(Section(next, claim.Section)?.Contains(claim.Text, StringComparison.Ordinal) ?? false))
            {
                throw Drift($"The changed claim in section '{claim.Section}' is not in the staged current state.");
            }
        }
    }

    /// <summary>
    /// A section's text: the lines under the <c>## </c> heading named, up to the next one.
    /// The empty name is the text before the first <c>## </c> heading.
    /// </summary>
    private static string? Section(string text, string name)
    {
        string[] lines = text.Split('\n');
        int start = name.Length == 0 ? 0 : Array.IndexOf(lines, "## " + name) + 1;

        if (name.Length != 0 && start == 0)
        {
            return null;
        }

        int end = start;

        while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal))
        {
            end++;
        }

        return string.Join('\n', lines[start..end]);
    }

    private static void CheckForbiddenImplications(Payload payload, LedgerEntry entry, string prior, ParsedLedger decisions)
    {
        FieldBlock? block = entry.Field(LedgerKind.ForbiddenImplications);

        if (block is null)
        {
            return;
        }

        List<string> phrases = [.. entry.BlockLines(block).Skip(1)
            .Select(x => x.Trim())
            .Where(x => x.StartsWith("- ", StringComparison.Ordinal))
            .Select(x => x[2..].Trim().TrimEnd(';', '.', ',').Trim())
            .Where(x => x.Length != 0)];

        HashSet<string> priorLines = [.. prior.Split('\n')];
        List<string> introduced = [.. payload.NextText.Split('\n').Where(x => !priorLines.Contains(x))];
        introduced.AddRange(payload.NewDecisions.SelectMany(x => decisions.Entry(x.Id)!.Lines));

        foreach (string phrase in phrases)
        {
            if (introduced.Any(x => x.Contains(phrase, StringComparison.Ordinal)))
            {
                throw Drift($"Introduced text contains the forbidden implication '{phrase}'.");
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Confirms, by asking the remote, that its head is exactly the expected commit.</summary>
    private string RemoteReadback(string branch, string expected, string failureClass)
    {
        _repo.Fetch();

        if (_repo.RemoteHead(branch) != expected || _repo.Resolve("refs/remotes/origin/" + branch) != expected)
        {
            throw new PublisherFailure(failureClass, $"The remote head is not {expected} on readback.");
        }

        return "refs/remotes/origin/" + branch;
    }

    private void RequireBranch(Payload payload, string branch)
    {
        if (payload.Branch != branch)
        {
            throw Drift($"The staged payload is for {payload.Branch}, not {branch}.");
        }
    }

    private void Describe(Payload payload)
    {
        _receipt.DeltaId = payload.DeltaId;
        _receipt.Mode = payload.Mode;
        _receipt.Branch = payload.Branch;
    }

    private void Record(IReadOnlyList<Change> changes)
    {
        foreach (Change change in changes)
        {
            string line = $"{change.Status} {change.Path}";

            if (!_receipt.ChangedPaths.Contains(line))
            {
                _receipt.ChangedPaths.Add(line);
            }
        }
    }

    /// <summary>The first line where two entry texts differ, quoted, as failure evidence.</summary>
    private static string FirstDifference(string before, string? after)
    {
        if (after is null)
        {
            return "the entry was removed";
        }

        string[] a = before.Split('\n');
        string[] b = after.Split('\n');

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string left = i < a.Length ? a[i] : "(end)";
            string right = i < b.Length ? b[i] : "(end)";

            if (left != right)
            {
                return string.Create(CultureInfo.InvariantCulture, $"line {i + 1} was \"{left}\" and is \"{right}\"");
            }
        }

        return "the bytes differ";
    }

    private static int Occurrences(string text, string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        int count = 0;

        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static PublisherFailure Drift(string detail) => new(FailureClasses.PreconditionDrift, detail);

    private static PublisherFailure Invalid(string detail) => new(FailureClasses.SealAuthorizationInvalid, detail);

    private static PublisherFailure Mutation(string detail) => new(FailureClasses.SubstantiveMutationDuringSeal, detail);

    [GeneratedRegex("^(.+)@([0-9a-f]{40})$")]
    private static partial Regex PinnedPath();

    [GeneratedRegex("^Adjudication of (DELTA-[0-9]{8}-[0-9]{3})$")]
    private static partial Regex AdjudicationOf();

    [GeneratedRegex("^(?:SUPERSEDED|REVOKED) by (DECISION-[0-9]{8}-[0-9]{3})$")]
    private static partial Regex StatusChangePattern();
}
