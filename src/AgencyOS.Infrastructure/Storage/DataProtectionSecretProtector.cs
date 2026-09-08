using System.Text;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Documents;
using Microsoft.AspNetCore.DataProtection;

namespace AgencyOS.Infrastructure.Storage;

/// <summary>
/// Encrypts stored secrets with ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>
/// One purpose string, so a ciphertext produced for a mailbox credential cannot be
/// decrypted by any other part of the system even if it were somehow handed one.
/// </para>
/// <para>
/// <strong>The ALPHA limitation, stated plainly.</strong> The key ring lives on the
/// server's own disk. That protects a leaked database backup, which is the common
/// case and the reason plaintext tokens are unacceptable. It does not protect
/// against somebody who already has the server, because they have the keys too.
/// Moving custody to a managed key store is M15's, and until then this is what the
/// protection is worth (ADR-0027).
/// </para>
/// </remarks>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    /// <summary>
    /// The purpose these secrets are encrypted for.
    /// </summary>
    /// <remarks>
    /// Deliberately specific. A generic purpose would let a ciphertext from one part
    /// of the system be decrypted by another, which is the whole failure purpose
    /// strings exist to prevent.
    /// </remarks>
    public const string Purpose = "AgencyOS.Communications.MailboxCredential.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("There is no secret to protect.", nameof(plaintext));
        }

        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception failure)
        {
            // The message carries no ciphertext, no key identifier and no fragment
            // of either. A person reconnecting the mailbox is the only cure, and
            // that is what the caller tells them.
            throw new SecretProtectionException(
                "The stored credential could not be decrypted. The mailbox must be reconnected.",
                failure);
        }
    }
}

/// <summary>
/// Pulls text out of the formats this build can read without a parser.
/// </summary>
/// <remarks>
/// <para>
/// Plain text and its relatives only. PDF and DOCX are declared unsupported and say
/// so on the version, rather than being handled by a parser this build has not
/// fuzzed: <c>docs/11_TESTING_AND_FORMAL_METHODS.md</c> makes binary and document
/// parsers a mandatory fuzzing target, and adding two of them before that
/// infrastructure exists would be doing the work in the wrong order (ADR-0024).
/// </para>
/// <para>
/// Extraction feeds nothing on an M10 path — search indexes metadata only, and M11
/// is the consumer — so the cost of waiting is a projection nobody reads yet.
/// </para>
/// <para>
/// No OCR, no Python, no model. Extracted text is a derived projection and never
/// the canonical content of a document.
/// </para>
/// </remarks>
public sealed class PlainTextDocumentExtractor : IDocumentTextExtractor
{
    /// <summary>The largest extraction this build attempts.</summary>
    /// <remarks>
    /// A two-hundred-megabyte log file is a valid upload and a useless projection.
    /// Beyond this the version says the text was not extracted, which is true and
    /// costs nothing.
    /// </remarks>
    private const int MaximumBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
        "text/tab-separated-values",
        "application/json",
        "application/xml",
        "text/xml",
    };

    /// <inheritdoc />
    public bool CanExtract(string mediaType) =>
        !string.IsNullOrWhiteSpace(mediaType) && Supported.Contains(mediaType);

    /// <inheritdoc />
    public async Task<DocumentTextExtraction> ExtractAsync(
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!CanExtract(mediaType))
        {
            return new DocumentTextExtraction(
                TextExtractionState.Unsupported,
                Detail: $"This build extracts no text from {mediaType}.");
        }

        try
        {
            byte[] buffer = new byte[MaximumBytes];
            int total = 0;

            while (total < buffer.Length)
            {
                int read = await content
                    .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total == 0)
            {
                return new DocumentTextExtraction(
                    TextExtractionState.Extracted, Text: string.Empty);
            }

            // Decoded strictly. Bytes that are not valid UTF-8 are a file claiming a
            // text media type while holding something else, and silently replacing
            // them would produce a page of replacement characters presented as the
            // document's text.
            string text;

            try
            {
                text = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(buffer, 0, total);
            }
            catch (DecoderFallbackException)
            {
                return new DocumentTextExtraction(
                    TextExtractionState.Failed,
                    Detail: "The bytes are not valid UTF-8, despite the declared media type.");
            }

            bool truncated = total == buffer.Length;

            return new DocumentTextExtraction(
                TextExtractionState.Extracted,
                text,
                truncated ? $"Truncated at {MaximumBytes:N0} bytes." : null);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return new DocumentTextExtraction(
                TextExtractionState.Failed, Detail: failure.GetType().Name);
        }
    }
}
