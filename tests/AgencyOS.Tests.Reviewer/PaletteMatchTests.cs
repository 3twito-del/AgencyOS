using AgencyOS.Client.Commands;
using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That the dialog pass presses Enter on the command it typed, and only on that
/// one.
/// </summary>
/// <remarks>
/// <para>
/// The pass confirms the palette narrowed to the right command before committing,
/// by reading the top result's accessible name. A row usually announces the record
/// it binds, identifier and all; sometimes it announces the plain label instead,
/// which is <c>AOS-R002-018</c> and not this wave's to fix. When it does, the
/// identifier match refuses a command sitting correctly and alone at the top, and
/// the pass records "nothing appeared" for a command it never ran — an opener
/// audit's worst possible confusion, and one Repair Wave 003A met head-on while
/// verifying its own fix.
/// </para>
/// <para>
/// So the label is accepted too, on two conditions that are the whole safety of
/// the rule: exact equality, and no other command shares the label. These are the
/// controls for both halves.
/// </para>
/// </remarks>
public sealed class PaletteMatchTests
{
    /// <summary>Positive: the row announced exactly this command's label.</summary>
    [Fact]
    public void AnExactUniqueLabelMatches() =>
        Assert.True(DialogPass.MatchesUniqueLabel("mailbox.connect", "Connect mailbox"));

    /// <summary>Negative: a different command's label is not this command.</summary>
    /// <remarks>
    /// The case that made the original rule refuse labels at all: one mailbox
    /// command connects and another disconnects.
    /// </remarks>
    [Fact]
    public void AnotherCommandsLabelDoesNotMatch() =>
        Assert.False(DialogPass.MatchesUniqueLabel("mailbox.connect", "Disconnect mailbox"));

    /// <summary>Negative: a label that merely contains this one is not this one.</summary>
    [Fact]
    public void ALabelThatOnlyContainsThisOneDoesNotMatch() =>
        Assert.False(
            DialogPass.MatchesUniqueLabel("mailbox.connect", "Connect mailbox and synchronize"));

    /// <summary>Negative: an unknown identifier matches nothing.</summary>
    [Fact]
    public void AnUnknownCommandDoesNotMatch() =>
        Assert.False(DialogPass.MatchesUniqueLabel("nothing.at.all", "Connect mailbox"));

    /// <summary>
    /// Negative: a label two commands share is declined for both.
    /// </summary>
    /// <remarks>
    /// There is one such pair in the registry today. The rule declines it rather
    /// than guessing, which is the difference between a loosened check and a
    /// broken one — and the assertion is written so that it still means something
    /// if the duplicate is ever resolved.
    /// </remarks>
    [Fact]
    public void ALabelTwoCommandsShareIsDeclined()
    {
        CommandDefinition[] shared =
        [
            .. CommandRegistry.Default.Commands
                .GroupBy(x => x.Label, StringComparer.Ordinal)
                .Where(x => x.Count() > 1)
                .SelectMany(x => x),
        ];

        foreach (CommandDefinition command in shared)
        {
            Assert.False(DialogPass.MatchesUniqueLabel(command.Id, command.Label));
        }
    }

    /// <summary>
    /// Every command this repair wave verifies can be matched by its label.
    /// </summary>
    /// <remarks>
    /// Not a property of the rule but of the registry, and worth pinning: if one
    /// of these four labels ever becomes a duplicate, the pass quietly loses the
    /// ability to confirm the four dialogs AOS-R002-019 named.
    /// </remarks>
    [Theory]
    [InlineData("mailbox.connect")]
    [InlineData("intelligence.prediction.create")]
    [InlineData("intelligence.source.record")]
    [InlineData("intelligence.prediction.resolve")]
    public void TheRepairedOpenersHaveLabelsOfTheirOwn(string commandId)
    {
        CommandDefinition command = Assert.IsType<CommandDefinition>(
            CommandRegistry.Default.Find(commandId));

        Assert.True(DialogPass.MatchesUniqueLabel(command.Id, command.Label));
    }
}
