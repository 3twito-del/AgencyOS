namespace AgencyOS.Application;

/// <summary>
/// Assembly anchor for the AgencyOS application layer.
/// </summary>
/// <remarks>
/// <para>
/// Governing documents:
/// <c>docs/02_ARCHITECTURE.md</c> (module composition),
/// <c>docs/07_SECURITY_AND_AUDIT.md</c> (server-side authorization),
/// <c>docs/10_ENGINEERING_STANDARDS.md</c> (explicit domain commands).
/// </para>
/// <para>
/// This layer orchestrates domain commands and queries, and is where
/// authorization and audit decisions are made. Per
/// <c>docs/07_SECURITY_AND_AUDIT.md</c>, the Windows client is never trusted to
/// enforce permissions, so no enforcement may be delegated outward from here.
/// </para>
/// </remarks>
public static class ApplicationAssembly
{
    /// <summary>Gets the application assembly, for reflection-based registration and tests.</summary>
    public static System.Reflection.Assembly Value => typeof(ApplicationAssembly).Assembly;
}
