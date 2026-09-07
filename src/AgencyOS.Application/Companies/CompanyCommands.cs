using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Companies;

/// <param name="Name">Trading name.</param>
/// <param name="Type">Kind of external body.</param>
/// <param name="LegalName">Registered legal name, when it differs.</param>
/// <param name="Website">Public website.</param>
/// <param name="Notes">Unstructured judgment.</param>
public sealed record CompanyDetails(
    string Name,
    CompanyType Type,
    string? LegalName = null,
    string? Website = null,
    string? Notes = null);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="Details">Descriptive fields.</param>
public sealed record CreateCompanyCommand(OrganizationId OrganizationId, CompanyDetails Details);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="CompanyId">Company to update.</param>
/// <param name="Details">Replacement descriptive fields.</param>
public sealed record UpdateCompanyCommand(
    OrganizationId OrganizationId,
    CompanyId CompanyId,
    CompanyDetails Details);

/// <summary>Creates an external company record within a tenant.</summary>
public sealed class CreateCompanyHandler
{
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCompanyHandler(
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<CompanyId> HandleAsync(
        CreateCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.CompaniesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Company company = Company.Create(
            command.OrganizationId,
            command.Details.Name,
            command.Details.Type,
            actor,
            _clock.UtcNow,
            command.Details.LegalName,
            command.Details.Website,
            command.Details.Notes);

        _companies.Add(company);

        _audit.Record(
            AuditAction.CompanyCreated,
            entityType: nameof(Company),
            entityId: company.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CompaniesWrite,
            semanticDelta: new { company.Name, company.LegalName, Type = company.Type.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return company.Id;
    }
}

/// <summary>Replaces the descriptive fields of a company.</summary>
public sealed class UpdateCompanyHandler
{
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCompanyHandler(
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdateCompanyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.CompaniesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Company company =
            await _companies.FindAsync(command.OrganizationId, command.CompanyId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Company), command.CompanyId.ToString());

        company.Update(
            command.Details.Name,
            command.Details.Type,
            _clock.UtcNow,
            command.Details.LegalName,
            command.Details.Website,
            command.Details.Notes);

        _audit.Record(
            AuditAction.CompanyUpdated,
            entityType: nameof(Company),
            entityId: company.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CompaniesWrite,
            semanticDelta: new { company.Name, company.LegalName, Type = company.Type.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
