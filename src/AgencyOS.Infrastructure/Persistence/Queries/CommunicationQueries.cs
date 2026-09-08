using AgencyOS.Application.Documents;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for communications.
/// </summary>
/// <remarks>
/// Every list is narrowed to the mailboxes the caller may read, <strong>inside the
/// query</strong>. A post-filter would produce correct rows and an incorrect count,
/// and the count of a colleague's correspondence with a named client is a
/// disclosure of its own (ADR-0026).
/// </remarks>
public sealed class CommunicationQueries : ICommunicationQueries
{
    private readonly AgencyOsDbContext _context;

    public CommunicationQueries(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunicationAccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        List<CommunicationAccount> accounts = await _context.CommunicationAccounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.MailboxAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> counts = await _context.CommunicationMessages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId.Value, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. accounts.Select(x => ToAccount(x, counts, users))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListMessagesAsync(
        OrganizationId organizationId,
        CommunicationQueryScope scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<CommunicationMessage> query = Narrow(organizationId, scope.ReadableAccountIds);

        CommunicationFilter filter = scope.Filter;

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(x => x.AccountId == accountId);
        }

        if (filter.Direction is { } direction)
        {
            query = query.Where(x => x.Direction == direction);
        }

        if (filter.HasAttachments)
        {
            query = query.Where(x => x.HasAttachments);
        }

        if (filter.UnlinkedOnly)
        {
            query = query.Where(x => x.Links.Count == 0);
        }

        if (filter.LinkedTarget is { } target)
        {
            query = filter.LinkedTargetId is { } targetId
                ? query.Where(x => x.Links.Any(l => l.Target == target && l.TargetId == targetId))
                : query.Where(x => x.Links.Any(l => l.Target == target));
        }

        if (filter.OccurredAfter is { } after)
        {
            DateTimeOffset from = new(after.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => (x.SentAt ?? x.ReceivedAt ?? x.SynchronizedAt) >= from);
        }

        if (filter.OccurredBefore is { } before)
        {
            DateTimeOffset to = new(before.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(x => (x.SentAt ?? x.ReceivedAt ?? x.SynchronizedAt) <= to);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            string pattern = $"%{search}%";

            // Subject and participant addresses. Deliberately never the body: a
            // match inside a message would report its contents, and a message body
            // is somebody's correspondence (ADR-0026).
            query = query.Where(x =>
                (x.Subject != null && EF.Functions.ILike(x.Subject, pattern))
                || x.Participants.Any(p => EF.Functions.ILike(p.Address, pattern)));
        }

        List<CommunicationMessage> messages = await query
            .Include(x => x.Participants)
            .Include(x => x.Attachments)
            .Include(x => x.Links)
            .OrderByDescending(x => x.SentAt ?? x.ReceivedAt ?? x.SynchronizedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> mailboxes = await LoadMailboxesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return [.. messages.Select(x => ToSummary(x, mailboxes))];
    }

    /// <inheritdoc />
    public async Task<CommunicationMessageDetailModel?> GetMessageAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken = default)
    {
        CommunicationMessage? message = await _context.CommunicationMessages
            .AsNoTracking()
            .Include(x => x.Participants)
            .Include(x => x.Attachments)
            .Include(x => x.Links)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return null;
        }

        Dictionary<Guid, string> mailboxes = await LoadMailboxesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> people = await LoadPeopleAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await LoadCompaniesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        List<CommunicationMessage> thread = message.ThreadId is { } threadId
            ? await _context.CommunicationMessages
                .AsNoTracking()
                .Include(x => x.Participants)
                .Include(x => x.Attachments)
                .Include(x => x.Links)
                .Where(x => x.ThreadId == threadId)
                .OrderBy(x => x.SentAt ?? x.ReceivedAt ?? x.SynchronizedAt)
                .Take(100)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];

