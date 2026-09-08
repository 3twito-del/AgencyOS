using AgencyOS.Application.Documents;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for documents.
/// </summary>
/// <remarks>
/// Every list narrows by the caller's readable classifications <strong>inside the
/// query</strong>. Filtering afterwards would produce correct rows and an incorrect
/// count, and a count of privileged documents about a named person is a disclosure
/// on its own (ADR-0025).
/// </remarks>
public sealed class DocumentQueries : IDocumentQueries
{
    private readonly AgencyOsDbContext _context;

    public DocumentQueries(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentSummaryModel>> ListAsync(
        OrganizationId organizationId,
        DocumentQueryScope scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<Document> query = Narrow(organizationId, scope.ReadableSensitivities);

        DocumentFilter filter = scope.Filter;

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.CreatedAfter is { } after)
        {
            DateTimeOffset from = new(after.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt >= from);
        }

        if (filter.CreatedBefore is { } before)
        {
            DateTimeOffset to = new(before.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt <= to);
        }

        if (filter.LinkedTarget is { } target)
        {
            query = filter.LinkedTargetId is { } targetId
                ? query.Where(x => x.Links.Any(l => l.Target == target && l.TargetId == targetId))
                : query.Where(x => x.Links.Any(l => l.Target == target));
        }

        if (filter.HasContent)
        {
            query = query.Where(x => x.Versions.Count > 0);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(x => x.Versions.Any(v => v.Source == source));
        }

        if (filter.Search is { Length: > 0 } search)
        {
            string pattern = $"%{search}%";

            // Title, reference and filename. Deliberately never the extracted text:
            // a match inside a privileged contract would report its contents to
            // whoever ran the search (ADR-0025).
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.Reference != null && EF.Functions.ILike(x.Reference, pattern))
                || x.Versions.Any(v => EF.Functions.ILike(v.DisplayFileName, pattern)));
        }

        List<Document> documents = await query
            .Include(x => x.Versions)
            .Include(x => x.Links)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return [.. documents.Select(x => ToSummary(x, users))];
    }

    /// <inheritdoc />
    public async Task<DocumentDetailModel?> GetAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken = default)
    {
        Document? document = await _context.Documents
            .AsNoTracking()
            .Include(x => x.Versions)
            .Include(x => x.Links)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        List<DocumentEvent> history = await _context.DocumentEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.DocumentId == id)
            .OrderByDescending(x => x.OccurredAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<(DocumentLinkTarget, Guid), string> labels = await LinkLabels
            .DescribeAsync(
                _context,
                organizationId,
                [.. document.Links.Select(x => (x.Target, x.TargetId))],
                cancellationToken)
            .ConfigureAwait(false);

        return new DocumentDetailModel(
            ToSummary(document, users),
            document.Description,
            document.ArchiveReason,
            [.. document.Versions.OrderBy(x => x.Sequence).Select(ToVersion)],
            [
                .. document.Links.Select(x => new DocumentLinkModel(
                    x.Id,
                    x.Target,
                    x.TargetId,
                    labels.GetValueOrDefault((x.Target, x.TargetId), x.Target.ToString()),
                    x.Note,
                    x.LinkedAt,
                    Name(users, x.LinkedBy))),
            ],
            [
                .. history.Select(x => new DocumentEventModel(
                    x.OccurredAt, x.Kind, x.Summary, x.Detail, Name(users, x.ActorUserId))),
            ],

            // The extracted text of the newest version, and only for a caller who
            // reached this method - which is only after the document's own
            // classification was authorized (ADR-0025).
            document.CurrentVersion?.ExtractedText);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        IReadOnlySet<DocumentSensitivity> readableSensitivities,
        CancellationToken cancellationToken = default)
    {
        List<Document> documents = await Narrow(organizationId, readableSensitivities)
            .Where(x => x.Links.Any(l => l.Target == target && l.TargetId == targetId))
            .Include(x => x.Versions)
            .Include(x => x.Links)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return [.. documents.Select(x => ToSummary(x, users))];
    }

    /// <summary>Restricts to the tenant and to what the caller may read.</summary>
    private IQueryable<Document> Narrow(
        OrganizationId organizationId,
        IReadOnlySet<DocumentSensitivity> readable)
    {
        DocumentSensitivity[] allowed = [.. readable];

        return _context.Documents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private static DocumentSummaryModel ToSummary(Document document, Dictionary<Guid, string> users)
    {
        DocumentVersion? current = document.Versions.Count == 0
            ? null
            : document.Versions.MaxBy(x => x.Sequence);

        return new DocumentSummaryModel(
            document.Id,
            document.Title,
            document.Kind,
            document.Status,
            document.Sensitivity,
            document.Reference,
            current is null ? null : ToVersion(current),
            document.Versions.Count,
            document.Links.Count,
            document.Versions.Count > 0,
            document.CreatedAt,
            document.UpdatedAt,
            Name(users, document.CreatedBy),
            document.Version);
    }

    private static DocumentVersionSummaryModel ToVersion(DocumentVersion version) =>
        new(
            version.Id,
            version.Sequence,
            version.DisplayFileName,
            version.MediaType,
            version.ByteLength,
            version.ContentHash,
            version.Source,
            version.SourceExternalReference,
            version.RecordedAt,
            null,
            version.Notes,
            version.ExtractionState,
            version.ExtractionDetail,

            // Scan state lives on the blob and is projected as unscanned unless a
            // scanner said otherwise. Never inferred from the file having been
            // stored (ADR-0024).
            BlobScanState.Unscanned);

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

    private static string? Name(Dictionary<Guid, string> users, UserId id) =>
        users.GetValueOrDefault(id.Value);

    private static string? Name(Dictionary<Guid, string> users, UserId? id) =>
        id is { } value ? users.GetValueOrDefault(value.Value) : null;
}
