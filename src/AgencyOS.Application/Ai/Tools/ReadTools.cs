using System.Globalization;
using System.Text;
using System.Text.Json;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Intelligence;
using AgencyOS.Application.Search;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai.Tools;

/// <summary>
/// Reads one person.
/// </summary>
/// <remarks>
/// Goes through the same query service the Windows client uses, so a tool cannot
/// see a field the person's own detail page redacts. That is the general rule for
/// every read tool here: they are facades over canonical queries, never second
/// paths to the data (§10).
/// </remarks>
public sealed class PersonGetTool : AiToolBase
{
    private readonly PeopleSliceQueryService _people;

    public PersonGetTool(TenantGuard guard, PeopleSliceQueryService people)
        : base(guard) => _people = people;

    public override string Name => "person.get";

    public override int Version => 1;

    public override string Description =>
        "Reads one person: name, title, company, and the professional relationships "
            + "recorded about them. Does not return notes.";

    public override string JsonSchema => IdSchema("personId", "The person to read.");

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.PeopleRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "personId", out Guid id)
            ? $"Read person {id}"
            : "Read a person";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "personId", out Guid personId))
        {
            return ToolResult.Refused("personId must be a UUID.");
        }

        PersonDetailModel? person = await _people
            .GetPersonAsync(context.OrganizationId, new Domain.People.PersonId(personId),
                cancellationToken)
            .ConfigureAwait(false);

        if (person is null)
        {
            return NotAvailable();
        }

        StringBuilder builder = new();

        Field(builder, "Name", person.Summary.DisplayName);
        Field(builder, "Title", person.Summary.Title);
        Field(builder, "Company", person.Summary.PrimaryCompanyName);
        Field(builder, "Status", person.Summary.Status);

        // Notes are deliberately absent. They are free text somebody typed about a
        // person, which is both the least structured and most personal thing on the
        // record, and a brief does not need them to be useful.
        if (person.Relationships.Count > 0)
        {
            builder.AppendLine("Relationships:");

            foreach (RelationshipModel relationship in person.Relationships.Take(MaximumRows))
            {
                // Both ends, because which one is "the other party" depends on
                // which side of the relationship this person is, and a tool that
                // guessed would occasionally describe somebody's relationship to
                // themselves.
                builder.Append("  - ").Append(relationship.Type).Append(": ")
                    .Append(relationship.From.Name).Append(" -> ")
                    .Append(relationship.To.Name).Append(" [")
                    .Append(relationship.Status).AppendLine("]");
            }
        }

        return Bounded(
            builder.ToString(),
            [new AiCitationReference("Person", personId)]);
    }
}

/// <summary>Reads one company.</summary>
public sealed class CompanyGetTool : AiToolBase
{
    private readonly PeopleSliceQueryService _people;

    public CompanyGetTool(TenantGuard guard, PeopleSliceQueryService people)
        : base(guard) => _people = people;

    public override string Name => "company.get";

    public override int Version => 1;

    public override string Description =>
        "Reads one company: name, type, status and website.";

    public override string JsonSchema => IdSchema("companyId", "The company to read.");

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.CompaniesRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "companyId", out Guid id)
            ? $"Read company {id}"
            : "Read a company";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "companyId", out Guid companyId))
        {
            return ToolResult.Refused("companyId must be a UUID.");
        }

        CompanyDetailModel? company = await _people
            .GetCompanyAsync(context.OrganizationId, new Domain.Companies.CompanyId(companyId),
                cancellationToken)
            .ConfigureAwait(false);

        if (company is null)
        {
            return NotAvailable();
        }

        StringBuilder builder = new();

        Field(builder, "Name", company.Summary.Name);
        Field(builder, "Legal name", company.Summary.LegalName);
        Field(builder, "Type", company.Summary.Type);
        Field(builder, "Status", company.Summary.Status);
        Field(builder, "Website", company.Summary.Website);

        return Bounded(
            builder.ToString(),
            [new AiCitationReference("Company", companyId)]);
    }
}

