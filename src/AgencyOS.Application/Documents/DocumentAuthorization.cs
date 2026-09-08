using System.Buffers;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

/// <summary>
/// Decides who may read a document, and on what grounds.
/// </summary>
/// <remarks>
/// <para>
/// The rule the milestone turns on: <strong>a link is not an authorization</strong>.
/// Being able to read the deal a contract is attached to says nothing about whether
/// you may read the contract. If it did, document classification would be
/// decorative, because almost every sensitive document is linked to something
/// somebody can already see (ADR-0025).
/// </para>
/// <para>
/// So sensitivity is evaluated on the document itself, every time, and the grant it
/// requires is stated here in one place rather than remembered at each call site.
/// </para>
/// </remarks>
public sealed class DocumentAuthorization
{
    private readonly TenantGuard _guard;

    public DocumentAuthorization(TenantGuard guard) => _guard = guard;

    /// <summary>The grant a classification needs on top of <c>documents.read</c>.</summary>
    /// <remarks>
    /// Financial documents lean on the M9 finance grant rather than inventing a
    /// second one: a remittance advice and the payment it explains are the same
    /// disclosure, and two separate permissions for it would drift apart.
    /// </remarks>
    public static string? ElevatedGrantFor(DocumentSensitivity sensitivity) => sensitivity switch
    {
        DocumentSensitivity.Privileged => Permission.DocumentsPrivilegedRead,
        DocumentSensitivity.Restricted => Permission.DocumentsRestrictedRead,
        DocumentSensitivity.Financial => Permission.FinanceRead,
        _ => null,
    };

    /// <summary>Authorizes reading the document surface at all.</summary>
    public Task<UserId> AuthorizeReadAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.DocumentsRead, organizationId, cancellationToken);

    /// <summary>
    /// Authorizes reading one document, including its classification.
    /// </summary>
    /// <remarks>
    /// Refuses rather than redacts. A document list with the privileged rows
    /// silently removed reads as a complete list, and somebody would conclude the
    /// contract was never filed (ADR-0025).
    /// </remarks>
    public async Task<UserId> AuthorizeReadAsync(
        OrganizationId organizationId,
        DocumentSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(Permission.DocumentsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>Authorizes creating or changing documents.</summary>
    public Task<UserId> AuthorizeWriteAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.DocumentsWrite, organizationId, cancellationToken);

    /// <summary>
    /// Authorizes classifying a document at a given sensitivity.
    /// </summary>
    /// <remarks>
    /// A writer may not file a document into a classification they could not then
    /// read. Otherwise anybody could hide a document from themselves, and — worse —
    /// could move somebody else's document out of their reach.
    /// </remarks>
    public async Task<UserId> AuthorizeClassifyAsync(
        OrganizationId organizationId,
        DocumentSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await AuthorizeWriteAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>Authorizes linking a document to a business record.</summary>
    public Task<UserId> AuthorizeLinkAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.DocumentsLink, organizationId, cancellationToken);

    /// <summary>
    /// The classifications a caller may see, for filtering a list.
    /// </summary>
    /// <remarks>
    /// Used by the query layer to narrow a list <em>before</em> counting, ranking or
    /// paging it. Filtering afterwards would leak the count, and a count of
    /// privileged documents about a named person is itself a disclosure.
    /// </remarks>
    public async Task<IReadOnlySet<DocumentSensitivity>> ReadableSensitivitiesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        HashSet<DocumentSensitivity> readable =
        [
            DocumentSensitivity.Internal,
            DocumentSensitivity.Confidential,
        ];

        foreach (DocumentSensitivity sensitivity in new[]
        {
            DocumentSensitivity.Privileged,
            DocumentSensitivity.Restricted,
            DocumentSensitivity.Financial,
        })
        {
            string grant = ElevatedGrantFor(sensitivity)!;

            if (await _guard.HasPermissionAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false))
            {
                readable.Add(sensitivity);
            }
        }

        return readable;
    }
}

/// <summary>
/// The rules about bytes arriving from outside.
/// </summary>
/// <remarks>
/// Every uploaded file is hostile until proven otherwise, and most of these checks
/// exist because the alternative failure is silent: a filename that escapes the
/// storage root, a media type taken from an extension, an archive that expands to
/// fill a disk (ADR-0024).
/// </remarks>
public static class UploadPolicy
{
    /// <summary>The largest single file M10 accepts.</summary>
    /// <remarks>
    /// Two hundred megabytes: comfortably above a long-form contract, a deck or a
    /// headshot, and comfortably below a screener. A limit that admitted video would
    /// be a limit that let one upload fill the volume every other document lives on.
    /// </remarks>
    public const long MaximumByteLength = 200L * 1024 * 1024;

    /// <summary>The largest message attachment ingested without being asked twice.</summary>
    public const long MaximumAttachmentByteLength = 50L * 1024 * 1024;

    private const string DefaultMediaType = "application/octet-stream";

