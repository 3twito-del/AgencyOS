using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <summary>Why a lease did not authorize what was presented against it.</summary>
/// <remarks>
/// <para>
/// Categories rather than prose, because the client renders different words for
/// each and a test asserts on them. Deliberately coarse where the answer is
/// authorization-shaped: <see cref="NotFound"/> covers a lease that belongs to
/// another tenant, another user or another run, so a caller learns nothing about
/// runs that are not theirs by probing identifiers (§4, §N).
/// </para>
/// <para>
/// The distinctions that <em>are</em> drawn are the ones the holder legitimately
/// needs: their own lease lapsed, was already used, or was withdrawn because they
/// cancelled the run. Each calls for a different thing from the person.
/// </para>
/// </remarks>
public enum LocalResultRefusal
{
    None = 0,

    /// <summary>
    /// No lease this caller may present. Covers wrong tenant, wrong user, wrong
    /// run and simply absent.
    /// </summary>
    NotFound,

    /// <summary>The window closed before the result came back.</summary>
    Expired,

    /// <summary>Already used. A replay, or a second workstation.</summary>
    Consumed,

    /// <summary>Withdrawn, because the run was cancelled.</summary>
    Invalidated,

    /// <summary>
    /// The context presented is not the context the lease was issued against.
    /// </summary>
    ContextMismatch,

    /// <summary>The run moved on and no longer awaits a local result.</summary>
    RunNotWaiting,

    /// <summary>The caller no longer holds the grant the context was assembled under.</summary>
    PermissionRevoked,

    /// <summary>The client returned nothing usable.</summary>
    EmptyResult,
}

/// <param name="ModelKey">Which device-local model the workstation will use.</param>
public sealed record IssueContextLeaseCommand(
    OrganizationId OrganizationId,
    AgentRunId RunId,
    string ModelKey);

/// <param name="Text">What the local model produced. Untrusted.</param>
/// <param name="Failure">The client's category for why it produced nothing.</param>
public sealed record SubmitLocalResultCommand(
    OrganizationId OrganizationId,
    AgentRunId RunId,
    AiContextLeaseId LeaseId,
    string? Text,
    string? Failure,
    string? ExecutionDevice);

/// <summary>What the workstation is given, and what it must present back.</summary>
public sealed record IssuedLease(
    AiContextLease Lease,
    string Prompt,
    AiContext Context,
    int MaxOutputTokens);

/// <summary>The outcome of presenting a local result.</summary>
public sealed record LocalResultOutcome(
    bool Accepted,
    AgentRunStatus Status,
    LocalResultRefusal Refusal);

/// <summary>
/// Issues context leases and accepts what comes back.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The server half of the device-local protocol.</strong> Everything the
/// workstation is trusted with is decided here: what context it receives, for how
/// long, and whether what it returns becomes part of the run. The client decides
/// none of it, and a result arriving from it is validated exactly as a cloud
/// provider's answer is (ADR-0035).
/// </para>
/// <para>
/// The two operations are separate commands because they are separated by an
/// interval AgencyOS does not control — the model runs on somebody's laptop, and
/// between issuing and returning the grant can be revoked, the run cancelled and
/// the lease expired. <c>specs/LocalInferenceLease.tla</c> models exactly that
/// interval.
/// </para>
/// </remarks>
public sealed class LocalInferenceHandler
{
    private readonly IAgentRunRepository _runs;
    private readonly IAiContextLeaseRepository _leases;
    private readonly IAiContextAssembler _context;
    private readonly ModelDataPolicy _dataPolicy;
    private readonly IModelGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;

