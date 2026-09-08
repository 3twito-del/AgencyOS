using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <summary>
/// Decides what AgencyOS may transmit to a model provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a different question from authorization, and asking it second
/// is the point.</strong> "May Ariel read this" is settled by the milestone that
/// owns the record. "May AgencyOS put it in a prompt" is settled here, and a firm
/// can perfectly reasonably answer yes to the first and no to the second — a
/// source-sensitive signal names somebody who spoke in confidence, and reading it
/// at a desk is not the same act as sending it to another company's datacentre
/// (§5, §42).
/// </para>
/// <para>
/// Three things must all agree before anything leaves: the user holds the grant,
/// the organization's policy permits the classification, and the classification is
/// not restricted. A user grant cannot raise the organization's ceiling and the
/// ceiling cannot reach restricted, so the two are not substitutes for each other
/// (ADR-0031).
/// </para>
/// </remarks>
public sealed class ModelDataPolicy
{
    private readonly TenantGuard _guard;
    private readonly IAiProviderPolicyRepository _policies;

    public ModelDataPolicy(TenantGuard guard, IAiProviderPolicyRepository policies)
    {
        _guard = guard;
        _policies = policies;
    }

    /// <summary>
    /// Maps an M11 classification onto the transmission scale.
    /// </summary>
    /// <remarks>
    /// Confidential and restricted map where their names suggest.
    /// <see cref="IntelligenceSensitivity.SourceSensitive"/> maps to
    /// <see cref="ModelDataSensitivity.Protected"/> rather than to Confidential:
    /// what it protects is a person's identity, and the damage from disclosing that
    /// is different in kind from a commercial confidence (ADR-0030).
    /// </remarks>
    public static ModelDataSensitivity Classify(IntelligenceSensitivity sensitivity) =>
        sensitivity switch
        {
            IntelligenceSensitivity.Confidential => ModelDataSensitivity.Confidential,
            IntelligenceSensitivity.SourceSensitive => ModelDataSensitivity.Protected,
            IntelligenceSensitivity.Restricted => ModelDataSensitivity.Restricted,
            _ => ModelDataSensitivity.Internal,
        };

    /// <summary>
    /// Maps an M10 document classification onto the transmission scale.
    /// </summary>
    /// <remarks>
    /// Privileged maps to <see cref="ModelDataSensitivity.Protected"/>. Legal
    /// privilege is a conclusion a lawyer drew about a document, and transmitting
    /// one to a third-party processor is exactly the act that can be argued to
    /// waive it — a question AgencyOS is in no position to answer, so it does not
    /// send the document (ADR-0025).
    /// </remarks>
    public static ModelDataSensitivity Classify(DocumentSensitivity sensitivity) =>
        sensitivity switch
        {
            DocumentSensitivity.Confidential => ModelDataSensitivity.Confidential,
            DocumentSensitivity.Financial => ModelDataSensitivity.Confidential,
            DocumentSensitivity.Privileged => ModelDataSensitivity.Protected,
            DocumentSensitivity.Restricted => ModelDataSensitivity.Restricted,
            _ => ModelDataSensitivity.Internal,
        };

    /// <summary>
    /// Maps a mailbox's visibility onto the transmission scale.
    /// </summary>
    /// <remarks>
    /// A private mailbox is somebody's correspondence. Even where a reader is
    /// entitled to it, transmitting it is treated as protected rather than
    /// ordinary: the people in those messages did not choose AgencyOS, let alone a
    /// provider (ADR-0026).
    /// </remarks>
    public static ModelDataSensitivity Classify(MailboxVisibility visibility) =>
        visibility == MailboxVisibility.Private
            ? ModelDataSensitivity.Protected
            : ModelDataSensitivity.Confidential;

    /// <summary>
    /// The strictest classification in a set, which is the one that governs.
    /// </summary>
    /// <remarks>
    /// A context block is only as transmissible as its most sensitive part. Taking
    /// the maximum rather than an average is the difference between refusing a
    /// prompt and sending a confidence buried in ten pages of ordinary material.
    /// </remarks>
    public static ModelDataSensitivity Highest(IEnumerable<ModelDataSensitivity> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ModelDataSensitivity highest = ModelDataSensitivity.Internal;

        foreach (ModelDataSensitivity value in values)
        {
            if (value > highest)
            {
                highest = value;
            }
        }

        return highest;
    }

    /// <summary>
    /// Whether this material may be transmitted to this provider, for this caller.
    /// </summary>
    /// <remarks>
    /// Returns a verdict rather than throwing, because the ordinary use is to
    /// filter a context block rather than to abort a run: a brief that leaves out
    /// one protected signal is useful, and one that refuses to run because a
    /// protected signal existed is not. The caller decides which case it is in.
    /// </remarks>
    public async Task<ModelDataVerdict> EvaluateAsync(
        OrganizationId organizationId,
        string providerKey,
        ModelDataSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        // Restricted is refused before anything else is consulted. No grant and no
        // configuration reaches it, so asking about them would imply otherwise.
        if (sensitivity == ModelDataSensitivity.Restricted)
        {
            return ModelDataVerdict.Refused(
                "Restricted material is never transmitted to a model provider.");
        }

        AiProviderPolicy policy = await _policies
            .FindAsync(organizationId, providerKey, cancellationToken)
            .ConfigureAwait(false)
            ?? AiProviderPolicy.ClosedDefault(organizationId, providerKey);

        if (!policy.IsEnabled)
        {
            return ModelDataVerdict.Refused(
                $"This organization has not enabled the {providerKey} provider. "
                    + "A configured server credential does not enable a tenant.");
        }

        if (!policy.Permits(sensitivity))
        {
            return ModelDataVerdict.Refused(
                $"This organization permits transmitting up to "
                    + $"{policy.MaximumSensitivity} material to {providerKey}, and "
                    + $"this is {sensitivity}.");
        }

        // Above ordinary internal, the caller needs the second grant as well.
        // Holding the read permission for the underlying record is not it.
        if (sensitivity > ModelDataSensitivity.Internal
            && !await _guard
                .HasPermissionAsync(Permission.AiSensitiveUse, organizationId, cancellationToken)
                .ConfigureAwait(false))
        {
            return ModelDataVerdict.Refused(
                $"Reasoning over {sensitivity} material needs {Permission.AiSensitiveUse}, "
                    + "which is a separate grant from being able to read it.");
        }

        return ModelDataVerdict.Allowed;
    }

    /// <summary>Whether an organization allows AI to propose canonical writes at all.</summary>
    public async Task<bool> AllowsWriteProposalsAsync(
        OrganizationId organizationId,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        AiProviderPolicy policy = await _policies
            .FindAsync(organizationId, providerKey, cancellationToken)
            .ConfigureAwait(false)
            ?? AiProviderPolicy.ClosedDefault(organizationId, providerKey);

        return policy.IsEnabled && policy.AllowsCanonicalWriteProposals;
    }
}

/// <param name="Reason">
/// Why not, in words a person can act on. Never the content that was refused, and
/// never a count of how much was refused where that would itself disclose
/// something (§6).
/// </param>
public sealed record ModelDataVerdict(bool IsAllowed, string? Reason)
{
    public static ModelDataVerdict Allowed { get; } = new(true, null);

    public static ModelDataVerdict Refused(string reason) => new(false, reason);
}

/// <summary>Reads and writes an organization's provider policy.</summary>
public interface IAiProviderPolicyRepository
{
    Task<AiProviderPolicy?> FindAsync(
        OrganizationId organizationId,
        string providerKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiProviderPolicy>> ListAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    void Add(AiProviderPolicy policy);
}