    /// <summary>Characters no stored filename may contain.</summary>
    private static readonly SearchValues<char> Forbidden =
        SearchValues.Create("\\/:*?\"<>|\0\r\n\t");

    /// <summary>
    /// Reduces a supplied filename to something safe to store and to echo back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filename never becomes a path: storage keys are derived from a digest
    /// AgencyOS computed. This is defence for the second use — the name shown in a
    /// list and sent in a <c>Content-Disposition</c> header — where a newline or a
    /// quote is a header injection and <c>..\</c> is a request to a browser to save
    /// somewhere unexpected.
    /// </para>
    /// <para>
    /// Traversal segments are stripped rather than rejected, because a file
    /// genuinely called <c>..pitch.pdf</c> exists and refusing it would be wrong.
    /// </para>
    /// </remarks>
    public static string SafeFileName(string? supplied)
    {
        string candidate = (supplied ?? string.Empty).Trim();

        if (candidate.Length == 0)
        {
            return "untitled";
        }

        // Take the last segment under either separator, so a client that sent a
        // full path contributes only the leaf.
        int separator = candidate.LastIndexOfAny(['/', '\\']);

        if (separator >= 0)
        {
            candidate = candidate[(separator + 1)..];
        }

        Span<char> buffer = candidate.Length <= 300
            ? stackalloc char[candidate.Length]
            : new char[300];

        int written = 0;

        foreach (char character in candidate)
        {
            if (written == buffer.Length)
            {
                break;
            }

            // Control characters are the header-injection risk; the forbidden set
            // is the filesystem and quoting risk.
            buffer[written++] = char.IsControl(character) || Forbidden.Contains(character)
                ? '_'
                : character;
        }

        string safe = new string(buffer[..written]).Trim(' ', '.');

        return safe.Length == 0 ? "untitled" : safe;
    }

    /// <summary>
    /// Settles on a media type, without trusting the extension alone.
    /// </summary>
    /// <remarks>
    /// The declared type is accepted when it is well-formed and known; otherwise
    /// the file becomes an opaque byte stream. AgencyOS never widens a declared
    /// type into something a browser would render, because the whole risk is a file
    /// claiming to be HTML.
    /// </remarks>
    public static string ResolveMediaType(string? declared)
    {
        string candidate = (declared ?? string.Empty).Trim().ToLowerInvariant();

        if (candidate.Length == 0 || candidate.Length > 150)
        {
            return DefaultMediaType;
        }

        // A control character anywhere makes the whole declaration opaque, before
        // any of it is parsed. The resolved type is written into a response header,
        // and a newline in the parameter section is a second header of the sender's
        // choosing - stripping parameters first would discard the evidence rather
        // than the risk (ADR-0025).
        foreach (char character in candidate)
        {
            if (char.IsControl(character))
            {
                return DefaultMediaType;
            }
        }

        // Strip parameters: "text/plain; charset=utf-8" is stored as its type.
        int semicolon = candidate.IndexOf(';', StringComparison.Ordinal);

        if (semicolon >= 0)
        {
            candidate = candidate[..semicolon].Trim();
        }

        int slash = candidate.IndexOf('/', StringComparison.Ordinal);

        if (slash <= 0 || slash == candidate.Length - 1)
        {
            return DefaultMediaType;
        }

        foreach (char character in candidate)
        {
            bool allowed = char.IsAsciiLetterOrDigit(character)
                || character is '/' or '.' or '+' or '-' or '_';

            if (!allowed)
            {
                return DefaultMediaType;
            }
        }

        return candidate;
    }

    /// <summary>
    /// Whether a stored file may be shown inline rather than downloaded.
    /// </summary>
    /// <remarks>
    /// A deliberately short allow-list. Anything not on it is sent as an attachment
    /// with <c>X-Content-Type-Options: nosniff</c>, because inline rendering of an
    /// uploaded file is how a document store becomes a way to run script on the
    /// application's own origin (ADR-0025).
    /// </remarks>
    public static bool MayRenderInline(string mediaType) => mediaType switch
    {
        "application/pdf" => true,
        "image/png" or "image/jpeg" or "image/gif" or "image/webp" => true,
        "text/plain" => true,
        _ => false,
    };

    /// <summary>
    /// Refuses a file that cannot be stored, before any of it is.
    /// </summary>
    /// <remarks>
    /// Too large, and also empty. A zero-byte upload is almost always a transfer
    /// that failed, and recording one would leave a document reporting that
    /// AgencyOS holds the contract when it holds nothing - which is exactly the
    /// claim somebody relies on during an argument (ADR-0024).
    /// </remarks>
    public static void RequireAcceptableLength(long? declaredLength, long limit)
    {
        if (declaredLength is not { } length)
        {
            return;
        }

        if (length > limit)
        {
            throw new DomainException(
                $"That file is {length:N0} bytes, and the limit is {limit:N0}.");
        }

        if (length == 0)
        {
            throw new DomainException(
                "That file is empty. AgencyOS records a document only when it holds "
                    + "something to record.");
        }
    }
}
