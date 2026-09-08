using System.Globalization;
using System.Text;
using System.Text.Json;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai.Tools;

/// <summary>
/// What every tool does the same way.
/// </summary>
/// <remarks>
/// <para>
/// Authorization, argument reading and result bounding are here rather than in
/// each tool, because they are the parts that must not vary. A tool that forgot to
/// re-check a permission would be a confused deputy, and the failure would look
/// exactly like the tool working (§9).
/// </para>
/// <para>
/// The re-check is not redundant with the run's own authorization. A run
/// establishes who the user is when it starts and may sit awaiting approval for
/// hours; a permission can be revoked in between, and the tool that runs
/// afterwards must find out (§47).
/// </para>
/// </remarks>
public abstract class AiToolBase : IAiTool
{
    /// <summary>The most characters any tool returns.</summary>
    /// <remarks>
    /// A ceiling rather than a suggestion. An unbounded tool result is how a run
    /// ends up sending an entire mailbox to a provider in one turn, and the model
    /// is told when it happened rather than silently given a prefix (§33).
    /// </remarks>
    protected const int MaximumResultCharacters = 12_000;

    /// <summary>The most rows any list tool returns.</summary>
    protected const int MaximumRows = 25;

    private readonly TenantGuard _guard;

    protected AiToolBase(TenantGuard guard) => _guard = guard;

    public abstract string Name { get; }

    public abstract int Version { get; }

    public abstract string Description { get; }

    public abstract string JsonSchema { get; }

    public abstract ToolEffect Effect { get; }

    public abstract string RequiredPermission { get; }

    public abstract string Describe(JsonElement arguments);

    /// <summary>
    /// Re-checks authorization, then runs the tool.
    /// </summary>
    /// <remarks>
    /// Sealed. A tool that overrode this could skip the check, and the whole point
    /// of the base class is that it cannot be skipped by forgetting.
    /// </remarks>
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await _guard
            .HasPermissionAsync(RequiredPermission, context.OrganizationId, cancellationToken)
            .ConfigureAwait(false))
        {
            // Says which permission, because the person reading the run needs to
            // know what to ask for. Says nothing about whether the record exists.
            return ToolResult.Refused(
                $"That needs {RequiredPermission}, which you do not hold.");
        }

        return await RunAsync(context, arguments, cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);

    /// <summary>Reads a required identifier, or refuses.</summary>
    /// <remarks>
    /// A model that emits a malformed identifier gets a refusal rather than a
    /// best-effort parse. Guessing what it meant is how a tool ends up operating on
    /// the wrong record (§38).
    /// </remarks>
    protected static bool TryGuid(JsonElement arguments, string name, out Guid value)
    {
        value = Guid.Empty;

        return arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value);
    }

    protected static string? OptionalString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    protected static int? OptionalInt(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out int value)
                ? value
                : null;

    /// <summary>Trims a result to the ceiling and says whether it had to.</summary>
    protected static ToolResult Bounded(
        string content,
        IReadOnlyList<AiCitationReference>? references = null)
    {
        if (content.Length <= MaximumResultCharacters)
        {
            return ToolResult.Ok(content, truncated: false, references);
        }

        return ToolResult.Ok(
            content[..MaximumResultCharacters],
            truncated: true,
            references);
    }

    /// <summary>Renders a labelled field, skipping the ones with nothing in them.</summary>
    protected static void Field(StringBuilder builder, string label, object? value)
    {
        if (value is null or "")
        {
            return;
        }

        builder.Append(label).Append(": ").AppendLine(
            value switch
            {
                DateTimeOffset moment => moment.ToString("u", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable =>
                    formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            });
    }

    /// <summary>A schema taking one required identifier.</summary>
    protected static string IdSchema(string name, string description) =>
        $$"""
        {
          "type": "object",
          "properties": {
            "{{name}}": { "type": "string", "format": "uuid", "description": "{{description}}" }
          },
          "required": ["{{name}}"],
          "additionalProperties": false
        }
        """;

    /// <summary>The refusal used when an identifier names nothing the caller may read.</summary>
    /// <remarks>
    /// One sentence for both cases, deliberately. "No such record" and "you may not
    /// read it" are different facts, and telling them apart is how a model — or
    /// somebody driving one — enumerates what exists (§6).
    /// </remarks>
    protected static ToolResult NotAvailable() =>
        ToolResult.Refused("No such record is available to you.");
}
