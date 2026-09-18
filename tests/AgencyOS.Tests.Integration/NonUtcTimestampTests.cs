using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Api.Http;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That a timestamp written in the caller's own offset means the instant it
/// denotes, and can be saved.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-001</c>. The contract types these fields as
/// <c>DateTimeOffset</c>, so every offset is a legal way to write an instant —
/// but any offset other than zero travelled untouched to Npgsql, which refused
/// it, and the operator got a <c>500</c>. Recording a pitch or a submission was
/// impossible from anywhere but UTC.
/// </para>
/// <para>
/// The values here are fixed and carry explicit offsets, so these tests mean the
/// same thing on a runner in London as on the machine in Tel Aviv where the
/// defect was found. Nothing asserts the host's own time zone.
/// </para>
/// </remarks>
public sealed class NonUtcTimestampSerializationTests
{
    private static readonly JsonSerializerOptions Options = Configured();

    /// <summary>The same moment, written six ways.</summary>
    /// <remarks>
    /// Tel Aviv and Los Angeles disagree about what to call 2026-09-18T00:13:39Z,
    /// and both are right. The instant is what the domain stores.
    /// </remarks>
    [Theory]
    [InlineData("2026-09-18T00:13:39.1646065Z")]
    [InlineData("2026-09-18T00:13:39.1646065+00:00")]
    [InlineData("2026-09-18T03:13:39.1646065+03:00")]
    [InlineData("2026-09-17T17:13:39.1646065-07:00")]
    [InlineData("2026-09-18T14:13:39.1646065+14:00")]
    [InlineData("2026-09-17T12:13:39.1646065-12:00")]
    public void EveryOffsetMeansTheSameInstant(string written)
    {
        DateTimeOffset read = Read(written);

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-18T00:13:39.1646065Z", CultureInfo.InvariantCulture),
            read);

