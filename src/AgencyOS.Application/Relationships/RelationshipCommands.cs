using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Relationships;

namespace AgencyOS.Application.Relationships;

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="From">Party the relationship runs from.</param>
/// <param name="To">Party the relationship runs to.</param>
/// <param name="Type">Kind of relationship.</param>
/// <param name="Direction">How it reads; defaults to the type's natural direction.</param>
/// <param name="Strength">Subjective strength, 1 to 5.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record CreateRelationshipCommand(
    OrganizationId OrganizationId,
    RelationshipEndpoint From,
    RelationshipEndpoint To,
    RelationshipType Type,
    RelationshipDirection? Direction = null,
    int? Strength = null,
    DateTimeOffset? StartedAt = null,
    string? Notes = null);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="RelationshipId">Relationship to end.</param>
/// <param name="EndedAt">When it ended; defaults to now.</param>
public sealed record EndRelationshipCommand(
    OrganizationId OrganizationId,
    RelationshipId RelationshipId,
    DateTimeOffset? EndedAt = null);

/// <summary>Connects two parties.</summary>
/// <remarks>
/// Both endpoints are verified to exist, to be active, and to belong to this
/// tenant before the relationship is built. The database would refuse a
/// cross-tenant endpoint through its composite foreign keys, but a constraint
/// violation is a poor way to tell a user they picked the wrong person.
/// </remarks>
public sealed class CreateRelationshipHandler
{
    private readonly IRelationshipRepository _relationships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRelationshipHandler(
        IRelationshipRepository relationships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _relationships = relationships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RelationshipId> HandleAsync(
        CreateRelationshipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RelationshipsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard.RequirePartyAsync(command.OrganizationId, command.From, cancellationToken)
            .ConfigureAwait(false);
        await _guard.RequirePartyAsync(command.OrganizationId, command.To, cancellationToken)
            .ConfigureAwait(false);

        ProfessionalRelationship relationship = ProfessionalRelationship.Create(
            command.OrganizationId,
            command.From,
            command.To,
            command.Type,
            actor,
            _clock.UtcNow,
            command.Direction,
            command.Strength,
            command.StartedAt,
            endedAt: null,
            command.Notes);

        _relationships.Add(relationship);

        _audit.Record(
            AuditAction.RelationshipCreated,
            entityType: nameof(ProfessionalRelationship),
            entityId: relationship.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RelationshipsWrite,
            semanticDelta: new
            {
                From = relationship.From.ToString(),
                To = relationship.To.ToString(),
                Type = relationship.Type.ToString(),
                Direction = relationship.Direction.ToString(),
                relationship.Strength,
                relationship.StartedAt,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return relationship.Id;
    }
}

/// <summary>Ends a relationship without deleting it.</summary>
public sealed class EndRelationshipHandler
{
    private readonly IRelationshipRepository _relationships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public EndRelationshipHandler(
        IRelationshipRepository relationships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _relationships = relationships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(EndRelationshipCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RelationshipsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        ProfessionalRelationship relationship =
            await _relationships.FindAsync(command.OrganizationId, command.RelationshipId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(ProfessionalRelationship),
                command.RelationshipId.ToString());

        DateTimeOffset now = _clock.UtcNow;
        relationship.End(actor, command.EndedAt ?? now, now);

        _audit.Record(
            AuditAction.RelationshipEnded,
            entityType: nameof(ProfessionalRelationship),
            entityId: relationship.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RelationshipsWrite,
            semanticDelta: new
            {
                From = relationship.From.ToString(),
                To = relationship.To.ToString(),
                Type = relationship.Type.ToString(),
                relationship.EndedAt,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
