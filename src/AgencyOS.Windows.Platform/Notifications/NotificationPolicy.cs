using AgencyOS.Windows.Platform.Activation;

namespace AgencyOS.Windows.Platform.Notifications;

/// <summary>
/// The kinds of thing AgencyOS will interrupt somebody about.
/// </summary>
/// <remarks>
/// A closed set, and every member corresponds to a work queue the server already
/// computes. Nothing here is derived on the client: a notification is a view of
/// canonical state, and inventing one locally would mean the workstation could
/// tell somebody a deadline had passed that the server did not agree had passed
/// (ADR-0034).
/// </remarks>
public enum NotificationCategory
{
    TaskDue = 1,
    LegalDeadline,
    OptionDeadline,
    ReceivableOverdue,
    AiApprovalAwaiting,
    AiRunCompleted,
    AiRunFailed,
}

/// <summary>
/// How sensitive the underlying material is, as the server classified it.
/// </summary>
/// <remarks>
/// Mirrors the server's transmission scale rather than re-deriving it. The client
/// is told; it does not decide. A workstation that classified its own toasts
/// would be a workstation that could be wrong about it (ADR-0034).
/// </remarks>
public enum NotificationSensitivity
{
    Internal = 1,
    Confidential = 2,
    Protected = 3,
    Restricted = 4,
}

/// <summary>How much a user has asked to see on the lock screen.</summary>
public enum NotificationDetailPreference
{
    /// <summary>Never say what it is about. The default.</summary>
    Generic = 0,

    /// <summary>Name the subject, where policy also allows it.</summary>
    Detailed = 1,
}

/// <summary>
/// What the server says needs attention.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately <strong>no amount field, at any sensitivity</strong>.
/// §8 says no finance amounts in notifications by default; a field that could
/// carry one would make that a setting somebody can get wrong, and a toast is
/// rendered on a lock screen in a coffee shop. Leaving money out of the type is
/// stronger than leaving it out of the policy (ADR-0034).
/// </para>
/// <para>
/// <paramref name="Subject"/> is a display name — a person, a contract title, a
/// deal. It reaches a toast only when both the user and the organization have
/// asked for detail and the material is not classified above Confidential.
/// </para>
/// </remarks>
/// <param name="Route">The canonical object, so clicking opens the right thing.</param>
public sealed record NotificationRequest(
    NotificationCategory Category,
    NotificationSensitivity Sensitivity,
    ActivationRouteKind Route,
    Guid Id,
    string? Subject = null);

/// <summary>
/// What a toast is permitted to say, and where clicking it goes.
/// </summary>
/// <param name="Title">Bounded. Safe to render on a locked screen.</param>
/// <param name="Body">Bounded, and empty when policy allows nothing beyond the title.</param>
/// <param name="Link">
/// The deep link. Carries a kind and an identifier and nothing else — no action,
/// no token and no grant.
/// </param>
/// <param name="IsDetailed">Whether the subject was named. Asserted by tests.</param>
public sealed record NotificationContent(
    string Title,
    string Body,
    string Link,
    bool IsDetailed);

/// <summary>
/// Decides what a Windows notification may say.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Conservative by default and by construction.</strong> A toast surfaces
/// on a lock screen, in a shared meeting room, on a screen being mirrored. The
/// user having permission to read something inside AgencyOS is not permission to
/// render it there, which is the same distinction M12 drew between reading a
/// record and transmitting it to a provider (ADR-0031, ADR-0034).
/// </para>
/// <para>
/// Detail requires three independent yeses: the user asked for it, the
/// organization permits it, and the material is not classified above
/// Confidential. Any one missing yields the generic form.
/// </para>
/// <para>
/// The payload never carries the content itself. It carries a link, and clicking
/// it re-authorizes against the server — so a toast that outlived a permission
/// change opens nothing.
/// </para>
/// </remarks>
public static class NotificationPolicy
{
    /// <summary>What every notification says when it may not say more.</summary>
    public const string GenericTitle = "AgencyOS";

    /// <summary>The generic body. Names nothing and implies nothing.</summary>
    public const string GenericBody = "You have an item requiring attention.";

    /// <summary>The longest subject a toast will render.</summary>
    /// <remarks>
    /// Windows truncates for us, but a bounded value is what makes the test
    /// meaningful and stops a pathological record title becoming the whole toast.
    /// </remarks>
    public const int MaximumSubjectLength = 80;

    /// <summary>
    /// The classification above which nothing is ever named.
    /// </summary>
    /// <remarks>
    /// Protected covers M11 source-sensitive material and M10 privileged
    /// documents. Naming the subject of either on a lock screen discloses the very
    /// thing the classification exists to protect — who spoke, or that a matter is
    /// with lawyers.
    /// </remarks>
    public const NotificationSensitivity DetailCeiling = NotificationSensitivity.Confidential;

    /// <summary>Builds what may be shown.</summary>
    public static NotificationContent Compose(
        NotificationRequest request,
        NotificationDetailPreference preference,
        bool organizationAllowsDetail)
    {
        ArgumentNullException.ThrowIfNull(request);

        string link = ActivationRouter.Link(request.Route, request.Id);

        bool mayDetail =
            preference == NotificationDetailPreference.Detailed
            && organizationAllowsDetail
            && request.Sensitivity <= DetailCeiling
            && !string.IsNullOrWhiteSpace(request.Subject);

        if (!mayDetail)
        {
            return new NotificationContent(GenericTitle, GenericBody, link, IsDetailed: false);
        }

        return new NotificationContent(
            Headline(request.Category),
            Bound(request.Subject!),
            link,
            IsDetailed: true);
    }

    /// <summary>
    /// What the category is called, in words that name no record.
    /// </summary>
    /// <remarks>
    /// Safe at every classification: it says what kind of work is waiting, not
    /// whose. "An approval is waiting for you" discloses that the reader uses AI,
    /// which is not a confidence anybody is keeping.
    /// </remarks>
    public static string Headline(NotificationCategory category) => category switch
    {
        NotificationCategory.TaskDue => "A task is due",
        NotificationCategory.LegalDeadline => "A contract deadline is approaching",
        NotificationCategory.OptionDeadline => "An option deadline is approaching",
        NotificationCategory.ReceivableOverdue => "A receivable is overdue",
        NotificationCategory.AiApprovalAwaiting => "An approval is waiting for you",
        NotificationCategory.AiRunCompleted => "An AI task finished",
        NotificationCategory.AiRunFailed => "An AI task did not finish",
        _ => GenericTitle,
    };

    private static string Bound(string subject)
    {
        string trimmed = subject.Trim();

        // Control characters are stripped rather than escaped. A record title is
        // text somebody typed, and a newline in a toast is a way to push the rest
        // of the notification out of view.
        string clean = new([.. trimmed.Where(c => !char.IsControl(c))]);

        return clean.Length <= MaximumSubjectLength
            ? clean
            : clean[..MaximumSubjectLength].TrimEnd() + "…";
    }
}
