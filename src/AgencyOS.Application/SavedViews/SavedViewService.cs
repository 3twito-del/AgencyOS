using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
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
    /// <summary>
    /// The permission each target's records are gated by.
    /// </summary>
    /// <remarks>
    /// Every target must appear. A missing entry is not a permissive default -
    /// the lookup throws when a view of that target is saved - but it is a runtime
    /// failure for something knowable at build time, so
    /// <c>SavedViewVersionTests</c> asserts the table is complete. M7 added a
    /// target and found this table was the one parallel shape with no such test
    /// (ADR-0021).
    /// </remarks>
    public static IReadOnlyDictionary<SavedViewTarget, string> RequiredPermissions { get; } =
        new Dictionary<SavedViewTarget, string>
        {
            [SavedViewTarget.People] = Permission.PeopleRead,
            [SavedViewTarget.Companies] = Permission.CompaniesRead,
            [SavedViewTarget.Tasks] = Permission.TasksRead,

            // The representation targets are gated by the permission that governs
            // the records themselves, re-checked when the view runs rather than
            // when it was saved.
            [SavedViewTarget.Talent] = Permission.TalentRead,
            [SavedViewTarget.Prospects] = Permission.ProspectsRead,

            // Same rule for the slate: the view is gated by read access to the
            // records it lists, re-checked when it runs rather than when it was
            // saved.
            [SavedViewTarget.Projects] = Permission.ProjectsRead,
            [SavedViewTarget.Packages] = Permission.PackagesRead,
            [SavedViewTarget.Opportunities] = Permission.OpportunitiesRead,

            // Reading a negotiation exists is deals.read. What it pays is a
            // separate grant applied when the results are projected, so a saved
            // view cannot widen what its owner may see.
            [SavedViewTarget.Deals] = Permission.DealsRead,

            // Same again for the paper. Reading that a contract exists is
            // contracts.read; what it says needs contracts.terms.read, and what
            // counsel thinks of it needs contracts.privileged.read. Both are
            // applied where the results are projected, so a saved view cannot
            // widen what its owner may see (ADR-0022).
            [SavedViewTarget.Contracts] = Permission.ContractsRead,

            // Finance targets take their own grants, and payments takes the
            // narrower of the two. A saved view is a second route to the same
            // records, so it must not be a way around the permission the direct
            // route enforces (ADR-0023).
            [SavedViewTarget.Receivables] = Permission.FinanceRead,
            [SavedViewTarget.Invoices] = Permission.FinanceRead,
            [SavedViewTarget.Payments] = Permission.FinancePaymentsRead,

            // Documents and communications take their own floors, and the finer
            // grain is applied where the results are projected: a document view
            // returns only classifications the caller may read, and a message view
            // only mailboxes they may open. A saved view is a second route to the
            // same records and must not be a way around either (ADR-0025,
            // ADR-0026).
            [SavedViewTarget.Documents] = Permission.DocumentsRead,
            [SavedViewTarget.Communications] = Permission.CommunicationsRead,
        };

    /// <summary>Largest page a saved view returns.</summary>
    private const int MaximumResults = 200;

    private readonly ISavedViewRepository _views;
    private readonly ISavedViewResultQueries _results;
    private readonly TenantGuard _guard;
    private readonly SensitiveNotes _notes;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SavedViewService(
        ISavedViewRepository views,
        ISavedViewResultQueries results,
        TenantGuard guard,
        SensitiveNotes notes,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _views = views;
        _results = results;
        _guard = guard;
        _notes = notes;
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

    /// <summary>
    /// Runs one of the caller's saved views.
    /// </summary>
    /// <remarks>
    /// The target's read permission is checked here, at execution time, not at the
    /// time the view was saved. A view composed in March must stop working in June
    /// if the grant behind it was revoked; a stored document is not a standing
    /// authorization.
    /// </remarks>
    public async Task<SavedViewResultModel?> RunAsync(
        OrganizationId organizationId,
        SavedViewId id,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        UserId owner = await AuthorizeOwnerAsync(organizationId, cancellationToken).ConfigureAwait(false);

        SavedView? view = await _views
            .FindAsync(organizationId, owner, id, cancellationToken)
            .ConfigureAwait(false);

        if (view is null)
        {
            return null;
        }

        await _guard
            .AuthorizeAsync(RequiredPermissions[view.Target], organizationId, cancellationToken)
            .ConfigureAwait(false);

        SavedViewResultModel results = await _results
            .RunAsync(
                organizationId,
                view.Definition,
                _clock.UtcNow,
                Math.Clamp(limit ?? 50, 1, MaximumResults),
                cancellationToken)
            .ConfigureAwait(false);

        // A saved view is a second route to the same records, and a prospect read
        // through one must not reveal more than a prospect read through the other.
        // The projection returns strategy notes in full; the same redaction the
        // prospects endpoint applies is applied here, from the same place.
        if (results.Prospects.Count > 0)
        {
            results = results with
            {
                Prospects = await _notes
                    .ApplyAsync(organizationId, results.Prospects, cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        return results;
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
