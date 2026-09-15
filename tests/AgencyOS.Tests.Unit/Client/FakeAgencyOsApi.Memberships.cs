using AgencyOS.Contracts.Organizations;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The membership administration half of the fake client.
/// </summary>
/// <remarks>
/// Keeps enough state to answer the view model honestly: adding somebody makes
/// them appear in the list, changing a role changes it, and ending a membership
/// removes them. A fake that returned canned lists would let a view-model test
/// pass while the screen never refreshed.
/// </remarks>
internal sealed partial class FakeAgencyOsApi
{
    /// <summary>The organization's active members, as the fake holds them.</summary>
    public List<OrganizationMemberResponse> Members { get; } = [];

    /// <summary>The last person the fake was asked to add.</summary>
    public AddMemberRequest? LastMemberAdded { get; private set; }

    /// <summary>Memberships the fake was asked to end.</summary>
    public List<Guid> RevokedMemberships { get; } = [];

    /// <summary>Set to refuse the next membership call, the way a server would.</summary>
    public Exception? NextMembershipFailure { get; set; }

    public Task<IReadOnlyList<OrganizationMemberResponse>> ListOrganizationMembersAsync(
        CancellationToken cancellationToken = default)
    {
        Refuse();

        return Task.FromResult<IReadOnlyList<OrganizationMemberResponse>>([.. Members]);
    }

    public Task<AddMemberResponse> AddOrganizationMemberAsync(
        AddMemberRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        ArgumentNullException.ThrowIfNull(request);

        LastMemberAdded = request;

        Guid membershipId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        Members.Add(new OrganizationMemberResponse(
            membershipId, userId, request.DisplayName, request.Email,
            request.Role, DateTimeOffset.UtcNow, IsSelf: false));

        return Task.FromResult(new AddMemberResponse(membershipId, userId, UserWasRegistered: true));
    }

    public Task RevokeOrganizationMembershipAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        RevokedMemberships.Add(membershipId);
        Members.RemoveAll(x => x.MembershipId == membershipId);

        return Task.CompletedTask;
    }

    public Task<ChangeMemberRoleResponse> ChangeOrganizationMemberRoleAsync(
        Guid membershipId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        ArgumentNullException.ThrowIfNull(request);

        int at = Members.FindIndex(x => x.MembershipId == membershipId);

        if (at < 0)
        {
            throw new InvalidOperationException("No such membership.");
        }

        // The server ends one membership and grants another, so the identifier
        // changes. A fake that kept it would hide a client that never re-read.
        Guid replacement = Guid.CreateVersion7();

        Members[at] = Members[at] with { MembershipId = replacement, Role = request.Role };

        return Task.FromResult(new ChangeMemberRoleResponse(replacement));
    }

    private void Refuse()
    {
        if (NextMembershipFailure is not { } failure)
        {
            return;
        }

        NextMembershipFailure = null;

        throw failure;
    }
}
