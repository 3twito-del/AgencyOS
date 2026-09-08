using System.Diagnostics;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Application.Documents;
using AgencyOS.Domain.Communications;

namespace AgencyOS.Api.Workers;

/// <summary>How often the worker looks for something to do.</summary>
public sealed class CommunicationWorkerOptions
{
    /// <summary>Whether the worker runs at all.</summary>
    /// <remarks>
    /// Off in tests that drive the protocol themselves, so a background loop cannot
    /// race a test's own stepping and turn a deterministic assertion into a flaky
    /// one.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a claim lasts before another worker may take it.</summary>
    /// <remarks>
    /// Long enough for one external call and short enough that a crashed worker
    /// does not strand a message. A lease that never expired would mean a process
    /// killed mid-send left that dispatch stuck for ever (ADR-0029).
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long an unfinished upload waits before it is swept.</summary>
    /// <remarks>
    /// Comfortably longer than the longest plausible upload, so a request still in
    /// flight is never swept out from under itself (ADR-0024).
    /// </remarks>
    public TimeSpan SweepGracePeriod { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// The server-side loop that synchronizes mailboxes, drives outbound sends and
/// collects abandoned uploads.
/// </summary>
/// <remarks>
/// <para>
/// A hosted service inside the modular monolith, not a service of its own. The
/// work is three small periodic jobs whose canonical state already lives in
/// PostgreSQL; extracting them would add a deployment, a failure domain and a
/// second account of what has happened, in exchange for nothing (CLAUDE.md §5,
/// ADR-0029).
/// </para>
/// <para>
/// <strong>Nothing here is the source of truth.</strong> Every claim, every state
/// transition and every attempt count is a row. The worker holds no queue in
/// memory, so a restart loses nothing and two instances running at once is
/// ordinary rather than a bug: each claims rows with <c>FOR UPDATE SKIP LOCKED</c>
/// and an expiring lease, so one wins and the other moves on.
/// </para>
/// <para>
/// Temporal was evaluated and not adopted. There is no long-running orchestration
/// here — each step is one transaction and one external call — and the recovery
/// that matters is a query against the provider, which a workflow engine would not
/// perform for us (ADR-0029).
/// </para>
/// </remarks>
public sealed class CommunicationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CommunicationWorkerOptions _options;
    private readonly ILogger<CommunicationWorker> _logger;

    /// <summary>Identifies this worker in a lease. Unique per process.</summary>
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}";

    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    public CommunicationWorker(
        IServiceScopeFactory scopes,
        CommunicationWorkerOptions options,
        ILogger<CommunicationWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("The communication worker is disabled by configuration.");
            return;
        }

        _logger.LogInformation("Communication worker {Owner} started.", _owner);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sends first. An unknown outcome is the only state in the system
                // that a person has to resolve by hand, so getting one resolved by
                // reconciliation matters more than reading new mail.
                bool worked = await ProcessDispatchesAsync(stoppingToken).ConfigureAwait(false);

                worked |= await SynchronizeMailboxesAsync(stoppingToken).ConfigureAwait(false);

                await SweepAsync(stoppingToken).ConfigureAwait(false);

                if (!worked)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                // The loop must survive anything. A worker that died on one bad row
                // would leave every other mailbox unsynchronized and every queued
                // message unsent, with no signal that it had stopped.
                _logger.LogError(failure, "The communication worker loop failed. Continuing.");

                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Communication worker {Owner} stopped.", _owner);
    }

    /// <summary>
    /// Advances one outbound dispatch, if one is claimable.
    /// </summary>
    /// <remarks>
    /// One step per pass, and one dispatch per pass. Draining the queue inside a
    /// single claim would hold a lease across many external calls; stepping means
    /// every intermediate state is committed and recoverable (ADR-0028).
    /// </remarks>
    private async Task<bool> ProcessDispatchesAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        IOutboundDispatchRepository dispatches =
            scope.ServiceProvider.GetRequiredService<IOutboundDispatchRepository>();

        IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        OutboundDispatch? dispatch = await dispatches
            .ClaimNextAsync(_owner, _options.LeaseDuration, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (dispatch is null)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        using Activity? activity =
            AgencyOsTelemetry.Source.StartActivity("agencyos.communication.dispatch");

        OutboundDispatchState before = dispatch.State;

        OutboundSendProcessor processor =
            scope.ServiceProvider.GetRequiredService<OutboundSendProcessor>();

        OutboundStepResult result = await processor
            .StepAsync(dispatch, cancellationToken).ConfigureAwait(false);

        dispatch.ReleaseLease(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The state reached, and nothing else. No subject, no recipient, no body:
        // telemetry is exported to places holding no communications permission
        // (ADR-0026).
        AgencyOsTelemetry.OutboundStateChanges.Add(
            1,
            new KeyValuePair<string, object?>("from", before.ToString()),
            new KeyValuePair<string, object?>("to", result.State.ToString()));

        if (result.State == OutboundDispatchState.UnknownOutcome)
        {
            // Counted separately because it is the one outcome nothing can resolve
            // on its own. A rising number is a person's problem, not a retry.
            AgencyOsTelemetry.OutboundUnknownOutcomes.Add(1);

            _logger.LogWarning(
                "Dispatch {DispatchId} has an unknown outcome and will be reconciled, not retried.",
                dispatch.Id);
        }

        return true;
    }

    /// <summary>Synchronizes one mailbox, if one is claimable.</summary>
    private async Task<bool> SynchronizeMailboxesAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        ICommunicationAccountRepository accounts =
            scope.ServiceProvider.GetRequiredService<ICommunicationAccountRepository>();

        IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        CommunicationAccount? account = await accounts
            .ClaimForSyncAsync(_owner, _options.LeaseDuration, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        using Activity? activity =
            AgencyOsTelemetry.Source.StartActivity("agencyos.communication.sync");

        MailboxSynchronizer synchronizer =
            scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();

        MailboxSyncResult result = await synchronizer
            .SynchronizeAsync(account, cancellationToken).ConfigureAwait(false);

        account.ReleaseLease(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Counts and provider, never a subject or an address.
        AgencyOsTelemetry.MessagesSynchronized.Add(
            result.MessagesCreated,
            new KeyValuePair<string, object?>("provider", account.Provider.ToString()));

        if (result.CursorReset)
        {
            AgencyOsTelemetry.SyncCursorResets.Add(1);
        }

        if (result.Error is not null)
        {
            AgencyOsTelemetry.SyncFailures.Add(1);
        }

        return true;
    }

    /// <summary>Collects bytes from uploads that never became a document.</summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();

        if (clock.UtcNow - _lastSweep < _options.SweepInterval)
        {
            return;
        }

        _lastSweep = clock.UtcNow;

        DocumentIngestion ingestion =
            scope.ServiceProvider.GetRequiredService<DocumentIngestion>();

        int swept = await ingestion
            .SweepAsync(_options.SweepGracePeriod, 200, cancellationToken)
            .ConfigureAwait(false);

        if (swept > 0)
        {
            AgencyOsTelemetry.OrphanedUploadsSwept.Add(swept);

            _logger.LogInformation("Swept {Count} unfinished upload(s).", swept);
        }
    }
}