        Assert.Equal(TimeSpan.Zero, read.Offset);
    }

    /// <summary>
    /// A timestamp just after local midnight keeps its instant, which is the
    /// previous day in UTC — and renders as the day the operator chose.
    /// </summary>
    /// <remarks>
    /// The half hour either side of midnight is where a careless repair does its
    /// damage. These fields are instants, so the assertion is that the instant
    /// survives; the calendar date an operator sees is the instant rendered in
    /// their own zone, and it is still the 18th.
    /// </remarks>
    [Fact]
    public void JustAfterLocalMidnightKeepsTheInstantAndTheOperatorsDate()
    {
        DateTimeOffset read = Read("2026-09-18T00:30:00+03:00");

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-17T21:30:00Z", CultureInfo.InvariantCulture), read);

        Assert.Equal(TimeSpan.Zero, read.Offset);
        Assert.Equal(17, read.UtcDateTime.Day);
        Assert.Equal(18, read.ToOffset(TimeSpan.FromHours(3)).Day);
    }

    /// <summary>
    /// And just before local midnight west of Greenwich, where the UTC date runs
    /// ahead instead of behind.
    /// </summary>
    [Fact]
    public void JustBeforeLocalMidnightWestKeepsTheInstantAndTheOperatorsDate()
    {
        DateTimeOffset read = Read("2026-09-17T23:30:00-05:00");

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-18T04:30:00Z", CultureInfo.InvariantCulture), read);

        Assert.Equal(TimeSpan.Zero, read.Offset);
        Assert.Equal(18, read.UtcDateTime.Day);
        Assert.Equal(17, read.ToOffset(TimeSpan.FromHours(-5)).Day);
    }

    /// <summary>
    /// An hour that happens twice stays two different instants.
    /// </summary>
    /// <remarks>
    /// Israel leaves daylight time on 2026-10-25, so 01:30 occurs at +03:00 and
    /// again an hour later at +02:00. The offset in the text says which one, and
    /// the converter must not consult a time zone to decide.
    /// </remarks>
    [Fact]
    public void AnAmbiguousLocalHourStaysTwoInstants()
    {
        DateTimeOffset daylight = Read("2026-10-25T01:30:00+03:00");
        DateTimeOffset standard = Read("2026-10-25T01:30:00+02:00");

        Assert.Equal(
            DateTimeOffset.Parse("2026-10-24T22:30:00Z", CultureInfo.InvariantCulture), daylight);

        Assert.Equal(
            DateTimeOffset.Parse("2026-10-24T23:30:00Z", CultureInfo.InvariantCulture), standard);

        Assert.Equal(TimeSpan.FromHours(1), standard - daylight);
        Assert.Equal(TimeSpan.Zero, daylight.Offset);
        Assert.Equal(TimeSpan.Zero, standard.Offset);
    }

    /// <summary>
    /// A local hour that never happens is still a legible instant.
    /// </summary>
    /// <remarks>
    /// Israel enters daylight time on 2026-03-27, so 02:30 local does not exist
    /// that night. Written with an offset it denotes a real moment, and is read as
    /// one rather than rejected or shifted.
    /// </remarks>
    [Fact]
    public void ALocalHourThatNeverHappensIsStillAnInstant() =>
        Assert.Equal(
            DateTimeOffset.Parse("2026-03-27T00:30:00Z", CultureInfo.InvariantCulture),
            Read("2026-03-27T02:30:00+02:00"));

    /// <summary>What comes back out means what went in.</summary>
    [Fact]
    public void TheInstantSurvivesARoundTrip()
    {
        DateTimeOffset original = DateTimeOffset.Parse(
            "2026-09-18T03:13:39.1646065+03:00", CultureInfo.InvariantCulture);

        string json = JsonSerializer.Serialize(original, Options);

        Assert.Equal(original, JsonSerializer.Deserialize<DateTimeOffset>(json, Options));
        Assert.Equal(original.ToUniversalTime(), JsonSerializer.Deserialize<DateTimeOffset>(json, Options));
    }

    /// <summary>A missing timestamp stays missing.</summary>
    /// <remarks>
    /// Both fields are optional; the server fills them with its own clock. A
    /// converter that turned null into an instant would invent a fact.
    /// </remarks>
    [Fact]
    public void AnAbsentTimestampIsStillAbsent()
    {
        RecordSubmissionRequest request = JsonSerializer.Deserialize<RecordSubmissionRequest>(
            """{"channel":"Email","expectedVersion":1}""", Options)!;

        Assert.Null(request.SentAt);
        Assert.Null(request.ResponseExpectedBy);
    }

    /// <summary>A timestamp that is not one is refused, not guessed at.</summary>
    [Theory]
    [InlineData("\"not a timestamp\"")]
    [InlineData("\"2026-13-01T00:00:00Z\"")]
    [InlineData("1758153600")]
    public void AMalformedTimestampIsRefused(string json) =>
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<DateTimeOffset>(json, Options));

    /// <summary>A calendar date is not an instant and is not converted.</summary>
    /// <remarks>
    /// "Reply expected by" is a business date: a <c>DateOnly</c> against a
    /// <c>date</c> column. Running it through a time zone is the mistake this wave
    /// exists to avoid, so the converter must not touch it.
    /// </remarks>
    [Fact]
    public void ACalendarDateIsLeftAlone()
    {
        RecordSubmissionRequest request = JsonSerializer.Deserialize<RecordSubmissionRequest>(
            """{"channel":"Email","expectedVersion":1,"sentAt":"2026-09-18T00:30:00+03:00","responseExpectedBy":"2026-10-02"}""",
            Options)!;

        Assert.Equal(new DateOnly(2026, 10, 2), request.ResponseExpectedBy);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-17T21:30:00Z", CultureInfo.InvariantCulture),
            request.SentAt);
    }

    /// <summary>The whole request, as the Windows client writes it.</summary>
    [Fact]
    public void ThePitchRequestTheClientSendsIsReadAsAnInstant()
    {
        RecordPitchRequest request = JsonSerializer.Deserialize<RecordPitchRequest>(
            """
            {"interactionType":"Meeting","summary":"Pitched the feature",
             "participants":[],"kind":"Formal","outcome":"NoDecision","expectedVersion":3,
             "occurredAt":"2026-09-18T03:13:39.1646065+03:00",
             "followUp":{"title":"Chase","dueAt":"2026-09-25T03:13:39+03:00"}}
            """,
            Options)!;

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-18T00:13:39.1646065Z", CultureInfo.InvariantCulture),
            request.OccurredAt);

        Assert.Equal(TimeSpan.Zero, request.FollowUp!.DueAt!.Value.Offset);
    }

    private static DateTimeOffset Read(string written) =>
        JsonSerializer.Deserialize<DateTimeOffset>(
            JsonSerializer.Serialize(written, Options), Options);

    /// <summary>The API host's own serializer options, converter included.</summary>
    private static JsonSerializerOptions Configured()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        options.Converters.Add(new UtcInstantConverter());

        return options;
    }
}