        IReadOnlyDictionary<(DocumentLinkTarget, Guid), string> labels = await LinkLabels
            .DescribeAsync(
                _context,
                organizationId,
                [.. message.Links.Select(x => (x.Target, x.TargetId))],
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, Guid> documentsByVersion = await LoadDocumentsByVersionAsync(
            organizationId,
            [.. message.Attachments
                .Where(x => x.DocumentVersionId is not null)
                .Select(x => x.DocumentVersionId!.Value.Value)],
            cancellationToken)
            .ConfigureAwait(false);

        return new CommunicationMessageDetailModel(
            ToSummary(message, mailboxes),
            message.BodyText,

            // Already sanitized on the way in. Nothing here re-parses provider HTML,
            // because a second parser is a second chance to get it wrong (ADR-0026).
            message.SanitizedHtml,
            message.InternetMessageId,
            message.ExternalMessageId,
            [
                .. message.Participants.Select(x => new CommunicationParticipantModel(
                    x.Id,
                    x.Role,
                    x.Address,
                    x.DisplayName,
                    x.PersonId,
                    x.PersonId is { } personId ? people.GetValueOrDefault(personId) : null,
                    x.CompanyId,
                    x.CompanyId is { } companyId ? companies.GetValueOrDefault(companyId) : null,
                    x.ResolvedBy is { } resolvedBy
                        ? users.GetValueOrDefault(resolvedBy.Value)
                        : null)),
            ],
            [
                .. message.Attachments.Select(x => new CommunicationAttachmentModel(
                    x.Id,
                    x.FileName,
                    x.MediaType,
                    x.ByteLength,
                    x.IsInline,
                    x.HoldsContent,
                    x.DocumentVersionId,
                    x.DocumentVersionId is { } versionId
                        && documentsByVersion.TryGetValue(versionId.Value, out Guid documentId)
                            ? new DocumentId(documentId)
                            : null,
                    x.IngestedAt)),
            ],
            [
                .. message.Links.Select(x => new CommunicationLinkModel(
                    x.Id,
                    x.Target,
                    x.TargetId,
                    labels.GetValueOrDefault((x.Target, x.TargetId), x.Target.ToString()),
                    x.Note,
                    x.LinkedAt,
                    users.GetValueOrDefault(x.LinkedBy.Value))),
            ],
            [.. thread.Select(x => ToSummary(x, mailboxes))]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboundDispatchModel>> ListDispatchesAsync(
        OrganizationId organizationId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        OutboundDispatchState? state,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readableAccountIds);

        CommunicationAccountId[] accounts = [.. readableAccountIds];

        IQueryable<OutboundDispatch> query = _context.OutboundDispatches
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && accounts.Contains(x.AccountId));

        if (state is { } wanted)
        {
            query = query.Where(x => x.State == wanted);
        }

        List<OutboundDispatch> dispatches = await query
            .Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> mailboxes = await LoadMailboxesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. dispatches.Select(x => ToDispatch(x, mailboxes, users))];
    }

    /// <inheritdoc />
    public async Task<OutboundDispatchModel?> GetDispatchAsync(
        OrganizationId organizationId,
        OutboundDispatchId id,
        CancellationToken cancellationToken = default)
    {
        OutboundDispatch? dispatch = await _context.OutboundDispatches
            .AsNoTracking()
            .Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (dispatch is null)
        {
            return null;
        }

        Dictionary<Guid, string> mailboxes = await LoadMailboxesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(cancellationToken)
            .ConfigureAwait(false);

        return ToDispatch(dispatch, mailboxes, users);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        CancellationToken cancellationToken = default)
    {
        List<CommunicationMessage> messages = await Narrow(organizationId, readableAccountIds)
            .Where(x => x.Links.Any(l => l.Target == target && l.TargetId == targetId))
            .Include(x => x.Participants)
            .Include(x => x.Attachments)
            .Include(x => x.Links)
            .OrderByDescending(x => x.SentAt ?? x.ReceivedAt ?? x.SynchronizedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> mailboxes = await LoadMailboxesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return [.. messages.Select(x => ToSummary(x, mailboxes))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunicationEventModel>> GetHistoryAsync(
        OrganizationId organizationId,
        CommunicationAccountId? accountId,
        OutboundDispatchId? dispatchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CommunicationEvent> query = _context.CommunicationEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (accountId is { } account)
        {
            query = query.Where(x => x.AccountId == account);
        }

        if (dispatchId is { } dispatch)
        {
            query = query.Where(x => x.DispatchId == dispatch);
        }

        List<CommunicationEvent> events = await query
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. events.Select(x => new CommunicationEventModel(
                x.OccurredAt,
                x.Kind,
                x.Summary,
                x.Detail,
                x.ActorUserId is { } actor ? users.GetValueOrDefault(actor.Value) : null)),
        ];
    }

    /// <inheritdoc />
    public async Task<CommunicationCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OutboundDispatchModel> unknown = await ListDispatchesAsync(
            organizationId, readableAccountIds, OutboundDispatchState.UnknownOutcome, 50,
            cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OutboundDispatchModel> failed = await ListDispatchesAsync(
            organizationId, readableAccountIds, OutboundDispatchState.FailedPermanent, 50,
            cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<CommunicationAccountModel> accounts = await ListAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        CommunicationAccountModel[] needingAttention =
        [
            .. accounts.Where(x =>
                readableAccountIds.Contains(x.Id)
                && x.State is CommunicationAccountState.ReauthorizationRequired
                    or CommunicationAccountState.Error),
        ];

        return new CommunicationCommandCenterModel(
            unknown,
            failed,
            needingAttention,
            unknown.Count,
            failed.Count,
            needingAttention.Length);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParticipantSuggestionModel>> SuggestParticipantsAsync(
        OrganizationId organizationId,
        string address,
        CancellationToken cancellationToken = default)
    {
        string normalized = (address ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return [];
        }

        // Exact matches only. A fuzzy match on an email address would suggest that
        // "j.smith@studio.test" is "jsmith@studio.test", and a wrong identification
        // quietly attributes somebody's correspondence to the wrong person
        // (ADR-0026).
        var people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.Email != null
                && x.Email.ToLower() == normalized)
            .Select(x => new { x.Id, x.DisplayName, Email = x.Email! })
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int total = people.Count;

        return
        [
            .. people.Select(x => new ParticipantSuggestionModel(
                x.Id.Value, null, x.DisplayName, x.Email, IsUnambiguous: total == 1)),
        ];
    }

    private IQueryable<CommunicationMessage> Narrow(
        OrganizationId organizationId,
        IReadOnlySet<CommunicationAccountId> readable)
    {
        CommunicationAccountId[] accounts = [.. readable];

        return _context.CommunicationMessages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && accounts.Contains(x.AccountId));
    }

    private static CommunicationAccountModel ToAccount(
        CommunicationAccount account,
        Dictionary<Guid, int> counts,
        Dictionary<Guid, string> users) =>
        new(
            account.Id,
            account.Provider,
            account.MailboxAddress,
            account.DisplayName,
            account.OwnerUserId.Value,
            users.GetValueOrDefault(account.OwnerUserId.Value),
            account.State,
            account.Visibility,
            account.GrantedScopes,
            account.LastSyncedAt,
            account.LastSyncError,

            // Whether a credential exists, never the credential. The projection has
            // no field that could hold one (ADR-0027).
            account.ProtectedRefreshToken is not null,
            account.CredentialExpiresAt,
            counts.GetValueOrDefault(account.Id.Value),
            account.CreatedAt,
            account.Version);

    private static CommunicationMessageSummaryModel ToSummary(
        CommunicationMessage message,
        Dictionary<Guid, string> mailboxes)
    {
        CommunicationParticipant? from = message.Participants
            .FirstOrDefault(x => x.Role == ParticipantRole.From);

        return new CommunicationMessageSummaryModel(
            message.Id,
            message.AccountId,
            mailboxes.GetValueOrDefault(message.AccountId.Value, string.Empty),
            message.Direction,
            message.Subject,
            from?.Address,
            from?.DisplayName,
            [
                .. message.Participants
                    .Where(x => x.Role == ParticipantRole.To)
                    .Select(x => x.Address),
            ],
            message.OccurredAt,
            message.SynchronizedAt,
            message.HasAttachments,
            message.Attachments.Count,
            message.Links.Count,
            message.IsDeletedAtProvider,
            message.Folder);
    }

    private static OutboundDispatchModel ToDispatch(
        OutboundDispatch dispatch,
        Dictionary<Guid, string> mailboxes,
        Dictionary<Guid, string> users) =>
        new(
            dispatch.Id,
            dispatch.AccountId,
            mailboxes.GetValueOrDefault(dispatch.AccountId.Value, string.Empty),
            dispatch.State,
            dispatch.Subject,
            [
                .. dispatch.Recipients.Select(x => new CommunicationParticipantModel(
                    x.Id, x.Role, x.Address, x.DisplayName, null, null, null, null, null)),
            ],
            [
                .. dispatch.Attachments.Select(x => new CommunicationAttachmentSummaryModel(
                    x.DocumentId, x.DocumentVersionId, x.FileName, x.MediaType, x.ByteLength)),
            ],
            dispatch.AttemptCount,
            dispatch.LastError,
            dispatch.LastVerdict,
            dispatch.LastReconciledAt,

            // Whether a draft and a confirmed message exist, never their provider
            // identifiers: those are the provider's namespace and mean nothing to a
            // reader.
            dispatch.ProviderDraftId is not null,
            dispatch.ProviderMessageId is not null,
            dispatch.SentMessageId,
            dispatch.SentAt,
            dispatch.CreatedAt,
            dispatch.UpdatedAt,
            users.GetValueOrDefault(dispatch.CreatedBy.Value),
            dispatch.NeedsAttention,
            dispatch.Version);

    private async Task<Dictionary<Guid, string>> LoadMailboxesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        await _context.CommunicationAccounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.MailboxAddress })
            .ToDictionaryAsync(x => x.Id.Value, x => x.MailboxAddress, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, string>> LoadPeopleAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, string>> LoadCompaniesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id.Value, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, Guid>> LoadDocumentsByVersionAsync(
        OrganizationId organizationId,
        Guid[] versionIds,
        CancellationToken cancellationToken)
    {
        DocumentVersionId[] keys = [.. versionIds.Select(x => new DocumentVersionId(x))];

        return versionIds.Length == 0
            ? []
            : await _context.DocumentVersions
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
                .Select(x => new { x.Id, x.DocumentId })
                .ToDictionaryAsync(x => x.Id.Value, x => x.DocumentId.Value, cancellationToken)
                .ConfigureAwait(false);
    }
}
