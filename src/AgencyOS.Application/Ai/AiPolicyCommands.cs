using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <param name="ExpectedVersion">
/// Zero when no policy exists yet. The closed default reads as version 0, so
/// "there is no row" and "the row I read" are the same conversation.
/// </param>
public sealed record SetAiProviderPolicyCommand(
    OrganizationId OrganizationId,
    string ProviderKey,
    bool IsEnabled,
    ModelDataSensitivity MaximumSensitivity,
    bool AllowsCanonicalWriteProposals,
    int ExpectedVersion);

/// <summary>
/// Sets what an organization permits AgencyOS to transmit to one provider.
/// </summary>
/// <remarks>
/// <para>
/// Separate from every other AI permission. <c>ai.administer</c> is the authority
/// to decide what leaves the building, which is not the same authority as using a
/// model or approving one of its proposals — a person can reasonably hold either
/// of those without holding this (§42).
/// </para>
/// <para>
/// No credential passes through here. The command names a provider key that
/// already exists in server configuration and says what may be sent to it; whether
/// a key is configured at all is not this command's business and is not reported
/// back (§3).
/// </para>
/// </remarks>
public sealed class AiProviderPolicyHandler
{
    private readonly IAiProviderPolicyRepository _policies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;

    public AiProviderPolicyHandler(
        IAiProviderPolicyRepository policies,
        IUnitOfWork unitOfWork,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock)
    {
        _policies = policies;
        _unitOfWork = unitOfWork;
        _guard = guard;
        _audit = audit;
        _clock = clock;
    }

    public async Task HandleAsync(
        SetAiProviderPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiAdminister, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AiProviderPolicy? existing = await _policies
            .FindAsync(command.OrganizationId, command.ProviderKey, cancellationToken)
            .ConfigureAwait(false);

        // Recorded before the change so the audit answers "what did they turn on",
        // which is the question somebody reading it afterwards actually has.
        object before = existing is null
            ? new { isEnabled = false, ceiling = "Internal", writes = false }
            : new
            {
                isEnabled = existing.IsEnabled,
                ceiling = existing.MaximumSensitivity.ToString(),
                writes = existing.AllowsCanonicalWriteProposals,
            };

        if (existing is null)
        {
            _policies.Add(AiProviderPolicy.Create(
                command.OrganizationId,
                command.ProviderKey,
                command.IsEnabled,
                command.MaximumSensitivity,
                command.AllowsCanonicalWriteProposals,
                actor,
                _clock.UtcNow));
        }
        else
        {
            existing.Update(
                command.IsEnabled,
                command.MaximumSensitivity,
                command.AllowsCanonicalWriteProposals,
                actor,
                _clock.UtcNow,
                command.ExpectedVersion);
        }

        _audit.Record(
            AuditAction.AiProviderPolicyChanged,
            entityType: nameof(AiProviderPolicy),
            entityId: command.ProviderKey,
            organizationId: command.OrganizationId,
            permission: Permission.AiAdminister,
            semanticDelta: new
            {
                before,
                after = new
                {
                    isEnabled = command.IsEnabled,
                    ceiling = command.MaximumSensitivity.ToString(),
                    writes = command.AllowsCanonicalWriteProposals,
                },
            },
            reason: "Changed what this organization permits transmitting to a model "
                + "provider.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
