using AgencyOS.Domain.Common;
using Xunit;

namespace AgencyOS.Tests.Unit.Refusals;

/// <summary>
/// That the message shown when somebody has just lost work is written for them.
/// </summary>
/// <remarks>
/// <c>AOS-R002-007</c>. The sentence was correct and unusable: it named the record
/// by an identifier the product displays nowhere, and it stopped without saying
/// what to do. It is the one message an operator reads at the moment their change
/// was refused.
/// </remarks>
public sealed class ConcurrencyRefusalTests
{
    /// <summary>The identifier is gone from the sentence.</summary>
    /// <remarks>
    /// It remains on the exception, and the API puts it in the problem's
    /// <c>entityId</c> extension, so nothing that reads this by machine loses
    /// anything.
    /// </remarks>
    [Fact]
    public void TheSentenceDoesNotQuoteAnIdentifier()
    {
        ConcurrencyConflictException conflict = new(
            "Contract", "01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9", 9, 10);

        Assert.DoesNotContain("01a0a1ff", conflict.Message, StringComparison.Ordinal);
        Assert.Equal("01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9", conflict.EntityId);
    }

    /// <summary>Both versions are still there, because the reader needs both.</summary>
    [Fact]
    public void BothVersionsAreStated()
    {
        ConcurrencyConflictException conflict = new("Contract", "irrelevant", 9, 10);

        Assert.Contains("you had version 9", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("it is now 10", conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>It says what to do next.</summary>
    [Fact]
    public void ItSaysWhatToDoNext() =>
        Assert.Contains(
            "Refresh",
            new ConcurrencyConflictException("Contract", "irrelevant", 9, 10).Message,
            StringComparison.Ordinal);

    /// <summary>The kind of record is said the way a person would say it.</summary>
    [Theory]
    [InlineData("Contract", "This contract has changed")]
    [InlineData("Project", "This project has changed")]
    [InlineData("OpportunityTarget", "This opportunity target has changed")]
    [InlineData("SavedView", "This saved view has changed")]
    public void TheKindOfRecordReadsAsWords(string entityType, string expected) =>
        Assert.StartsWith(
            expected,
            new ConcurrencyConflictException(entityType, "irrelevant", 1, 2).Message,
            StringComparison.Ordinal);

    /// <summary>An unnamed kind still produces a sentence.</summary>
    [Fact]
    public void AnUnnamedKindIsStillReadable() =>
        Assert.StartsWith(
            "This record has changed",
            new ConcurrencyConflictException("", "irrelevant", 1, 2).Message,
            StringComparison.Ordinal);
}
