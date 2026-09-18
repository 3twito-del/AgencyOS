using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// Whether a refused mutation can be corrected where it was made, and what it is about.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-010</c>. A dialog used to close on a server refusal: the operator's
/// typing was gone, the explanation appeared on the page behind, and focus was on
/// the button that had opened the dialog rather than anywhere near the reason. The
/// owner's decision is that a **recoverable** refusal keeps the dialog, its values
/// and its context, so the entry can be corrected and retried.
/// </para>
/// <para>
/// Not every failure is recoverable. A session that is no longer valid, or a parent
/// record that is gone, cannot be corrected by editing the form, and holding the
/// dialog open over one would be pretending. Those close, and the page says what
/// happened.
/// </para>
/// <para>
/// This lives in the client assembly rather than in code-behind because it is the
/// part with rules, and code-behind cannot be constructed off a UI thread.
/// </para>
/// </remarks>
/// <param name="KeepsDialogOpen">Whether the operator can correct this where they are.</param>
/// <param name="Message">What to show them — the server's reason, never its title.</param>
/// <param name="Field">
/// The field it is about, or null when it is about the submission as a whole.
/// </param>
public sealed record RefusalPresentation(bool KeepsDialogOpen, string Message, string? Field);

public static class DialogRefusal
{
    /// <summary>
    /// The whole decision a dialog makes about a refusal, in one place.
    /// </summary>
    /// <remarks>
    /// Kept together and kept here so it can be executed by a test. The dialog side
    /// of this is control wiring — put the message in the bar, point the field at
    /// it, move focus — and nothing that decides anything.
    /// </remarks>
    public static RefusalPresentation Present(
        HttpStatusCode status,
        string? detail,
        string message,
        IReadOnlyCollection<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        // The server's own explanation, not the problem's title (AOS-R002-024).
        string said = string.IsNullOrWhiteSpace(detail) ? message : detail;

        return KeepsTheDialogOpen(status)
            ? new RefusalPresentation(true, said, FieldNamed(said, fields))
            : new RefusalPresentation(false, said, null);
    }

    /// <summary>
    /// Whether the operator could fix this from the dialog they are looking at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>400</c> and <c>422</c> are the entry being wrong. <c>409</c> is somebody
    /// else having moved first, which this product's workflows answer by reloading
    /// or renaming and trying again. <c>403</c> will not start succeeding, but the
    /// dialog still holds work the operator may want to copy or reroute, and
    /// throwing it away teaches them to distrust the form.
    /// </para>
    /// <para>
    /// Everything else closes: <c>401</c> (the session is gone), <c>404</c> (the
    /// thing being added to is gone), <c>426</c> (this build may no longer talk to
    /// this server) and any <c>5xx</c>, which is not a refusal at all.
    /// </para>
    /// </remarks>
    public static bool KeepsTheDialogOpen(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => true,
        HttpStatusCode.UnprocessableEntity => true,
        HttpStatusCode.Conflict => true,
        HttpStatusCode.Forbidden => true,
        _ => false,
    };

    /// <summary>
    /// The field a refusal is about, when it is about one this dialog has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The domain writes <c>"firstName must be at most 128 characters."</c> and a
    /// body that could not be read is refused as <c>"'personId' could not be read.
    /// Expected an identifier."</c> (<c>AOS-R002-008</c>). Both name the field the
    /// caller sent, first.
    /// </para>
    /// <para>
    /// Matching is exact against the names the dialog declares. A refusal about the
    /// whole submission — <c>"This target has not been approved yet."</c> — matches
    /// nothing and is shown without being pinned to a control, because attaching it
    /// to one would tell a screen-reader user something false.
    /// </para>
    /// </remarks>
    public static string? FieldNamed(string? detail, IReadOnlyCollection<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (string.IsNullOrWhiteSpace(detail) || fields.Count == 0)
        {
            return null;
        }

        string candidate = LeadingToken(detail);

        return candidate.Length == 0
            ? null
            : fields.FirstOrDefault(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The first word, or the first quoted word when the sentence opens with one.</summary>
    private static string LeadingToken(string detail)
    {
        string trimmed = detail.TrimStart();

        if (trimmed.StartsWith('\''))
        {
            int close = trimmed.IndexOf('\'', 1);

            if (close > 1)
            {
                return trimmed[1..close];
            }
        }

        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        string word = space < 0 ? trimmed : trimmed[..space];

        return word.Trim('\'', '"', '.', ',', ':', ';');
    }
}
