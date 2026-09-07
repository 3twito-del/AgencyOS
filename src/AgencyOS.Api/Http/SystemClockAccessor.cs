using AgencyOS.Api.Endpoints;

namespace AgencyOS.Api.Http;

/// <summary>Supplies today's date, in UTC.</summary>
/// <remarks>
/// UTC rather than server-local, so the date an endpoint defaults does not depend
/// on where the host happens to run.
/// </remarks>
internal sealed class SystemClockAccessor : IClockAccessor
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
