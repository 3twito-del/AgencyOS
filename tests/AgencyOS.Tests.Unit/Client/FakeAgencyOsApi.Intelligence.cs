using AgencyOS.Contracts.Intelligence;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The M11 half of the fake API.
/// </summary>
/// <remarks>
/// Records what the client asked for and returns what a test set up. It has no
/// behaviour of its own beyond that: the point of these tests is what the view
/// models do with an answer, not what a stub decides the answer should be.
/// </remarks>
internal sealed partial class FakeAgencyOsApi
{
    public List<IntelligenceSourceResponse> Sources { get; } = [];

    public List<SignalResponse> Signals { get; } = [];

    public List<ThesisResponse> Theses { get; } = [];

    public List<PredictionResponse> Predictions { get; } = [];

    public List<WatchlistResponse> Watchlists { get; } = [];

    public List<TalentRadarResponse> RadarEntries { get; } = [];

    public List<ResearchCaseResponse> ResearchCases { get; } = [];

    public SignalDetailResponse? SignalDetail { get; set; }

    public ThesisDetailResponse? ThesisDetail { get; set; }

    public PredictionDetailResponse? PredictionDetail { get; set; }

    public WatchlistDetailResponse? WatchlistDetail { get; set; }

    public WatchlistActivityResponse? WatchlistActivity { get; set; }

    public TalentRadarDetailResponse? RadarDetail { get; set; }

    public ResearchCaseDetailResponse? ResearchCaseDetail { get; set; }

    public RelationshipIntelligenceResponse? Relationship { get; set; }

    public PredictionCalibrationResponse Calibration { get; set; } =
        new(0, 0, 0, 0, 0, null, null, null);

    public IntelligenceCommandCenterResponse IntelligenceCommandCenter { get; set; } =
        new([], [], [], [], [], [], 0, 0, 0, 0);

    /// <summary>Filters the client last sent, so a test can assert what was asked.</summary>
    public List<string?> SignalFilters { get; } = [];

    public ChangeSignalVerificationRequest? LastVerification { get; private set; }

    public RecordPredictionRevisionRequest? LastForecast { get; private set; }

    public ResolvePredictionRequest? LastResolution { get; private set; }

    public AssessSourceReliabilityRequest? LastAssessment { get; private set; }

    public DismissRadarEntryRequest? LastDismissal { get; private set; }

    public ConvertRadarEntryRequest? LastConversion { get; private set; }

    public RadarConversionResponse Conversion { get; set; } =
        new(Guid.Empty, Guid.Empty, false);

    // ---- sources ----

    public Task<IReadOnlyList<IntelligenceSourceResponse>> ListIntelligenceSourcesAsync(
        string? kind = null,
        string? reliability = null,
        string? sensitivity = null,
        Guid? recordedBy = null,
        DateOnly? observedAfter = null,
        DateOnly? observedBefore = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<IntelligenceSourceResponse>>([.. Sources]);
    }

