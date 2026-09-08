using System.Globalization;
using System.Text;
using AgencyOS.Application.Ai;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Intelligence;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Intelligence;

namespace AgencyOS.Infrastructure.Ai;

/// <summary>
/// Builds what a run may send, and is the only thing that does.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The security boundary between AgencyOS data and a model provider.</strong>
/// Every agent comes through here; none reaches a repository. One path means one
/// place to review, one place the classification matrix is tested, and one place a
/// mistake can be made rather than six (§34).
/// </para>
/// <para>
/// The order of operations is the design. Read through the authorized query
/// service, so nothing appears that the caller could not see on the record's own
/// page; classify what came back; ask the data policy whether it may leave; keep or
/// drop the whole block. There is no redaction step, because a partially redacted
/// block reads exactly like a complete one and a model cannot tell the difference
/// (§6).
/// </para>
/// </remarks>
public sealed class AiContextAssembler : IAiContextAssembler
{
    private readonly IntelligenceQueryService _intelligence;
    private readonly PeopleSliceQueryService _people;
    private readonly ModelDataPolicy _policy;

    public AiContextAssembler(
        IntelligenceQueryService intelligence,
        PeopleSliceQueryService people,
        ModelDataPolicy policy)
    {
        _intelligence = intelligence;
        _people = people;
        _policy = policy;
    }

