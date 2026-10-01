using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AgencyOS.Canonical.Publisher.Json;
using AgencyOS.Canonical.Publisher.Publishing;

namespace AgencyOS.Canonical.Publisher.Model;

internal sealed record Claim(string Section, string Text);

internal sealed record NewDecision(string Id, string EntryText);

internal sealed record StatusChange(string Id, string Status);

internal sealed record ReceiptToSeal(string Id, string ContentBearing);

internal sealed record TextReplacement(string Path, string Before, string After);

internal static class Modes
{
    public const string PublishNewDelta = "PUBLISH_NEW_DELTA";
    public const string AdvanceExistingDelta = "ADVANCE_EXISTING_DELTA";
    public const string DescriptiveCorrection = "DESCRIPTIVE_CORRECTION";
}

/// <summary>
/// The Accepted Publication Payload, contract version <see cref="ContractId"/>
/// (CANONICAL-PUBLISHER-CONTRACT.md section 3).
/// </summary>
/// <remarks>
/// <para>
/// Parsing is strict: every key the contract names, no other key, the right type, and
/// stored bytes that are already RFC 8785 canonical. A payload of another contract
/// version is refused rather than read as this one.
/// </para>
/// <para>
/// All semantic text comes from here, verbatim: the staged current state, the delta
/// entry and every new decision entry. The publisher derives only hashes, SHAs, readback
/// times, record numbers and the paths the contract fixes.
/// </para>
/// </remarks>
internal sealed partial class Payload
{
    public const string ContractId = "agencyos-canonical-publisher/v1.1";

    /// <summary>
    /// Paths Publisher V1.1 never changes, whatever the payload or its authority says
    /// (contract section 3.2). There is no exception field: a transition that needs one of
    /// these paths is outside this implementation.
    /// </summary>
    private static readonly string[] MandatoryForbidden =
    [
        "src/", "tests/", ".github/", "scripts/", "docs/control-room/CANONICAL-STATE-PROTOCOL.md",
        "docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md", "docs/releases/", "docs/adr/",
    ];

    public required byte[] Bytes { get; init; }

    public required string Sha256 { get; init; }

    public required string Mode { get; init; }

    public required string DeltaId { get; init; }

    public required string Branch { get; init; }

    public required string ExpectedStartSha { get; init; }

    public required IReadOnlyList<string> AuthorityClasses { get; init; }

    public required IReadOnlyList<string> AdjudicationReferences { get; init; }

    public required bool OwnerDecisionApplies { get; init; }

    public required IReadOnlyList<string> GoverningDecisionIds { get; init; }

    public required string PriorSha256 { get; init; }

    public required string NextSha256 { get; init; }

    public required string NextText { get; init; }

    public required IReadOnlyList<Claim> Changes { get; init; }

    public required IReadOnlyList<Claim> Unchanged { get; init; }

    public required string DeltaKind { get; init; }

    public required string DeltaSubstantiveSha256 { get; init; }

    public required string DeltaEntryText { get; init; }

    public required IReadOnlyList<NewDecision> NewDecisions { get; init; }

    public required IReadOnlyList<StatusChange> StatusChanges { get; init; }

    public required IReadOnlyList<ReceiptToSeal> ReceiptsToSeal { get; init; }

    public required IReadOnlyList<TextReplacement> ProvenanceProse { get; init; }

    public required IReadOnlyList<TextReplacement> Corrections { get; init; }

    public required IReadOnlyList<string> AllowedPaths { get; init; }

    public required IReadOnlyList<string> ForbiddenPaths { get; init; }

    public required bool ProductCodeChange { get; init; }

    public required bool SchemaChange { get; init; }

    public required bool ApiChange { get; init; }

    public required bool DomainExpansion { get; init; }

    public bool IsDescriptive => Mode == Modes.DescriptiveCorrection;

    public static IReadOnlyList<string> MandatoryForbiddenPaths => MandatoryForbidden;

    /// <summary>Whether a path is in the V1.1 hard-forbidden set.</summary>
    public static bool IsHardForbidden(string path) =>
        MandatoryForbidden.Any(x => x.EndsWith('/') ? path.StartsWith(x, StringComparison.Ordinal) : path == x);