/// <summary>
/// Reads what is factually known about a working relationship.
/// </summary>
/// <remarks>
/// Returns M11's dimensions and, deliberately, no composite score — because there
/// is none to return. A model asked to summarize a relationship gets counts and a
/// recorded assessment kept apart, which is the distinction M11 exists to preserve
/// and which a generated paragraph could easily blur (§26, ADR-0030).
/// </remarks>
public sealed class RelationshipIntelligenceTool : AiToolBase
{
    private readonly IntelligenceQueryService _intelligence;

    public RelationshipIntelligenceTool(TenantGuard guard, IntelligenceQueryService intelligence)
        : base(guard) => _intelligence = intelligence;

    public override string Name => "relationship.intelligence";

    public override int Version => 1;

    public override string Description =>
        "Reads what is factually known about a working relationship: the recorded "
            + "assessment somebody wrote down, kept separate from interaction counts "
            + "over 30, 90 and 365 days, open tasks, and recent signals. There is no "
            + "relationship score and you must not invent one.";

    public override string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "enum": ["Person", "Company"] },
            "subjectId": { "type": "string", "format": "uuid" }
          },
          "required": ["kind", "subjectId"],
          "additionalProperties": false
        }
        """;

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.IntelligenceRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "subjectId", out Guid id)
            ? $"Read relationship intelligence for {OptionalString(arguments, "kind")} {id}"
            : "Read relationship intelligence";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "subjectId", out Guid subjectId))
        {
            return ToolResult.Refused("subjectId must be a UUID.");
        }

        if (!Enum.TryParse(OptionalString(arguments, "kind"), out IntelligenceSubjectKind kind)
            || kind is not (IntelligenceSubjectKind.Person or IntelligenceSubjectKind.Company))
        {
            return ToolResult.Refused("kind must be Person or Company.");
        }

        RelationshipIntelligenceModel? model = await _intelligence
            .GetRelationshipIntelligenceAsync(
                context.OrganizationId, kind, subjectId, cancellationToken)
            .ConfigureAwait(false);

        if (model is null)
        {
            return NotAvailable();
        }

        StringBuilder builder = new();

        Field(builder, "Name", model.DisplayName);
        Field(builder, "Title", model.Title);
        Field(builder, "Company", model.CompanyName);

        builder.AppendLine();
        builder.AppendLine("What a person wrote down:");
        Field(builder, "  Recorded strength", model.RecordedStrength ?? "not recorded");
        Field(builder, "  Note", model.RecordedRelationshipNote);
        Field(builder, "  Relationship owner", model.RelationshipOwnerDisplayName);

        builder.AppendLine();
        builder.AppendLine("What the records count:");
        Field(builder, "  Interactions, 30 days", model.Interactions30Days);
        Field(builder, "  Interactions, 90 days", model.Interactions90Days);
        Field(builder, "  Interactions, 365 days", model.Interactions365Days);
        Field(builder, "  Days since last contact", model.DaysSinceLastInteraction);
        Field(builder, "  Last interaction kind", model.LastInteractionKind);
        Field(builder, "  Open tasks", model.OpenTaskCount);
        Field(builder, "  Overdue tasks", model.OverdueTaskCount);

        builder.AppendLine();
        builder.AppendLine(
            "These counts are facts about recorded activity. They are not a measure "
                + "of how strong the relationship is, and you must not present them "
                + "as one.");

        List<AiCitationReference> references =
            [new AiCitationReference(kind.ToString(), subjectId)];

        if (model.RecentSignals.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent signals:");

            foreach (SignalSummaryModel signal in model.RecentSignals.Take(MaximumRows))
            {
                builder.Append("  - ").Append(signal.Title).Append(" [")
                    .Append(signal.Verification).AppendLine("]");

                references.Add(new AiCitationReference("Signal", signal.Id.Value));
            }
        }

        return Bounded(builder.ToString(), references);
    }
}

/// <summary>
/// Searches signals.
/// </summary>
/// <remarks>
/// Narrowed by the caller's own classifications inside the query, as every M11 read
/// is. A model asking about a person gets the claims that person's reader may see,
/// and no indication that there were others (§6, ADR-0030).
/// </remarks>
public sealed class SignalsSearchTool : AiToolBase
{
    private readonly IntelligenceQueryService _intelligence;

    public SignalsSearchTool(TenantGuard guard, IntelligenceQueryService intelligence)
        : base(guard) => _intelligence = intelligence;

    public override string Name => "signals.search";

    public override int Version => 1;

    public override string Description =>
        "Finds recorded signals. A signal is a claim somebody wrote down with at "
            + "least one source; it is not established fact, and its verification "
            + "state says only what other evidence shows.";

    public override string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "description": "Words to match in the title or claim." },
            "subjectKind": { "type": "string" },
            "subjectId": { "type": "string", "format": "uuid" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 25 }
          },
          "additionalProperties": false
        }
        """;

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.IntelligenceRead;

    public override string Describe(JsonElement arguments) =>
        $"Search signals for \"{OptionalString(arguments, "text") ?? "anything"}\"";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        Enum.TryParse(OptionalString(arguments, "subjectKind"), out IntelligenceSubjectKind kind);
        TryGuid(arguments, "subjectId", out Guid subjectId);

        SignalFilter filter = new(
            SubjectKind: kind == default ? null : kind,
            SubjectId: subjectId == Guid.Empty ? null : subjectId,
            TextContains: OptionalString(arguments, "text"));

        IReadOnlyList<SignalSummaryModel> signals = await _intelligence
            .ListSignalsAsync(
                context.OrganizationId,
                filter,
                Math.Min(OptionalInt(arguments, "limit") ?? MaximumRows, MaximumRows),
                cancellationToken)
            .ConfigureAwait(false);

        if (signals.Count == 0)
        {
            return ToolResult.Ok("No signals matched.");
        }

        StringBuilder builder = new();
        List<AiCitationReference> references = [];

        foreach (SignalSummaryModel signal in signals)
        {
            builder.Append("- ").AppendLine(signal.Title);
            builder.Append("  claim: ").AppendLine(signal.Claim);
            builder.Append("  verification: ").AppendLine(signal.Verification.ToString());
            builder.Append("  observed: ")
                .AppendLine(signal.ObservedAt.ToString("u", CultureInfo.InvariantCulture));
            builder.Append("  sources: ")
                .AppendLine(signal.EvidenceCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  reference: Signal ").AppendLine(signal.Id.Value.ToString());

            references.Add(new AiCitationReference("Signal", signal.Id.Value));
        }

        return Bounded(builder.ToString(), references);
    }
}

