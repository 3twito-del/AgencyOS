namespace AgencyOS.Domain;

/// <summary>
/// Assembly anchor for the AgencyOS domain layer.
/// </summary>
/// <remarks>
/// <para>
/// Governing documents:
/// <c>docs/08_DATA_MODEL_FOUNDATION.md</c> (entity and history rules),
/// <c>docs/02_ARCHITECTURE.md</c> (module composition),
/// <c>docs/10_ENGINEERING_STANDARDS.md</c> (domain command discipline).
/// </para>
/// <para>
/// This layer holds entities, value objects and domain commands. It takes no
/// dependency on persistence, transport or the Windows client. Milestone M0
/// intentionally ships no business entities; they arrive with M1 (Identity,
/// Organization, Audit) and M2 (People vertical slice).
/// </para>
/// </remarks>
public static class DomainAssembly
{
    /// <summary>Gets the domain assembly, for reflection-based registration and tests.</summary>
    public static System.Reflection.Assembly Value => typeof(DomainAssembly).Assembly;
}
