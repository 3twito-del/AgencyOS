using System;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// Builds one instant from the date and the time of day an operator chose.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-026</c>. An instant field offered a date picker only, so the time
/// of day stored was whenever the dialog happened to be constructed: a meeting
/// recorded on Friday for Tuesday was stored as Tuesday at Friday's time of day.
/// The operator could not see that part of the value, let alone correct it.
/// </para>
/// <para>
/// The offset is the one in force on **that date**, not today's. A meeting in
/// January recorded in July would otherwise be written an hour out, which is the
/// same class of quiet wrongness the finding is about.
/// </para>
/// <para>
/// This produces a local instant and stops. Canonical UTC normalization stays
/// where <c>AOS-R002-001</c> put it — once, at the API boundary — and nothing here
/// converts anything.
/// </para>
/// </remarks>
public static class LocalInstant
{
    /// <summary>The instant at this local date and time of day.</summary>
    /// <param name="date">The calendar date the operator picked.</param>
    /// <param name="timeOfDay">The time of day the operator picked.</param>
    public static DateTimeOffset From(DateTimeOffset date, TimeSpan timeOfDay) =>
        From(DateOnly.FromDateTime(date.Date), timeOfDay);

    /// <summary>The instant at this local date and time of day.</summary>
    public static DateTimeOffset From(DateOnly date, TimeSpan timeOfDay)
    {
        DateTime local = date.ToDateTime(TimeOnly.FromTimeSpan(Clamp(timeOfDay)));

        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    /// <summary>Now, as the two parts an operator edits.</summary>
    /// <remarks>
    /// Local rather than UTC, because it is what the pickers display and what the
    /// operator is answering about.
    /// </remarks>
    public static (DateTimeOffset Date, TimeSpan TimeOfDay) Now()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        return (now, now.TimeOfDay);
    }

    /// <summary>
    /// Keeps a time of day inside one day.
    /// </summary>
    /// <remarks>
    /// A time picker cannot produce anything else, but this is arithmetic on a
    /// value that arrives from a control, and a negative or 24-hour span would
    /// silently move the date.
    /// </remarks>
    private static TimeSpan Clamp(TimeSpan timeOfDay) =>
        timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1)
            ? TimeSpan.Zero
            : timeOfDay;
}
