using System.Globalization;

namespace AgencyOS.Windows.Platform.Documents;

/// <summary>
/// How sensitive a document is, as M10 classified it.
/// </summary>
/// <remarks>
/// Mirrors <c>DocumentSensitivity</c> rather than re-deriving it. The client is
/// told; it does not decide (ADR-0025, ADR-0034).
/// </remarks>
public enum HandoffSensitivity
{
    Internal = 1,
    Confidential = 2,
    Privileged = 3,
    Financial = 4,
    Restricted = 5,
}

/// <summary>What may be done with a materialized copy.</summary>
/// <param name="Directory">
/// Where the copy goes. Always inside AgencyOS's own temporary folder, never the
/// user's Downloads or the shell's temp root — a folder AgencyOS owns is a folder
/// it can enumerate and clean.
/// </param>
/// <param name="FileName">
/// A safe name derived from the document's, never the name as stored. A document
/// title is text somebody typed.
/// </param>
/// <param name="MayOpenWith">
/// Whether the copy may be handed to another application. Opening in Word means a
/// second process holds the bytes and AgencyOS stops being able to say where they
/// are.
/// </param>
/// <param name="DeleteAfter">
/// How long the copy may live. Advisory: AgencyOS deletes on close and sweeps on
/// start, and neither is a guarantee (see <see cref="DocumentHandoffPolicy"/>).
/// </param>
public sealed record HandoffPlan(
    string Directory,
    string FileName,
    bool MayOpenWith,
    TimeSpan DeleteAfter)
{
    /// <summary>The full path the copy would take.</summary>
    public string Path => System.IO.Path.Combine(Directory, FileName);
}

/// <summary>
/// Whether and how a canonical document may be copied to this machine.
/// </summary>
/// <remarks>
/// <para>
/// <strong>M10 remains the only canonical document layer.</strong> Everything
/// here is about a <em>copy</em>: the canonical document is a version in the
/// content store addressed by SHA-256, and a file on a workstation is a
/// materialization of it that AgencyOS can neither version nor recall. A local
/// path is never a document identity, and nothing in this class produces one
/// (§U, ADR-0024).
/// </para>
/// <para>
/// <strong>AgencyOS does not claim secure deletion.</strong> It deletes the file
/// and sweeps the folder on start. It cannot reach a copy the user saved
/// elsewhere, a copy another application made, a shadow copy, a page file, or the
/// bytes still on the disk after an unlink. Saying "cleaned up" would be a
/// stronger claim than the filesystem supports.
/// </para>
/// </remarks>
public static class DocumentHandoffPolicy
{
    /// <summary>The folder AgencyOS materializes into.</summary>
    /// <remarks>
    /// Under the local application data folder rather than the shell's temp root.
    /// A folder AgencyOS owns can be enumerated and swept; the shared temp folder
    /// contains everybody's files and cannot be cleaned safely.
    /// </remarks>
    public static string Root { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgencyOS",
        "materialized");

    /// <summary>
    /// The longest a copy of ordinary material may sit on disk.
    /// </summary>
    /// <remarks>
    /// Short enough that a forgotten file is gone within a working session, long
    /// enough that opening a contract and reading it for an hour is not
    /// interrupted.
    /// </remarks>
    public static TimeSpan OrdinaryLifetime { get; } = TimeSpan.FromHours(4);

    /// <summary>
    /// The longest a copy of sensitive material may sit on disk.
    /// </summary>
    /// <remarks>
    /// Materially shorter. Privileged and financial material on a laptop is the
    /// case where the gap between "deleted" and "unrecoverable" matters most, so
    /// the window in which the question arises is kept small.
    /// </remarks>
    public static TimeSpan SensitiveLifetime { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Decides what may be done with a document on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Restricted material is never materialized. It is the classification an
    /// organization uses to mean "this does not leave", and a copy on a laptop is
    /// leaving — the same reasoning that keeps Restricted unreachable for model
    /// inference at every residency (ADR-0031, ADR-0035).
    /// </para>
    /// <para>
    /// Privileged and financial material may be opened by AgencyOS and not handed
    /// to another application. Open-With means a second process holds the bytes
    /// and decides for itself where they go next, which is a disclosure AgencyOS
    /// cannot describe afterwards.
    /// </para>
    /// </remarks>
    public static HandoffPlan? Plan(
        HandoffSensitivity sensitivity, string documentTitle, string extension)
    {
        ArgumentNullException.ThrowIfNull(documentTitle);
        ArgumentNullException.ThrowIfNull(extension);

        if (sensitivity == HandoffSensitivity.Restricted)
        {
            return null;
        }

        bool sensitive = sensitivity
            is HandoffSensitivity.Privileged
            or HandoffSensitivity.Financial;

        return new HandoffPlan(
            Root,
            SafeName(documentTitle, extension),
            MayOpenWith: !sensitive,
            DeleteAfter: sensitive ? SensitiveLifetime : OrdinaryLifetime);
    }

    /// <summary>
    /// Why a document cannot be copied here, when it cannot.
    /// </summary>
    /// <remarks>
    /// Said in terms of the classification rather than of a permission, because
    /// the reader may well be allowed to read it inside AgencyOS. What they cannot
    /// do is take it with them.
    /// </remarks>
    public static string Refusal(HandoffSensitivity sensitivity) =>
        sensitivity == HandoffSensitivity.Restricted
            ? "This document is marked as never leaving AgencyOS, so it cannot be "
                + "opened outside it. You can still read it here."
            : string.Empty;

    /// <summary>
    /// Turns a document title into a file name that is only a file name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A title is text somebody typed. Path separators, traversal sequences,
    /// reserved device names and control characters all have to stop being
    /// meaningful before it reaches the filesystem — this is the same class of
    /// problem M10 solved for stored content, arriving at a different layer
    /// (ADR-0024).
    /// </para>
    /// <para>
    /// The result is bounded, because a path has a limit and a title does not.
    /// </para>
    /// </remarks>
    public static string SafeName(string documentTitle, string extension)
    {
        ArgumentNullException.ThrowIfNull(documentTitle);
        ArgumentNullException.ThrowIfNull(extension);

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();

        string cleaned = new([
            .. documentTitle
                .Trim()
                .Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c),
        ]);

        // A name made entirely of dots and spaces resolves to the directory
        // itself on Windows, and a leading dot hides the file.
        cleaned = cleaned.Trim('.', ' ');

        if (cleaned.Length == 0)
        {
            cleaned = "document";
        }

        if (cleaned.Length > 96)
        {
            cleaned = cleaned[..96].TrimEnd('.', ' ');
        }

        // Reserved device names are refused whatever their extension, because
        // Windows resolves CON.txt to the console.
        if (Reserved.Contains(cleaned.Split('.')[0], StringComparer.OrdinalIgnoreCase))
        {
            cleaned = "_" + cleaned;
        }

        string suffix = extension.TrimStart('.');

        suffix = new([.. suffix.Where(c => char.IsLetterOrDigit(c))]);

        return suffix.Length == 0
            ? cleaned
            : string.Create(CultureInfo.InvariantCulture, $"{cleaned}.{suffix}");
    }

    /// <summary>Whether a path is one AgencyOS materialized.</summary>
    /// <remarks>
    /// Used before deleting anything. A sweeper that trusted a stored path could
    /// be pointed at somebody's documents folder by a corrupted record.
    /// </remarks>
    public static bool IsMaterialized(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string full = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetFullPath(Root);

        return full.StartsWith(
            root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];
}
