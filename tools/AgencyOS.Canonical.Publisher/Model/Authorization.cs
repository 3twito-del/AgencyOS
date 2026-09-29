using System.Text.RegularExpressions;
using AgencyOS.Canonical.Publisher.Json;
using AgencyOS.Canonical.Publisher.Publishing;

namespace AgencyOS.Canonical.Publisher.Model;

/// <summary>
/// The durable seal authorization input: what the authority authorized, stated exactly.
/// </summary>
/// <remarks>
/// The publisher turns this into one record in the delta's <c>Seal authorizations</c>
/// history (contract section 5.2). The only value it supplies is the record number. It
/// never infers an authorization from a conversation, from a commit's existence or from
/// any intent. Without this input it stops at the hold.
/// </remarks>
internal sealed partial class Authorization
{
    public const string ContractId = "agencyos-canonical-seal-authorization/v1";

    public required string Scope { get; init; }

    public required string Authority { get; init; }

    public required string DeltaId { get; init; }

    public required string SemanticBasis { get; init; }

    public required string NextSha256 { get; init; }

    public required string PayloadSha256 { get; init; }

    public required string AuthorizedUtc { get; init; }

    public required string Reference { get; init; }

    public required string RecordedBy { get; init; }

    public static Authorization Parse(byte[] bytes)
    {
        object tree;

        try
        {
            tree = CanonicalJson.Parse(bytes);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            throw Invalid($"The authorization is not valid JSON: {ex.Message}");
        }

        string[] keys =
        [
            "contract", "scope", "authority", "delta_id", "semantic_basis_sha",
            "current_state_next_sha256", "payload_sha256", "authorized_utc", "reference", "recorded_by",
        ];

        if (tree is not JsonMap map || map.Count != keys.Length || keys.Any(x => !map.ContainsKey(x) || map[x] is not string))
        {
            throw Invalid($"The authorization must hold exactly these string fields: {string.Join(", ", keys)}.");
        }

        string Value(string key) => (string)map[key];

        if (Value("contract") != ContractId)
        {
            throw Invalid($"The authorization names contract '{Value("contract")}', not '{ContractId}'.");
        }

        Authorization authorization = new()
        {
            Scope = Value("scope"),
            Authority = Value("authority"),
            DeltaId = Value("delta_id"),
            SemanticBasis = Value("semantic_basis_sha"),
            NextSha256 = Value("current_state_next_sha256"),
            PayloadSha256 = Value("payload_sha256"),
            AuthorizedUtc = Value("authorized_utc"),
            Reference = Value("reference"),
            RecordedBy = Value("recorded_by"),
        };

        if (keys.Skip(1).Any(x => Value(x).Length == 0 || Value(x).Contains('\n', StringComparison.Ordinal) || Value(x).Contains('\r', StringComparison.Ordinal)))
        {
            throw Invalid("Every authorization value must be a non-empty single line.");
        }

        if (authorization.Scope != "AUTHORIZE_SEAL_ONLY")
        {
            throw Invalid("The authorization's scope must be AUTHORIZE_SEAL_ONLY.");
        }

        if (authorization.Authority is not ("CONTROL_ROOM" or "OWNER"))
        {
            throw Invalid("The authorization's authority must be CONTROL_ROOM or OWNER.");
        }

        if (!Ledger.LedgerKind.DeltaId().IsMatch(authorization.DeltaId) || !Payload.FullSha().IsMatch(authorization.SemanticBasis)
            || !Payload.Digest().IsMatch(authorization.NextSha256) || !Payload.Digest().IsMatch(authorization.PayloadSha256)
            || !Utc().IsMatch(authorization.AuthorizedUtc))
        {
            throw Invalid("The authorization's Delta ID, SHA, digests or UTC time are malformed.");
        }

        return authorization;
    }

    private static PublisherFailure Invalid(string detail) => new(FailureClasses.SealAuthorizationInvalid, detail);

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")]
    public static partial Regex Utc();
}
