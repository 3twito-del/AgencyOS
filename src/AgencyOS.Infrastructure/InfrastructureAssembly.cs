namespace AgencyOS.Infrastructure;

/// <summary>
/// Assembly anchor for the AgencyOS infrastructure layer.
/// </summary>
/// <remarks>
/// <para>
/// Governing documents:
/// <c>docs/02_ARCHITECTURE.md</c> (canonical PostgreSQL, outbox, projections),
/// <c>docs/08_DATA_MODEL_FOUNDATION.md</c> (persistence rules),
/// <c>docs/10_ENGINEERING_STANDARDS.md</c> (SQL and migration standards).
/// </para>
/// <para>
/// This layer owns persistence, external adapters and cross-cutting host
/// concerns. Milestone M0 ships only the structured logging foundation and the
/// dependency-injection seam; PostgreSQL, EF Core and migrations arrive with M1.
/// </para>
/// </remarks>
public static class InfrastructureAssembly
{
    /// <summary>Gets the infrastructure assembly, for reflection-based registration and tests.</summary>
    public static System.Reflection.Assembly Value => typeof(InfrastructureAssembly).Assembly;
}
