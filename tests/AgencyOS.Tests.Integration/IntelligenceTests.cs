using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M11 intelligence chain, end to end against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The properties under test are the ones the milestone would be worthless
/// without: a claim cannot exist without provenance, a forecast cannot be edited
/// after the fact, a thesis is never marked true, and a reader who may not open a
/// classification learns nothing about how much of it exists — not from a list,
/// not from a count, and not from a citation on something they may read
/// (§1, §28, ADR-0030).
/// </para>
/// <para>
/// The database-level refusals are exercised directly rather than through the API.
/// The aggregate refuses them too, and testing only the aggregate would prove that
/// one code path is careful rather than that the schema is.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class IntelligenceTests
{
    private readonly AgencyOsTestFixture _fixture;

    public IntelligenceTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------- the chain

    /// <summary>
    /// Source, signal, thesis, prediction, resolution — in the order an analyst
    /// does it.
    /// </summary>
    [Fact]
    public async Task Workflow_SourceSignalThesisPrediction()
    {
        Actor a = await ActorAsync("m11-chain", AgencyRole.Owner);

        PersonDetailResponse person = await CreatePersonAsync(a, "Marguerite Sable");

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/sources",
            new RecordSourceRequest(
                "ExternalUrl",
                "Trade report: Northgate restructures its drama slate",
                "Internal",
                Url: "https://example.test/northgate-drama",
                Publisher: "The Trade"));

        IntelligenceSourceResponse recorded = await GetAsync<IntelligenceSourceResponse>(
            a, $"intelligence/sources/{source.Id}");

        // A URL is a reference. AgencyOS did not archive the page and says so.
        Assert.False(recorded.IsHeldByAgencyOS);
        Assert.Equal("Unassessed", recorded.Reliability);
        Assert.Null(recorded.ReliabilityAssessedByDisplayName);

        // Reliability is a separate judgment, with a name on it.
        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/sources/{source.Id}/reliability",
            new AssessSourceReliabilityRequest(
                "Medium", recorded.Version, "Reliable on scheduling, weak on money")));

        IntelligenceSourceResponse assessed = await GetAsync<IntelligenceSourceResponse>(
            a, $"intelligence/sources/{source.Id}");

        Assert.Equal("Medium", assessed.Reliability);
        Assert.NotNull(assessed.ReliabilityAssessedByDisplayName);
        Assert.NotNull(assessed.ReliabilityAssessedAt);

        IntelligenceIdResponse signal = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/signals",
            new RecordSignalRequest(
                "Sable leaving Northgate",
                "Marguerite Sable is expected to leave Northgate before the autumn.",
                "PersonnelMove",
                "Internal",
                [new SignalEvidenceRequest(source.Id, "Primary", "…is expected to depart…")],
                [new IntelligenceSubjectRequest("Person", person.Person.Id, "The subject")]));

        SignalDetailResponse claim = await GetAsync<SignalDetailResponse>(
            a, $"intelligence/signals/{signal.Id}");

        // Unverified is the honest default, and there is no state that says true.
        Assert.Equal("Unverified", claim.Signal.Verification);
        Assert.Single(claim.Evidence);
        Assert.Equal(1, claim.Signal.EvidenceCount);
        Assert.Equal("Marguerite Sable", claim.Signal.Subjects[0].Label);
        Assert.False(claim.Evidence[0].ExcerptWithheld);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/signals/{signal.Id}/verification",
            new ChangeSignalVerificationRequest(
                "Corroborated", claim.Signal.Version, "Second trade carried it")));

        IntelligenceIdResponse thesis = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/theses",
            new CreateThesisRequest(
                "Northgate is retrenching in drama",
                "Northgate will commission fewer drama series over the next two years.",
                "Internal",
                Confidence: "Medium"));

        ThesisDetailResponse view = await GetAsync<ThesisDetailResponse>(
            a, $"intelligence/theses/{thesis.Id}");

        // Draft, and the opening position is already revision one.
        Assert.Equal("Draft", view.Thesis.Status);
        Assert.Single(view.Revisions);
        Assert.Equal(1, view.Revisions[0].Sequence);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/theses/{thesis.Id}/activate",
            new ActivateThesisRequest(view.Thesis.Version)));

        ThesisDetailResponse active = await GetAsync<ThesisDetailResponse>(
            a, $"intelligence/theses/{thesis.Id}");

        await PostAsync<IntelligenceIdResponse>(
            a,
            $"intelligence/theses/{thesis.Id}/evidence",
            new LinkThesisEvidenceRequest(
                signal.Id, "Supports", active.Thesis.Version, "Directly on point"));

        ThesisDetailResponse supported = await GetAsync<ThesisDetailResponse>(
            a, $"intelligence/theses/{thesis.Id}");

        // Reported side by side, never netted into one figure.
        Assert.Equal(1, supported.Thesis.SupportingCount);
        Assert.Equal(0, supported.Thesis.ChallengingCount);

        IntelligenceIdResponse prediction = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/predictions",
            new CreatePredictionRequest(
                "Northgate orders three or fewer new dramas this year",
                DateTimeOffset.UtcNow.AddDays(30),
                0.7m,
                "Internal",
                ResolutionCriteria: "Counted from the published slate announcement"));

        PredictionDetailResponse forecast = await GetAsync<PredictionDetailResponse>(
            a, $"intelligence/predictions/{prediction.Id}");

        Assert.Equal("Open", forecast.Prediction.Status);
        Assert.Equal(0.7m, forecast.Prediction.CurrentProbability);
        Assert.Null(forecast.Prediction.BrierScore);
        Assert.NotNull(forecast.Prediction.CurrentProbabilityByDisplayName);

        // A new probability is a revision. The old one stays.
        await PostAsync<IntelligenceIdResponse>(
            a,
            $"intelligence/predictions/{prediction.Id}/revisions",
            new RecordPredictionRevisionRequest(
                0.4m, forecast.Prediction.Version, "The slate announcement slipped"));

        PredictionDetailResponse revised = await GetAsync<PredictionDetailResponse>(
            a, $"intelligence/predictions/{prediction.Id}");

        Assert.Equal(2, revised.Revisions.Count);
        Assert.Equal(0.7m, revised.Revisions[0].Probability);
        Assert.Equal(0.4m, revised.Revisions[1].Probability);
        Assert.Equal(0.4m, revised.Prediction.CurrentProbability);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/predictions/{prediction.Id}/resolve",
            new ResolvePredictionRequest("Yes", revised.Prediction.Version, "Two were ordered")));

        PredictionDetailResponse resolved = await GetAsync<PredictionDetailResponse>(
            a, $"intelligence/predictions/{prediction.Id}");

        Assert.Equal("Resolved", resolved.Prediction.Status);
        Assert.Equal("Yes", resolved.Prediction.Outcome);

        // (0.4 - 1)² = 0.36, computed from the last forecast stated before
        // resolution rather than from the opening one.
        Assert.Equal(0.36m, resolved.Prediction.BrierScore);

        PredictionCalibrationResponse calibration =
            await GetAsync<PredictionCalibrationResponse>(a, "intelligence/predictions/calibration");

        Assert.Equal(1, calibration.ResolvedCount);
        Assert.Equal(1, calibration.YesCount);
        Assert.Equal(0.36m, calibration.MeanBrierScore);
    }

    // ------------------------------------------------------------ provenance

    /// <summary>A claim with no source is refused.</summary>
    [Fact]
    public async Task ASignalWithNoEvidence_IsRefused()
    {
        Actor a = await ActorAsync("m11-no-evidence", AgencyRole.Owner);

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/signals",
            new RecordSignalRequest(
                "Something somebody said",
                "A claim with nothing behind it.",
                "Observation",
                "Internal",
                []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The last source cannot be removed, and the database says so too.
    /// </summary>
    /// <remarks>
    /// Exercised against PostgreSQL directly. The aggregate refuses it as well;
    /// testing only the aggregate would prove that one code path is careful rather
    /// than that the claim can never lose its provenance (§1).
    /// </remarks>
    [Fact]
    public async Task RemovingTheLastSource_IsRefusedByTheDatabase()
    {
        Actor a = await ActorAsync("m11-last-source", AgencyRole.Owner);

        (Guid _, Guid signalId) = await SourceAndSignalAsync(a, "Only source", "Only claim.");

        SignalDetailResponse claim = await GetAsync<SignalDetailResponse>(
            a, $"intelligence/signals/{signalId}");

        Guid evidenceId = claim.Evidence[0].Id;

        // The API refuses it.
        HttpResponseMessage response = await a.Client.DeleteAsync(
            $"{a.Root}/intelligence/signals/{signalId}/evidence/{evidenceId}"
                + $"?expectedVersion={claim.Signal.Version}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And so does the database, for anything that reached it another way.
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(
                "DELETE FROM signal_evidence WHERE id = @id",
                ("id", evidenceId)));

        Assert.Contains("at least one source", failure.MessageText, StringComparison.Ordinal);
    }

    /// <summary>A forecast cannot be edited or deleted once stated.</summary>
    /// <remarks>
    /// The history is what calibration measures. A forecaster who could revise a
    /// number after resolution would score perfectly every time (§10, §14).
    /// </remarks>
    [Fact]
    public async Task AStatedForecast_CannotBeRewritten()
    {
        Actor a = await ActorAsync("m11-forecast-frozen", AgencyRole.Owner);

        IntelligenceIdResponse prediction = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/predictions",
            new CreatePredictionRequest(
                "A question with a date",
                DateTimeOffset.UtcNow.AddDays(14),
                0.25m,
                "Internal"));

        PredictionDetailResponse detail = await GetAsync<PredictionDetailResponse>(
            a, $"intelligence/predictions/{prediction.Id}");

        Guid revisionId = detail.Revisions[0].Id;

        PostgresException updated = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(
                "UPDATE prediction_revisions SET probability = 0.99 WHERE id = @id",
                ("id", revisionId)));

        Assert.Contains("cannot be edited", updated.MessageText, StringComparison.Ordinal);

        PostgresException deleted = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(
                "DELETE FROM prediction_revisions WHERE id = @id",
                ("id", revisionId)));

        Assert.Contains("what somebody stated", deleted.MessageText, StringComparison.Ordinal);
    }

    /// <summary>A stated thesis position cannot be rewritten either.</summary>
    [Fact]
    public async Task AStatedThesisPosition_CannotBeRewritten()
    {
        Actor a = await ActorAsync("m11-thesis-frozen", AgencyRole.Owner);

        IntelligenceIdResponse thesis = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/theses",
            new CreateThesisRequest("A view", "Something the agency believes.", "Internal"));

        ThesisDetailResponse detail = await GetAsync<ThesisDetailResponse>(
            a, $"intelligence/theses/{thesis.Id}");

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(
                "UPDATE thesis_revisions SET proposition = 'Something else' WHERE id = @id",
                ("id", detail.Revisions[0].Id)));

        Assert.Contains("cannot be edited", failure.MessageText, StringComparison.Ordinal);
    }

    /// <summary>A probability outside zero to one never reaches a row.</summary>
    [Fact]
    public async Task AProbabilityOutsideTheRange_IsRefused()
    {
        Actor a = await ActorAsync("m11-probability", AgencyRole.Owner);

        foreach (decimal probability in new[] { -0.1m, 1.5m })
        {
            HttpResponseMessage response = await a.Client.PostAsJsonAsync(
                $"{a.Root}/intelligence/predictions",
                new CreatePredictionRequest(
                    "Out of range",
                    DateTimeOffset.UtcNow.AddDays(7),
                    probability,
                    "Internal"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // --------------------------------------------------------- classification

    /// <summary>
    /// A reader who may not open a classification learns nothing about how much of
    /// it exists.
    /// </summary>
    /// <remarks>
    /// Not from the list, not from the count on a source they may read, and not
    /// from a citation on a thesis they may open. Three separate leaks, each of
    /// which would answer "is there something about this person" on its own (§28).
    /// </remarks>
    [Fact]
    public async Task ElevatedIntelligence_IsInvisibleInEveryProjection()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m11-elevated-owner");
        Actor writer = Actor.For(_fixture, owner);

        SeededActor memberActor = await SeedIntoAsync(owner, AgencyRole.Member, "m11-elevated-member");
        Actor member = Actor.For(_fixture, memberActor, owner);

        PersonDetailResponse person = await CreatePersonAsync(writer, "Ilse Renard");

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            writer,
            "intelligence/sources",
            new RecordSourceRequest(
                "ManualObservation", "What she told me in confidence", "Internal"));

        // One ordinary claim and one that names a confidential informant.
        await PostAsync<IntelligenceIdResponse>(
            writer,
            "intelligence/signals",
            new RecordSignalRequest(
                "Renard is looking",
                "Ilse Renard is taking meetings.",
                "TalentActivity",
                "Internal",
                [new SignalEvidenceRequest(source.Id)],
                [new IntelligenceSubjectRequest("Person", person.Person.Id)]));

        IntelligenceIdResponse hidden = await PostAsync<IntelligenceIdResponse>(
            writer,
            "intelligence/signals",
            new RecordSignalRequest(
                "Who told us",
                "The approach came through somebody who asked not to be named.",
                "Observation",
                "SourceSensitive",
                [new SignalEvidenceRequest(source.Id)],
                [new IntelligenceSubjectRequest("Person", person.Person.Id)]));

        // The writer sees both.
        IReadOnlyList<SignalResponse> all = await GetAsync<SignalResponse[]>(
            writer, $"intelligence/signals?subjectKind=Person&subjectId={person.Person.Id}");

        Assert.Equal(2, all.Count);

        // The member sees one, and nothing tells them a second exists.
        IReadOnlyList<SignalResponse> visible = await GetAsync<SignalResponse[]>(
            member, $"intelligence/signals?subjectKind=Person&subjectId={person.Person.Id}");

        Assert.Single(visible);
        Assert.Equal("Renard is looking", visible[0].Title);

        // Opening it directly is refused rather than answered as missing, so
        // somebody who followed a reference learns a grant exists to ask for.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.Client.GetAsync(
                $"{member.Root}/intelligence/signals/{hidden.Id}")).StatusCode);

        // The source is readable, and its citation count is counted over claims
        // this reader may see. Two would answer the question the list refused.
        IntelligenceSourceResponse asWriter = await GetAsync<IntelligenceSourceResponse>(
            writer, $"intelligence/sources/{source.Id}");

        IntelligenceSourceResponse asMember = await GetAsync<IntelligenceSourceResponse>(
            member, $"intelligence/sources/{source.Id}");

        Assert.Equal(2, asWriter.SignalCount);
        Assert.Equal(1, asMember.SignalCount);

        // A thesis a member may open cannot name the hidden claim in its citations.
        IntelligenceIdResponse thesis = await PostAsync<IntelligenceIdResponse>(
            writer,
            "intelligence/theses",
            new CreateThesisRequest("Renard moves", "Ilse Renard changes agency.", "Internal"));

        ThesisDetailResponse draft = await GetAsync<ThesisDetailResponse>(
            writer, $"intelligence/theses/{thesis.Id}");

        await PostAsync<IntelligenceIdResponse>(
            writer,
            $"intelligence/theses/{thesis.Id}/evidence",
            new LinkThesisEvidenceRequest(hidden.Id, "Supports", draft.Thesis.Version));

        ThesisDetailResponse memberView = await GetAsync<ThesisDetailResponse>(
            member, $"intelligence/theses/{thesis.Id}");

        Assert.Empty(memberView.Evidence);

        // And the supporting count agrees with the rows rather than with the truth
        // the member is not entitled to.
        Assert.Equal(0, memberView.Thesis.SupportingCount);

        ThesisDetailResponse writerView = await GetAsync<ThesisDetailResponse>(
            writer, $"intelligence/theses/{thesis.Id}");

        Assert.Single(writerView.Evidence);
        Assert.Equal(1, writerView.Thesis.SupportingCount);
    }

    /// <summary>
    /// Global search finds Internal claims and stops there.
    /// </summary>
    /// <remarks>
    /// The palette shows results beside people and projects. A source-sensitive
    /// claim surfacing there would be the disclosure the classification exists to
    /// prevent, and so would the result count on its own (§28).
    /// </remarks>
    [Fact]
    public async Task GlobalSearch_FindsInternalClaimsOnly()
    {
        Actor a = await ActorAsync("m11-search", AgencyRole.Owner);

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/sources",
            new RecordSourceRequest("ManualObservation", "Overheard at a screening", "Internal"));

        await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/signals",
            new RecordSignalRequest(
                "Fenwick greenlight",
                "Fenwick has greenlit the adaptation.",
                "ProjectStatus",
                "Internal",
                [new SignalEvidenceRequest(source.Id)]));

        await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/signals",
            new RecordSignalRequest(
                "Fenwick financing",
                "Fenwick is quietly short of money.",
                "CorporateAction",
                "Restricted",
                [new SignalEvidenceRequest(source.Id)]));

        SearchResponse results = await GetAsync<SearchResponse>(
            a, "search?q=Fenwick&types=Signal");

        // Even for the owner, who may read everything on the intelligence surface.
        Assert.Single(results.Hits);
        Assert.Equal("Fenwick greenlight", results.Hits[0].Title);
    }

    // -------------------------------------------------------- tenant safety

    /// <summary>A claim cannot cite another organization's source.</summary>
    [Fact]
    public async Task CitingAnotherTenantsSource_IsRefused()
    {
        Actor a = await ActorAsync("m11-tenant-a", AgencyRole.Owner);
        Actor b = await ActorAsync("m11-tenant-b", AgencyRole.Owner);

        IntelligenceIdResponse theirs = await PostAsync<IntelligenceIdResponse>(
            b,
            "intelligence/sources",
            new RecordSourceRequest("ManualObservation", "Theirs", "Internal"));

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/signals",
            new RecordSignalRequest(
                "Borrowed", "A claim on somebody else's evidence.", "Observation", "Internal",
                [new SignalEvidenceRequest(theirs.Id)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A signal cannot be about another organization's person.</summary>
    [Fact]
    public async Task NamingAnotherTenantsRecord_IsRefused()
    {
        Actor a = await ActorAsync("m11-subject-a", AgencyRole.Owner);
        Actor b = await ActorAsync("m11-subject-b", AgencyRole.Owner);

        PersonDetailResponse theirs = await CreatePersonAsync(b, "Not Yours");

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/sources",
            new RecordSourceRequest("ManualObservation", "Mine", "Internal"));

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/signals",
            new RecordSignalRequest(
                "About somebody else's person",
                "A claim naming a record in another tenant.",
                "Observation",
                "Internal",
                [new SignalEvidenceRequest(source.Id)],
                [new IntelligenceSubjectRequest("Person", theirs.Person.Id)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- radar

    /// <summary>
    /// The radar hands a pursuit to M4 and stops.
    /// </summary>
    /// <remarks>
    /// Courting, signing and representation live in M4. A second pursuit pipeline
    /// here would be two systems disagreeing about the same relationship (§40).
    /// </remarks>
    [Fact]
    public async Task ARadarEntry_ConvertsIntoAProspect()
    {
        Actor a = await ActorAsync("m11-radar", AgencyRole.Owner);

        PersonDetailResponse person = await CreatePersonAsync(a, "Tobias Wren");

        IntelligenceIdResponse entry = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/radar",
            new CreateRadarEntryRequest(
                person.Person.Id,
                "Two shorts at festivals this year and a manager who keeps calling.",
                "Internal",
                IntendedDisciplines: "Writer, director",
                Priority: "High"));

        TalentRadarDetailResponse detail = await GetAsync<TalentRadarDetailResponse>(
            a, $"intelligence/radar/{entry.Id}");

        Assert.Equal("Watching", detail.Entry.Status);
        Assert.Equal("High", detail.Entry.Priority);
        Assert.Equal("Tobias Wren", detail.Entry.PersonDisplayName);
        Assert.Equal(["Writer", "director"], detail.Disciplines);

        // One open entry per person. A second is refused rather than allowed to
        // become a parallel pursuit nobody knows about.
        HttpResponseMessage duplicate = await a.Client.PostAsJsonAsync(
            $"{a.Root}/intelligence/radar",
            new CreateRadarEntryRequest(person.Person.Id, "Again", "Internal"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        RadarConversionResponse conversion = await PostAsync<RadarConversionResponse>(
            a,
            $"intelligence/radar/{entry.Id}/convert",
            new ConvertRadarEntryRequest(detail.Entry.Version, Source: "Festival circuit"));

        Assert.NotEqual(Guid.Empty, conversion.ProspectId);
        Assert.True(conversion.CreatedTalentProfile);

        TalentRadarDetailResponse converted = await GetAsync<TalentRadarDetailResponse>(
            a, $"intelligence/radar/{entry.Id}");

        Assert.Equal("ConvertedToProspect", converted.Entry.Status);
        Assert.Equal(conversion.ProspectId, converted.Entry.ProspectId);
        Assert.NotNull(converted.ConvertedAt);

        // The prospect really exists in M4, under its own permission.
        ProspectResponse prospect = await GetAsync<ProspectResponse>(
            a, $"prospects/{conversion.ProspectId}");

        Assert.Equal(person.Person.Id, prospect.PersonId);
    }

    // ----------------------------------------------------------- saved views

    /// <summary>
    /// A signals view is a version 9 document, and it narrows to whoever runs it.
    /// </summary>
    /// <remarks>
    /// Saved views are private to their owner, so the property is demonstrated with
    /// the same definition saved twice rather than with one view shared. What
    /// matters is that the definition does not carry its author's reach: the same
    /// query returns fewer rows to somebody who may read less, and says nothing
    /// about the difference (§28, §31).
    /// </remarks>
    [Fact]
    public async Task ASignalsView_RunsUnderTheReadersOwnGrants()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m11-view-owner");
        Actor writer = Actor.For(_fixture, owner);

        SeededActor memberActor = await SeedIntoAsync(owner, AgencyRole.Member, "m11-view-member");
        Actor member = Actor.For(_fixture, memberActor, owner);

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            writer,
            "intelligence/sources",
            new RecordSourceRequest("ManualObservation", "Conversation", "Internal"));

        foreach ((string title, string sensitivity) in new[]
        {
            ("Open claim", "Internal"),
            ("Closed claim", "Confidential"),
        })
        {
            await PostAsync<IntelligenceIdResponse>(
                writer,
                "intelligence/signals",
                new RecordSignalRequest(
                    title, "A claim.", "Observation", sensitivity,
                    [new SignalEvidenceRequest(source.Id)]));
        }

        SavedViewDefinitionModel definition = new(
            9,
            "Signals",
            new SavedViewFiltersModel(),
            new SavedViewSortModel("ObservedAt", "Descending"));

        SavedViewResponse theirs = await PostAsync<SavedViewResponse>(
            writer,
            "saved-views",
            new CreateSavedViewRequest("Everything we have heard", definition));

        SavedViewResponse mine = await PostAsync<SavedViewResponse>(
            member,
            "saved-views",
            new CreateSavedViewRequest("Everything we have heard", definition));

        SavedViewResultsResponse asWriter = await GetAsync<SavedViewResultsResponse>(
            writer, $"saved-views/{theirs.Id}/results");

        SavedViewResultsResponse asMember = await GetAsync<SavedViewResultsResponse>(
            member, $"saved-views/{mine.Id}/results");

        Assert.Equal("Signals", asWriter.Target);
        Assert.Equal(2, asWriter.Signals.Count);

        // The same definition, run by somebody who may read less.
        Assert.Single(asMember.Signals);
        Assert.Equal("Open claim", asMember.Signals[0].Title);
    }

    /// <summary>A version 8 document naming a version 9 target is refused.</summary>
    [Fact]
    public async Task ABackdatedIntelligenceView_IsRefused()
    {
        Actor a = await ActorAsync("m11-view-backdated", AgencyRole.Owner);

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/saved-views",
            new CreateSavedViewRequest(
                "Backdated",
                new SavedViewDefinitionModel(8, "Predictions", new SavedViewFiltersModel())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- audit

    /// <summary>Consequential intelligence acts are audited; reads are not.</summary>
    [Fact]
    public async Task RecordingAClaim_IsAudited()
    {
        Actor a = await ActorAsync("m11-audit", AgencyRole.Owner);

        (Guid _, Guid signalId) = await SourceAndSignalAsync(a, "Audit source", "Audit claim.");

        await GetAsync<SignalDetailResponse>(a, $"intelligence/signals/{signalId}");

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        List<AuditEvent> events = await context.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == signalId.ToString())
            .ToListAsync();

        Assert.Contains(events, x => x.Action == AuditAction.SignalRecorded);

        // Reading is not an audited act. An audit trail that grew on every read
        // would be a trail nobody can search when something actually happens.
        Assert.DoesNotContain(events, x => x.Action.Contains("read", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------- helpers

    private sealed record Actor(HttpClient Client, string Root)
    {
        public static Actor For(
            AgencyOsTestFixture fixture,
            SeededActor actor,
            SeededActor? tenantOwner = null) =>
            new(
                fixture.CreateClient(actor.Subject),
                $"/api/v1/organizations/{(tenantOwner ?? actor).Organization.Id.Value}");
    }

    private async Task<Actor> ActorAsync(string label, AgencyRole role = AgencyRole.Member)
    {
        SeededActor actor = await _fixture.SeedActorAsync(role, label);

        return Actor.For(_fixture, actor);
    }

    private async Task<SeededActor> SeedIntoAsync(
        SeededActor tenant,
        AgencyRole role,
        string label)
    {
        string subject = $"{label}-{Guid.NewGuid():N}";

        Domain.Identity.User user = await _fixture
            .SeedUserAsync(subject, $"{label} {role}")
            .ConfigureAwait(false);

        await _fixture
            .SeedMembershipAsync(tenant.Organization.Id, user.Id, role, tenant.User.Id)
            .ConfigureAwait(false);

        return new SeededActor(subject, user, tenant.Organization);
    }

    /// <summary>A source and a claim that cites it, for tests that need both.</summary>
    private async Task<(Guid SourceId, Guid SignalId)> SourceAndSignalAsync(
        Actor actor,
        string sourceTitle,
        string claim)
    {
        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            actor,
            "intelligence/sources",
            new RecordSourceRequest("ManualObservation", sourceTitle, "Internal"));

        IntelligenceIdResponse signal = await PostAsync<IntelligenceIdResponse>(
            actor,
            "intelligence/signals",
            new RecordSignalRequest(
                sourceTitle, claim, "Observation", "Internal",
                [new SignalEvidenceRequest(source.Id)]));

        return (source.Id, signal.Id);
    }

    /// <summary>Runs SQL against the test database, bypassing every application path.</summary>
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(Actor actor, string name)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/people", new CreatePersonRequest(name));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<T> GetAsync<T>(Actor actor, string route)
    {
        using HttpResponseMessage response = await actor.Client.GetAsync($"{actor.Root}/{route}");

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PostAsync<T>(Actor actor, string route, object body)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/{route}", body);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        await EnsureAsync(response);
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
