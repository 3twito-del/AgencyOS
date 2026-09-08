using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Communications;

/// <summary>What one synchronization pass did.</summary>
/// <param name="CursorReset">
/// Whether the provider rejected the cursor and the mailbox was resynchronized from
/// the beginning. Not a failure: it is the provider saying "start again".
/// </param>
public sealed record MailboxSyncResult(
    int MessagesSeen,
    int MessagesCreated,
    int MessagesUpdated,
    bool CursorReset,
    string? Error = null);

/// <summary>
/// Pulls what has changed in a connected mailbox.
/// </summary>
/// <remarks>
/// <para>
/// Delta-based, because the alternative is wrong. Asking for everything received
/// since a timestamp misses messages the provider back-dated, re-reads every
/// message whose flags changed, and has no answer at all for a message that moved
/// folders. The provider's own cursor is its answer to "what is different", and it
/// is the only correct one (ADR-0026).
/// </para>
/// <para>
/// Every write is keyed on the provider's message identifier within the account, so
/// synchronizing twice produces the same rows. A provider will hand back the same
/// message after a cursor reset, after a move, and sometimes for no reason at all;
/// without that key, one email becomes six.
/// </para>
/// </remarks>
public sealed class MailboxSynchronizer
{
    private const int PageSize = 50;
    private const int MaximumPages = 20;

