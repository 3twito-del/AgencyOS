using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// That the instant stored is the one the operator chose, both halves of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-026</c>. <c>OccurredAt</c> and <c>SentAt</c> are instants, and
/// their dialogs offered a date picker only. The time of day stored was whenever
/// the dialog was constructed, so a meeting recorded on Friday for Tuesday was
/// filed as Tuesday at Friday's time of day — a part of the value the operator
/// could neither see nor correct.
/// </para>
/// <para>
/// This composes a local instant and nothing more. Canonical UTC normalization
/// stays at the API boundary, where <c>AOS-R002-001</c> put it.
/// </para>
/// </remarks>
public sealed class LocalInstantTests
{
    /// <summary>The date and the time both survive.</summary>
    [Fact]
    public void BothHalvesOfTheChoiceSurvive()
    {
        DateTimeOffset made = LocalInstant.From(new DateOnly(2026, 9, 15), new TimeSpan(14, 30, 0));

        Assert.Equal(new DateTime(2026, 9, 15, 14, 30, 0), made.DateTime);
    }

    /// <summary>
    /// The day the operator picked is the day stored.
    /// </summary>
    /// <remarks>
    /// The defect in one assertion: composing from a date picker whose own time
    /// component was set at construction gave the right day and the wrong hour.
    /// </remarks>
    [Fact]
    public void TheDialogsOwnClockDoesNotLeakIn()
    {
        DateTimeOffset pickedOnFridayForTuesday = new(
            2026, 9, 15, 17, 43, 21, TimeSpan.FromHours(3));

        DateTimeOffset made = LocalInstant.From(pickedOnFridayForTuesday, new TimeSpan(9, 0, 0));

        Assert.Equal(new DateTime(2026, 9, 15, 9, 0, 0), made.DateTime);
        Assert.Equal(0, made.Second);
    }

    /// <summary>Midnight is a time of day like any other.</summary>
    /// <remarks>
    /// The boundary that a date-only field silently used to produce, and the one
    /// most likely to be read as "no time was given".
    /// </remarks>
    [Fact]
    public void MidnightIsKept()
    {
        DateTimeOffset made = LocalInstant.From(new DateOnly(2026, 9, 15), TimeSpan.Zero);

        Assert.Equal(new DateTime(2026, 9, 15, 0, 0, 0), made.DateTime);
    }

    /// <summary>The last minute of the day stays on that day.</summary>
    [Fact]
    public void TheEndOfTheDayDoesNotRollOver()
    {
        DateTimeOffset made = LocalInstant.From(new DateOnly(2026, 9, 15), new TimeSpan(23, 59, 0));

        Assert.Equal(15, made.Day);
        Assert.Equal(23, made.Hour);
    }

    /// <summary>
    /// The offset is the one in force on the chosen date.
    /// </summary>
    /// <remarks>
    /// Not today's offset. A January meeting recorded in July would otherwise be
    /// written an hour out wherever daylight saving applies — the same quiet
    /// wrongness the finding is about, one level down.
    /// </remarks>
    [Theory]
    [InlineData(2026, 1, 15)]
    [InlineData(2026, 7, 15)]
    public void TheOffsetBelongsToTheChosenDate(int year, int month, int day)
    {
        DateOnly date = new(year, month, day);
        DateTimeOffset made = LocalInstant.From(date, new TimeSpan(12, 0, 0));

        DateTime local = date.ToDateTime(new TimeOnly(12, 0));

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(local), made.Offset);
    }

    /// <summary>
    /// The instant read back is the one intended, whatever the offset.
    /// </summary>
    /// <remarks>
    /// Constructed rather than taken from the machine, so the assertion holds on
    /// an agent in any zone: the same wall-clock reading at +03:00 and at -05:00
    /// are different moments, and both round-trip to themselves.
    /// </remarks>
    [Theory]
    [InlineData(3)]
    [InlineData(-5)]
    [InlineData(0)]
    public void AnInstantRoundTripsThroughUtc(int offsetHours)
    {
        DateTime local = new(2026, 9, 15, 14, 30, 0);
        DateTimeOffset made = new(local, TimeSpan.FromHours(offsetHours));

        DateTimeOffset canonical = made.ToUniversalTime();

        Assert.Equal(made, canonical);
        Assert.Equal(local.AddHours(-offsetHours), canonical.DateTime);
    }

    /// <summary>Now is offered as the two parts the operator edits.</summary>
    [Fact]
    public void NowIsOfferedAsDateAndTime()
    {
        (DateTimeOffset date, TimeSpan timeOfDay) = LocalInstant.Now();

        Assert.Equal(DateTimeOffset.Now.Date, date.Date);
        Assert.InRange(timeOfDay, TimeSpan.Zero, TimeSpan.FromDays(1));
    }

    /// <summary>A time of day outside one day cannot move the date.</summary>
    /// <remarks>
    /// A picker cannot produce these, but the value arrives from a control and
    /// silently shifting the day would be worse than ignoring the time.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void AnImpossibleTimeDoesNotMoveTheDay(int hours)
    {
        DateTimeOffset made = LocalInstant.From(
            new DateOnly(2026, 9, 15), TimeSpan.FromHours(hours));

        Assert.Equal(15, made.Day);
        Assert.Equal(0, made.Hour);
    }
}
