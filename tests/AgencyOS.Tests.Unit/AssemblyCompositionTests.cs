using AgencyOS.Application;
using AgencyOS.Domain;
using Xunit;

namespace AgencyOS.Tests.Unit;

/// <summary>
/// Verifies the layer anchors used for reflection-based registration.
/// </summary>
public sealed class AssemblyCompositionTests
{
    [Fact]
    public void DomainAnchor_ResolvesDomainAssembly()
    {
        Assert.Equal("AgencyOS.Domain", DomainAssembly.Value.GetName().Name);
    }

    [Fact]
    public void ApplicationAnchor_ResolvesApplicationAssembly()
    {
        Assert.Equal("AgencyOS.Application", ApplicationAssembly.Value.GetName().Name);
    }

    /// <summary>
    /// The domain layer must not acquire a dependency on the application layer;
    /// the reference runs one way only (docs/02_ARCHITECTURE.md).
    /// </summary>
    [Fact]
    public void Domain_DoesNotReferenceApplication()
    {
        Assert.DoesNotContain(
            DomainAssembly.Value.GetReferencedAssemblies(),
            reference => reference.Name == "AgencyOS.Application");
    }
}
