using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Communications;

/// <summary>Opaque, immutable identifier for a <see cref="CommunicationAccount"/>.</summary>
public readonly record struct CommunicationAccountId(Guid Value)
{
    public static CommunicationAccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Which external system a mailbox lives in.
/// </summary>
/// <remarks>
/// Provider-neutral by design. The domain names the provider and nothing else about
/// it: no Graph vocabulary, no Graph identifiers, no Graph-shaped state reaches the
/// aggregates (ADR-0026).
/// </remarks>
public enum CommunicationProviderKind
{
    /// <summary>Microsoft 365 mail, through Microsoft Graph.</summary>
    MicrosoftGraph = 1,

    /// <summary>A deterministic in-process provider, used by tests.</summary>
    /// <remarks>
    /// A first-class member rather than a test-only hack. CI has no Microsoft
    /// tenant, so the protocol AgencyOS actually depends on is exercised against
    /// this one, and the model says so plainly rather than pretending otherwise.
    /// </remarks>
    Fake = 99,
}

/// <summary>Where a connected mailbox stands.</summary>
public enum CommunicationAccountState
{
    /// <summary>Usable: credentials are present and the provider accepted them.</summary>
    Connected = 1,

    /// <summary>The owner disconnected it. Credentials are destroyed.</summary>
    Disconnected = 2,

    /// <summary>The provider refused the credentials. A person must reconnect.</summary>
    ReauthorizationRequired = 3,

    /// <summary>Synchronization is failing for a reason that is not authorization.</summary>
    Error = 4,
}

/// <summary>
/// Who may read a mailbox's messages.
/// </summary>
/// <remarks>
/// A connected mailbox is one person's correspondence. Sharing a tenant with
/// somebody is not a reason to read their mail, so the default is private and
/// anything else is an explicit act (ADR-0026).
/// </remarks>
public enum MailboxVisibility
{
    /// <summary>Only the owner, and holders of the shared-read grant, may read it.</summary>
    Private = 1,

    /// <summary>
    /// A shared agency mailbox: anybody who may read communications may read it.
    /// </summary>
    Shared = 2,
}

/// <summary>
/// A mailbox AgencyOS has been given access to.
/// </summary>
/// <remarks>
/// <para>
/// The credentials are the point of this aggregate and are the reason it is
/// careful. The refresh token is held encrypted, is never returned by any endpoint,
/// never reaches a log or a metric, and is destroyed on disconnect. The plaintext
/// exists only inside the provider adapter, for the length of one call (ADR-0027).
/// </para>
/// <para>
/// Mailbox OAuth is entirely separate from AgencyOS sign-in. Connecting a mailbox
/// does not change who the user is or how they authenticate, and AgencyOS did not
/// become an Entra application because it can read one person's mail.
/// </para>
/// </remarks>
public sealed class CommunicationAccount
{
    private CommunicationAccount()
    {
    }

    public CommunicationAccountId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The member whose mailbox this is.</summary>
    public UserId OwnerUserId { get; private set; }

    public CommunicationProviderKind Provider { get; private set; }

    /// <summary>The address as the provider states it.</summary>
    public string MailboxAddress { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    /// <summary>The provider's own identifier for the account.</summary>
    public string ExternalAccountId { get; private set; } = string.Empty;

    /// <summary>
    /// The scopes the connection was granted, recorded verbatim.
    /// </summary>
    /// <remarks>
    /// Kept so an operator can see what AgencyOS actually asked for. Least
    /// privilege is only checkable if the grant is visible.
    /// </remarks>
    public string GrantedScopes { get; private set; } = string.Empty;

    public CommunicationAccountState State { get; private set; }

    public MailboxVisibility Visibility { get; private set; }

    /// <summary>
    /// The provider's delta cursor, so the next sync asks for changes rather than
    /// for everything since a timestamp.
    /// </summary>
    /// <remarks>
    /// A timestamp comparison misses messages the provider back-dated and re-reads
    /// messages it did not change. The cursor is the provider's own answer to
    /// "what is different", and it is the only correct one (ADR-0026).
    /// </remarks>
    public string? DeltaCursor { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Why the last sync failed, when it did.</summary>
    public string? LastSyncError { get; private set; }

    /// <summary>
    /// The encrypted refresh token.
    /// </summary>
    /// <remarks>
    /// Ciphertext, produced by the server-side secret protector. Never mapped to
    /// any API contract, never serialized into an audit delta, never logged.
    /// </remarks>
    public string? ProtectedRefreshToken { get; private set; }

    /// <summary>When the provider says the current grant expires, if it says.</summary>
    public DateTimeOffset? CredentialExpiresAt { get; private set; }

    // ---- worker lease -------------------------------------------------------

    /// <summary>Which worker is currently synchronizing this mailbox.</summary>
    public string? SyncLeaseOwner { get; private set; }

    /// <summary>When the lease lapses, so a crashed worker does not hold it for ever.</summary>
    public DateTimeOffset? SyncLeaseExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    /// <summary>Whether the account can currently be used against the provider.</summary>
    public bool IsUsable => State == CommunicationAccountState.Connected;

    public static CommunicationAccount Connect(
        OrganizationId organizationId,
        UserId ownerUserId,
        CommunicationProviderKind provider,
        string mailboxAddress,
        string externalAccountId,
        string grantedScopes,
        string protectedRefreshToken,
        MailboxVisibility visibility,
        DateTimeOffset now,
        string? displayName = null,
        DateTimeOffset? credentialExpiresAt = null)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new DomainException($"'{provider}' is not a communication provider.");
        }

        return new CommunicationAccount
        {
            Id = CommunicationAccountId.New(),
            OrganizationId = organizationId,
            OwnerUserId = ownerUserId,
            Provider = provider,
            MailboxAddress = Ensure.NotBlankMax(mailboxAddress, nameof(mailboxAddress), 320),
            DisplayName = Ensure.OptionalMax(displayName, nameof(displayName), 200),
            ExternalAccountId = Ensure.NotBlankMax(externalAccountId, nameof(externalAccountId), 200),
            GrantedScopes = Ensure.NotBlankMax(grantedScopes, nameof(grantedScopes), 1000),
            State = CommunicationAccountState.Connected,
            Visibility = visibility,
            ProtectedRefreshToken =
                Ensure.NotBlankMax(protectedRefreshToken, nameof(protectedRefreshToken), 8000),
            CredentialExpiresAt = credentialExpiresAt,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Replaces the stored credential after a refresh.</summary>
    public void UpdateCredential(
        string protectedRefreshToken,
        DateTimeOffset now,
        DateTimeOffset? credentialExpiresAt = null)
    {
        ProtectedRefreshToken =
            Ensure.NotBlankMax(protectedRefreshToken, nameof(protectedRefreshToken), 8000);
        CredentialExpiresAt = credentialExpiresAt;
        State = CommunicationAccountState.Connected;
        LastSyncError = null;

        Touch(now);
    }

    /// <summary>
    /// Ends the connection and destroys the credential.
    /// </summary>
    /// <remarks>
    /// The token is cleared rather than kept "in case". A disconnected mailbox that
    /// still holds a usable refresh token is a mailbox AgencyOS could still read.
    /// </remarks>
    public void Disconnect(int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);

        State = CommunicationAccountState.Disconnected;
        ProtectedRefreshToken = null;
        CredentialExpiresAt = null;
        DeltaCursor = null;
        SyncLeaseOwner = null;
        SyncLeaseExpiresAt = null;

        Touch(now);
    }

    public void ChangeVisibility(MailboxVisibility visibility, int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);

        Visibility = visibility;
        Touch(now);
    }

    /// <summary>Records a completed synchronization and the cursor to resume from.</summary>
    public void RecordSync(string? deltaCursor, DateTimeOffset now)
    {
        DeltaCursor = Ensure.OptionalMax(deltaCursor, nameof(deltaCursor), 4000);
        LastSyncedAt = now;
        LastSyncError = null;

        if (State == CommunicationAccountState.Error)
        {
            State = CommunicationAccountState.Connected;
        }

        Touch(now);
    }

    /// <summary>
    /// Records that synchronization failed.
    /// </summary>
    /// <remarks>
    /// An expired or rejected cursor is not an error to report: it is the provider
    /// saying "start again", and the caller clears the cursor and re-syncs. Only a
    /// genuine failure lands here.
    /// </remarks>
    public void RecordSyncFailure(string error, bool requiresReauthorization, DateTimeOffset now)
    {
        LastSyncError = Ensure.NotBlankMax(error, nameof(error), 1000);

        State = requiresReauthorization
            ? CommunicationAccountState.ReauthorizationRequired
            : CommunicationAccountState.Error;

        Touch(now);
    }

    /// <summary>Forgets the cursor, so the next sync starts from the beginning.</summary>
    public void ResetCursor(DateTimeOffset now)
    {
        DeltaCursor = null;
        Touch(now);
    }

    /// <summary>
    /// Claims this mailbox for one worker.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing when somebody else holds a live lease.
    /// Two workers reaching for the same mailbox is ordinary, not exceptional.
    /// </remarks>
    public bool TryLease(string owner, TimeSpan duration, DateTimeOffset now)
    {
        if (SyncLeaseExpiresAt is { } expires && expires > now && SyncLeaseOwner != owner)
        {
            return false;
        }

        SyncLeaseOwner = Ensure.NotBlankMax(owner, nameof(owner), 100);
        SyncLeaseExpiresAt = now.Add(duration);

        Touch(now);
        return true;
    }

    public void ReleaseLease(DateTimeOffset now)
    {
        SyncLeaseOwner = null;
        SyncLeaseExpiresAt = null;

        Touch(now);
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(CommunicationAccount), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