    private readonly ICommunicationProviderRegistry _providers;
    private readonly ICommunicationMessageRepository _messages;
    private readonly ICommunicationThreadRepository _threads;
    private readonly ICommunicationEventRepository _events;
    private readonly ISecretProtector _secrets;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public MailboxSynchronizer(
        ICommunicationProviderRegistry providers,
        ICommunicationMessageRepository messages,
        ICommunicationThreadRepository threads,
        ICommunicationEventRepository events,
        ISecretProtector secrets,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _providers = providers;
        _messages = messages;
        _threads = threads;
        _events = events;
        _secrets = secrets;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Runs one synchronization pass for a mailbox.
    /// </summary>
    /// <remarks>
    /// Bounded to a fixed number of pages per pass. An unbounded loop over a mailbox
    /// with two hundred thousand messages would hold a lease and a transaction for
    /// as long as it took; stopping and saving the cursor means the next pass picks
    /// up exactly where this one left off (ADR-0029).
    /// </remarks>
    public async Task<MailboxSyncResult> SynchronizeAsync(
        CommunicationAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.IsUsable || account.ProtectedRefreshToken is not { } protectedToken)
        {
            return new MailboxSyncResult(0, 0, 0, false, "The mailbox is not connected.");
        }

        if (!_providers.Supports(account.Provider))
        {
            return new MailboxSyncResult(
                0, 0, 0, false, $"No adapter is configured for {account.Provider}.");
        }

        string refreshToken;

        try
        {
            refreshToken = _secrets.Unprotect(protectedToken);
        }
        catch (SecretProtectionException)
        {
            // The stored credential can no longer be read: a rotated or lost key.
            // The only cure is a person reconnecting, so the account says so rather
            // than retrying for ever.
            account.RecordSyncFailure(
                "The stored credential could not be read. Reconnect the mailbox.",
                requiresReauthorization: true,
                _clock.UtcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new MailboxSyncResult(0, 0, 0, false, "The stored credential could not be read.");
        }

        ICommunicationProvider provider = _providers.Resolve(account.Provider);

        int seen = 0;
        int created = 0;
        int updated = 0;
        bool reset = false;
        string? cursor = account.DeltaCursor;

        try
        {
            for (int page = 0; page < MaximumPages; page++)
            {
                ProviderSyncPage result = await provider
                    .SyncAsync(refreshToken, cursor, PageSize, cancellationToken)
                    .ConfigureAwait(false);

                if (result.CursorExpired)
                {
                    // The provider will not honour the cursor. Starting again is the
                    // correct response and is not an error; idempotent writes make
                    // the full pass harmless (ADR-0026).
                    account.ResetCursor(_clock.UtcNow);
                    cursor = null;
                    reset = true;

                    continue;
                }

                foreach (ProviderMessage message in result.Messages)
                {
                    seen++;

                    if (await ApplyAsync(account, message, cancellationToken).ConfigureAwait(false))
                    {
                        created++;
                    }
                    else
                    {
                        updated++;
                    }
                }

                cursor = result.NextCursor ?? cursor;

                if (!result.HasMore)
                {
                    break;
                }
            }

            account.RecordSync(cursor, _clock.UtcNow);

            _events.Add(CommunicationEvent.Record(
                account.OrganizationId,
                CommunicationEventKind.MailboxSynchronized,
                $"Synchronized {account.MailboxAddress}",
                _clock.UtcNow,
                accountId: account.Id,

                // Counts and shape. Never a subject, never a sender, never a body:
                // the history is read by people who may not read the mailbox.
                detail: $"{created} new, {updated} updated{(reset ? ", cursor reset" : string.Empty)}"));

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new MailboxSyncResult(seen, created, updated, reset);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            bool reauthorize = failure is ProviderAuthorizationException;

            account.RecordSyncFailure(Describe(failure), reauthorize, _clock.UtcNow);

            _events.Add(CommunicationEvent.Record(
                account.OrganizationId,
                CommunicationEventKind.SyncFailed,
                $"Could not synchronize {account.MailboxAddress}",
                _clock.UtcNow,
                accountId: account.Id,
                detail: Describe(failure)));

            await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            return new MailboxSyncResult(seen, created, updated, reset, Describe(failure));
        }
    }

    /// <summary>Writes one provider message, creating or updating.</summary>
    /// <returns>True when a new row was created.</returns>
    private async Task<bool> ApplyAsync(
        CommunicationAccount account,
        ProviderMessage message,
        CancellationToken cancellationToken)
    {
        CommunicationMessage? existing = await _messages
            .FindByExternalIdAsync(
                account.OrganizationId, account.Id, message.ExternalMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // A later view of the same message. Folder moves, deletions and subject
            // edits are updates, and treating them as arrivals is how a delta sync
            // turns one email into a thread of duplicates.
            existing.ApplyProviderUpdate(
                _clock.UtcNow, message.Folder, message.IsDeleted, message.Subject);

            return false;
        }

        if (message.IsDeleted)
        {
            // A deletion for something never synchronized. Nothing to record: a row
            // saying "a message we never saw is gone" is noise.
            return false;
        }

        CommunicationThreadId? threadId = await ResolveThreadAsync(
            account, message, cancellationToken).ConfigureAwait(false);

        string? sanitized = MessageSanitizer.Sanitize(message.BodyHtml);
        string? bodyText = message.BodyText ?? MessageSanitizer.ToPlainText(message.BodyHtml);

        CommunicationMessage created = CommunicationMessage.Record(
            account.OrganizationId,
            account.Id,
            message.ExternalMessageId,
            message.Direction,
            _clock.UtcNow,
            threadId,
            message.InternetMessageId,
            message.Subject,
            bodyText,
            sanitized,
            message.SentAt,
            message.ReceivedAt,
            message.Folder,
            message.Attachments.Count > 0);

        foreach (ProviderParticipant participant in message.Participants)
        {
            // The raw address and name, exactly as they appeared. Any AgencyOS
            // identification is a separate, explicit act (ADR-0026).
            created.AddParticipant(
                participant.Role, participant.Address, participant.DisplayName);
        }

        foreach (ProviderAttachment attachment in message.Attachments)
        {
            // Metadata only. The bytes stay at the provider until somebody asks for
            // them, because pulling every attachment on sight would move gigabytes
            // of unrequested video through the server.
            created.AddAttachment(
                attachment.ExternalAttachmentId,
                attachment.FileName,
                attachment.MediaType,
                attachment.ByteLength,
                attachment.IsInline);
        }

        _messages.Add(created);

        return true;
    }

    private async Task<CommunicationThreadId?> ResolveThreadAsync(
        CommunicationAccount account,
        ProviderMessage message,
        CancellationToken cancellationToken)
    {
        if (message.ExternalThreadId is not { Length: > 0 } externalThreadId)
        {
            // No provider conversation identifier means no thread. AgencyOS does not
            // group by subject line: two unrelated "Re: contract" exchanges are not
            // one conversation (ADR-0026).
            return null;
        }

        CommunicationThread? thread = await _threads
            .FindByExternalIdAsync(
                account.OrganizationId, account.Id, externalThreadId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset occurredAt = message.SentAt ?? message.ReceivedAt ?? _clock.UtcNow;

        if (thread is null)
        {
            thread = CommunicationThread.Open(
                account.OrganizationId, account.Id, externalThreadId, occurredAt, message.Subject);

            _threads.Add(thread);
        }

        thread.NoteMessage(occurredAt);

        return thread.Id;
    }

    /// <summary>Describes a failure without leaking a token, a URL or a body.</summary>
    private static string Describe(Exception failure) => failure switch
    {
        ProviderAuthorizationException => "The provider refused the stored credential.",
        ProviderTransientException transient => transient.Message,
        _ => $"The provider call failed ({failure.GetType().Name}).",
    };
}

/// <summary>Raised when a provider refuses a credential.</summary>
/// <remarks>
/// Distinguished from every other failure because it is the one a retry cannot fix:
/// a person must reconnect the mailbox. The message deliberately carries no token
/// and no fragment of one (ADR-0027).
/// </remarks>
public sealed class ProviderAuthorizationException : Exception
{
    public ProviderAuthorizationException(string message = "The provider refused the credential.")
        : base(message)
    {
    }
}

/// <summary>Raised for a provider failure worth retrying.</summary>
public sealed class ProviderTransientException : Exception
{
    public ProviderTransientException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
