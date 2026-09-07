using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Application.People;

/// <summary>Descriptive fields shared by person create and update.</summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name, when the person has one.</param>
/// <param name="DisplayName">Explicit display name; derived when omitted.</param>
/// <param name="MiddleName">Middle name.</param>
/// <param name="PreferredName">What they actually go by.</param>
/// <param name="PrimaryCompanyId">Company they are principally associated with.</param>
/// <param name="Title">Free-text professional role.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Telephone number.</param>
/// <param name="Notes">Unstructured judgment.</param>
public sealed record PersonDetails(
    string FirstName,
    string? LastName = null,
    string? DisplayName = null,
    string? MiddleName = null,
    string? PreferredName = null,
    CompanyId? PrimaryCompanyId = null,
    string? Title = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="Details">Descriptive fields.</param>
public sealed record CreatePersonCommand(OrganizationId OrganizationId, PersonDetails Details);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="PersonId">Person to update.</param>
/// <param name="Details">Replacement descriptive fields.</param>
/// <param name="ExpectedVersion">
/// Version the caller observed. Refused with a concurrency conflict when the
/// record has moved on, so a stale edit - typed offline an hour ago, or in a
/// window somebody else has since saved over - cannot win by arriving last.
/// </param>
public sealed record UpdatePersonCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    PersonDetails Details,
    int ExpectedVersion);

/// <summary>Creates a person within a tenant.</summary>
public sealed class CreatePersonHandler
{
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePersonHandler(
        IPersonRepository people,
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _people = people;
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<PersonId> HandleAsync(
        CreatePersonCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.PeopleWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await RequirePrimaryCompanyAsync(
            command.OrganizationId,
            command.Details.PrimaryCompanyId,
            cancellationToken).ConfigureAwait(false);

        Person person = Person.Create(
            command.OrganizationId,
            command.Details.FirstName,
            command.Details.LastName,
            actor,
            _clock.UtcNow,
            command.Details.DisplayName,
            command.Details.MiddleName,
            command.Details.PreferredName,
            command.Details.PrimaryCompanyId,
            command.Details.Title,
            command.Details.Email,
            command.Details.Phone,
            command.Details.Notes);

        _people.Add(person);

        _audit.Record(
            AuditAction.PersonCreated,
            entityType: nameof(Person),
            entityId: person.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PeopleWrite,
            semanticDelta: new
            {
                person.DisplayName,
                person.FirstName,
                person.LastName,
                person.Title,
                PrimaryCompanyId = person.PrimaryCompanyId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return person.Id;
    }

    private async Task RequirePrimaryCompanyAsync(
        OrganizationId organizationId,
        CompanyId? primaryCompanyId,
        CancellationToken cancellationToken)
    {
        if (primaryCompanyId is not { } companyId)
        {
            return;
        }

        bool exists = await _companies
            .ExistsActiveAsync(organizationId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Company), companyId.ToString());
        }
    }
}

/// <summary>Replaces the descriptive fields of a person.</summary>
/// <remarks>
/// An explicit command rather than a patch: the caller states the whole
/// descriptive record, so the audit delta describes a coherent result rather than
/// a fragment. Status changes are separate commands.
/// </remarks>
public sealed class UpdatePersonHandler
{
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePersonHandler(
        IPersonRepository people,
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _people = people;
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdatePersonCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.PeopleWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Person person =
            await _people.FindAsync(command.OrganizationId, command.PersonId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());

        person.RequireVersion(command.ExpectedVersion);

        if (command.Details.PrimaryCompanyId is { } companyId)
        {
            bool exists = await _companies
                .ExistsActiveAsync(command.OrganizationId, companyId, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Company), companyId.ToString());
            }
        }

        person.Update(
            command.Details.FirstName,
            command.Details.LastName,
            _clock.UtcNow,
            command.Details.DisplayName,
            command.Details.MiddleName,
            command.Details.PreferredName,
            command.Details.PrimaryCompanyId,
            command.Details.Title,
            command.Details.Email,
            command.Details.Phone,
            command.Details.Notes);

        _audit.Record(
            AuditAction.PersonUpdated,
            entityType: nameof(Person),
            entityId: person.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PeopleWrite,
            semanticDelta: new
            {
                person.DisplayName,
                person.FirstName,
                person.LastName,
                person.Title,
                PrimaryCompanyId = person.PrimaryCompanyId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
