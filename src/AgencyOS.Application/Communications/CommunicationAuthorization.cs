using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Communications;

/// <summary>
/// Decides who may read and send from a mailbox.
/// </summary>
/// <remarks>
/// <para>
/// A mailbox is one person's correspondence, and sharing a tenant with them is not
/// a reason to read it. So mailbox access turns on <strong>ownership</strong> first
/// and on grants second: the owner reads their own mail, an explicitly shared
/// agency mailbox is readable by anybody who may read communications, and reading
/// somebody else's private mailbox needs a grant that exists for exactly that
/// (ADR-0026).
/// </para>
/// <para>
/// A link changes none of this. An email linked to a deal is evidence about the
/// deal; it is still the mailbox owner's correspondence, and it may contain three
/// paragraphs about something else entirely.
/// </para>
/// </remarks>
public sealed class CommunicationAuthorization
{
    private readonly TenantGuard _guard;

    public CommunicationAuthorization(TenantGuard guard) => _guard = guard;

    /// <summary>Authorizes the communications surface at all.</summary>
    public Task<UserId> AuthorizeReadAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.CommunicationsRead, organizationId, cancellationToken);

    /// <summary>
    /// Authorizes reading one mailbox.
    /// </summary>
    /// <remarks>
    /// The owner always may. Everybody else needs either a mailbox the agency has
    /// deliberately shared, or the grant that says they may read other people's.
    /// </remarks>
    public async Task<UserId> AuthorizeReadAsync(
        CommunicationAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        UserId actor = await AuthorizeReadAsync(account.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (account.OwnerUserId == actor || account.Visibility == MailboxVisibility.Shared)
        {
            return actor;
        }

        await _guard
            .AuthorizeAsync(
                Permission.CommunicationsSharedRead, account.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        return actor;
    }

    /// <summary>
    /// Whether the caller may read one mailbox, without refusing if they may not.
    /// </summary>
    /// <remarks>
    /// The same rule as the authorizing overload, asked as a question. It exists so
    /// a message in a mailbox the caller cannot read can be reported as absent
    /// rather than as forbidden: for correspondence, confirming that a message
    /// exists is already a disclosure about who is talking to whom (ADR-0026).
    /// </remarks>
    public async Task<bool> CanReadAsync(
        CommunicationAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        UserId actor = await AuthorizeReadAsync(account.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        return account.OwnerUserId == actor
            || account.Visibility == MailboxVisibility.Shared
            || await _guard
                .HasPermissionAsync(
                    Permission.CommunicationsSharedRead,
                    account.OrganizationId,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Authorizes sending from one mailbox.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions, both server-side. The caller must hold the send grant, and
    /// the mailbox must be theirs: knowing an account identifier is not authority to
    /// send as its owner, and a From address is never taken from the request
    /// (ADR-0028).
    /// </para>
    /// <para>
    /// A shared agency mailbox is deliberately not enough on its own. Reading the
    /// agency inbox and writing to a client as the agency are different acts.
    /// </para>
    /// </remarks>
    public async Task<UserId> AuthorizeSendAsync(
        CommunicationAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.CommunicationsSend, account.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (account.OwnerUserId != actor)
        {
            // Deliberately reported as the missing permission rather than as a
            // missing mailbox. Distinguishing the two would tell a caller which
            // account identifiers are real.
            throw new PermissionDeniedException(Permission.CommunicationsSend);
        }

        if (!account.IsUsable)
        {
            throw new Domain.Common.DomainException(
                $"That mailbox is {account.State.ToString().ToLowerInvariant()} and cannot send.");
        }

        return actor;
    }

    /// <summary>Authorizes connecting or disconnecting a mailbox.</summary>
    public Task<UserId> AuthorizeManageAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(
            Permission.CommunicationsAccountManage, organizationId, cancellationToken);

    /// <summary>Whether the caller may read mailboxes other than their own.</summary>
    /// <remarks>
    /// Used to narrow a message list <em>in the query</em>, before counting or
    /// paging. Filtering afterwards would leak how many messages exist in a mailbox
    /// the caller cannot open, and a count of a colleague's correspondence with a
    /// named client is itself a disclosure.
    /// </remarks>
    public Task<bool> CanReadOtherMailboxesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(
            Permission.CommunicationsSharedRead, organizationId, cancellationToken);
}
