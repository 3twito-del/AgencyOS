using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using Xunit;

namespace AgencyOS.Tests.Unit.Refusals;

/// <summary>
/// That a refusal says what was refused, not which permission string was missing.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-014</c>, decided by the owner: the operator-facing sentence uses
/// human capability language and never exposes the internal permission
/// identifier. The identifier itself is unchanged and still reaches the caller as
/// the problem's <c>requiredPermission</c> extension.
/// </para>
/// <para>
/// Authorization is not touched by any of this. What the server decides, and the
/// status it answers with, are exactly what they were.
/// </para>
/// </remarks>
public sealed class PermissionCapabilityTests
{
    /// <summary>
    /// Every permission this build has, has words.
    /// </summary>
    /// <remarks>
    /// The completeness mechanism. A permission added to <see cref="Permission.All"/>
    /// without a description would silently start speaking the fallback, which is
    /// the defect coming back quietly.
    /// </remarks>
    [Fact]
    public void EveryKnownPermissionHasWords()
    {
        List<string> undescribed = [.. Permission.All.Where(x => !PermissionCapability.Knows(x))];

        Assert.True(
            undescribed.Count == 0,
            "These permissions would be refused without saying what was refused "
                + "(AOS-R002-014): " + string.Join(", ", undescribed.Order(StringComparer.Ordinal)));
    }

    /// <summary>No sentence contains the identifier it is about.</summary>
    /// <remarks>
    /// The defect itself, checked across the whole vocabulary rather than on the
    /// one example the finding quoted.
    /// </remarks>
    [Fact]
    public void NoSentenceNamesThePermission()
    {
        List<string> leaking = [];

        foreach (string permission in Permission.All)
        {
            string said = PermissionCapability.Describe(permission);

            if (said.Contains(permission, StringComparison.OrdinalIgnoreCase)
                || said.Contains('\'', StringComparison.Ordinal))
            {
                leaking.Add(permission);
            }
        }

        Assert.Empty(leaking);
    }

    /// <summary>No sentence contains a dotted identifier of any kind.</summary>
    /// <remarks>
    /// A stronger form of the same rule: an identifier pasted from a different
    /// permission would pass the check above and still be an identifier.
    /// </remarks>
    [Fact]
    public void NoSentenceContainsADottedIdentifier()
    {
        foreach (string permission in Permission.All)
        {
            string said = PermissionCapability.Describe(permission);
            string body = said.Replace(" is not part of your role.", string.Empty, StringComparison.Ordinal);

            Assert.DoesNotContain('.', body);
        }
    }

    /// <summary>The example the finding quoted, before and after.</summary>
    [Fact]
    public void TheFindingsOwnExampleReadsAsCapability()
    {
        Assert.Equal(
            "Reading payments is not part of your role.",
            PermissionCapability.Describe("finance.payments.read"));
    }

    /// <summary>
    /// A permission this build has never heard of says nothing about itself.
    /// </summary>
    /// <remarks>
    /// An older client can be refused a permission a newer server added. Falling
    /// back to the identifier is exactly the behaviour being repaired, so the
    /// fallback says less rather than more.
    /// </remarks>
    [Theory]
    [InlineData("finance.somethingnew.read")]
    [InlineData("a.permission.from.the.future")]
    [InlineData("")]
    public void AnUnknownPermissionFallsBackWithoutNamingItself(string permission)
    {
        string said = PermissionCapability.Describe(permission);

        Assert.Equal("That is not part of your role.", said);
        Assert.DoesNotContain(".", said.Replace(" is not part of your role.", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>A null permission is still a sentence.</summary>
    [Fact]
    public void ANullPermissionStillReads() =>
        Assert.Equal("That is not part of your role.", PermissionCapability.Describe(null));

    /// <summary>The exception keeps the identifier for machines.</summary>
    /// <remarks>
    /// What the handler publishes as <c>requiredPermission</c>. If this stopped
    /// being exact, a client distinguishing refusals by permission would break
    /// while the prose looked fine.
    /// </remarks>
    [Fact]
    public void TheExceptionStillCarriesTheExactPermission()
    {
        PermissionDeniedException denied = new("finance.payments.read");

        Assert.Equal("finance.payments.read", denied.Permission);
        Assert.Equal("Reading payments is not part of your role.", denied.Message);
    }

    /// <summary>No sentence claims the capability does not exist.</summary>
    /// <remarks>
    /// "There is no such thing" would be a lie, and a different disclosure: it
    /// would tell a refused caller what this build does and does not have.
    /// </remarks>
    [Fact]
    public void NoSentenceDeniesThatTheCapabilityExists()
    {
        foreach (string permission in Permission.All)
        {
            string said = PermissionCapability.Describe(permission);

            Assert.DoesNotContain("does not exist", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not found", said, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>No sentence promises who could grant it.</summary>
    /// <remarks>
    /// The owner ruled this out: the repository does not establish that
    /// remediation promise, and naming a role would disclose how the organization
    /// is administered to somebody who has just been refused.
    /// </remarks>
    [Fact]
    public void NoSentencePromisesRemediation()
    {
        foreach (string permission in Permission.All)
        {
            string said = PermissionCapability.Describe(permission);

            Assert.DoesNotContain("administrator", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ask ", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("contact ", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("owner", said, StringComparison.OrdinalIgnoreCase);
        }
    }
}