    public LocalInferenceHandler(
        IAgentRunRepository runs,
        IAiContextLeaseRepository leases,
        IAiContextAssembler context,
        ModelDataPolicy dataPolicy,
        IModelGateway gateway,
        IUnitOfWork unitOfWork,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock)
    {
        _runs = runs;
        _leases = leases;
        _context = context;
        _dataPolicy = dataPolicy;
        _gateway = gateway;
        _unitOfWork = unitOfWork;
        _guard = guard;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>How long a workstation has to answer.</summary>
    /// <remarks>
    /// Short, and shorter than an approval window. A lease is permission to
    /// compute now; a long one would be a standing grant to a device, and the
    /// material is already in that device's memory the moment it is issued.
    /// </remarks>
    public static TimeSpan LeaseValidity { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Assembles authorized context and issues a single-use lease for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permission is checked immediately before disclosure, not merely when the
    /// run started. A run may sit queued while somebody's role changes, and the
    /// one revocation case AgencyOS can honour completely is the one that happens
    /// before the material leaves (§C.1, §C.2).
    /// </para>
    /// <para>
    /// The residency comes from the run, never from the request. A client that
    /// could name its own residency could ask for a device-local lease on a run
    /// the policy admitted only for the cloud.
    /// </para>
    /// </remarks>
    public async Task<IssuedLease> HandleAsync(
        IssueContextLeaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiUse, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AgentRun run = await _runs
            .FindAsync(command.OrganizationId, command.RunId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(AgentRun), command.RunId.ToString());

        // A run is private to whoever started it, so a lease on somebody else's is
        // not refused — it is not found (§39, ADR-0031).
        if (run.UserId != actor)
        {
            throw new EntityNotFoundException(nameof(AgentRun), command.RunId.ToString());
        }

        if (run.Residency != ModelResidency.DeviceLocal)
        {
            throw new DomainException(
                "That run executes on the server. A context lease is how material "
                    + "reaches somewhere AgencyOS does not control, and this run does "
                    + "not go anywhere.");
        }

        if (run.IsTerminal)
        {
            throw new DomainException(
                $"That run is {run.Status.ToString().ToLowerInvariant()} and needs no "
                    + "further context.");
        }

        if (_gateway.Describe(command.ModelKey) is not { } descriptor
            || descriptor.Residency != ModelResidency.DeviceLocal)
        {
            throw new DomainException(
                "That model is not a device-local one on this server.");
        }

        AgentDefinition definition = AgentCatalog.ByKind[run.Kind];

        // Assembly re-applies the data policy for this residency. It is not
        // trusted from the moment the run started: an administrator may have
        // narrowed what may leave since.
        AiContext assembled = await _context
            .AssembleAsync(
                new AiContextRequest(
                    run.OrganizationId,
                    run.Kind,
                    run.SubjectKind,
                    run.SubjectId,
                    run.ProviderKey,
                    definition.Limits.MaxContextCharacters),
                cancellationToken)
            .ConfigureAwait(false);

        if (assembled.RefusedOutright)
        {
            run.Fail(
                AgentFailureKind.PolicyRefused,
                assembled.RefusalReason,
                _clock.UtcNow,
                run.Version);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            throw new DomainException(assembled.RefusalReason!);
        }

        int policyVersion = await _dataPolicy
            .PolicyVersionAsync(run.OrganizationId, run.ProviderKey, cancellationToken)
            .ConfigureAwait(false);

        string rendered = assembled.Render();

        AiContextLease lease = AiContextLease.Issue(
            run.OrganizationId,
            actor,
            run.Id,
            run.SubjectKind,
            run.SubjectId,
            ModelResidency.DeviceLocal,
            AiContextLease.ComputeFingerprint(
                run.OrganizationId,
                actor,
                run.Id,
                run.SubjectKind,
                run.SubjectId,
                ModelResidency.DeviceLocal,
                rendered),
            command.ModelKey,
            policyVersion,
            _clock.UtcNow,
            LeaseValidity);

        _leases.Add(lease);

        run.AwaitLocalExecution(_clock.UtcNow, run.Version);

        run.AppendStep(
            AgentStepKind.ContextAssembled,
            $"Assembled {assembled.Blocks.Count} context blocks for this device.",
            _clock.UtcNow,
            assembled.OmittedBlockCount > 0
                ? $"{assembled.OmittedBlockCount} were withheld by policy."
                : null,
            lease.Id.Value);

        // Recorded because it is a disclosure. The audit says material left the
        // server for a device, which is the one fact a reviewer cannot reconstruct
        // afterwards from anything else (§C.3).
        _audit.Record(
            AuditAction.AiContextLeaseIssued,
            entityType: nameof(AiContextLease),
            entityId: lease.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.AiUse,
            semanticDelta: new
            {
                agentRun = run.Id.ToString(),
                residency = ModelResidency.DeviceLocal.ToString(),
                model = command.ModelKey,
                blocks = assembled.Blocks.Count,
                withheld = assembled.OmittedBlockCount,
                expiresAt = lease.ExpiresAt,
            },
            reason: "Authorized context was disclosed to the user's workstation for "
                + "device-local inference.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new IssuedLease(
            lease, definition.SystemPrompt, assembled, definition.Limits.MaxOutputTokens);
    }

    /// <summary>
    /// Accepts, or refuses, what the workstation returned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is re-established here and nothing is trusted from issuance: the
    /// lease is still outstanding, it belongs to this caller and this run, the
    /// window has not closed, the run still awaits a result, the caller still
    /// holds the grant, and the context the server would assemble now still
    /// matches what the lease was issued against.
    /// </para>
    /// <para>
    /// That last check is the one that costs something — it reassembles the
    /// context — and it is the one that makes the fingerprint mean anything. A
    /// server that took the client's word for what context was used would have a
    /// fingerprint that proves the client can echo a string.
    /// </para>
    /// </remarks>
    public async Task<LocalResultOutcome> HandleAsync(
        SubmitLocalResultCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiUse, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AiContextLease? lease = await _leases
            .FindAsync(command.OrganizationId, command.LeaseId, cancellationToken)
            .ConfigureAwait(false);

        // One answer for every way a lease can fail to be this caller's, so
        // probing identifiers reveals nothing about runs that are not theirs.
        if (lease is null || lease.UserId != actor || lease.AgentRunId != command.RunId)
        {
            return Refused(LocalResultRefusal.NotFound);
        }

        if (lease.State == AiContextLeaseState.Consumed)
        {
            return Refused(LocalResultRefusal.Consumed);
        }

        if (lease.State == AiContextLeaseState.Invalidated)
        {
            return Refused(LocalResultRefusal.Invalidated);
        }

        if (_clock.UtcNow > lease.ExpiresAt)
        {
            return Refused(LocalResultRefusal.Expired);
        }

        AgentRun run = await _runs
            .FindAsync(command.OrganizationId, command.RunId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(AgentRun), command.RunId.ToString());

        if (run.Status != AgentRunStatus.AwaitingLocalExecution)
        {
            return Refused(LocalResultRefusal.RunNotWaiting);
        }

        AgentDefinition definition = AgentCatalog.ByKind[run.Kind];

        AiContext assembled = await _context
            .AssembleAsync(
                new AiContextRequest(
                    run.OrganizationId,
                    run.Kind,
                    run.SubjectKind,
                    run.SubjectId,
                    run.ProviderKey,
                    definition.Limits.MaxContextCharacters),
                cancellationToken)
            .ConfigureAwait(false);

        // A grant lost while the model ran shows up here as a context the server
        // will no longer assemble, or as an outright refusal. Both mean the same
        // thing: this result was produced from material the caller may no longer
        // have (§C.5).
        if (assembled.RefusedOutright)
        {
            return Refused(LocalResultRefusal.PermissionRevoked);
        }

        string fingerprint = AiContextLease.ComputeFingerprint(
            run.OrganizationId,
            actor,
            run.Id,
            run.SubjectKind,
            run.SubjectId,
            ModelResidency.DeviceLocal,
            assembled.Render());

        if (!lease.Authorizes(
            command.OrganizationId,
            actor,
            command.RunId,
            ModelResidency.DeviceLocal,
            fingerprint,
            _clock.UtcNow))
        {
            return Refused(LocalResultRefusal.ContextMismatch);
        }

        // The lease is consumed either way. It authorized one execution, and that
        // execution happened — whether the model produced prose or the workstation
        // reported it could not run is not the lease's business (§J).
        lease.Consume(_clock.UtcNow, lease.Version);

        run.RecordExecutionDevice(command.ExecutionDevice, _clock.UtcNow, run.Version);

        if (command.Failure is { Length: > 0 } failure)
        {
            run.AppendStep(
                AgentStepKind.Refused,
                "The workstation could not run the model.",
                _clock.UtcNow,
                Sanitize(failure));

            run.Fail(
                Classify(failure),
                "Device-local inference did not run. Nothing was sent anywhere else.",
                _clock.UtcNow,
                run.Version);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new LocalResultOutcome(
                Accepted: true, run.Status, LocalResultRefusal.None);
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            run.Fail(
                AgentFailureKind.ProviderInvalidResponse,
                "The device-local model returned nothing.",
                _clock.UtcNow,
                run.Version);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new LocalResultOutcome(
                Accepted: false, run.Status, LocalResultRefusal.EmptyResult);
        }

        run.AppendStep(
            AgentStepKind.ModelInvocation,
            $"The workstation ran {lease.ModelKey}.",
            _clock.UtcNow,
            command.ExecutionDevice is { Length: > 0 } device
                ? $"Reported device: {Sanitize(device)}."
                : null);

        // Validated exactly as a cloud answer is. A citation naming something the
        // run was not given resolves to nothing whichever machine produced it.
        run.Complete(
            AiCitationValidator.Strip(command.Text, assembled.Citable),
            _clock.UtcNow,
            run.Version);

        _audit.Record(
            AuditAction.AiLocalResultAccepted,
            entityType: nameof(AgentRun),
            entityId: run.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.AiUse,
            semanticDelta: new
            {
                lease = lease.Id.ToString(),
                model = lease.ModelKey,
                device = command.ExecutionDevice,
            },
            reason: "A device-local result was validated and accepted.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LocalResultOutcome(Accepted: true, run.Status, LocalResultRefusal.None);
    }

    /// <summary>
    /// Maps the workstation's category onto a failure kind.
    /// </summary>
    /// <remarks>
    /// The client's word is taken for <em>why</em> it could not run, because only
    /// it knows, and nothing depends on the answer beyond what a person reads. It
    /// is not taken for anything else.
    /// </remarks>
    private static AgentFailureKind Classify(string failure) => failure switch
    {
        nameof(AgentFailureKind.LocalModelNotReady) => AgentFailureKind.LocalModelNotReady,
        nameof(AgentFailureKind.ProviderTimeout) => AgentFailureKind.ProviderTimeout,
        _ => AgentFailureKind.LocalProviderUnavailable,
    };

    /// <summary>Makes a client-supplied string safe to put in a run history.</summary>
    private static string Sanitize(string value)
    {
        string trimmed = value.Length > 200 ? value[..200] : value;

        return new string([.. trimmed.Where(c => !char.IsControl(c))]);
    }

    private static LocalResultOutcome Refused(LocalResultRefusal refusal) =>
        new(Accepted: false, AgentRunStatus.AwaitingLocalExecution, refusal);
}

/// <summary>Reads and writes context leases.</summary>
public interface IAiContextLeaseRepository
{
    Task<AiContextLease?> FindAsync(
        OrganizationId organizationId,
        AiContextLeaseId id,
        CancellationToken cancellationToken = default);

    /// <summary>Every outstanding lease for a run, so cancelling can withdraw them.</summary>
    Task<IReadOnlyList<AiContextLease>> ListOutstandingAsync(
        OrganizationId organizationId,
        AgentRunId runId,
        CancellationToken cancellationToken = default);

    void Add(AiContextLease lease);
}
