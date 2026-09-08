using AgencyOS.Application.Ai;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Infrastructure.Ai;

/// <summary>
/// Every tool this build has.
/// </summary>
/// <remarks>
/// <para>
/// Populated from dependency injection, which means from the explicit
/// registrations in composition. There is no scanning, no attribute discovery and
/// no reflection over application services: a tool exists because somebody wrote a
/// line adding it, and that line is what a reviewer reads (§8, §32).
/// </para>
/// <para>
/// Duplicate names are refused at construction. Two tools answering to one name
/// would make an approval fingerprint ambiguous, and the failure would appear as a
/// wrong action rather than as a startup error.
/// </para>
/// </remarks>
public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiTool> _byName;
    private readonly TenantGuard _guard;

    public AiToolRegistry(IEnumerable<IAiTool> tools, TenantGuard guard)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _guard = guard;

        Dictionary<string, IAiTool> byName = new(StringComparer.Ordinal);

        foreach (IAiTool tool in tools)
        {
            if (!byName.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Two tools are registered as '{tool.Name}'. A tool name is what an "
                        + "approval binds to, so it has to identify one thing.");
            }
        }

        _byName = byName;
        All = [.. byName.Values.OrderBy(x => x.Name, StringComparer.Ordinal)];
    }

    public IReadOnlyList<IAiTool> All { get; }

    public IAiTool? Find(string name) =>
        _byName.TryGetValue(name, out IAiTool? tool) ? tool : null;

    /// <summary>
    /// What this caller may be offered for this agent.
    /// </summary>
    /// <remarks>
    /// Two filters, both narrowing. The agent's allow-list decides what is relevant
    /// to the task; the caller's permissions decide what they could do themselves.
    /// A tool failing either is absent rather than present-and-refused, because a
    /// refusal tells the model the tool exists and models are good at asking again
    /// (§6, §8).
    /// </remarks>
    public async Task<IReadOnlyList<IAiTool>> AvailableAsync(
        OrganizationId organizationId,
        AgentKind agentKind,
        CancellationToken cancellationToken = default)
    {
        if (!AgentToolAllowList.ByAgent.TryGetValue(agentKind, out IReadOnlySet<string>? allowed))
        {
            return [];
        }

        List<IAiTool> available = [];

        foreach (IAiTool tool in All)
        {
            if (!allowed.Contains(tool.Name))
            {
                continue;
            }

            if (await _guard
                .HasPermissionAsync(tool.RequiredPermission, organizationId, cancellationToken)
                .ConfigureAwait(false))
            {
                available.Add(tool);
            }
        }

        return available;
    }
}
