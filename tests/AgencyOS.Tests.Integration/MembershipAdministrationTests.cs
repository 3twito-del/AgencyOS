using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Audit;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That an organization can gain and lose people, safely.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 could not review anything as a Member, an Observer or an
/// Administrator, because AgencyOS had no way to produce one. Registering a user
/// was reachable only from first-run bootstrap, and membership was write-once:
/// one <c>POST</c>, no read, no change, no revoke, and a handler that refused a
/// second grant. An organization held exactly one identity holding exactly one
/// role, permanently (<c>AOS-R002-002</c>, <c>AOS-R002-006</c>).
/// </para>
/// <para>
/// Every refusal below is asserted against the server. A hidden or disabled
/// control in the Windows client is not evidence that anything is enforced.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class MembershipAdministrationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public MembershipAdministrationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------- the gap

    /// <summary>An owner can bring somebody new into the organization.</summary>
    /// <remarks>
    /// The operation `AOS-R002-002` said did not exist anywhere.
    /// </remarks>
    [Fact]
    public async Task AnOwner_CanAddSomebodyWhoHasNeverUsedAgencyOs()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{owner.Organization.Id.Value}/members",
            Request(AgencyRole.Member));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AddMemberResponse? added = await response.Content
            .ReadFromJsonAsync<AddMemberResponse>();

        Assert.NotNull(added);
        Assert.True(added.UserWasRegistered);
        Assert.NotEqual(Guid.Empty, added.UserId);
    }

    /// <summary>The new person can then act, which is what makes them a member.</summary>
    [Fact]
    public async Task SomebodyAdded_CanThenUseTheirRole()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient adding = _fixture.CreateClient(owner.Subject);

        string subject = $"newcomer-{Guid.NewGuid():N}";

        using HttpResponseMessage added = await adding.PostAsJsonAsync(
            $"/api/v1/organizations/{owner.Organization.Id.Value}/members",
            new AddMemberRequest(subject, "A Newcomer", $"{subject}@review.invalid", "Observer"));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // The subject now authenticates, and reads what an Observer may read.
        using HttpClient newcomer = _fixture.CreateClient(subject);

        using HttpResponseMessage read = await newcomer.GetAsync(
            $"/api/v1/organizations/{owner.Organization.Id.Value}/members");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    /// <summary>The members list is readable, and says who holds what.</summary>
    [Fact]
    public async Task MembersCanBeListed()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{owner.Organization.Id.Value}/members",
            Request(AgencyRole.Member));

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{owner.Organization.Id.Value}/members");

        Assert.NotNull(members);
        Assert.Equal(2, members.Length);
        Assert.Contains(members, x => x.Role == "Owner" && x.IsSelf);
        Assert.Contains(members, x => x.Role == "Member" && !x.IsSelf);

        // A name, not an identifier. The list is what a person reads.
        Assert.All(members, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayName)));
    }

    /// <summary>A membership can be ended, and the person then cannot act.</summary>
    [Fact]
    public async Task AMembershipCanBeEnded_AndTheAccessGoesWithIt()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        string subject = $"leaver-{Guid.NewGuid():N}";
        Guid organization = owner.Organization.Id.Value;

        AddMemberResponse? added = await (await client.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members",
                new AddMemberRequest(subject, "A Leaver", $"{subject}@review.invalid", "Member")))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        Assert.NotNull(added);

        using HttpClient leaver = _fixture.CreateClient(subject);

        using HttpResponseMessage before = await leaver.GetAsync(
            $"/api/v1/organizations/{organization}/members");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using HttpResponseMessage revoked = await client.PostAsync(
            $"/api/v1/organizations/{organization}/members/{added.MembershipId}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using HttpResponseMessage after = await leaver.GetAsync(
            $"/api/v1/organizations/{organization}/members");

        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    // -------------------------------------------------------- owner safety

    /// <summary>
    /// The only owner cannot be removed.
    /// </summary>
    /// <remarks>
    /// Nothing in AgencyOS protected this before 003E-A, because nothing called
    /// <c>Revoke</c>. Exposing revocation made it reachable: an organization with
    /// no owner is one nobody can ever administer again, because granting the
    /// owner role requires being one.
    /// </remarks>
    [Fact]
    public async Task TheOnlyOwner_CannotBeRemoved()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "sole");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        OrganizationMemberResponse sole = Assert.Single(members!);

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/organizations/{organization}/members/{sole.MembershipId}/revoke", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // The refusal says what to do about it.
        Assert.Contains("only owner", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A second owner makes the first removable.</summary>
    [Fact]
    public async Task OnceThereIsASecondOwner_TheFirstCanLeave()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "first");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members",
            Request(AgencyRole.Owner));

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        OrganizationMemberResponse self = members!.Single(x => x.IsSelf);

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/organizations/{organization}/members/{self.MembershipId}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ------------------------------------------------- privilege escalation

    /// <summary>
    /// An administrator cannot create an owner.
    /// </summary>
    /// <remarks>
    /// An administrator holds <c>memberships.grant</c>, which is what lets them
    /// build a team. Without a check it would also let them add a second account
    /// of their own as an owner, which is escalation with extra steps.
    /// </remarks>
    [Fact]
    public async Task AnAdministrator_CannotMakeAnOwner()
    {
        SeededActor admin = await _fixture.SeedActorAsync(AgencyRole.Administrator, "admin");
        using HttpClient client = _fixture.CreateClient(admin.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{admin.Organization.Id.Value}/members",
            Request(AgencyRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator can still build a team below themselves.</summary>
    [Fact]
    public async Task AnAdministrator_CanAddAMember()
    {
        SeededActor admin = await _fixture.SeedActorAsync(AgencyRole.Administrator, "admin");
        using HttpClient client = _fixture.CreateClient(admin.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{admin.Organization.Id.Value}/members",
            Request(AgencyRole.Member));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>A member cannot add anybody, whatever role they ask for.</summary>
    [Theory]
    [InlineData("Observer")]
    [InlineData("Member")]
    [InlineData("Administrator")]
    [InlineData("Owner")]
    public async Task AMember_CannotAddAnybody(string role)
    {
        SeededActor member = await _fixture.SeedActorAsync(AgencyRole.Member, "member");
        using HttpClient client = _fixture.CreateClient(member.Subject);

        string subject = $"attempt-{Guid.NewGuid():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{member.Organization.Id.Value}/members",
            new AddMemberRequest(subject, "An Attempt", $"{subject}@review.invalid", role));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An observer cannot end anybody's membership.</summary>
    [Fact]
    public async Task AnObserver_CannotEndAMembership()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient adding = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;
        string subject = $"observer-{Guid.NewGuid():N}";

        await adding.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members",
            new AddMemberRequest(subject, "An Observer", $"{subject}@review.invalid", "Observer"));

        OrganizationMemberResponse[]? members = await adding
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        Guid target = members!.Single(x => x.Role == "Owner").MembershipId;

        using HttpClient observer = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await observer.PostAsync(
            $"/api/v1/organizations/{organization}/members/{target}/revoke", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An observer can read the list, which every role may.</summary>
    [Fact]
    public async Task AnObserver_CanReadTheMembersList()
    {
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "observer");
        using HttpClient client = _fixture.CreateClient(observer.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{observer.Organization.Id.Value}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------- other tenants

    /// <summary>A member of one organization cannot read another's members.</summary>
    [Fact]
    public async Task ACaller_CannotReadAnotherOrganizationsMembers()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Owner, "mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "theirs");

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A caller cannot add themselves to somebody else's organization.</summary>
    [Fact]
    public async Task ACaller_CannotAddThemselvesToAnotherOrganization()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Owner, "mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "theirs");

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/members",
            Request(AgencyRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A membership cannot be revoked through an organization it does not belong to.
    /// </summary>
    /// <remarks>
    /// The identifier is a real membership and the caller is a real owner — of a
    /// different organization. Without the ownership check on the handler, holding
    /// <c>memberships.revoke</c> anywhere would revoke memberships everywhere.
    /// </remarks>
    [Fact]
    public async Task AMembership_CannotBeRevokedThroughTheWrongOrganization()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Owner, "mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "theirs");

        using HttpClient other = _fixture.CreateClient(theirs.Subject);

        OrganizationMemberResponse[]? members = await other
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{theirs.Organization.Id.Value}/members");

        Guid foreign = members!.Single().MembershipId;

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/organizations/{mine.Organization.Id.Value}/members/{foreign}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A stranger cannot list or change anybody's membership.</summary>
    [Fact]
    public async Task ANonMember_CannotListOrChangeMembership()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient stranger = _fixture.CreateClient($"ghost-{Guid.NewGuid():N}");

        Guid organization = owner.Organization.Id.Value;

        using HttpResponseMessage read = await stranger.GetAsync(
            $"/api/v1/organizations/{organization}/members");

        using HttpResponseMessage write = await stranger.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Member));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    // ---------------------------------------------------------- duplicates

    /// <summary>Adding the same person twice is refused rather than duplicated.</summary>
    [Fact]
    public async Task AddingTheSamePersonTwice_IsRefused()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;
        string subject = $"twice-{Guid.NewGuid():N}";
        AddMemberRequest request = new(subject, "Twice Over", $"{subject}@review.invalid", "Member");

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members", request);

        using HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        // Two: the owner and one of them.
        Assert.Equal(2, members!.Length);
    }

    /// <summary>
    /// Ending the same membership twice is refused, and ends it once.
    /// </summary>
    /// <remarks>
    /// A double submission from a dialog looks exactly like this, and the
    /// membership aggregate carries no version — so the refusal comes from the
    /// domain's own "already revoked" rule rather than from optimistic
    /// concurrency.
    /// </remarks>
    [Fact]
    public async Task EndingTheSameMembershipTwice_IsRefusedTheSecondTime()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        AddMemberResponse? added = await (await client.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Member)))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        string path = $"/api/v1/organizations/{organization}/members/{added!.MembershipId}/revoke";

        using HttpResponseMessage first = await client.PostAsync(path, null);
        using HttpResponseMessage second = await client.PostAsync(path, null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    // --------------------------------------------------------------- audit

    /// <summary>Adding and removing a member is written to the audit trail.</summary>
    [Fact]
    public async Task MembershipChanges_AreAudited()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        AddMemberResponse? added = await (await client.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Observer)))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        await client.PostAsync(
            $"/api/v1/organizations/{organization}/members/{added!.MembershipId}/revoke", null);

        AuditEventResponse[]? events = await client
            .GetFromJsonAsync<AuditEventResponse[]>(
                $"/api/v1/audit?entityType=Membership&entityId={added.MembershipId}");

        Assert.NotNull(events);
        Assert.Contains(events, x => x.Action == "membership.granted");
        Assert.Contains(events, x => x.Action == "membership.revoked");

        // The actor is recorded, and the trail says what changed.
        Assert.All(events, x => Assert.NotNull(x.ActorUserId));
    }

    // ---------------------------------------------------------- role change

    /// <summary>A role change ends one membership and grants another.</summary>
    /// <remarks>
    /// A membership is never edited, so the identifier changes. A client that
    /// held the old one and did not re-read would act on a revoked membership.
    /// </remarks>
    [Fact]
    public async Task ARoleCanBeChanged()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        AddMemberResponse? added = await (await client.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Observer)))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members/{added!.MembershipId}/role",
            new ChangeMemberRoleRequest("Member"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ChangeMemberRoleResponse? changed = await response.Content
            .ReadFromJsonAsync<ChangeMemberRoleResponse>();

        Assert.NotEqual(added.MembershipId, changed!.MembershipId);

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        // Two people, not three: the old membership is revoked, not left active.
        Assert.Equal(2, members!.Length);
        Assert.Contains(members, x => x.UserId == added.UserId && x.Role == "Member");
    }

    /// <summary>The last owner cannot be demoted, which is removal by another name.</summary>
    [Fact]
    public async Task TheLastOwner_CannotBeDemoted()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "sole");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        OrganizationMemberResponse[]? members = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members/{members!.Single().MembershipId}/role",
            new ChangeMemberRoleRequest("Member"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And they still hold it.
        OrganizationMemberResponse[]? after = await client
            .GetFromJsonAsync<OrganizationMemberResponse[]>(
                $"/api/v1/organizations/{organization}/members");

        Assert.Equal("Owner", after!.Single().Role);
    }

    /// <summary>An administrator cannot promote anybody to owner.</summary>
    [Fact]
    public async Task AnAdministrator_CannotPromoteToOwner()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;
        string adminSubject = $"admin-{Guid.NewGuid():N}";

        await ownerClient.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members",
            new AddMemberRequest(
                adminSubject, "An Administrator", $"{adminSubject}@review.invalid", "Administrator"));

        AddMemberResponse? target = await (await ownerClient.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Member)))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        using HttpClient admin = _fixture.CreateClient(adminSubject);

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members/{target!.MembershipId}/role",
            new ChangeMemberRoleRequest("Owner"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A member cannot change anybody's role, including their own.</summary>
    [Fact]
    public async Task AMember_CannotChangeARole()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;
        string subject = $"member-{Guid.NewGuid():N}";

        AddMemberResponse? added = await (await ownerClient.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members",
                new AddMemberRequest(subject, "A Member", $"{subject}@review.invalid", "Member")))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        using HttpClient member = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await member.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members/{added!.MembershipId}/role",
            new ChangeMemberRoleRequest("Owner"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A role change is written to the audit trail as both halves.</summary>
    [Fact]
    public async Task ARoleChange_IsAuditedAsBothHalves()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "owner");
        using HttpClient client = _fixture.CreateClient(owner.Subject);

        Guid organization = owner.Organization.Id.Value;

        AddMemberResponse? added = await (await client.PostAsJsonAsync(
                $"/api/v1/organizations/{organization}/members", Request(AgencyRole.Observer)))
            .Content.ReadFromJsonAsync<AddMemberResponse>();

        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/members/{added!.MembershipId}/role",
            new ChangeMemberRoleRequest("Member"));

        AuditEventResponse[]? events = await client
            .GetFromJsonAsync<AuditEventResponse[]>(
                $"/api/v1/audit?entityType=Membership&entityId={added.MembershipId}");

        AuditEventResponse revoked = Assert.Single(
            events!, x => x.Action == "membership.revoked");

        // The trail says what they held before, so the change is explainable.
        Assert.Contains("Observer", revoked.SemanticDelta ?? string.Empty, StringComparison.Ordinal);
    }

    private static AddMemberRequest Request(AgencyRole role)
    {
        string subject = $"added-{Guid.NewGuid():N}";

        return new AddMemberRequest(
            subject, $"Added {role}", $"{subject}@review.invalid", role.ToString());
    }
}
