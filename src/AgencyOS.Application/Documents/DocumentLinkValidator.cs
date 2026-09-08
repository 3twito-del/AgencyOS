using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

/// <summary>
/// Checks that a link target is a real record in the caller's own tenant.
/// </summary>
/// <remarks>
/// <para>
/// The database enforces this too, through a composite foreign key on
/// <c>(organization_id, target_id)</c> for every target column. This exists so the
/// refusal is a sentence a person can act on rather than a constraint violation,
/// and so the check happens before any bytes are written (ADR-0025).
/// </para>
/// <para>
/// Existence only. Whether the caller may <em>read</em> the target is a separate
/// question that each target's own permissions answer, and linking is not a way to
/// discover records: the identifier had to come from somewhere the caller could
/// already see it.
/// </para>
/// </remarks>
public interface IDocumentLinkValidator
{
    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a short human label for a target, for lists and detail pages.</summary>
    /// <remarks>
    /// Names only. A label never carries a figure, a clause or a classification,
    /// because it is rendered beside documents whose own permissions differ from the
    /// target's.
    /// </remarks>
    Task<IReadOnlyDictionary<(DocumentLinkTarget Target, Guid TargetId), string>> DescribeAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<(DocumentLinkTarget Target, Guid TargetId)> targets,
        CancellationToken cancellationToken = default);
}

/// <summary>Refuses a link whose target does not exist here.</summary>
public sealed class DocumentLinkValidator
{
    private readonly IDocumentLinkValidator _targets;

    public DocumentLinkValidator(IDocumentLinkValidator targets) => _targets = targets;

    public async Task RequireTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _targets
            .ExistsAsync(organizationId, target, targetId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            // Deliberately the same message whether the record is absent or belongs
            // to another tenant. Distinguishing them would answer "does this
            // identifier exist somewhere" for anybody who asked.
            throw new EntityNotFoundException(target.ToString(), targetId.ToString());
        }
    }
}
