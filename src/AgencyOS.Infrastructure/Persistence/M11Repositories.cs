using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Reads and writes evidence records.</summary>
public sealed class IntelligenceSourceRepository : IIntelligenceSourceRepository
{
    private readonly AgencyOsDbContext _context;

    public IntelligenceSourceRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(IntelligenceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _context.IntelligenceSources.Add(source);
    }

    /// <inheritdoc />
    public Task<IntelligenceSource?> FindAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        CancellationToken cancellationToken = default) =>
        _context.IntelligenceSources
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// One query for the whole set rather than one per source. A signal citing six
    /// sources should not be six round trips before anything is written.
    /// </remarks>
    public async Task<bool> AllExistAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<IntelligenceSourceId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return true;
        }

        IntelligenceSourceId[] wanted = [.. ids.Distinct()];

        int found = await _context.IntelligenceSources
            .AsNoTracking()
            .CountAsync(
                x => x.OrganizationId == organizationId && wanted.Contains(x.Id),
                cancellationToken)
            .ConfigureAwait(false);

        return found == wanted.Length;
    }
}

/// <summary>Reads and writes signals with their subjects and evidence.</summary>
public sealed class SignalRepository : ISignalRepository
{
    private readonly AgencyOsDbContext _context;

    public SignalRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _context.Signals.Add(signal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Evidence is included because the aggregate refuses to be left without it,
    /// and that check cannot run against a collection nobody loaded (ADR-0030).
    /// </remarks>
    public Task<Signal?> FindAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default) =>
        _context.Signals
            .Include(x => x.Evidence)
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default) =>
        _context.Signals
            .AsNoTracking()
            .AnyAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Reads and writes theses with their revisions, subjects and evidence.</summary>
public sealed class ThesisRepository : IThesisRepository
{
    private readonly AgencyOsDbContext _context;

    public ThesisRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(Thesis thesis)
    {
        ArgumentNullException.ThrowIfNull(thesis);

        _context.Theses.Add(thesis);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Revisions are included because a new one is numbered from the count, and
    /// numbering from a collection nobody loaded would restart the sequence at one
    /// and collide with the existing rows.
    /// </remarks>
    public Task<Thesis?> FindAsync(
        OrganizationId organizationId,
        ThesisId id,
        CancellationToken cancellationToken = default) =>
        _context.Theses
            .Include(x => x.Revisions)
            .Include(x => x.Evidence)
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Reads and writes predictions with their forecast history.</summary>
public sealed class PredictionRepository : IPredictionRepository
{
    private readonly AgencyOsDbContext _context;

    public PredictionRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(Prediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        _context.Predictions.Add(prediction);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The revisions are not optional. The current probability and the Brier score
    /// are both derived from them, so a prediction loaded without its history would
    /// report a probability of zero (ADR-0030).
    /// </remarks>
    public async Task<Prediction?> FindAsync(
        OrganizationId organizationId,
        PredictionId id,
        CancellationToken cancellationToken = default)
    {
        Prediction? prediction = await _context.Predictions
            .Include(x => x.Evidence)
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (prediction is null)
        {
            return null;
        }

        // Loaded in order, because the latest revision is the current forecast and
        // "latest" here means the last element rather than a sort at read time.
        await _context.Entry(prediction)
            .Collection(x => x.Revisions)
            .Query()
            .OrderBy(x => x.Sequence)
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return prediction;
    }
}

/// <summary>Reads and writes watchlists and their membership.</summary>
public sealed class WatchlistRepository : IWatchlistRepository
{
    private readonly AgencyOsDbContext _context;

    public WatchlistRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(Watchlist watchlist)
    {
        ArgumentNullException.ThrowIfNull(watchlist);

        _context.Watchlists.Add(watchlist);
    }

    /// <inheritdoc />
    public Task<Watchlist?> FindAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken = default) =>
        _context.Watchlists
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Reads and writes talent radar entries.</summary>
public sealed class TalentRadarRepository : ITalentRadarRepository
{
    private static readonly TalentRadarStatus[] Open =
    [
        TalentRadarStatus.Watching,
        TalentRadarStatus.Researching,
        TalentRadarStatus.ReadyForReview,
    ];

    private readonly AgencyOsDbContext _context;

    public TalentRadarRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(TalentRadarEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _context.TalentRadarEntries.Add(entry);
    }

    /// <inheritdoc />
    public Task<TalentRadarEntry?> FindAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        CancellationToken cancellationToken = default) =>
        _context.TalentRadarEntries
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The partial unique index enforces it too. This exists so a second entry is
    /// refused with a sentence rather than by a constraint violation at save time
    /// (ADR-0030).
    /// </remarks>
    public Task<TalentRadarEntry?> FindOpenForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default) =>
        _context.TalentRadarEntries
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.PersonId == personId
                    && Open.Contains(x.Status),
                cancellationToken);
}

/// <summary>Reads and writes research cases with their subjects and links.</summary>
public sealed class ResearchCaseRepository : IResearchCaseRepository
{
    private readonly AgencyOsDbContext _context;

    public ResearchCaseRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(ResearchCase researchCase)
    {
        ArgumentNullException.ThrowIfNull(researchCase);

        _context.ResearchCases.Add(researchCase);
    }

    /// <inheritdoc />
    public Task<ResearchCase?> FindAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        CancellationToken cancellationToken = default) =>
        _context.ResearchCases
            .Include(x => x.Links)
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Appends the curated intelligence history.</summary>
/// <remarks>
/// Add only. There is no update and no delete here, which is what append-only
/// means when it is a property of the code rather than a promise in a comment.
/// </remarks>
public sealed class IntelligenceEventRepository : IIntelligenceEventRepository
{
    private readonly AgencyOsDbContext _context;

    public IntelligenceEventRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(IntelligenceEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _context.IntelligenceEvents.Add(entry);
    }
}
