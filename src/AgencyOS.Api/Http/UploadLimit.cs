namespace AgencyOS.Api.Http;

/// <summary>
/// The largest upload this deployment accepts.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than a loose number, because two different framework defaults
/// used to answer this question and neither of them was a decision AgencyOS had
/// made. Kestrel stops at roughly 28.6 MiB, multipart buffering at 128 MiB, and
/// nothing anywhere said which one a person filing a document would meet first
/// (§27, ADR-0037).
/// </para>
/// <para>
/// Holding it in one place means the refusal can say what the limit is. "Request
/// entity too large" tells somebody their contract did not file; it does not tell
/// them what would.
/// </para>
/// </remarks>
/// <param name="Bytes">The configured ceiling, in bytes.</param>
public sealed record UploadLimit(long Bytes)
{
    /// <summary>The ceiling as a human-readable size, for a refusal message.</summary>
    public string Describe() => Bytes >= 1024 * 1024
        ? $"{Bytes / (1024 * 1024)} MB"
        : $"{Bytes / 1024} KB";
}
