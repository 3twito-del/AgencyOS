using AgencyOS.Application.Directory;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Turns persisted entities into the application's read models.
/// </summary>
/// <remarks>
/// Shared by the directory projections and the synchronization feed. Both hand
/// the same records to the same clients, so a second copy of these mappers would
/// eventually disagree about something small - a status string, a missing version
/// - and the disagreement would surface as a client cache that differs from what
/// the same client sees online.
/// </remarks>
internal static class PeopleSliceProjection
{
    internal static PersonSummaryModel ToSummary(Person person, Dictionary<Guid, string> companyNames)
    {
        Guid? companyId = person.PrimaryCompanyId?.Value;

        return new PersonSummaryModel(
            person.Id.Value,
            person.DisplayName,
            person.Title,
            person.Email,
            person.Phone,
            person.Status.ToString(),
            companyId,
            companyId is { } id && companyNames.TryGetValue(id, out string? name) ? name : null,
            person.UpdatedAt,
            person.Version);
    }

    internal static CompanySummaryModel ToSummary(Company company) => new(
        company.Id.Value,
        company.Name,
        company.LegalName,
        company.Type.ToString(),
        company.Status.ToString(),
        company.Website,
        company.UpdatedAt,
        company.Version);

    internal static RelationshipModel ToModel(ProfessionalRelationship relationship, PartyNameLookup names) => new(
        relationship.Id.Value,
        names.Reference(relationship.From),
        names.Reference(relationship.To),
        relationship.Type.ToString(),
        relationship.Direction.ToString(),
        relationship.Status.ToString(),
        relationship.Strength,
        relationship.StartedAt,
        relationship.EndedAt,
        relationship.Notes,
        relationship.Version);

    internal static TaskModel ToModel(TaskItem task, PartyNameLookup names) => new(
        task.Id.Value,
        task.Title,
        task.State.ToString(),
        task.Priority.ToString(),
        task.DueAt,
        task.Subject is { } subject ? names.Reference(subject) : null,
        task.SourceInteractionId?.Value,
        task.CreatedAt,
        task.CompletedAt,
        task.Version);

    internal static InteractionModel ToModel(Interaction interaction, PartyNameLookup names) => new(
        interaction.Id.Value,
        interaction.Type.ToString(),
        interaction.OccurredAt,
        interaction.Summary,
        interaction.DetailedNotes,
        [.. interaction.Participants.Select(p => names.Reference(p.Party))]);

    /// <summary>Resolves party identifiers to display names for one request.</summary>
    internal sealed record PartyNameLookup(
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies)
    {
        public string Describe(RelationshipEndpoint endpoint)
        {
            Dictionary<Guid, string> source = endpoint.IsPerson ? People : Companies;

            // A name that cannot be resolved is shown as unknown rather than as an
            // error: a timeline is still useful when one party has been archived.
            return source.TryGetValue(endpoint.Id, out string? name) ? name : "(unknown)";
        }

        public PartyReference Reference(RelationshipEndpoint endpoint) =>
            new(endpoint.Kind.ToString(), endpoint.Id, Describe(endpoint));
    }
}