    public async Task<AiContext> AssembleAsync(
        AiContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nothing is read until the provider is enabled at all. Assembling first
        // and refusing afterwards would put protected material in a variable for
        // no reason, and the cheapest secret is the one never loaded (§43).
        ModelDataVerdict gate = await _policy
            .EvaluateAsync(
                request.OrganizationId,
                request.ProviderKey,
                ModelDataSensitivity.Internal,
                cancellationToken)
            .ConfigureAwait(false);

        if (!gate.IsAllowed)
        {
            return AiContext.Refused(gate.Reason!);
        }

        List<Candidate> candidates = request.SubjectKind switch
        {
            AgentSubjectKind.ResearchCase => await ResearchCaseAsync(
                request, cancellationToken).ConfigureAwait(false),

            AgentSubjectKind.Person or AgentSubjectKind.Company => await RelationshipAsync(
                request, cancellationToken).ConfigureAwait(false),

            _ => [],
        };

        return await FilterAsync(request, candidates, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps the blocks policy permits, counts the rest, and bounds the total.
    /// </summary>
    /// <remarks>
    /// The omitted count is a number and never a description. "Two source-sensitive
    /// signals were withheld" answers the question the classification exists to
    /// refuse, and answering it to the model is the same disclosure as answering it
    /// to a person (§6, §28).
    /// </remarks>
    private async Task<AiContext> FilterAsync(
        AiContextRequest request,
        IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        List<AiContextBlock> kept = [];
        int omitted = 0;
        int characters = 0;
        ModelDataSensitivity highest = ModelDataSensitivity.Internal;

        foreach (Candidate candidate in candidates)
        {
            ModelDataVerdict verdict = await _policy
                .EvaluateAsync(
                    request.OrganizationId,
                    request.ProviderKey,
                    candidate.Sensitivity,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!verdict.IsAllowed)
            {
                omitted++;

                continue;
            }

            if (characters + candidate.Block.Content.Length > request.MaxCharacters)
            {
                // Bounded here rather than at the provider, so truncation is a
                // decision AgencyOS made and can say it made, rather than an error
                // somebody else returns halfway through a run (§33).
                omitted++;

                continue;
            }

            characters += candidate.Block.Content.Length;
            kept.Add(candidate.Block);

            if (candidate.Sensitivity > highest)
            {
                highest = candidate.Sensitivity;
            }
        }

        return new AiContext(kept, omitted, highest, RefusedOutright: false, RefusalReason: null);
    }

    private async Task<List<Candidate>> ResearchCaseAsync(
        AiContextRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SubjectId is not { } id)
        {
            return [];
        }

        ResearchCaseDetailModel? detail = await _intelligence
            .GetResearchCaseAsync(
                request.OrganizationId, new ResearchCaseId(id), cancellationToken)
            .ConfigureAwait(false);

        if (detail is null)
        {
            return [];
        }

        List<Candidate> candidates = [];

        StringBuilder header = new();

        header.Append("Question: ").AppendLine(detail.ResearchCase.Question);
        header.Append("Status: ").AppendLine(detail.ResearchCase.Status.ToString());

        candidates.Add(new Candidate(
            new AiContextBlock(
                "ResearchCase",
                detail.ResearchCase.Question,
                header.ToString(),

                // The structure is AgencyOS's; the question itself was typed by a
                // person and is enveloped with the untrusted material below.
                ContextTrust.AgencyOs,
                ModelDataPolicy.Classify(detail.ResearchCase.Sensitivity),
                new AiCitationReference("ResearchCase", id)),
            ModelDataPolicy.Classify(detail.ResearchCase.Sensitivity)));

        if (detail.Context is { Length: > 0 } context)
        {
            candidates.Add(Untrusted(
                "ResearchCaseContext",
                "Context recorded on the case",
                context,
                ModelDataPolicy.Classify(detail.ResearchCase.Sensitivity),
                new AiCitationReference("ResearchCase", id)));
        }

        foreach (IntelligenceSourceModel source in detail.Sources)
        {
            StringBuilder body = new();

            body.Append("Kind: ").AppendLine(source.Kind.ToString());
            body.Append("Reliability: ").AppendLine(source.Reliability.ToString());
            body.Append("Held by AgencyOS: ").AppendLine(
                source.IsHeldByAgencyOS ? "yes" : "no, this is a reference only");
            body.Append("Observed: ").AppendLine(
                source.ObservedAt.ToString("u", CultureInfo.InvariantCulture));

            candidates.Add(Untrusted(
                "Source",
                source.Title,
                body.ToString(),
                ModelDataPolicy.Classify(source.Sensitivity),
                new AiCitationReference("Source", source.Id.Value)));
        }

        foreach (SignalSummaryModel signal in detail.Signals)
        {
            StringBuilder body = new();

            body.Append("Claim: ").AppendLine(signal.Claim);
            body.Append("Verification: ").AppendLine(signal.Verification.ToString());
            body.Append("Sources behind it: ").AppendLine(
                signal.EvidenceCount.ToString(CultureInfo.InvariantCulture));

            candidates.Add(Untrusted(
                "Signal",
                signal.Title,
                body.ToString(),
                ModelDataPolicy.Classify(signal.Sensitivity),
                new AiCitationReference("Signal", signal.Id.Value)));
        }

        return candidates;
    }

    private async Task<List<Candidate>> RelationshipAsync(
        AiContextRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SubjectId is not { } id)
        {
            return [];
        }

        IntelligenceSubjectKind kind = request.SubjectKind == AgentSubjectKind.Person
            ? IntelligenceSubjectKind.Person
            : IntelligenceSubjectKind.Company;

        RelationshipIntelligenceModel? model = await _intelligence
            .GetRelationshipIntelligenceAsync(
                request.OrganizationId, kind, id, cancellationToken)
            .ConfigureAwait(false);

        if (model is null)
        {
            return [];
        }

        List<Candidate> candidates = [];

        StringBuilder facts = new();

        facts.Append("Name: ").AppendLine(model.DisplayName);

        if (model.Title is { Length: > 0 })
        {
            facts.Append("Title: ").AppendLine(model.Title);
        }

        if (model.CompanyName is { Length: > 0 })
        {
            facts.Append("Company: ").AppendLine(model.CompanyName);
        }

        facts.AppendLine();
        facts.AppendLine("Recorded by a person (a judgment, not a measurement):");
        facts.Append("  Strength: ").AppendLine(model.RecordedStrength ?? "not recorded");
        facts.Append("  Owner: ").AppendLine(
            model.RelationshipOwnerDisplayName ?? "no lead agent recorded");

        facts.AppendLine();
        facts.AppendLine("Counted from recorded activity (facts about records, not about "
            + "the relationship):");
        facts.Append("  Interactions in 30 days: ").AppendLine(
            model.Interactions30Days.ToString(CultureInfo.InvariantCulture));
        facts.Append("  Interactions in 90 days: ").AppendLine(
            model.Interactions90Days.ToString(CultureInfo.InvariantCulture));
        facts.Append("  Interactions in a year: ").AppendLine(
            model.Interactions365Days.ToString(CultureInfo.InvariantCulture));
        facts.Append("  Open tasks: ").AppendLine(
            model.OpenTaskCount.ToString(CultureInfo.InvariantCulture));
        facts.Append("  Overdue tasks: ").AppendLine(
            model.OverdueTaskCount.ToString(CultureInfo.InvariantCulture));

        candidates.Add(new Candidate(
            new AiContextBlock(
                "Relationship",
                model.DisplayName,
                facts.ToString(),
                ContextTrust.AgencyOs,
                ModelDataSensitivity.Internal,
                new AiCitationReference(kind.ToString(), id)),
            ModelDataSensitivity.Internal));

        if (model.RecordedRelationshipNote is { Length: > 0 } note)
        {
            candidates.Add(Untrusted(
                "RelationshipNote",
                "What somebody wrote about this relationship",
                note,
                ModelDataSensitivity.Internal,
                new AiCitationReference(kind.ToString(), id)));
        }

        foreach (SignalSummaryModel signal in model.RecentSignals)
        {
            candidates.Add(Untrusted(
                "Signal",
                signal.Title,
                $"Claim: {signal.Claim}\nVerification: {signal.Verification}",
                ModelDataPolicy.Classify(signal.Sensitivity),
                new AiCitationReference("Signal", signal.Id.Value)));
        }

        return candidates;
    }

    /// <summary>
    /// Wraps something a person wrote.
    /// </summary>
    /// <remarks>
    /// Anything a human or an outside system authored is untrusted, without
    /// exception and regardless of who authored it. A note by a colleague is as
    /// capable of containing an injected instruction as an email from a stranger —
    /// the colleague may have pasted it (§7).
    /// </remarks>
    private static Candidate Untrusted(
        string kind,
        string label,
        string content,
        ModelDataSensitivity sensitivity,
        AiCitationReference reference) =>
        new(
            new AiContextBlock(
                kind, label, content, ContextTrust.Untrusted, sensitivity, reference),
            sensitivity);

    private readonly record struct Candidate(
        AiContextBlock Block,
        ModelDataSensitivity Sensitivity);
}
