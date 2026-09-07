using AgencyOS.Application.Abstractions;

namespace AgencyOS.Infrastructure.Time;

/// <summary>The real clock, in UTC.</summary>
/// <remarks>
/// Always UTC. <c>docs/08_DATA_MODEL_FOUNDATION.md</c> stores instants in UTC, and
/// Npgsql maps <see cref="DateTimeOffset"/> to <c>timestamptz</c> only when the
/// offset is zero.
/// </remarks>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
