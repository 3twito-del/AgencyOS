using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Intelligence;

// ------------------------------------------------------------------ watchlists

public sealed record CreateWatchlistCommand(
    OrganizationId OrganizationId,
    string Name,
    IntelligenceSensitivity Sensitivity,
    Guid? OwnerUserId = null,
    string? Purpose = null,
    IReadOnlyList<SubjectInput>? Entries = null);

public sealed record UpdateWatchlistCommand(
    OrganizationId OrganizationId,
    WatchlistId WatchlistId,
    string Name,
    IntelligenceSensitivity Sensitivity,
    int ExpectedVersion,
    string? Purpose = null);

public sealed record AddWatchlistEntryCommand(
    OrganizationId OrganizationId,
    WatchlistId WatchlistId,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);

public sealed record RemoveWatchlistEntryCommand(
    OrganizationId OrganizationId,
    WatchlistId WatchlistId,
    Guid EntryId,
    int ExpectedVersion);

public sealed record RecordWatchlistReviewCommand(
    OrganizationId OrganizationId,
    WatchlistId WatchlistId,
    int ExpectedVersion);

public sealed record ArchiveWatchlistCommand(
    OrganizationId OrganizationId,
    WatchlistId WatchlistId,
    int ExpectedVersion);

/// <summary>
/// Creates and maintains monitoring lists.
/// </summary>
/// <remarks>
/// Membership is explicit throughout. M11 adds no rule engine and no filter
/// language of its own, because a second query DSL beside M3's saved views would
/// give the agency two ways to express the same cohort that drift apart
/// (ADR-0030).
/// </remarks>
public sealed class WatchlistHandler
{
    private readonly IWatchlistRepository _watchlists;
    private readonly IIntelligenceSubjectValidator _subjects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public WatchlistHandler(
        IWatchlistRepository watchlists,
        IIntelligenceSubjectValidator subjects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _watchlists = watchlists;
        _subjects = subjects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<WatchlistId> HandleAsync(
        CreateWatchlistCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        foreach (SubjectInput entry in command.Entries ?? [])
        {
            await RequireSubjectAsync(command.OrganizationId, entry, cancellationToken)
                .ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        Watchlist watchlist = Watchlist.Create(
            command.OrganizationId,
            command.Name,
            command.Sensitivity,
            command.OwnerUserId is { } owner ? new UserId(owner) : actor,
            actor,
            now,
            command.Purpose);

        foreach (SubjectInput entry in command.Entries ?? [])
        {
            watchlist.AddEntry(entry.Kind, entry.SubjectId, actor, now, entry.Note);
        }

        _watchlists.Add(watchlist);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Watchlist,
            watchlist.Id.Value,
            IntelligenceEventKind.WatchlistCreated,
            $"Watchlist created: {watchlist.Name}",
            actor,
            now,
            $"{watchlist.Entries.Count} entries"));

        _audit.Record(
            AuditAction.WatchlistCreated,
            entityType: nameof(Watchlist),
            entityId: watchlist.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new
            {
                Sensitivity = watchlist.Sensitivity.ToString(),
                EntryCount = watchlist.Entries.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return watchlist.Id;
    }

    public async Task HandleAsync(
        UpdateWatchlistCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Watchlist watchlist = await RequireAsync(
            command.OrganizationId, command.WatchlistId, cancellationToken).ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        watchlist.Update(
            command.Name, command.Sensitivity, command.ExpectedVersion, command.Purpose);

        _audit.Record(
            AuditAction.WatchlistUpdated,
            entityType: nameof(Watchlist),
            entityId: watchlist.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddWatchlistEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Watchlist watchlist = await RequireAsync(
            command.OrganizationId, command.WatchlistId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSubjectAsync(
            command.OrganizationId,
            new SubjectInput(command.Kind, command.SubjectId, command.Note),
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        WatchlistEntry entry = watchlist.AddEntry(
            command.Kind, command.SubjectId, actor, now, command.Note);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Watchlist,
            watchlist.Id.Value,
            IntelligenceEventKind.WatchlistEntryAdded,
            $"Added a {command.Kind} to the list.",
            actor,
            now));

        _audit.Record(
            AuditAction.WatchlistEntryAdded,
            entityType: nameof(Watchlist),
            entityId: watchlist.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Kind = command.Kind.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.Id;
    }

    public async Task HandleAsync(
        RemoveWatchlistEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Watchlist watchlist = await RequireAsync(
            command.OrganizationId, command.WatchlistId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        watchlist.RemoveEntry(command.EntryId);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Watchlist,
            watchlist.Id.Value,
            IntelligenceEventKind.WatchlistEntryRemoved,
            "Removed from the list.",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.WatchlistEntryRemoved,
            entityType: nameof(Watchlist),
            entityId: watchlist.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records that somebody went through the list.</summary>
    /// <remarks>
    /// The anchor for "what is new since I last looked", which is a better question
    /// than "what is new in the last thirty days" and cannot be asked without it.
    /// </remarks>
    public async Task HandleAsync(
        RecordWatchlistReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Watchlist watchlist = await RequireAsync(
            command.OrganizationId, command.WatchlistId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        watchlist.RecordReview(actor, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Watchlist,
            watchlist.Id.Value,
            IntelligenceEventKind.WatchlistReviewed,
            "Watchlist reviewed.",
            actor,
            now));

        _audit.Record(
            AuditAction.WatchlistReviewed,
            entityType: nameof(Watchlist),
            entityId: watchlist.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ArchiveWatchlistCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Watchlist watchlist = await RequireAsync(
            command.OrganizationId, command.WatchlistId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        watchlist.Archive(now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Watchlist,
            watchlist.Id.Value,
            IntelligenceEventKind.WatchlistArchived,
            "Watchlist archived. Nothing was removed from it.",
            actor,
            now));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireSubjectAsync(
        OrganizationId organizationId,
        SubjectInput subject,
        CancellationToken cancellationToken)
    {
        if (!await _subjects
            .ExistsAsync(organizationId, subject.Kind, subject.SubjectId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new DomainException(
                $"There is no {subject.Kind} with that identifier in this organization.");
        }
    }

    private async Task<Watchlist> RequireAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken) =>
        await _watchlists.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Watchlist), id.ToString());
}

// ---------------------------------------------------------------- talent radar

public sealed record CreateRadarEntryCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    string Rationale,
    IntelligenceSensitivity Sensitivity,
    Guid? OwnerUserId = null,
    string? IntendedDisciplines = null,
    TalentRadarPriority Priority = TalentRadarPriority.Unassigned,
    DateTimeOffset? FirstObservedAt = null);

public sealed record UpdateRadarEntryCommand(
    OrganizationId OrganizationId,
    TalentRadarEntryId EntryId,
    string Rationale,
    IntelligenceSensitivity Sensitivity,
    TalentRadarPriority Priority,
    int ExpectedVersion,
    string? IntendedDisciplines = null);

public sealed record ChangeRadarStatusCommand(
    OrganizationId OrganizationId,
    TalentRadarEntryId EntryId,
    TalentRadarStatus Status,
    int ExpectedVersion);

public sealed record RecordRadarReviewCommand(
    OrganizationId OrganizationId,
    TalentRadarEntryId EntryId,
    int ExpectedVersion);

public sealed record DismissRadarEntryCommand(
    OrganizationId OrganizationId,
    TalentRadarEntryId EntryId,
    string Reason,
    int ExpectedVersion);

/// <summary>
/// Turns a radar entry into an M4 prospect.
/// </summary>
/// <remarks>
/// A human command with no threshold behind it. Nothing about a score, a signal
/// count or a priority promotes somebody to a prospect (ADR-0030).
/// </remarks>
public sealed record ConvertRadarEntryToProspectCommand(
    OrganizationId OrganizationId,
    TalentRadarEntryId EntryId,
    int ExpectedVersion,
    Guid? ProspectOwnerUserId = null,
    DateOnly? IdentifiedOn = null,
    string? Source = null,
    string? StrategyNotes = null);

/// <summary>What a conversion produced.</summary>
public sealed record RadarConversionResult(
    ProspectId ProspectId,
    TalentProfileId TalentProfileId,
    bool CreatedTalentProfile);

/// <summary>
/// Works the pre-prospect radar, and hands entries to M4 when a person decides.
/// </summary>
/// <remarks>
/// The boundary is the point. Everything up to "should we approach them" is here;
/// everything from the approach onwards is an M4 prospect. Duplicating M4's stages
/// would give the agency two places to look for the same answer (ADR-0030).
/// </remarks>
public sealed class TalentRadarHandler
{
    private readonly ITalentRadarRepository _radar;
    private readonly IPersonRepository _people;
    private readonly ITalentProfileRepository _profiles;
    private readonly IProspectRepository _prospects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public TalentRadarHandler(
        ITalentRadarRepository radar,
        IPersonRepository people,
        ITalentProfileRepository profiles,
        IProspectRepository prospects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _radar = radar;
        _people = people;
        _profiles = profiles;
        _prospects = prospects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<TalentRadarEntryId> HandleAsync(
        CreateRadarEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        if (await _people.FindAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false) is null)
        {
            throw new DomainException("That person is not in this organization.");
        }

        // One person is watched once at a time. Two open entries would be two
        // analysts researching in parallel without knowing.
        if (await _radar
            .FindOpenForPersonAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            throw new AlreadyExistsException(
                "That person is already on the radar. One person is watched once at a "
                    + "time, so two analysts cannot research them in parallel without "
                    + "knowing.");
        }

        DateTimeOffset now = _clock.UtcNow;

        TalentRadarEntry entry = TalentRadarEntry.Create(
            command.OrganizationId,
            command.PersonId,
            command.Rationale,
            command.Sensitivity,
            command.OwnerUserId is { } owner ? new UserId(owner) : actor,
            actor,
            now,
            command.IntendedDisciplines,
            command.Priority,
            command.FirstObservedAt);

        _radar.Add(entry);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.TalentRadarEntry,
            entry.Id.Value,
            IntelligenceEventKind.RadarEntryCreated,
            "Added to the talent radar.",
            actor,
            now));

        _audit.Record(
            AuditAction.RadarEntryCreated,
            entityType: nameof(TalentRadarEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceRadarWrite,

            // The person and the classification, never the rationale. Why the
            // agency is watching somebody is exactly what should not appear in an
            // exported log.
            semanticDelta: new
            {
                PersonId = command.PersonId.ToString(),
                Sensitivity = entry.Sensitivity.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.Id;
    }

    public async Task HandleAsync(
        UpdateRadarEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TalentRadarEntry entry = await RequireAsync(
            command.OrganizationId, command.EntryId, cancellationToken).ConfigureAwait(false);

        await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        entry.Update(
            command.Rationale,
            command.Sensitivity,
            command.Priority,
            command.ExpectedVersion,
            command.IntendedDisciplines);

        _audit.Record(
            AuditAction.RadarEntryUpdated,
            entityType: nameof(TalentRadarEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceRadarWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ChangeRadarStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TalentRadarEntry entry = await RequireAsync(
            command.OrganizationId, command.EntryId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        entry.ChangeStatus(command.Status, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.TalentRadarEntry,
            entry.Id.Value,
            IntelligenceEventKind.RadarStatusChanged,
            $"Radar status is now {command.Status}.",
            actor,
            now));

        _audit.Record(
            AuditAction.RadarStatusChanged,
            entityType: nameof(TalentRadarEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceRadarWrite,
            semanticDelta: new { Status = command.Status.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        RecordRadarReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TalentRadarEntry entry = await RequireAsync(
            command.OrganizationId, command.EntryId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        entry.RecordReview(actor, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.TalentRadarEntry,
            entry.Id.Value,
            IntelligenceEventKind.RadarReviewed,
            "Radar entry reviewed.",
            actor,
            now));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        DismissRadarEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TalentRadarEntry entry = await RequireAsync(
            command.OrganizationId, command.EntryId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        entry.Dismiss(command.Reason, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.TalentRadarEntry,
            entry.Id.Value,
            IntelligenceEventKind.RadarDismissed,
            "Set aside.",
            actor,
            now,
            command.Reason));

        _audit.Record(
            AuditAction.RadarDismissed,
            entityType: nameof(TalentRadarEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceRadarWrite,
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the M4 prospect a radar entry has been leading to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One transaction. A talent profile is created only if the person has none,
    /// the prospect is created through M4's own aggregate, and the radar entry is
    /// marked converted last — so a failure anywhere leaves the entry open rather
    /// than claiming a prospect that was never created (ADR-0030).
    /// </para>
    /// <para>
    /// The M4 rule that at most one pursuit of a person may be open is checked
    /// here too, so a second conversion is refused with a sentence rather than by
    /// a partial unique index at save time.
    /// </para>
    /// </remarks>
    public async Task<RadarConversionResult> HandleAsync(
        ConvertRadarEntryToProspectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TalentRadarEntry entry = await RequireAsync(
            command.OrganizationId, command.EntryId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeRadarAsync(command.OrganizationId, entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        // Creating a prospect is an M4 write, so it needs M4's grant as well as the
        // radar's. Being allowed to watch somebody is not being allowed to pursue
        // them.
        await _authorization
            .AuthorizeWriteAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (await _prospects
            .FindOpenForPersonAsync(command.OrganizationId, entry.PersonId, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            throw new AlreadyExistsException(
                "There is already an open prospect for that person. M4 allows one pursuit "
                    + "at a time, and a second would mean two agents courting them.");
        }

        DateTimeOffset now = _clock.UtcNow;

        TalentProfile? profile = await _profiles
            .FindByPersonAsync(command.OrganizationId, entry.PersonId, cancellationToken)
            .ConfigureAwait(false);

        bool createdProfile = profile is null;

        if (profile is null)
        {
            // The minimum valid profile. M4 owns what a rich one looks like, and
            // guessing a career stage or a positioning from radar notes would put
            // an analyst's shorthand into a canonical talent record.
            profile = TalentProfile.Create(command.OrganizationId, entry.PersonId, actor, now);

            _profiles.Add(profile);
        }

        Prospect prospect = Prospect.Create(
            command.OrganizationId,
            entry.PersonId,
            command.ProspectOwnerUserId is { } owner ? new UserId(owner) : entry.OwnerUserId,
            command.IdentifiedOn ?? DateOnly.FromDateTime(entry.FirstObservedAt.UtcDateTime),
            actor,
            now,
            command.Source,
            command.StrategyNotes);

        _prospects.Add(prospect);

        // Last, and only now that both records exist.
        entry.NoteConverted(prospect.Id, profile.Id, actor, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.TalentRadarEntry,
            entry.Id.Value,
            IntelligenceEventKind.RadarConvertedToProspect,
            "Converted to a prospect. The pursuit is recorded against the prospect from here.",
            actor,
            now,
            createdProfile ? "A talent profile was created." : "An existing talent profile was used."));

        _audit.Record(
            AuditAction.RadarConvertedToProspect,
            entityType: nameof(TalentRadarEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceRadarWrite,
            semanticDelta: new
            {
                PersonId = entry.PersonId.ToString(),
                ProspectId = prospect.Id.ToString(),
                TalentProfileId = profile.Id.ToString(),
                CreatedTalentProfile = createdProfile,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RadarConversionResult(prospect.Id, profile.Id, createdProfile);
    }

    private async Task<TalentRadarEntry> RequireAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        CancellationToken cancellationToken) =>
        await _radar.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TalentRadarEntry), id.ToString());
}
