using System.Reflection;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Audit;

/// <summary>
/// The first append-only defense: the type itself offers no way to change a record.
/// </summary>
public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_CapturesActorClientAndCorrelation()
    {
        UserId actor = UserId.New();
        OrganizationId organization = OrganizationId.New();

        AuditEvent audited = AuditEvent.Record(
            action: AuditAction.OrganizationCreated,
            entityType: nameof(Organization),
            entityId: organization.ToString(),
            occurredAt: Now,
            correlationId: "correlation-1",
            actorUserId: actor,
            actorSubject: "subject-1",
            organizationId: organization,
            permission: "organizations.create",
            semanticDelta: """{"Name":"Test"}""",
            clientPlatform: "windows-x64",
            clientChannel: "alpha",
            clientVersion: "0.3.0",
            clientBuildId: "20260907.1",
            apiContractVersion: 1);

        Assert.Equal(actor, audited.ActorUserId);
        Assert.Equal("subject-1", audited.ActorSubject);
        Assert.Equal(organization, audited.OrganizationId);
        Assert.Equal("correlation-1", audited.CorrelationId);
        Assert.Equal("alpha", audited.ClientChannel);
        Assert.Equal(1, audited.ApiContractVersion);
        Assert.Equal(Now, audited.OccurredAt);
    }

    /// <summary>A system action has no actor, and that is representable.</summary>
    [Fact]
    public void Record_AllowsASystemActionWithNoActor()
    {
        AuditEvent audited = AuditEvent.Record(
            AuditAction.UserRegistered,
            nameof(User),
            UserId.New().ToString(),
            Now,
            "correlation-2");

        Assert.Null(audited.ActorUserId);
        Assert.Null(audited.ActorSubject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_RequiresAnAction(string? action)
    {
        Assert.Throws<DomainException>(() =>
            AuditEvent.Record(action!, "Entity", "id", Now, "correlation"));
    }

    /// <summary>
    /// A record with no correlation identifier cannot be tied back to the request
    /// that caused it, which docs/07_SECURITY_AND_AUDIT.md requires.
    /// </summary>
    [Fact]
    public void Record_RequiresACorrelationId()
    {
        Assert.Throws<DomainException>(() =>
            AuditEvent.Record(AuditAction.OrganizationCreated, "Entity", "id", Now, "   "));
    }

    /// <summary>
    /// The structural guarantee behind the trail: the type exposes no way to change
    /// a recorded event. This is asserted rather than assumed, because a later
    /// convenience setter would silently dismantle the first defense while every
    /// other test kept passing.
    /// </summary>
    [Fact]
    public void AuditEvent_ExposesNoPublicMutator()
    {
        Type type = typeof(AuditEvent);

        PropertyInfo[] settable = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.Empty(settable);

        MethodInfo[] mutators = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Empty(mutators);
    }

    /// <summary>Identifiers are UUIDv7, so audit rows sort by creation time.</summary>
    [Fact]
    public void AuditEventId_IsTimeOrdered()
    {
        AuditEventId first = AuditEventId.New();
        AuditEventId second = AuditEventId.New();

        Assert.NotEqual(first, second);
        Assert.Equal(7, first.Value.Version);
    }
}
