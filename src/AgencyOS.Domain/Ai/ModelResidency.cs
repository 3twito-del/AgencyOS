namespace AgencyOS.Domain.Ai;

/// <summary>
/// Where inference physically executes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Residency is not authorization.</strong> It answers one question —
/// where does the computation happen — and none of the others. A model is no more
/// trustworthy for running on the user's laptop: it is still probabilistic, still
/// capable of inventing a tool argument, still subject to the registry and still
/// subject to a person's approval. The only thing residency changes is data
/// transmission (ADR-0035).
/// </para>
/// <para>
/// In particular <see cref="ModelDataSensitivity.Restricted"/> stays unreachable
/// at every residency. Material an organization marked as never leaving does not
/// become eligible because the computation moved closer; the classification is
/// about disclosure to a process, not about geography, and a laptop is a process
/// AgencyOS does not control either.
/// </para>
/// <para>
/// Ordered by how far the material travels, which is also the order in which a
/// policy ceiling may reasonably rise. Nothing depends on the numbering beyond
/// the stored value.
/// </para>
/// </remarks>
public enum ModelResidency
{
    /// <summary>
    /// A third-party service reached over the internet.
    /// </summary>
    /// <remarks>
    /// The material leaves the organization entirely and lands in a system with
    /// its own retention, its own logging and its own staff.
    /// </remarks>
    ExternalCloud = 1,

    /// <summary>
    /// Infrastructure the organization runs, including the AgencyOS server itself.
    /// </summary>
    /// <remarks>
    /// The material stays inside the organization's boundary. Not currently used
    /// by any provider in this build; the value exists so the scale is complete
    /// and a self-hosted model does not have to be mislabelled when one arrives.
    /// </remarks>
    OrganizationControlled = 2,

    /// <summary>
    /// The user's own workstation.
    /// </summary>
    /// <remarks>
    /// The material reaches one device, which the user already controls and could
    /// already read from. That is why device-local inference discloses nothing new
    /// to the <em>user</em> — and why it still discloses to a process AgencyOS does
    /// not audit, which is what keeps the ceiling conservative.
    /// </remarks>
    DeviceLocal = 3,
}
