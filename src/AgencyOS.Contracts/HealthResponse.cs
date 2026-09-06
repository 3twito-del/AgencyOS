namespace AgencyOS.Contracts;

/// <summary>
/// Liveness answer returned by <c>GET /health</c>.
/// </summary>
/// <param name="Status">Health state of the API host, for example <c>Healthy</c>.</param>
public sealed record HealthResponse(string Status);