    public static Payload Parse(byte[] bytes)
    {
        object tree;

        try
        {
            tree = CanonicalJson.Parse(bytes);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            throw Drift($"The payload is not valid contract JSON: {ex.Message}");
        }

        if (!CanonicalJson.Serialize(tree).AsSpan().SequenceEqual(bytes))
        {
            throw Drift("The payload bytes are not RFC 8785 canonical JSON (contract section 3.1).");
        }

        JsonMap root = Map(tree, "payload",
            "contract", "mode", "delta_id", "branch", "expected_start_sha", "authority",
            "current_state", "delta", "decisions", "provenance_prose", "corrections", "paths", "flags");

        string contract = Text(root, "contract");

        if (contract != ContractId)
        {
            throw Drift($"The payload names contract '{contract}'. This publisher accepts only '{ContractId}'.");
        }

        JsonMap authority = Map(root["authority"], "authority", "classes", "adjudication_references", "owner_decision_applies", "governing_decision_ids");
        JsonMap current = Map(root["current_state"], "current_state", "prior_sha256", "next_sha256", "next_text", "changes", "unchanged");
        JsonMap delta = Map(root["delta"], "delta", "kind", "substantive_sha256", "entry_text");
        JsonMap decisions = Map(root["decisions"], "decisions", "new_entries", "status_changes", "receipts_to_seal");
        JsonMap paths = Map(root["paths"], "paths", "allowed", "forbidden");
        JsonMap flags = Map(root["flags"], "flags", "product_code_change", "schema_change", "api_change", "domain_expansion");

        Payload payload = new()
        {
            Bytes = bytes,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Mode = Text(root, "mode"),
            DeltaId = Text(root, "delta_id"),
            Branch = Text(root, "branch"),
            ExpectedStartSha = Text(root, "expected_start_sha"),
            AuthorityClasses = Texts(authority, "classes"),
            AdjudicationReferences = Texts(authority, "adjudication_references"),
            OwnerDecisionApplies = Flag(authority, "owner_decision_applies"),
            GoverningDecisionIds = Texts(authority, "governing_decision_ids"),
            PriorSha256 = Text(current, "prior_sha256"),
            NextSha256 = Text(current, "next_sha256"),
            NextText = Text(current, "next_text"),
            Changes = Objects(current, "changes", "section", "claim").Select(x => new Claim(x[0], x[1])).ToList(),
            Unchanged = Objects(current, "unchanged", "section", "claim").Select(x => new Claim(x[0], x[1])).ToList(),
            DeltaKind = Text(delta, "kind"),
            DeltaSubstantiveSha256 = Text(delta, "substantive_sha256"),
            DeltaEntryText = Text(delta, "entry_text"),
            NewDecisions = Objects(decisions, "new_entries", "id", "entry_text").Select(x => new NewDecision(x[0], x[1])).ToList(),
            StatusChanges = Objects(decisions, "status_changes", "id", "status").Select(x => new StatusChange(x[0], x[1])).ToList(),
            ReceiptsToSeal = Objects(decisions, "receipts_to_seal", "id", "content_bearing").Select(x => new ReceiptToSeal(x[0], x[1])).ToList(),
            ProvenanceProse = Objects(root, "provenance_prose", "path", "before", "after").Select(x => new TextReplacement(x[0], x[1], x[2])).ToList(),
            Corrections = Objects(root, "corrections", "path", "before", "after").Select(x => new TextReplacement(x[0], x[1], x[2])).ToList(),
            AllowedPaths = Texts(paths, "allowed"),
            ForbiddenPaths = Texts(paths, "forbidden"),
            ProductCodeChange = Flag(flags, "product_code_change"),
            SchemaChange = Flag(flags, "schema_change"),
            ApiChange = Flag(flags, "api_change"),
            DomainExpansion = Flag(flags, "domain_expansion"),
        };

        payload.ValidateShape();
        return payload;
    }

