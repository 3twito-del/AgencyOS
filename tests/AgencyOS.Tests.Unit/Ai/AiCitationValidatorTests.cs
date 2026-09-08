using AgencyOS.Application.Ai;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// What a brief is allowed to point at.
/// </summary>
/// <remarks>
/// A model can produce a well-formed identifier for a record that does not exist,
/// belongs to another tenant, or was withheld from this reader. Fluent prose is no
/// evidence that a reference is real, and a brief whose citations cannot be opened
/// is worse than one with none, because it looks checkable (§38, §39).
/// </remarks>
public sealed class AiCitationValidatorTests
{
    private static readonly Guid Real = Guid.CreateVersion7();
    private static readonly Guid Invented = Guid.CreateVersion7();

    private static readonly AiCitationReference[] Allowed =
        [new("Signal", Real)];

    [Fact]
    public void AResolvingCitationSurvives()
    {
        string text = $"She left in August [cite:Signal:{Real:D}].";

        Assert.Equal(text, AiCitationValidator.Strip(text, Allowed));
        Assert.True(AiCitationValidator.AllResolve(text, Allowed));
    }

    /// <summary>An invented identifier is removed, and the prose survives.</summary>
    /// <remarks>
    /// Stripped rather than rejected: a brief with one bad reference is still worth
    /// reading without it.
    /// </remarks>
    [Fact]
    public void AnInventedCitationIsRemoved()
    {
        string text = $"She left in August [cite:Signal:{Invented:D}].";

        Assert.Equal("She left in August .", AiCitationValidator.Strip(text, Allowed));
        Assert.False(AiCitationValidator.AllResolve(text, Allowed));
    }

    /// <summary>
    /// The right identifier under the wrong kind does not resolve.
    /// </summary>
    /// <remarks>
    /// A citation naming a Deal is a claim about a deal. Letting a signal's
    /// identifier satisfy it would make the kind decorative.
    /// </remarks>
    [Fact]
    public void TheWrongKindDoesNotResolve()
    {
        string text = $"The deal moved [cite:Deal:{Real:D}].";

        Assert.Equal("The deal moved .", AiCitationValidator.Strip(text, Allowed));
    }

    /// <summary>Prose that merely sounds like a reference is left alone.</summary>
    /// <remarks>
    /// A model writing about "the 2024 agreement" is not making a
    /// machine-checkable claim, and silently deleting phrases would make briefs
    /// wrong in a way nobody would notice.
    /// </remarks>
    [Fact]
    public void ProseIsNotTouched()
    {
        const string text =
            "According to the 2024 agreement and the note from Dana, this looks settled.";

        Assert.Equal(text, AiCitationValidator.Strip(text, Allowed));
        Assert.True(AiCitationValidator.AllResolve(text, Allowed));
    }

    /// <summary>Nothing citable means nothing survives.</summary>
    [Fact]
    public void WithNothingCitableEveryCitationIsRemoved()
    {
        string text = $"[cite:Signal:{Real:D}] and [cite:Deal:{Invented:D}] agree.";

        Assert.Equal(" and  agree.", AiCitationValidator.Strip(text, []));
    }

    /// <summary>Extraction reports what the text claimed, resolving or not.</summary>
    /// <remarks>
    /// Used to record what a run asserted before the stripping happened, which is
    /// what makes "the model cited something that did not exist" a visible event
    /// rather than a silent edit.
    /// </remarks>
    [Fact]
    public void ExtractionReportsEveryClaim()
    {
        string text = $"[cite:Signal:{Real:D}] [cite:Deal:{Invented:D}]";

        IReadOnlyList<AiCitationReference> found = AiCitationValidator.Extract(text);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, x => x.Kind == "Signal" && x.Id == Real);
        Assert.Contains(found, x => x.Kind == "Deal" && x.Id == Invented);
    }

    /// <summary>A malformed marker is not a citation and is not touched.</summary>
    [Fact]
    public void AMalformedMarkerIsLeftAlone()
    {
        const string text = "[cite:Signal:not-a-guid] and [cite:Signal] and [cite::]";

        Assert.Equal(text, AiCitationValidator.Strip(text, Allowed));
        Assert.Empty(AiCitationValidator.Extract(text));
    }

    /// <summary>Several citations in one sentence are each checked on their own.</summary>
    [Fact]
    public void EachCitationIsCheckedIndependently()
    {
        string text =
            $"Both [cite:Signal:{Real:D}] and [cite:Signal:{Invented:D}] say so.";

        Assert.Equal(
            $"Both [cite:Signal:{Real:D}] and  say so.",
            AiCitationValidator.Strip(text, Allowed));
    }
}
