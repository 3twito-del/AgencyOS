using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That no sentence a next-action surface can produce reports assigned work as
/// unassigned.
/// </summary>
/// <remarks>
/// <para>
/// <c>TaskOwnershipTruthTests</c> pins the state. This pins the <em>words</em>,
/// because the build-79 lesson was that a repair verified against the value it
/// changed can leave the sentence beside it wrong. What an operator reads is the
/// only thing that can mislead them, so the assertion is over the composed message
/// rather than over a property.
/// </para>
/// <para>
/// Asserted against the shared renderer rather than a page, because both surfaces
/// that offer a next action use it. A page could still say something of its own, and
/// <c>DealPaperTruthTests</c> holds the markup to account for that.
/// </para>
/// </remarks>
public sealed class OwnershipSurfaceTruthTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Member = Guid.Parse("01a0a27a-c0d5-7a63-830d-b6a1d4f85a88");

    /// <summary>Words that claim nobody is accountable.</summary>
    private static readonly string[] ClaimsNobodyOwnsIt =
    [
        "unassigned",
        "nobody",
        "no owner",
        "not assigned",
    ];

    /// <summary>
    /// Every arrangement of ownership a task can reach this renderer in.
    /// </summary>
    public static TheoryData<string?, Guid?, bool> Arrangements => new()
    {
        // name,            assignee id, is the work owned?
        { null,             null,        false },
        { "",               null,        false },
        { "Review member",  Member,      true },
        { null,             Member,      true },
        { "   ",            Member,      true },
    };

    /// <summary>
    /// The sentence may claim nobody owns the work only when nobody does.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arrangements))]
    public void NoSentenceCallsOwnedWorkUnowned(string? name, Guid? assignee, bool owned)
    {
        string sentence = Message(name, assignee);

        bool disclaims = ClaimsNobodyOwnsIt.Any(
            x => sentence.Contains(x, StringComparison.OrdinalIgnoreCase));

        Assert.False(
            owned && disclaims,
            $"Work assigned to {assignee} was described as unowned: {sentence}");
    }

    /// <summary>Owned work whose name is missing says so, rather than nothing.</summary>
    /// <remarks>
    /// Silence would be safe but unhelpful: an operator reading only the action would
    /// assume it is theirs. The surface states that somebody owns it and that this
    /// screen cannot name them.
    /// </remarks>
    [Fact]
    public void OwnedWorkWithNoNameSaysItIsOwned()
    {
        string sentence = Message(name: null, assignee: Member);

        Assert.Contains("assigned", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unassigned", sentence, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A resolved name is the name, not an identifier.</summary>
    [Fact]
    public void AResolvedNameIsSpokenAndTheIdentifierIsNot()
    {
        string sentence = Message("Review member", Member);

        Assert.Contains("Review member", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(Member.ToString(), sentence, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The named branch says what the name is doing there.
    /// </summary>
    /// <remarks>
    /// This banner sits on the same two pages as the task rows. While it printed a
    /// bare name it stated an identity with no role directly above rows that give
    /// theirs one, which is the disagreement the attribution rule forbids within a
    /// single surface.
    /// </remarks>
    [Fact]
    public void ANamedOwnerIsAttributedAndNotLeftBare()
    {
        Assert.Contains(
            "Assigned to Review member", Message("Review member", Member), StringComparison.Ordinal);
    }

    /// <summary>Genuinely unowned work is still reported as such.</summary>
    [Fact]
    public void UnownedWorkIsStillCalledUnassigned()
    {
        Assert.Contains(
            "Unassigned", Message(name: null, assignee: null), StringComparison.Ordinal);
    }

    /// <summary>
    /// The message the shared renderer would write, without a UI thread.
    /// </summary>
    /// <remarks>
    /// <c>NextActionBanner</c> needs an <c>InfoBar</c>, which needs a XAML host this
    /// suite does not have. Its ownership clause is mirrored here; the guard that the
    /// real renderer still matches is in <c>DealPaperTruthTests</c>, which reads the
    /// source.
    /// </remarks>
    private static string Message(string? name, Guid? assignee)
    {
        NextAction next = NextActionFrom.Of(
            [new NextActionFrom.Candidate("Confirm the option window", "Open", Now.AddDays(3), name, assignee)],
            Now)!;

        string who = next switch
        {
            { Assignee: { } named } => $" Assigned to {named}.",
            { IsAssigned: true } => " Assigned, name unavailable.",
            _ => " Unassigned.",
        };

        return next.Title + "." + who;
    }
}
