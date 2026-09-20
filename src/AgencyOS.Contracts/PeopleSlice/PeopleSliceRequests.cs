namespace AgencyOS.Contracts.PeopleSlice;

/// <summary>
/// Names one end of a relationship, or a participant, or a task's subject.
/// </summary>
/// <param name="Kind">Either <c>Person</c> or <c>Company</c>.</param>
/// <param name="Id">Identifier of that party.</param>
public sealed record PartyRefRequest(string Kind, Guid Id);

/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name, when the person has one.</param>
/// <param name="DisplayName">Explicit display name; derived from the other names when omitted.</param>
/// <param name="MiddleName">Middle name.</param>
/// <param name="PreferredName">What they actually go by.</param>
/// <param name="PrimaryCompanyId">Company they are principally associated with.</param>
/// <param name="Title">Free-text professional role, for example "Literary Agent".</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Telephone number.</param>
/// <param name="Notes">Unstructured judgment, kept apart from structured fact.</param>
public sealed record CreatePersonRequest(
    string FirstName,
    string? LastName = null,
    string? DisplayName = null,
    string? MiddleName = null,
    string? PreferredName = null,
    Guid? PrimaryCompanyId = null,
    string? Title = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

/// <summary>
/// Replacement descriptive fields for a person.
/// </summary>
/// <remarks>
/// A whole-record command rather than a patch: the caller states the intended
/// result, so the audit delta describes something coherent. Status changes are
/// separate operations.
/// </remarks>
/// <param name="FirstName">Given name.</param>
/// <param name="ExpectedVersion">
/// The version the caller observed. Required: an optional concurrency token is
/// last-write-wins with extra steps. A mismatch is refused with 409.
/// </param>
/// <param name="LastName">Family name.</param>
/// <param name="DisplayName">Explicit display name.</param>
/// <param name="MiddleName">Middle name.</param>
/// <param name="PreferredName">What they go by.</param>
/// <param name="PrimaryCompanyId">Principal company.</param>
/// <param name="Title">Free-text professional role.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Telephone number.</param>
/// <param name="Notes">Unstructured judgment.</param>
public sealed record UpdatePersonRequest(
    string FirstName,
    int ExpectedVersion,
    string? LastName = null,
    string? DisplayName = null,
    string? MiddleName = null,
    string? PreferredName = null,
    Guid? PrimaryCompanyId = null,
    string? Title = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

/// <param name="Name">Trading name.</param>
/// <param name="Type">Studio, Network, Streamer, ProductionCompany, ManagementCompany, TalentAgency, LawFirm, Publisher, Brand or Other.</param>
/// <param name="LegalName">Registered legal name, when it differs.</param>
/// <param name="Website">Public website.</param>
/// <param name="Notes">Unstructured judgment.</param>
public sealed record CreateCompanyRequest(
    string Name,
    string Type,
    string? LegalName = null,
    string? Website = null,
    string? Notes = null);

/// <param name="Name">Trading name.</param>
/// <param name="Type">Kind of external body.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
/// <param name="LegalName">Registered legal name.</param>
/// <param name="Website">Public website.</param>
/// <param name="Notes">Unstructured judgment.</param>
public sealed record UpdateCompanyRequest(
    string Name,
    string Type,
    int ExpectedVersion,
    string? LegalName = null,
    string? Website = null,
    string? Notes = null);

/// <param name="From">Party the relationship runs from.</param>
/// <param name="To">Party the relationship runs to.</param>
/// <param name="Type">Employment, Affiliation, Colleague, Introduction, Collaboration, Advisor, Counsel or Other.</param>
/// <param name="Direction">Directed or Mutual; defaults to the type's natural direction.</param>
/// <param name="Strength">Subjective strength, 1 to 5.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record CreateRelationshipRequest(
    PartyRefRequest From,
    PartyRefRequest To,
    string Type,
    string? Direction = null,
    int? Strength = null,
    DateTimeOffset? StartedAt = null,
    string? Notes = null);

/// <param name="EndedAt">When it ended; defaults to now.</param>
public sealed record EndRelationshipRequest(DateTimeOffset? EndedAt = null);

/// <param name="Party">Who took part.</param>
/// <param name="Role">Optional note about how they took part.</param>
public sealed record InteractionParticipantRequest(PartyRefRequest Party, string? Role = null);

/// <param name="Title">What needs doing.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Priority">Low, Normal, High or Urgent.</param>
/// <param name="Subject">Party the task concerns; defaults to the first participant.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record FollowUpTaskRequest(
    string Title,
    DateTimeOffset? DueAt = null,
    string? Priority = null,
    PartyRefRequest? Subject = null,
    string? Notes = null);

/// <summary>
/// Records a contact and, optionally, the next action arising from it.
/// </summary>
/// <remarks>
/// The interaction and its follow-up task are created in one transaction. Either
/// both exist or neither does, because an interaction recorded without the
/// follow-up the user asked for is worse than an outright failure: it looks like
/// the commitment was captured.
/// </remarks>
/// <param name="Type">Meeting, Call, Email, Message, Event, Note or Other.</param>
/// <param name="OccurredAt">When it happened, which is not when it was recorded.</param>
/// <param name="Summary">One-line factual summary.</param>
/// <param name="Participants">Everyone involved. At least one.</param>
/// <param name="DetailedNotes">Longer, subjective account.</param>
/// <param name="FollowUp">Optional next action.</param>
public sealed record RecordInteractionRequest(
    string Type,
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyList<InteractionParticipantRequest> Participants,
    string? DetailedNotes = null,
    FollowUpTaskRequest? FollowUp = null);

/// <summary>Transitions a task, guarded by the version the caller observed.</summary>
/// <param name="ExpectedVersion">
/// The version the caller observed. Required, so a task completed offline cannot
/// silently overwrite a change somebody else made in the meantime.
/// </param>
public sealed record TaskTransitionRequest(int ExpectedVersion);

/// <param name="Title">What needs doing.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Priority">Low, Normal, High or Urgent.</param>
/// <param name="Subject">Party the task concerns.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="AssigneeUserId">
/// The member who should do it. Omitted means the caller, which is what this
/// endpoint has always done.
/// </param>
public sealed record CreateTaskRequest(
    string Title,
    DateTimeOffset? DueAt = null,
    string? Priority = null,
    PartyRefRequest? Subject = null,
    string? Notes = null,
    Guid? AssigneeUserId = null);

/// <summary>
/// Makes a member accountable for a task, or clears the assignment.
/// </summary>
/// <param name="AssigneeUserId">
/// The member to make accountable. Null clears it, which says plainly that nobody
/// currently owns the work.
/// </param>
/// <param name="ExpectedVersion">The version the caller observed.</param>
public sealed record AssignTaskRequest(int ExpectedVersion, Guid? AssigneeUserId = null);
