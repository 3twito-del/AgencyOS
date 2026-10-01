using System.Text;
using System.Text.RegularExpressions;
using AgencyOS.Canonical.Publisher.Model;

namespace AgencyOS.Canonical.Publisher.Publishing;

/// <summary>
/// The durable record a descriptive correction carries in its own commit message
/// (contract section 7.3): the exact accepted payload bytes, so that the Control Room's
/// classification, authority classes and adjudication references can be reconstructed
/// from repository history alone.
/// </summary>
/// <remarks>
/// <para>
/// The message is fixed. A subject line, a blank line, then exactly three lines:
/// </para>
/// <code>
/// Apply a descriptive canonical correction
///
/// AgencyOS-Correction-Contract: agencyos-canonical-publisher/v1.1
/// AgencyOS-Correction-Payload-SHA256: &lt;SHA-256 of the payload bytes&gt;
/// AgencyOS-Correction-Payload-Base64: &lt;standard padded Base64 of the payload bytes, one line&gt;
/// </code>
/// <para>
/// The publisher generates nothing semantic here: every line is the contract ID, a hash
/// or an encoding of bytes the Control Room supplied. Reading is strict. A missing,
/// repeated or extra line, a Base64 value that is not the one canonical encoding, a hash
/// that does not match, or bytes the v1.1 parser refuses are all refused.
/// </para>
/// </remarks>
internal static partial class CorrectionRecord
{
    public const string Subject = "Apply a descriptive canonical correction";
    public const string ContractKey = "AgencyOS-Correction-Contract";
    public const string Sha256Key = "AgencyOS-Correction-Payload-SHA256";
    public const string Base64Key = "AgencyOS-Correction-Payload-Base64";

    /// <summary>
    /// The largest payload the record carries. The message travels as one command-line
    /// argument, and Windows limits a command line to 32,767 characters; this bound keeps
    /// the encoded payload well inside it on every platform, so the limit is deterministic.
    /// </summary>
    public const int MaxPayloadBytes = 18_000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The exact commit message for <paramref name="payload"/>.</summary>
    public static string Message(Payload payload) =>
        $"{Subject}\n\n{ContractKey}: {Payload.ContractId}\n{Sha256Key}: {payload.Sha256}\n{Base64Key}: {Convert.ToBase64String(payload.Bytes)}";

    /// <summary>
    /// Reconstructs the accepted payload from a raw commit object (<c>git cat-file commit</c>
    /// output). Returns the parsed payload, or null with the reason it is not a durable record.
    /// </summary>
    public static Payload? Reconstruct(byte[] commitObject, out string reason)
    {
        string text;

        try
        {
            text = StrictUtf8.GetString(commitObject);
        }
        catch (DecoderFallbackException)
        {
            reason = "the commit object is not valid UTF-8";
            return null;
        }

        int split = text.IndexOf("\n\n", StringComparison.Ordinal);

        if (split < 0)
        {
            reason = "the commit object has no message";
            return null;
        }

        string[] lines = text[(split + 2)..].TrimEnd('\n').Split('\n');

        if (lines.Length != 5 || lines[0] != Subject || lines[1].Length != 0)
        {
            reason = $"the message is not the fixed correction record: expected the subject, a blank line and exactly three record lines, found {lines.Length} line(s)";
            return null;
        }

        string? contract = Value(lines[2], ContractKey);
        string? sha = Value(lines[3], Sha256Key);
        string? encoded = Value(lines[4], Base64Key);

        if (contract is null || sha is null || encoded is null)
        {
            reason = $"the record lines are not {ContractKey}, {Sha256Key} and {Base64Key}, in that order";
            return null;
        }

        if (contract != Payload.ContractId)
        {
            reason = $"the record names contract '{contract}', not '{Payload.ContractId}'";
            return null;
        }

        if (!Base64().IsMatch(encoded) || encoded.Length % 4 != 0)
        {
            reason = "the payload Base64 is malformed";
            return null;
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            reason = "the payload Base64 is malformed";
            return null;
        }

        if (Convert.ToBase64String(bytes) != encoded)
        {
            reason = "the payload Base64 is not the canonical encoding of its bytes";
            return null;
        }

        if (Repository.Sha256(bytes) != sha)
        {
            reason = $"the decoded payload hashes to {Repository.Sha256(bytes)}, not the recorded {sha}";
            return null;
        }

        Payload payload;

        try
        {
            payload = Payload.Parse(bytes);
        }
        catch (PublisherFailure ex)
        {
            reason = $"the decoded payload does not parse under {Payload.ContractId}: {ex.Message}";
            return null;
        }

        if (!payload.IsDescriptive)
        {
            reason = $"the decoded payload is {payload.Mode}, not {Modes.DescriptiveCorrection}";
            return null;
        }

        reason = string.Empty;
        return payload;
    }

    private static string? Value(string line, string key) =>
        line.StartsWith(key + ": ", StringComparison.Ordinal) ? line[(key.Length + 2)..] : null;

    [GeneratedRegex("^[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex Base64();
}