    private void ValidateShape()
    {
        if (Mode is not (Modes.PublishNewDelta or Modes.AdvanceExistingDelta or Modes.DescriptiveCorrection))
        {
            throw Drift($"mode '{Mode}' is not a contract mode.");
        }

        if (!FullSha().IsMatch(ExpectedStartSha))
        {
            throw Drift("expected_start_sha is not a full lowercase SHA.");
        }

        if (!BranchName().IsMatch(Branch))
        {
            throw Drift($"branch '{Branch}' is not a plain branch name.");
        }

        if (!Digest().IsMatch(PriorSha256) || !Digest().IsMatch(NextSha256))
        {
            throw Drift("current_state.prior_sha256 and next_sha256 must be lowercase SHA-256 digests.");
        }

        if (AuthorityClasses.Count == 0 || AuthorityClasses.Any(x => x is not ("MACHINE_VERIFIABLE_FACT" or "CONTROL_ROOM" or "OWNER"))
            || AuthorityClasses.Distinct(StringComparer.Ordinal).Count() != AuthorityClasses.Count)
        {
            throw Drift("authority.classes must be a non-empty set of MACHINE_VERIFIABLE_FACT, CONTROL_ROOM and OWNER.");
        }

        if (GoverningDecisionIds.Any(x => !Ledger.LedgerKind.DecisionId().IsMatch(x)))
        {
            throw Drift("authority.governing_decision_ids holds something that is not a Decision ID.");
        }

        foreach (string text in AllTexts())
        {
            if (text.Contains('\r', StringComparison.Ordinal) || text.Contains('﻿', StringComparison.Ordinal))
            {
                throw Drift("A payload text contains a carriage return or a byte-order mark; canonical text is UTF-8 with LF.");
            }
        }

        if (IsDescriptive)
        {
            if (DeltaId != "None" || DeltaKind != "None" || DeltaSubstantiveSha256 != "None" || DeltaEntryText != "None"
                || NextText != "None" || NewDecisions.Count != 0 || StatusChanges.Count != 0 || ReceiptsToSeal.Count != 0
                || ProvenanceProse.Count != 0 || Corrections.Count == 0)
            {
                throw Drift("A DESCRIPTIVE_CORRECTION payload carries corrections only: its delta, staged-state and decision fields must be \"None\" or empty.");
            }

            return;
        }

        if (!Ledger.LedgerKind.DeltaId().IsMatch(DeltaId))
        {
            throw Drift($"delta_id '{DeltaId}' is not a Delta ID.");
        }

        if ((Mode == Modes.PublishNewDelta && DeltaKind != "new") || (Mode == Modes.AdvanceExistingDelta && DeltaKind != "existing"))
        {
            throw Drift($"delta.kind '{DeltaKind}' does not match mode {Mode}.");
        }

        if (!Digest().IsMatch(DeltaSubstantiveSha256))
        {
            throw Drift("delta.substantive_sha256 is not a lowercase SHA-256 digest.");
        }

        if (Corrections.Count != 0)
        {
            throw Drift("Only a DESCRIPTIVE_CORRECTION payload may carry corrections.");
        }

        RequireCanonicalText(NextText, "current_state.next_text");
        RequireCanonicalText(DeltaEntryText, "delta.entry_text");

        foreach (NewDecision decision in NewDecisions)
        {
            RequireCanonicalText(decision.EntryText, $"decisions.new_entries[{decision.Id}]");
        }
    }

    private IEnumerable<string> AllTexts() =>
        new[] { NextText, DeltaEntryText }
            .Concat(Changes.Select(x => x.Text)).Concat(Unchanged.Select(x => x.Text))
            .Concat(NewDecisions.Select(x => x.EntryText))
            .Concat(ProvenanceProse.SelectMany(x => new[] { x.Before, x.After }))
            .Concat(Corrections.SelectMany(x => new[] { x.Before, x.After }));

    /// <summary>Canonical text ends with exactly one LF and has no trailing blank line.</summary>
    private static void RequireCanonicalText(string text, string field)
    {
        if (text.Length == 0 || !text.EndsWith('\n') || text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            throw Drift($"{field} must end with exactly one line feed and no trailing blank line.");
        }
    }

    private static PublisherFailure Drift(string detail) => new(FailureClasses.PreconditionDrift, detail);

    private static JsonMap Map(object value, string name, params string[] keys)
    {
        if (value is not JsonMap map)
        {
            throw Drift($"{name} is not an object.");
        }

        List<string> missing = [.. keys.Where(x => !map.ContainsKey(x))];
        List<string> unknown = [.. map.Keys.Where(x => !keys.Contains(x))];

        if (missing.Count != 0 || unknown.Count != 0)
        {
            throw Drift($"{name} is incomplete or has unknown keys (missing: {string.Join(", ", missing)}; unknown: {string.Join(", ", unknown)}).");
        }

        return map;
    }

    private static string Text(JsonMap map, string key) =>
        map[key] as string ?? throw Drift($"'{key}' is not a string.");

    private static bool Flag(JsonMap map, string key) =>
        map[key] is bool value ? value : throw Drift($"'{key}' is not a boolean.");

    private static List<string> Texts(JsonMap map, string key) =>
        map[key] is List<object> items && items.All(x => x is string)
            ? [.. items.Cast<string>()]
            : throw Drift($"'{key}' is not an array of strings.");

    private static List<string[]> Objects(JsonMap map, string key, params string[] fields)
    {
        if (map[key] is not List<object> items)
        {
            throw Drift($"'{key}' is not an array.");
        }

        return [.. items.Select(item =>
        {
            JsonMap entry = Map(item, key, fields);
            return fields.Select(field => Text(entry, field)).ToArray();
        })];
    }

    [GeneratedRegex("^[0-9a-f]{40}$")]
    public static partial Regex FullSha();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    public static partial Regex Digest();

    [GeneratedRegex("^[A-Za-z0-9._/-]+$")]
    private static partial Regex BranchName();
}
