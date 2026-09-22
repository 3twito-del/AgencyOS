using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Representations;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Two surfaces, one person, one answer about whether the agency represents them.
/// </summary>
/// <remarks>
/// <para>
/// F-03. The opportunity subject projection labelled every talent subject
/// <c>"Client"</c>, unconditionally, from the subject's <em>kind</em>. It joins
/// <c>TalentProfiles</c> for the name and never reads <c>Representations</c> at
/// all, so the word was not a fact about the person: it was a constant. A person
/// with a talent profile and no representation read as <c>Client</c> on Pipeline
/// while Talent read <c>Not represented</c>, and the product had no way to tell an
/// operator which screen was lying.
/// </para>
/// <para>
/// <c>TalentProfile</c> states the rule it broke: being a client is a consequence
/// of holding an active representation, derived and never stored, because a second
/// source of truth drifts the moment somebody terminates a representation without
/// remembering to clear the flag. A hardcoded literal is that second source at its
/// worst - it cannot even drift, because it was never right except by coincidence.
/// </para>
/// <para>
/// These run the real projection against PostgreSQL in all three representation
/// states. The subject line is asserted to state the <em>kind</em> of record it
/// points at, as every sibling branch does, and never a representation state.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class IdentitySemanticsTests
{
    private readonly AgencyOsTestFixture _fixture;

    public IdentitySemanticsTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Pipeline and Talent never disagree about whether somebody is a client.
    /// </summary>
    /// <remarks>
    /// The three states a talent subject can be in. Before this, the first and the
    /// third both read "Client" on Pipeline while Talent said otherwise, and only
    /// the second was accidentally true.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("Active")]
    [InlineData("Terminated")]
    public async Task PipelineAndTalentAgreeAboutRepresentation(string? status)
    {
        Fixture f = await SetUpAsync($"f03-{status ?? "none"}", status);

        OpportunitySubjectResponse subject = Assert.Single(
            (await GetAsync<OpportunityDetailResponse>(
                f.Client, $"{f.Root}/opportunities/{f.OpportunityId}")).Subjects);

        ClientOverviewResponse overview = await GetAsync<ClientOverviewResponse>(
            f.Client, $"{f.Root}/talent/{f.PersonId}/overview");

        TalentSummaryResponse talent = overview.Talent.Talent;

        // The authoritative answer, from the aggregate that owns the question.
        Assert.Equal(status, talent.RepresentationStatus);
        Assert.Equal(status == "Active", talent.IsClient);

        // The subject line states what kind of record it points at, which is true
        // in all three states and is what the field is documented to carry.
        Assert.Equal("TalentProfile", subject.Kind);
        Assert.Equal("Talent", subject.Detail);

        // The contradiction itself: no surface may call them a client while the
        // surface that owns the fact says they are not one.
        if (!talent.IsClient)
        {
            Assert.NotEqual("Client", subject.Detail);
        }
    }

    /// <summary>
    /// The subject line never carries a representation state, in any state.
    /// </summary>
    /// <remarks>
    /// Enumerated from the domain rather than from a list written here, so a
    /// status added later is covered without anybody remembering to add it. A
    /// type or category label must not be able to masquerade as current state.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("Active")]
    [InlineData("Terminated")]
    public async Task TheSubjectLineNeverCarriesARepresentationState(string? status)
    {
        Fixture f = await SetUpAsync($"f03-vocab-{status ?? "none"}", status);

        OpportunitySubjectResponse subject = Assert.Single(
            (await GetAsync<OpportunityDetailResponse>(
                f.Client, $"{f.Root}/opportunities/{f.OpportunityId}")).Subjects);

        foreach (RepresentationStatus state in Enum.GetValues<RepresentationStatus>())
        {
            Assert.NotEqual(state.ToString(), subject.Detail);
        }

        Assert.NotEqual("Client", subject.Detail);
    }

    /// <summary>
    /// A person the agency does represent is still described by kind here.
    /// </summary>
    /// <remarks>
    /// The repair is not "say Talent when they are not a client". The slot is a
    /// kind slot - its siblings emit Project, Package and Role - so it says the
    /// kind whatever the relationship is doing, and the surface that owns
    /// representation keeps saying that. Pinned so a later change does not restore
    /// the coincidence by making the line conditional.
    /// </remarks>
    [Fact]
    public async Task ARepresentedPersonIsStillDescribedByKind()
    {
        Fixture f = await SetUpAsync("f03-client", "Active");

        OpportunitySubjectResponse subject = Assert.Single(
            (await GetAsync<OpportunityDetailResponse>(
                f.Client, $"{f.Root}/opportunities/{f.OpportunityId}")).Subjects);

        ClientOverviewResponse overview = await GetAsync<ClientOverviewResponse>(
            f.Client, $"{f.Root}/talent/{f.PersonId}/overview");

        Assert.True(overview.Talent.Talent.IsClient);
        Assert.Equal("Talent", subject.Detail);
    }

    // ------------------------------------------------------------- plumbing

    private sealed record Fixture(
        HttpClient Client,
        string Root,
        Guid PersonId,
        Guid OpportunityId);

    private async Task<Fixture> SetUpAsync(string label, string? status)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Ravensworth", "Adeyemi-Lindqvist")));

        TalentDetailResponse talent = await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent",
                new CreateTalentProfileRequest(
                    person.Person.Id, "Established", Disciplines: ["Writer"])));

        if (status is not null)
        {
            RepresentationResponse representation =
                await CreatedAsync<RepresentationResponse>(client.PostAsJsonAsync(
                    $"{root}/representations",
                    new CreateRepresentationRequest(
                        person.Person.Id,
                        new DateOnly(2026, 1, 1),
                        actor.User.Id.Value,
                        ["Acting"])));

            // Terminated is reached through Active, because that is the shape of
            // the relationship the transition table describes.
            int version = await TransitionAsync(
                client, root, representation.Id, "Active", representation.Version);

            if (status == "Terminated")
            {
                await TransitionAsync(client, root, representation.Id, "Terminated", version);
            }
        }

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "Autumn staffing round",
                    "TalentEngagement",
                    actor.User.Id.Value,
                    Subjects:
                    [
                        new OpportunitySubjectRequest(
                            "TalentProfile", talent.Talent.Id, "Primary"),
                    ])));

        return new Fixture(client, root, person.Person.Id, opportunity.Opportunity.Id);
    }

    private static async Task<int> TransitionAsync(
        HttpClient client, string root, Guid representation, string status, int version)
    {
        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/representations/{representation}/transition",
            new TransitionRepresentationRequest(
                status, new DateOnly(2026, 1, 1), version)));

        return (await GetAsync<RepresentationResponse>(
            client, $"{root}/representations/{representation}")).Version;
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
}
