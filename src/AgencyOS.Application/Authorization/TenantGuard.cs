using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;

namespace AgencyOS.Application.Authorization;

/// <summary>
/// The single place a command establishes who is acting and whether they may.
/// </summary>
/// <remarks>
/// <para>
/// Every M2 command begins by calling <see cref="AuthorizeAsync"/>. Centralizing it
/// means the check cannot be subtly different in one handler, and it makes an
/// unauthorized handler visible in review as a missing first line rather than as a
/// subtly wrong condition buried in the middle.
/// </para>
/// <para>
/// The check is organization-scoped, which is what makes it tenant-safe: holding
/// <c>people.write</c> in one tenant confers nothing in another.
/// </para>
/// </remarks>
public sealed class TenantGuard
{
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;

    public TenantGuard(
        IPermissionEvaluator permissions,
        IExecutionContext execution,
        IPersonRepository people,
        ICompanyRepository companies)
    {
        _permissions = permissions;
        _execution = execution;
        _people = people;
        _companies = companies;
    }

    /// <summary>
    /// Establishes the acting user and confirms they hold the permission within the tenant.
    /// </summary>
    /// <returns>The acting user.</returns>
    public async Task<UserId> AuthorizeAsync(
        string permission,
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(actor, permission, organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            throw new PermissionDeniedException(permission);
        }

        return actor;
    }

    /// <summary>
    /// Confirms an endpoint names a party that exists, is active, and belongs to
    /// this tenant.
    /// </summary>
    /// <remarks>
    /// A relationship or participant pointing at a missing party is meaningless, and
    /// one pointing across tenants is a leak. The database refuses the second
    /// through composite foreign keys; this produces a comprehensible error instead
    /// of a constraint violation, and catches the first as well.
    /// </remarks>
    public async Task RequirePartyAsync(
        OrganizationId organizationId,
        RelationshipEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        if (endpoint.AsPerson is { } personId)
        {
            bool exists = await _people
                .ExistsActiveAsync(organizationId, personId, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Person), personId.ToString());
            }

            return;
        }

        CompanyId companyId = endpoint.AsCompany!.Value;

        bool companyExists = await _companies
            .ExistsActiveAsync(organizationId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!companyExists)
        {
            throw new EntityNotFoundException(nameof(Company), companyId.ToString());
        }
    }
}
