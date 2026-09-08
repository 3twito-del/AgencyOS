using AgencyOS.Contracts.Intelligence;

namespace AgencyOS.Client;

/// <summary>
/// The M11 intelligence surface, as the Windows client sees it.
/// </summary>
/// <remarks>
/// <para>
/// Every method here goes to the server and waits. Nothing is queued and nothing is
/// cached: <c>docs/13_OFFLINE_CLASSIFICATION.md</c> classifies the whole milestone
/// ONLINE_ONLY. Intelligence is the most sensitive data in AgencyOS — a
/// source-sensitive signal names somebody who spoke in confidence — and caching it
/// on a laptop would put it somewhere the server's classification cannot reach
/// (§51, ADR-0030).
/// </para>
/// <para>
/// There is no summarize call, no extract call and no generate call on this
/// interface, and there is no route on the server that would answer one (§54).
/// </para>
/// </remarks>
public partial interface IAgencyOsApi
{
    // ---- sources ----

    Task<IReadOnlyList<IntelligenceSourceResponse>> ListIntelligenceSourcesAsync(
        string? kind = null,
        string? reliability = null,
        string? sensitivity = null,
        Guid? recordedBy = null,
        DateOnly? observedAfter = null,
        DateOnly? observedBefore = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<IntelligenceSourceResponse> GetIntelligenceSourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> RecordIntelligenceSourceAsync(
        RecordSourceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateIntelligenceSourceAsync(
        Guid sourceId,
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default);

    Task AssessSourceReliabilityAsync(
        Guid sourceId,
        AssessSourceReliabilityRequest request,
        CancellationToken cancellationToken = default);

    // ---- signals ----

    Task<IReadOnlyList<SignalResponse>> ListSignalsAsync(
        string? kind = null,
        string? verification = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? sourceId = null,
        Guid? watchlistId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<SignalDetailResponse> GetSignalAsync(
        Guid signalId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> RecordSignalAsync(
        RecordSignalRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateSignalAsync(
        Guid signalId,
        UpdateSignalRequest request,
        CancellationToken cancellationToken = default);

    Task ChangeSignalVerificationAsync(
        Guid signalId,
        ChangeSignalVerificationRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> LinkSignalEvidenceAsync(
        Guid signalId,
        LinkSignalEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task UnlinkSignalEvidenceAsync(
        Guid signalId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> AddSignalSubjectAsync(
        Guid signalId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveSignalSubjectAsync(
        Guid signalId,
        Guid subjectRowId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    // ---- theses ----

    Task<IReadOnlyList<ThesisResponse>> ListThesesAsync(
        string? status = null,
        string? confidence = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? ownerUserId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<ThesisDetailResponse> GetThesisAsync(
        Guid thesisId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> CreateThesisAsync(
        CreateThesisRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ActivateThesisAsync(
        Guid thesisId,
        ActivateThesisRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> ReviseThesisAsync(
        Guid thesisId,
        ReviseThesisRequest request,
        CancellationToken cancellationToken = default);

    Task CloseThesisAsync(
        Guid thesisId,
        CloseThesisRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> LinkThesisEvidenceAsync(
        Guid thesisId,
        LinkThesisEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task UnlinkThesisEvidenceAsync(
        Guid thesisId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> AddThesisSubjectAsync(
        Guid thesisId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default);

    // ---- predictions ----

    Task<IReadOnlyList<PredictionResponse>> ListPredictionsAsync(
        string? status = null,
        string? outcome = null,
        string? sensitivity = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        Guid? ownerUserId = null,
        DateOnly? resolvesBefore = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<PredictionDetailResponse> GetPredictionAsync(
        Guid predictionId,
        CancellationToken cancellationToken = default);

    Task<PredictionCalibrationResponse> GetPredictionCalibrationAsync(
        Guid? ownerUserId = null,
        DateOnly? resolvedAfter = null,
        DateOnly? resolvedBefore = null,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> CreatePredictionAsync(
        CreatePredictionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> RecordPredictionRevisionAsync(
        Guid predictionId,
        RecordPredictionRevisionRequest request,
        CancellationToken cancellationToken = default);

    Task ResolvePredictionAsync(
        Guid predictionId,
        ResolvePredictionRequest request,
        CancellationToken cancellationToken = default);

    Task CancelPredictionAsync(
        Guid predictionId,
        CancelPredictionRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> AddPredictionSubjectAsync(
        Guid predictionId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default);

    // ---- watchlists ----

    Task<IReadOnlyList<WatchlistResponse>> ListWatchlistsAsync(
        string? status = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<WatchlistDetailResponse> GetWatchlistAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default);

    Task<WatchlistActivityResponse> GetWatchlistActivityAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> CreateWatchlistAsync(
        CreateWatchlistRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateWatchlistAsync(
        Guid watchlistId,
        UpdateWatchlistRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> AddWatchlistEntryAsync(
        Guid watchlistId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveWatchlistEntryAsync(
        Guid watchlistId,
        Guid entryId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task RecordWatchlistReviewAsync(
        Guid watchlistId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default);

    Task ArchiveWatchlistAsync(
        Guid watchlistId,
        ArchiveWatchlistRequest request,
        CancellationToken cancellationToken = default);

    // ---- talent radar ----

    Task<IReadOnlyList<TalentRadarResponse>> ListTalentRadarAsync(
        string? status = null,
        string? priority = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        Guid? personId = null,
        Guid? watchlistId = null,
        string? discipline = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<TalentRadarDetailResponse> GetTalentRadarEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> CreateTalentRadarEntryAsync(
        CreateRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateTalentRadarEntryAsync(
        Guid entryId,
        UpdateRadarEntryRequest request,
        CancellationToken cancellationToken = default);

    Task ChangeTalentRadarStatusAsync(
        Guid entryId,
        ChangeRadarStatusRequest request,
        CancellationToken cancellationToken = default);

    Task RecordTalentRadarReviewAsync(
        Guid entryId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default);

    Task DismissTalentRadarEntryAsync(
        Guid entryId,
        DismissRadarEntryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Hands a radar entry to M4 as a prospect.</summary>
    Task<RadarConversionResponse> ConvertTalentRadarEntryAsync(
        Guid entryId,
        ConvertRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    // ---- research cases ----

    Task<IReadOnlyList<ResearchCaseResponse>> ListResearchCasesAsync(
        string? status = null,
        string? sensitivity = null,
        Guid? ownerUserId = null,
        string? subjectKind = null,
        Guid? subjectId = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<ResearchCaseDetailResponse> GetResearchCaseAsync(
        Guid researchCaseId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> OpenResearchCaseAsync(
        OpenResearchCaseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateResearchCaseAsync(
        Guid researchCaseId,
        UpdateResearchCaseRequest request,
        CancellationToken cancellationToken = default);

    Task ChangeResearchCaseStatusAsync(
        Guid researchCaseId,
        ChangeResearchCaseStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> LinkResearchItemAsync(
        Guid researchCaseId,
        LinkResearchItemRequest request,
        CancellationToken cancellationToken = default);

    Task UnlinkResearchItemAsync(
        Guid researchCaseId,
        Guid linkId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<IntelligenceIdResponse> AddResearchSubjectAsync(
        Guid researchCaseId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default);

    // ---- derived ----

    Task<RelationshipIntelligenceResponse> GetRelationshipIntelligenceAsync(
        string kind,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<IntelligenceCommandCenterResponse> GetIntelligenceCommandCenterAsync(
        CancellationToken cancellationToken = default);
}

public sealed partial class AgencyOsApiClient
{
    private string IntelligenceRoot => $"{TenantRoot}/intelligence";

    // ------------------------------------------------------------- sources

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
        QueryBuilder query = new();
        query.Add("kind", kind);
        query.Add("reliability", reliability);
        query.Add("sensitivity", sensitivity);
        query.Add("recordedBy", recordedBy);
        query.Add("observedAfter", observedAfter);
        query.Add("observedBefore", observedBefore);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<IntelligenceSourceResponse>(
            query.Apply($"{IntelligenceRoot}/sources"), cancellationToken);
    }

    public Task<IntelligenceSourceResponse> GetIntelligenceSourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        GetAsync<IntelligenceSourceResponse>(
            $"{IntelligenceRoot}/sources/{sourceId}", cancellationToken);

    public Task<IntelligenceIdResponse> RecordIntelligenceSourceAsync(
        RecordSourceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordSourceRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/sources",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateIntelligenceSourceAsync(
        Guid sourceId,
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Patch,
            $"{IntelligenceRoot}/sources/{sourceId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task AssessSourceReliabilityAsync(
        Guid sourceId,
        AssessSourceReliabilityRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/sources/{sourceId}/reliability",
            request,
            idempotencyKey: null,
            cancellationToken);

    // ------------------------------------------------------------- signals

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
        QueryBuilder query = new();
        query.Add("kind", kind);
        query.Add("verification", verification);
        query.Add("sensitivity", sensitivity);
        query.Add("subjectKind", subjectKind);
        query.Add("subjectId", subjectId);
        query.Add("sourceId", sourceId);
        query.Add("watchlistId", watchlistId);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<SignalResponse>(
            query.Apply($"{IntelligenceRoot}/signals"), cancellationToken);
    }

    public Task<SignalDetailResponse> GetSignalAsync(
        Guid signalId,
        CancellationToken cancellationToken = default) =>
        GetAsync<SignalDetailResponse>(
            $"{IntelligenceRoot}/signals/{signalId}", cancellationToken);

    public Task<IntelligenceIdResponse> RecordSignalAsync(
        RecordSignalRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordSignalRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/signals",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateSignalAsync(
        Guid signalId,
        UpdateSignalRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Patch,
            $"{IntelligenceRoot}/signals/{signalId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task ChangeSignalVerificationAsync(
        Guid signalId,
        ChangeSignalVerificationRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/signals/{signalId}/verification",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> LinkSignalEvidenceAsync(
        Guid signalId,
        LinkSignalEvidenceRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<LinkSignalEvidenceRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/signals/{signalId}/evidence", request, cancellationToken);

    public Task UnlinkSignalEvidenceAsync(
        Guid signalId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteWithVersionAsync(
            $"{IntelligenceRoot}/signals/{signalId}/evidence/{evidenceId}",
            expectedVersion,
            cancellationToken);

    public Task<IntelligenceIdResponse> AddSignalSubjectAsync(
        Guid signalId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<AddIntelligenceSubjectRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/signals/{signalId}/subjects", request, cancellationToken);

    public Task RemoveSignalSubjectAsync(
        Guid signalId,
        Guid subjectRowId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteWithVersionAsync(
            $"{IntelligenceRoot}/signals/{signalId}/subjects/{subjectRowId}",
            expectedVersion,
            cancellationToken);

    // -------------------------------------------------------------- theses

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
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("confidence", confidence);
        query.Add("sensitivity", sensitivity);
        query.Add("subjectKind", subjectKind);
        query.Add("subjectId", subjectId);
        query.Add("ownerUserId", ownerUserId);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<ThesisResponse>(
            query.Apply($"{IntelligenceRoot}/theses"), cancellationToken);
    }

    public Task<ThesisDetailResponse> GetThesisAsync(
        Guid thesisId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ThesisDetailResponse>($"{IntelligenceRoot}/theses/{thesisId}", cancellationToken);

    public Task<IntelligenceIdResponse> CreateThesisAsync(
        CreateThesisRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateThesisRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/theses",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ActivateThesisAsync(
        Guid thesisId,
        ActivateThesisRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/theses/{thesisId}/activate",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> ReviseThesisAsync(
        Guid thesisId,
        ReviseThesisRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ReviseThesisRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/theses/{thesisId}/revisions", request, cancellationToken);

    public Task CloseThesisAsync(
        Guid thesisId,
        CloseThesisRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/theses/{thesisId}/close",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> LinkThesisEvidenceAsync(
        Guid thesisId,
        LinkThesisEvidenceRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<LinkThesisEvidenceRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/theses/{thesisId}/evidence", request, cancellationToken);

    public Task UnlinkThesisEvidenceAsync(
        Guid thesisId,
        Guid evidenceId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteWithVersionAsync(
            $"{IntelligenceRoot}/theses/{thesisId}/evidence/{evidenceId}",
            expectedVersion,
            cancellationToken);

    public Task<IntelligenceIdResponse> AddThesisSubjectAsync(
        Guid thesisId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<AddIntelligenceSubjectRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/theses/{thesisId}/subjects", request, cancellationToken);

    // --------------------------------------------------------- predictions

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
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("outcome", outcome);
        query.Add("sensitivity", sensitivity);
        query.Add("subjectKind", subjectKind);
        query.Add("subjectId", subjectId);
        query.Add("ownerUserId", ownerUserId);
        query.Add("resolvesBefore", resolvesBefore);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<PredictionResponse>(
            query.Apply($"{IntelligenceRoot}/predictions"), cancellationToken);
    }

    public Task<PredictionDetailResponse> GetPredictionAsync(
        Guid predictionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PredictionDetailResponse>(
            $"{IntelligenceRoot}/predictions/{predictionId}", cancellationToken);

    public Task<PredictionCalibrationResponse> GetPredictionCalibrationAsync(
        Guid? ownerUserId = null,
        DateOnly? resolvedAfter = null,
        DateOnly? resolvedBefore = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("ownerUserId", ownerUserId);
        query.Add("resolvedAfter", resolvedAfter);
        query.Add("resolvedBefore", resolvedBefore);

        return GetAsync<PredictionCalibrationResponse>(
            query.Apply($"{IntelligenceRoot}/predictions/calibration"), cancellationToken);
    }

    public Task<IntelligenceIdResponse> CreatePredictionAsync(
        CreatePredictionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreatePredictionRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/predictions",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IntelligenceIdResponse> RecordPredictionRevisionAsync(
        Guid predictionId,
        RecordPredictionRevisionRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<RecordPredictionRevisionRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/predictions/{predictionId}/revisions",
            request,
            cancellationToken);

    public Task ResolvePredictionAsync(
        Guid predictionId,
        ResolvePredictionRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/predictions/{predictionId}/resolve",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task CancelPredictionAsync(
        Guid predictionId,
        CancelPredictionRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/predictions/{predictionId}/cancel",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> AddPredictionSubjectAsync(
        Guid predictionId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<AddIntelligenceSubjectRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/predictions/{predictionId}/subjects", request, cancellationToken);

    // ---------------------------------------------------------- watchlists

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
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("sensitivity", sensitivity);
        query.Add("ownerUserId", ownerUserId);
        query.Add("subjectKind", subjectKind);
        query.Add("subjectId", subjectId);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<WatchlistResponse>(
            query.Apply($"{IntelligenceRoot}/watchlists"), cancellationToken);
    }

    public Task<WatchlistDetailResponse> GetWatchlistAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default) =>
        GetAsync<WatchlistDetailResponse>(
            $"{IntelligenceRoot}/watchlists/{watchlistId}", cancellationToken);

    public Task<WatchlistActivityResponse> GetWatchlistActivityAsync(
        Guid watchlistId,
        CancellationToken cancellationToken = default) =>
        GetAsync<WatchlistActivityResponse>(
            $"{IntelligenceRoot}/watchlists/{watchlistId}/activity", cancellationToken);

    public Task<IntelligenceIdResponse> CreateWatchlistAsync(
        CreateWatchlistRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateWatchlistRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/watchlists",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateWatchlistAsync(
        Guid watchlistId,
        UpdateWatchlistRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Patch,
            $"{IntelligenceRoot}/watchlists/{watchlistId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> AddWatchlistEntryAsync(
        Guid watchlistId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<AddIntelligenceSubjectRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/watchlists/{watchlistId}/entries", request, cancellationToken);

    public Task RemoveWatchlistEntryAsync(
        Guid watchlistId,
        Guid entryId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteWithVersionAsync(
            $"{IntelligenceRoot}/watchlists/{watchlistId}/entries/{entryId}",
            expectedVersion,
            cancellationToken);

    public Task RecordWatchlistReviewAsync(
        Guid watchlistId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/watchlists/{watchlistId}/review",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task ArchiveWatchlistAsync(
        Guid watchlistId,
        ArchiveWatchlistRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/watchlists/{watchlistId}/archive",
            request,
            idempotencyKey: null,
            cancellationToken);

    // --------------------------------------------------------------- radar

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
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("priority", priority);
        query.Add("sensitivity", sensitivity);
        query.Add("ownerUserId", ownerUserId);
        query.Add("personId", personId);
        query.Add("watchlistId", watchlistId);
        query.Add("discipline", discipline);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<TalentRadarResponse>(
            query.Apply($"{IntelligenceRoot}/radar"), cancellationToken);
    }

    public Task<TalentRadarDetailResponse> GetTalentRadarEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        GetAsync<TalentRadarDetailResponse>(
            $"{IntelligenceRoot}/radar/{entryId}", cancellationToken);

    public Task<IntelligenceIdResponse> CreateTalentRadarEntryAsync(
        CreateRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateRadarEntryRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/radar",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateTalentRadarEntryAsync(
        Guid entryId,
        UpdateRadarEntryRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Patch,
            $"{IntelligenceRoot}/radar/{entryId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task ChangeTalentRadarStatusAsync(
        Guid entryId,
        ChangeRadarStatusRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/radar/{entryId}/status",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task RecordTalentRadarReviewAsync(
        Guid entryId,
        RecordReviewRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/radar/{entryId}/review",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task DismissTalentRadarEntryAsync(
        Guid entryId,
        DismissRadarEntryRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/radar/{entryId}/dismiss",
            request,
            idempotencyKey: null,
            cancellationToken);

    /// <summary>
    /// Hands a radar entry to M4 as a prospect.
    /// </summary>
    /// <remarks>
    /// Takes an idempotency key because it creates records in another milestone. A
    /// retried conversion must not produce a second prospect for the same person
    /// (ADR-0013).
    /// </remarks>
    public Task<RadarConversionResponse> ConvertTalentRadarEntryAsync(
        Guid entryId,
        ConvertRadarEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConvertRadarEntryRequest, RadarConversionResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/radar/{entryId}/convert",
            request,
            idempotencyKey,
            cancellationToken);

    // ------------------------------------------------------ research cases

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
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("sensitivity", sensitivity);
        query.Add("ownerUserId", ownerUserId);
        query.Add("subjectKind", subjectKind);
        query.Add("subjectId", subjectId);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<ResearchCaseResponse>(
            query.Apply($"{IntelligenceRoot}/research-cases"), cancellationToken);
    }

    public Task<ResearchCaseDetailResponse> GetResearchCaseAsync(
        Guid researchCaseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ResearchCaseDetailResponse>(
            $"{IntelligenceRoot}/research-cases/{researchCaseId}", cancellationToken);

    public Task<IntelligenceIdResponse> OpenResearchCaseAsync(
        OpenResearchCaseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<OpenResearchCaseRequest, IntelligenceIdResponse>(
            HttpMethod.Post,
            $"{IntelligenceRoot}/research-cases",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateResearchCaseAsync(
        Guid researchCaseId,
        UpdateResearchCaseRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Patch,
            $"{IntelligenceRoot}/research-cases/{researchCaseId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task ChangeResearchCaseStatusAsync(
        Guid researchCaseId,
        ChangeResearchCaseStatusRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{IntelligenceRoot}/research-cases/{researchCaseId}/status",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IntelligenceIdResponse> LinkResearchItemAsync(
        Guid researchCaseId,
        LinkResearchItemRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<LinkResearchItemRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/research-cases/{researchCaseId}/links",
            request,
            cancellationToken);

    public Task UnlinkResearchItemAsync(
        Guid researchCaseId,
        Guid linkId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteWithVersionAsync(
            $"{IntelligenceRoot}/research-cases/{researchCaseId}/links/{linkId}",
            expectedVersion,
            cancellationToken);

    public Task<IntelligenceIdResponse> AddResearchSubjectAsync(
        Guid researchCaseId,
        AddIntelligenceSubjectRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<AddIntelligenceSubjectRequest, IntelligenceIdResponse>(
            $"{IntelligenceRoot}/research-cases/{researchCaseId}/subjects",
            request,
            cancellationToken);

    // ------------------------------------------------------------- derived

    public Task<RelationshipIntelligenceResponse> GetRelationshipIntelligenceAsync(
        string kind,
        Guid subjectId,
        CancellationToken cancellationToken = default) =>
        GetAsync<RelationshipIntelligenceResponse>(
            $"{IntelligenceRoot}/relationships/{kind}/{subjectId}", cancellationToken);

    public Task<IntelligenceCommandCenterResponse> GetIntelligenceCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IntelligenceCommandCenterResponse>(
            $"{IntelligenceRoot}/command-center", cancellationToken);

    /// <summary>
    /// Removes something, carrying the version the caller believed it was at.
    /// </summary>
    /// <remarks>
    /// The version travels in the query string rather than in a body: a DELETE with
    /// a body is legal and widely mishandled by proxies. It stays required, because
    /// removing a citation from a signal two people are editing is exactly where a
    /// silent overwrite loses provenance (ADR-0014).
    /// </remarks>
    private async Task DeleteWithVersionAsync(
        string uri,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        QueryBuilder query = new();
        query.Add("expectedVersion", expectedVersion);

        using HttpResponseMessage response = await _http
            .DeleteAsync(query.Apply(uri), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
