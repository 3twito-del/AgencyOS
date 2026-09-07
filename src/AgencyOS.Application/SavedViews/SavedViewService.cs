using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.SavedViews;

namespace AgencyOS.Application.SavedViews;

/// <summary>
/// Storage for saved views, always scoped to a tenant and an owner.
/// </summary>
/// <remarks>
/// Both keys are required on every lookup, for the same reason the M2
/// repositories require a tenant: a method that can be called without an owner is
/// somebody else's saved view waiting to be read.
/// </remarks>
public interface ISavedViewRepository
{
    Task<SavedView?> FindAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        SavedViewId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedView>> ListAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Determines whether the owner already has a view of this name.</summary>
    Task<bool> NameExistsAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        string name,
        SavedViewId? excluding,
        CancellationToken cancellationToken = default);

    void Add(SavedView view);

    void Remove(SavedView view);
}

/// <summary>Raised when a saved view would duplicate a name the owner already uses.</summary>
public sealed class SavedViewNameInUseException : Exception
{
    public SavedViewNameInUseException(string name)
        : base($"You already have a saved view called '{name}'.") => Name = name;

    public string Name { get; }
}

/// <summary>
/// Creates, reads, renames and deletes a user's own saved views.
/// </summary>
/// <remarks>
/// <para>
/// A saved view is a personal bookmark over data the user can already read, so
/// the permission it requires is read access to the target - not a separate
/// grant. Creating a view over people requires <c>people.read</c>; running it
/// re-checks the same permission at execution time, because a view saved in March
/// must not still work in June after the grant was revoked.
/// </para>
/// <para>
/// Deleting is a real delete rather than an archive. A saved view is not a
/// business fact: nothing downstream refers to it, and no audit question is
/// answered by keeping a bookmark somebody removed. The deletion is still
/// audited, because it is a privileged operation on stored user data.
/// </para>
/// </remarks>
public sealed class SavedViewService
{
    private static readonly IReadOnlyDictionary<SavedViewTarget, string> RequiredPermissions =
        new Dictionary<SavedViewTarget, string>
        {
            [SavedViewTarget.People] = Permission.PeopleRead,
            [SavedViewTarget.Companies] = Permission.CompaniesRead,
            [SavedViewTarget.Tasks] = Permission.TasksRead,
        };

    private readonly ISavedViewRepository _views;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SavedViewService(
        ISavedViewRepository views,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _views = views;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Lists the caller's own saved views.</summary>
    public async Task<IReadOnlyList<SavedView>> ListAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        UserId owner = await AuthorizeOwnerAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _views.ListAsync(organizationId, owner, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one of the caller's own saved views.</summary>
    public async Task<SavedView?> GetAsync(
        OrganizationId organizationId,
        SavedViewId id,
        CancellationToken cancellationToken = default)
    {
        UserId owner = await AuthorizeOwnerAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _views.FindAsync(organizationId, owner, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SavedViewId> CreateAsync(
        OrganizationId organizationId,
        string name,
        SavedViewDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.Validate();

        UserId owner = await AuthorizeTargetAsync(organizationId, definition.Target, cancellationToken)
            .ConfigureAwait(false);

        await RequireNameFreeAsync(organizationId, owner, name, excluding: null, cancellationToken)
            .ConfigureAwait(false);

        SavedView view = SavedView.Create(organizationId, owner, name, definition, _clock.UtcNow);

        _views.Add(view);

        _audit.Record(
            AuditAction.SavedViewCreated,
            entityType: nameof(SavedView),
            entityId: view.Id.ToString(),
            organizationId: organizationId,
            permission: RequiredPermissions[definition.Target],
            semanticDelta: new { view.Name, Target = view.Target.ToString(), view.DefinitionVersion });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return view.Id;
    }

    public async Task UpdateAsync(
        OrganizationId organizationId,
        SavedViewId id,
        string name,
        SavedViewDefinition definition,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.Validate();

        UserId owner = await AuthorizeTargetAsync(organizationId, definition.Target, cancellationToken)
            .ConfigureAwait(false);

        SavedView view =
            await _views.FindAsync(organizationId, owner, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(SavedView), id.ToString());

        view.RequireVersion(expectedVersion);

        await RequireNameFreeAsync(organizationId, owner, name, excluding: id, cancellationToken)
            .ConfigureAwait(false);

        view.Update(name, definition, _clock.UtcNow);

        _audit.Record(
            AuditAction.SavedViewUpdated,
            entityType: nameof(SavedView),
            entityId: view.Id.ToString(),
            organizationId: organizationId,
            permission: RequiredPermissions[definition.Target],
            semanticDelta: new { view.Name, Target = view.Target.ToString(), view.DefinitionVersion });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        OrganizationId organizationId,
        SavedViewId id,
        CancellationToken cancellationToken = default)
    {
        UserId owner = await AuthorizeOwnerAsync(organizationId, cancellationToken).ConfigureAwait(false);

        SavedView view =
            await _views.FindAsync(organizationId, owner, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(SavedView), id.ToString());

        _views.Remove(view);

        _audit.Record(
            AuditAction.SavedViewDeleted,
            entityType: nameof(SavedView),
            entityId: view.Id.ToString(),
            organizationId: organizationId,
            permission: RequiredPermissions[view.Target],
            semanticDelta: new { view.Name, Target = view.Target.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Establishes the caller as an owner of saved views in this tenant.
    /// </summary>
    /// <remarks>
    /// Membership of the tenant is what confers the right to keep bookmarks in it.
    /// <c>organizations.read</c> is the permission every role holds, so this is the
    /// check that says "you belong here" without implying access to any record.
    /// </remarks>
    private async Task<UserId> AuthorizeOwnerAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        await _guard.AuthorizeAsync(Permission.OrganizationsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

    private async Task<UserId> AuthorizeTargetAsync(
        OrganizationId organizationId,
        SavedViewTarget target,
        CancellationToken cancellationToken)
    {
        UserId owner = await AuthorizeOwnerAsync(organizationId, cancellationToken).ConfigureAwait(false);

        await _guard
            .AuthorizeAsync(RequiredPermissions[target], organizationId, cancellationToken)
            .ConfigureAwait(false);

        return owner;
    }

    private async Task RequireNameFreeAsync(
        OrganizationId organizationId,
        UserId owner,
        string name,
        SavedViewId? excluding,
        CancellationToken cancellationToken)
    {
        string trimmed = (name ?? string.Empty).Trim();

        bool taken = await _views
            .NameExistsAsync(organizationId, owner, trimmed, excluding, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            throw new SavedViewNameInUseException(trimmed);
        }
    }
}
