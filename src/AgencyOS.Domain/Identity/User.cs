using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.Identity;

/// <summary>
/// A person who can authenticate and act inside AgencyOS.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExternalSubject"/> is the identity-provider subject claim. It is
/// modelled as an external identifier rather than overloaded into the primary
/// key (<c>docs/08_DATA_MODEL_FOUNDATION.md</c> principle 8), which is what keeps
/// the identity provider replaceable: moving from the development provider to
/// Entra ID or any OIDC issuer changes this column's contents, not the schema or
/// any foreign key.
/// </para>
/// <para>
/// Status changes are commands, never arbitrary field edits (<c>CLAUDE.md</c>
/// section 5).
/// </para>
/// </remarks>
public sealed class User
{
    private User()
    {
    }

    public UserId Id { get; private set; }

    /// <summary>Issuer-scoped subject identifier from the identity provider.</summary>
    public string ExternalSubject { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Register(
        string externalSubject,
        string displayName,
        string email,
        DateTimeOffset now)
    {
        return new User
        {
            Id = UserId.New(),
            ExternalSubject = Ensure.NotBlankMax(externalSubject, nameof(externalSubject), 256),
            DisplayName = Ensure.NotBlankMax(displayName, nameof(displayName), 256),
            Email = Ensure.NotBlankMax(email, nameof(email), 320),
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Suspends the user. A suspended user retains history but cannot act.</summary>
    public void Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
        {
            throw new DomainException("A deactivated user cannot be suspended.");
        }

        Status = UserStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        if (Status != UserStatus.Suspended)
        {
            throw new DomainException("Only a suspended user can be reinstated.");
        }

        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>Gets a value indicating whether this user may perform actions.</summary>
    public bool CanAct => Status == UserStatus.Active;
}
