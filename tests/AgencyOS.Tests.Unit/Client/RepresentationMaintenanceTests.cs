using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Representations;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What a representation's scopes and team can be changed to.
/// </summary>
/// <remarks>
/// <c>AOS-R001-010</c>. Four server commands had no client caller: add a scope,
/// end one, assign somebody to the team, take them off. The rules about what may
/// be offered live in the client layer so they can be tested; the dialogs only
/// display what these return.
/// </remarks>
public sealed class RepresentationMaintenanceTests
{
    /// <summary>The areas offered are exactly the ones the domain has.</summary>
    /// <remarks>
    /// The client cannot reference the domain, so the list is mirrored. An area
    /// added to the enum and not here would be unreachable from the product; one
    /// invented here would earn the operator a refusal they cannot act on.
    /// </remarks>
    [Fact]
    public void TheAreasOfferedAreTheAreasTheDomainHas()
    {
        string[] domain = Enum.GetNames<RepresentationScopeArea>();

        Assert.Equal(domain.Order(StringComparer.Ordinal), RepresentationMaintenance.Areas.Order(StringComparer.Ordinal));
    }

    /// <summary>The roles offered are exactly the ones the domain has.</summary>
    [Fact]
    public void TheRolesOfferedAreTheRolesTheDomainHas()
    {
        string[] domain = Enum.GetNames<RepresentationTeamRole>();

        Assert.Equal(domain.Order(StringComparer.Ordinal), RepresentationMaintenance.Roles.Order(StringComparer.Ordinal));
    }

    /// <summary>An area already represented is not offered again.</summary>
    [Fact]
    public void AnAreaAlreadyRepresentedCannotBegin()
    {
        IReadOnlyList<string> offered = RepresentationMaintenance.AreasThatCanBegin(
            [Scope("Television"), Scope("Film")]);

        Assert.DoesNotContain("Television", offered);
        Assert.DoesNotContain("Film", offered);
        Assert.Contains("Literary", offered);
    }

    /// <summary>An area that ended can begin again.</summary>
    /// <remarks>
    /// Representation resumes. The record keeps the earlier stretch, and the
    /// operator is not stopped from starting a new one.
    /// </remarks>
    [Fact]
    public void AnAreaThatEndedCanBeginAgain()
    {
        IReadOnlyList<string> offered = RepresentationMaintenance.AreasThatCanBegin(
            [Scope("Television", ended: true)]);

        Assert.Contains("Television", offered);
    }

    /// <summary>Only what is represented now can end.</summary>
    [Fact]
    public void OnlyACurrentAreaCanEnd()
    {
        IReadOnlyList<string> offered = RepresentationMaintenance.AreasThatCanEnd(
            [Scope("Television"), Scope("Film", ended: true)]);

        Assert.Equal(["Television"], offered);
    }

    /// <summary>An area this client has no name for can still be ended.</summary>
    /// <remarks>
    /// The endable list is read from what the server sent rather than from the
    /// mirrored list, so a server that knows more than this client does not strand
    /// the operator with a scope they cannot close.
    /// </remarks>
    [Fact]
    public void AnUnfamiliarAreaCanStillEnd()
    {
        IReadOnlyList<string> offered = RepresentationMaintenance.AreasThatCanEnd(
            [Scope("Podcasting")]);

        Assert.Equal(["Podcasting"], offered);
    }

    /// <summary>Somebody already on the team stays offered, with what they do.</summary>
    /// <remarks>
    /// Assigning them again is how a role changes. Hiding them would make a role
    /// change unreachable.
    /// </remarks>
    [Fact]
    public void SomebodyOnTheTeamIsStillOfferedAndSaysWhatTheyDo()
    {
        Guid lead = Guid.NewGuid();

        IReadOnlyList<EntityChoice> offered = RepresentationMaintenance.MembersToAssign(
            [Member(lead, "Dana Reyes"), Member(Guid.NewGuid(), "Sam Ortiz")],
            [Assignment(lead, "Dana Reyes", "Lead")]);

        Assert.Equal(2, offered.Count);
        Assert.Contains(offered, x => x.Id == lead && x.Label.Contains("Lead", StringComparison.Ordinal));
        Assert.Contains(offered, x => x.Label == "Sam Ortiz");
    }

    /// <summary>Only people working it now can be taken off.</summary>
    [Fact]
    public void OnlyACurrentAssignmentCanBeRemoved()
    {
        Guid past = Guid.NewGuid();
        Guid present = Guid.NewGuid();

        IReadOnlyList<EntityChoice> offered = RepresentationMaintenance.MembersToRemove(
            [Assignment(past, "Gone", "Agent", ended: true), Assignment(present, "Here", "Agent")]);

        Assert.Equal(present, Assert.Single(offered).Id);
    }

    /// <summary>What somebody does on the team now, or nothing.</summary>
    [Fact]
    public void ARoleIsReadFromTheCurrentAssignment()
    {
        Guid person = Guid.NewGuid();

        Assert.Equal(
            "Coordinator",
            RepresentationMaintenance.RoleOf([Assignment(person, "Jo", "Coordinator")], person));

        Assert.Null(RepresentationMaintenance.RoleOf([], person));
    }

    /// <summary>Somebody who left and returned reads as what they are now.</summary>
    [Fact]
    public void AReturningMemberReadsAsTheirCurrentRole()
    {
        Guid person = Guid.NewGuid();

        string? role = RepresentationMaintenance.RoleOf(
            [
                Assignment(person, "Jo", "Assistant", ended: true),
                Assignment(person, "Jo", "Lead"),
            ],
            person);

        Assert.Equal("Lead", role);
    }

    private static RepresentationScopeResponse Scope(string area, bool ended = false) =>
        new(area, new DateOnly(2026, 1, 1), ended ? new DateOnly(2026, 6, 1) : null);

    private static OrganizationMemberResponse Member(Guid userId, string name) =>
        new(Guid.NewGuid(), userId, name, $"{name}@example.test", "Member", DateTimeOffset.UtcNow, IsSelf: false);

    private static RepresentationTeamMemberResponse Assignment(
        Guid userId,
        string name,
        string role,
        bool ended = false) =>
        new(userId, name, role, new DateOnly(2026, 1, 1), ended ? new DateOnly(2026, 6, 1) : null);
}
