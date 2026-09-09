using AgencyOS.Api.Authorization;
using AgencyOS.Application.Ai;
using AgencyOS.Contracts.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M13 device-local inference surface.
/// </summary>
/// <remarks>
/// <para>
/// Two routes and a catalogue. A run marked device-local takes a lease, which
/// discloses the assembled context to the workstation; the workstation returns a
/// result against that lease, and the server validates it exactly as it validates
/// a cloud provider's answer (ADR-0035).
/// </para>
/// <para>
/// <strong>The client gains no authority here.</strong> It cannot name its own
/// residency — that comes from the run. It cannot supply context — the server
/// assembles it. It cannot assert that a lease is valid — the server recomputes
/// the fingerprint from what it would assemble now. And it cannot make a failed
/// local run into a cloud run: there is no route that would, and the gateway
/// refuses a device-local model outright.
/// </para>
/// <para>
/// Both routes answer <c>404</c> for anything belonging to another tenant,
/// another user or another run. A caller learns nothing about runs that are not
/// theirs by trying identifiers, which is the same rule M12 applies to approvals
/// (ADR-0031).
/// </para>
/// </remarks>
internal static class M13Endpoints
{
    public static void MapLocalInference(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}/ai");

        // Issues the lease and returns the context in one call, because they are
        // one act: the endpoint that authorizes the disclosure is the endpoint
        // that performs it. Two calls would model an interval that does not exist
        // and would leave a lease outstanding for context nobody received.
        tenant.MapPost("/runs/{runId:guid}/local-lease", async (
                Guid organizationId,
                Guid runId,
                IssueContextLeaseRequest request,
                LocalInferenceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                IssuedLease issued = await handler.HandleAsync(
                        new IssueContextLeaseCommand(
                            new OrganizationId(organizationId),
                            new AgentRunId(runId),
                            request.ModelKey),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(issued));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("IssueAiContextLease");

        // Carries the lease identifier and the model's output. It does not carry
        // the context: what the result was produced from is the server's own
        // assembly, recomputed and compared, so a client cannot present material
        // AgencyOS never authorized.
        tenant.MapPost("/runs/{runId:guid}/local-result", async (
                Guid organizationId,
                Guid runId,
                SubmitLocalResultRequest request,
                LocalInferenceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                LocalResultOutcome outcome = await handler.HandleAsync(
                        new SubmitLocalResultCommand(
                            new OrganizationId(organizationId),
                            new AgentRunId(runId),
                            new AiContextLeaseId(request.LeaseId),
                            request.Text,
                            request.Failure,
                            request.ExecutionDevice),
                        cancellationToken)
                    .ConfigureAwait(false);

                // A refusal is an ordinary outcome rather than an error: a lease
                // may have lapsed or been withdrawn while the model was running,
                // and the workstation needs to be told which.
                return Results.Ok(new SubmitLocalResultResponse(
                    outcome.Accepted,
                    outcome.Status.ToString(),
                    outcome.Refusal == LocalResultRefusal.None
                        ? null
                        : outcome.Refusal.ToString()));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("SubmitAiLocalResult");

        // What this server can execute and where. Says nothing about the
        // workstation's hardware: the workstation asks itself, and a client told
        // by the server what its own device can do would be trusting the wrong
        // party with the one question only it can answer.
        tenant.MapGet("/execution-targets", async (
                Guid organizationId,
                AiQueryService queries,
                IModelGateway gateway,
                CancellationToken cancellationToken) =>
            {
                await queries
                    .ListAgentsAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(gateway.Catalog
                    .Select(x => new AiExecutionTargetResponse(
                        x.Key,
                        x.ProviderKey,
                        x.Residency.ToString(),
                        x.RequiresContextLease,
                        x.SupportsTools,
                        x.SupportsStructuredOutput,
                        x.MaxContextTokens))
                    .ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiExecutionTargets");
    }

    private static ContextLeaseResponse Map(IssuedLease issued) =>
        new(
            issued.Lease.Id.Value,
            issued.Lease.AgentRunId.Value,
            issued.Lease.Residency.ToString(),
            issued.Lease.IssuedAt,
            issued.Lease.ExpiresAt,
            issued.Lease.ContextFingerprint,
            issued.Lease.ModelKey,
            issued.Prompt,
            [
                .. issued.Context.Blocks.Select(x => new AiContextBlockResponse(
                    x.Kind,
                    x.Label,
                    x.Content,
                    x.Trust == ContextTrust.Untrusted)),
            ],
            issued.MaxOutputTokens,
            issued.Context.OmittedBlockCount);
}