/// <summary>
/// That the two workflows <c>AOS-R002-001</c> blocked can be saved from a machine
/// that is not on UTC.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class NonUtcTimestampWorkflowTests
{
    private readonly AgencyOsTestFixture _fixture;

    public NonUtcTimestampWorkflowTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>A pitch recorded from east and west of Greenwich.</summary>
    /// <remarks>
    /// Before the repair both of these were <c>500</c>. The instant read back is
    /// the one that was sent, whichever way it was written.
    /// </remarks>
    [Theory]
    [InlineData("2026-09-18T03:13:39+03:00")]
    [InlineData("2026-09-17T17:13:39-07:00")]
    [InlineData("2026-09-18T00:13:39Z")]
    public async Task APitchIsRecordedFromAnyOffset(string occurredAt)
    {
        Fixture f = await SetUpAsync("r002-001-pitch");
        Guid targetId = await ApproachedTargetAsync(f);

        DateTimeOffset sent = DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/pitches",
            new RecordPitchRequest(
                "Meeting",
                "Pitched the feature",
                [new PitchParticipantRequest("Company", f.StudioId, "Buyer")],
                "Formal",
                "NoDecision",
                await VersionAsync(f, targetId),
                OccurredAt: sent));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        OpportunityDetailResponse detail = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Assert.Equal(sent, Assert.Single(detail.Pitches).OccurredAt);
    }

    /// <summary>A submission recorded the same way, with its calendar date intact.</summary>
    [Theory]
    [InlineData("2026-09-18T03:14:06+03:00")]
    [InlineData("2026-09-17T17:14:06-07:00")]
    public async Task ASubmissionIsRecordedFromAnyOffset(string sentAt)
    {
        Fixture f = await SetUpAsync("r002-001-submission");
        Guid targetId = await ApproachedTargetAsync(f);

        DateTimeOffset sent = DateTimeOffset.Parse(sentAt, CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/submissions",
            new RecordSubmissionRequest(
                "Email",
                await VersionAsync(f, targetId),
                SentAt: sent,
                Subject: "The Undertow",
                ResponseExpectedBy: new DateOnly(2026, 10, 2),
                FollowUp: new OpportunityFollowUpRequest("Chase for a response", sent.AddDays(14))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        OpportunityDetailResponse detail = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        SubmissionResponse submission = Assert.Single(detail.Submissions);

        Assert.Equal(sent, submission.SentAt);
        Assert.Equal(new DateOnly(2026, 10, 2), submission.ResponseExpectedBy);
    }

    /// <summary>
    /// The same instant written two ways, under one idempotency key, is one
    /// command.
    /// </summary>
    /// <remarks>
    /// The fingerprint is taken from the bound request, so normalizing on the way
    /// in is what makes a retry that reformats its timestamp a retry rather than a
    /// conflict.
    /// </remarks>
    [Fact]
    public async Task ARetryThatRewritesTheOffsetIsStillTheSameCommand()
    {
        Fixture f = await SetUpAsync("r002-001-idempotent");
        Guid targetId = await ApproachedTargetAsync(f);

        int version = await VersionAsync(f, targetId);
        string key = Guid.CreateVersion7().ToString("N");

        RecordSubmissionRequest first = new(
            "Email", version, SentAt: DateTimeOffset.Parse(
                "2026-09-18T03:14:06+03:00", CultureInfo.InvariantCulture));

        RecordSubmissionRequest retry = first with
        {
            SentAt = DateTimeOffset.Parse("2026-09-18T00:14:06Z", CultureInfo.InvariantCulture),
        };

        using HttpResponseMessage one = await Send(f, targetId, first, key);
        using HttpResponseMessage two = await Send(f, targetId, retry, key);

        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);

        OpportunityDetailResponse detail = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Assert.Single(detail.Submissions);
    }

    /// <summary>
    /// The host itself normalizes, not just the test's copy of the options.
    /// </summary>
    /// <remarks>
    /// The whole repair is one registration. Dropping it would put every endpoint
    /// that takes a timestamp back where <c>AOS-R002-001</c> found them, and every
    /// other test here would still pass, because they exercise the converter rather
    /// than the host's configuration of it.
    /// </remarks>
    [Fact]
    public void TheHostReadsEveryIncomingInstantAsUtc()
    {
        JsonSerializerOptions options = _fixture.Factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-18T00:13:39Z", CultureInfo.InvariantCulture),
            JsonSerializer.Deserialize<DateTimeOffset>(
                "\"2026-09-18T03:13:39+03:00\"", options));

        Assert.Equal(
            TimeSpan.Zero,
            JsonSerializer.Deserialize<DateTimeOffset>(
                "\"2026-09-18T03:13:39+03:00\"", options).Offset);
    }

    /// <summary>A timestamp that is not one is refused as a bad request.</summary>
    /// <remarks>
    /// The repair must not turn a malformed value into a <c>500</c> either.
    /// </remarks>
    [Fact]
    public async Task AMalformedTimestampIsRefusedAsABadRequest()
    {
        Fixture f = await SetUpAsync("r002-001-malformed");
        Guid targetId = await ApproachedTargetAsync(f);

        using StringContent body = new(
            $$"""
            {"channel":"Email","expectedVersion":{{await VersionAsync(f, targetId)}},
             "sentAt":"the day before yesterday"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await f.Client.PostAsync(
            $"{f.Root}/opportunity-targets/{targetId}/submissions", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> Send(
        Fixture f, Guid targetId, RecordSubmissionRequest request, string key)
    {
        HttpRequestMessage message = new(
            HttpMethod.Post, $"{f.Root}/opportunity-targets/{targetId}/submissions")
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.TryAddWithoutValidation(
            AgencyOS.Contracts.ClientHeaders.IdempotencyKey, key);

        return f.Client.SendAsync(message);
    }

    private static async Task<int> VersionAsync(Fixture f, Guid targetId)
    {
        OpportunityDetailResponse detail = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        return detail.Targets.Single(x => x.Id == targetId).Version;
    }

    /// <summary>A pursuit with one target walked far enough to accept activity.</summary>
    private static async Task<Guid> ApproachedTargetAsync(Fixture f)
    {
        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Guid targetId = await CreatedIdAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                opportunity.Opportunity.Version, CompanyId: f.StudioId)));

        foreach (string stage in new[] { "Approved", "Contacted" })
        {
            OpportunityTargetResponse target = (await GetAsync<OpportunityDetailResponse>(
                f.Client, $"{f.Root}/opportunities/{f.OpportunityId}"))
                .Targets.Single(x => x.Id == targetId);

            await NoContentAsync(f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(stage, target.Version)));
        }

        return targetId;
    }

    private async Task<Fixture> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/projects", new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Subjects: [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        return new Fixture(client, root, studio.Company.Id, opportunity.Opportunity.Id);
    }

    private sealed record Fixture(HttpClient Client, string Root, Guid StudioId, Guid OpportunityId);

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record CreatedId(Guid Id);
}