/// <summary>
/// Searches across the tenant, through the canonical search service.
/// </summary>
/// <remarks>
/// Inherits M11's decision that global search returns Internal claims only. A model
/// cannot reach an elevated signal through search that it could not reach through
/// the signals tool, which is the point of routing both through the same services
/// rather than writing a bespoke query for the agent (§10, §61).
/// </remarks>
public sealed class AgencySearchTool : AiToolBase
{
    private readonly SearchService _search;

    public AgencySearchTool(TenantGuard guard, SearchService search)
        : base(guard) => _search = search;

    public override string Name => "agency.search";

    public override int Version => 1;

    public override string Description =>
        "Searches people, companies, projects, deals, contracts and signals by name "
            + "or title. Returns identifiers you can then read with a specific tool.";

    public override string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 25 }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.PeopleRead;

    public override string Describe(JsonElement arguments) =>
        $"Search for \"{OptionalString(arguments, "query")}\"";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        string? query = OptionalString(arguments, "query");

        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Refused("query must be a non-empty string.");
        }

        SearchResultModel results = await _search
            .SearchAsync(
                context.OrganizationId,
                query,
                requestedTypes: null,
                includeArchived: false,
                skip: 0,
                take: Math.Min(OptionalInt(arguments, "limit") ?? MaximumRows, MaximumRows),
                cancellationToken)
            .ConfigureAwait(false);

        if (results.Hits.Count == 0)
        {
            return ToolResult.Ok("Nothing matched.");
        }

        StringBuilder builder = new();
        List<AiCitationReference> references = [];

        foreach (SearchHitModel hit in results.Hits)
        {
            builder.Append("- ").Append(hit.Type).Append(' ').Append(hit.Id).Append(": ")
                .AppendLine(hit.Title);

            references.Add(new AiCitationReference(hit.Type.ToString(), hit.Id));
        }

        return Bounded(builder.ToString(), references);
    }
}