    public Task<IntelligenceSourceResponse> GetIntelligenceSourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Sources.Single(x => x.Id == sourceId));
    }

    public Task<IntelligenceIdResponse> RecordIntelligenceSourceAsync(
        RecordSourceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UpdateIntelligenceSourceAsync(
        Guid sourceId,
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task AssessSourceReliabilityAsync(
        Guid sourceId,
        AssessSourceReliabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastAssessment = request;

        return Task.CompletedTask;
    }

    // ---- signals ----

    public Task<IReadOnlyList<SignalResponse>> ListSignalsAsync(
        string? kind = null,
        string? verification = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? sourceId = null,
        Guid? watchlistId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        SignalFilters.Add(verification);

        return Task.FromResult<IReadOnlyList<SignalResponse>>([.. Signals]);
    }

    public Task<SignalDetailResponse> GetSignalAsync(
        Guid signalId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            SignalDetail ?? throw new InvalidOperationException("No signal was set up."));
    }

    public Task<IntelligenceIdResponse> RecordSignalAsync(
        RecordSignalRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UpdateSignalAsync(
        Guid signalId,
        UpdateSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ChangeSignalVerificationAsync(
        Guid signalId,
        ChangeSignalVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastVerification = request;

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> LinkSignalEvidenceAsync(
        Guid signalId,
        LinkSignalEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UnlinkSignalEvidenceAsync(
        Guid signalId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> AddSignalSubjectAsync(
        Guid signalId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task RemoveSignalSubjectAsync(
        Guid signalId,
        Guid subjectRowId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    // ---- theses ----

    public Task<IReadOnlyList<ThesisResponse>> ListThesesAsync(
        string? status = null,
        string? confidence = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? ownerUserId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<ThesisResponse>>([.. Theses]);
    }

    public Task<ThesisDetailResponse> GetThesisAsync(
        Guid thesisId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            ThesisDetail ?? throw new InvalidOperationException("No thesis was set up."));
    }

    public Task<IntelligenceIdResponse> CreateThesisAsync(
        CreateThesisRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task ActivateThesisAsync(
        Guid thesisId,
        ActivateThesisRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> ReviseThesisAsync(
        Guid thesisId,
        ReviseThesisRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task CloseThesisAsync(
        Guid thesisId,
        CloseThesisRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> LinkThesisEvidenceAsync(
        Guid thesisId,
        LinkThesisEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UnlinkThesisEvidenceAsync(
        Guid thesisId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> AddThesisSubjectAsync(
        Guid thesisId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    // ---- predictions ----

    public Task<IReadOnlyList<PredictionResponse>> ListPredictionsAsync(
        string? status = null,
        string? outcome = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? ownerUserId = null,
        DateOnly? resolvesBefore = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<PredictionResponse>>([.. Predictions]);
    }

    public Task<PredictionDetailResponse> GetPredictionAsync(
        Guid predictionId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            PredictionDetail
                ?? throw new InvalidOperationException("No prediction was set up."));
    }

    public Task<PredictionCalibrationResponse> GetPredictionCalibrationAsync(
        Guid? ownerUserId = null,
        DateOnly? resolvedAfter = null,
        DateOnly? resolvedBefore = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Calibration);
    }

    public Task<IntelligenceIdResponse> CreatePredictionAsync(
        CreatePredictionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task<IntelligenceIdResponse> RecordPredictionRevisionAsync(
        Guid predictionId,
        RecordPredictionRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastForecast = request;

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task ResolvePredictionAsync(
        Guid predictionId,
        ResolvePredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastResolution = request;

        return Task.CompletedTask;
    }

    public Task CancelPredictionAsync(
        Guid predictionId,
        CancelPredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> AddPredictionSubjectAsync(
        Guid predictionId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    // ---- watchlists ----

    public Task<IReadOnlyList<WatchlistResponse>> ListWatchlistsAsync(
        string? status = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<WatchlistResponse>>([.. Watchlists]);
    }

    public Task<WatchlistDetailResponse> GetWatchlistAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            WatchlistDetail ?? throw new InvalidOperationException("No watchlist was set up."));
    }

    public Task<WatchlistActivityResponse> GetWatchlistActivityAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            WatchlistActivity ?? throw new InvalidOperationException("No activity was set up."));
    }

    public Task<IntelligenceIdResponse> CreateWatchlistAsync(
        CreateWatchlistRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UpdateWatchlistAsync(
        Guid watchlistId,
        UpdateWatchlistRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> AddWatchlistEntryAsync(
        Guid watchlistId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task RemoveWatchlistEntryAsync(
        Guid watchlistId,
        Guid entryId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task RecordWatchlistReviewAsync(
        Guid watchlistId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ArchiveWatchlistAsync(
        Guid watchlistId,
        ArchiveWatchlistRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    // ---- radar ----

    public Task<IReadOnlyList<TalentRadarResponse>> ListTalentRadarAsync(
        string? status = null,
        string? priority = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        Guid? personId = null,
        Guid? watchlistId = null,
        string? discipline = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<TalentRadarResponse>>([.. RadarEntries]);
    }

    public Task<TalentRadarDetailResponse> GetTalentRadarEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            RadarDetail ?? throw new InvalidOperationException("No radar entry was set up."));
    }

    public Task<IntelligenceIdResponse> CreateTalentRadarEntryAsync(
        CreateRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UpdateTalentRadarEntryAsync(
        Guid entryId,
        UpdateRadarEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ChangeTalentRadarStatusAsync(
        Guid entryId,
        ChangeRadarStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task RecordTalentRadarReviewAsync(
        Guid entryId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task DismissTalentRadarEntryAsync(
        Guid entryId,
        DismissRadarEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastDismissal = request;

        return Task.CompletedTask;
    }

    public Task<RadarConversionResponse> ConvertTalentRadarEntryAsync(
        Guid entryId,
        ConvertRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);
        LastConversion = request;

        return Task.FromResult(Conversion);
    }

    // ---- research cases ----

    public Task<IReadOnlyList<ResearchCaseResponse>> ListResearchCasesAsync(
        string? status = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<ResearchCaseResponse>>([.. ResearchCases]);
    }

    public Task<ResearchCaseDetailResponse> GetResearchCaseAsync(
        Guid researchCaseId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            ResearchCaseDetail ?? throw new InvalidOperationException("No case was set up."));
    }

    public Task<IntelligenceIdResponse> OpenResearchCaseAsync(
        OpenResearchCaseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        IdempotencyKeys.Add(idempotencyKey);

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UpdateResearchCaseAsync(
        Guid researchCaseId,
        UpdateResearchCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ChangeResearchCaseStatusAsync(
        Guid researchCaseId,
        ChangeResearchCaseStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> LinkResearchItemAsync(
        Guid researchCaseId,
        LinkResearchItemRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    public Task UnlinkResearchItemAsync(
        Guid researchCaseId,
        Guid linkId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IntelligenceIdResponse> AddResearchSubjectAsync(
        Guid researchCaseId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new IntelligenceIdResponse(Guid.CreateVersion7()));
    }

    // ---- derived ----

    public Task<RelationshipIntelligenceResponse> GetRelationshipIntelligenceAsync(
        string kind,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            Relationship
                ?? throw new InvalidOperationException("No relationship was set up."));
    }

    public Task<IntelligenceCommandCenterResponse> GetIntelligenceCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(IntelligenceCommandCenter);
    }
}
