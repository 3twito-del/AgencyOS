using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That what a call was about reaches the person it was with.
/// </summary>
/// <remarks>
/// <para>
/// The build-80 retest could read a call's summary on the person and never learn
/// what the call was about: detailed notes were rendered only on the org-wide
/// Command Center. The repair moved the interaction template into <c>App.xaml</c>
/// so the Talent page renders notes too, and the tests that guarded it read
/// markup — which proves a template exists and a list is bound, and not that the
/// notes are in what the list is bound to (F-07).
/// </para>
/// <para>
/// This is the half markup cannot show. The Talent page binds
/// <c>overview.RecentInteractions</c> from <c>GET /talent/{personId}/overview</c>;
/// these record contact with one person and read that projection for them and for
/// somebody else. The notes must arrive with the person the contact was with, and
/// with nobody else.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class PersonContactReachTests
{
    private const string Notes =
        "She wants the Marin lead, not the ensemble part, and will not travel in March.";

    private readonly AgencyOsTestFixture _fixture;

    public PersonContactReachTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The person's own overview carries the detailed notes of contact with them.
    /// </summary>
    [Fact]
    public async Task APersonsOverviewCarriesTheDetailedNotesOfContactWithThem()
    {
        (HttpClient client, string root, Guid person, _, Guid interaction) =
            await SetUpAsync("f07-notes");

        ClientOverviewResponse overview = await GetAsync<ClientOverviewResponse>(
            client, $"{root}/talent/{person}/overview");

        InteractionResponse contact = Assert.Single(
            overview.RecentInteractions, x => x.Id == interaction);

        Assert.Equal(Notes, contact.DetailedNotes);
    }

    /// <summary>Somebody else's overview does not carry them.</summary>
    /// <remarks>
    /// The negative that makes the positive mean something: a projection that
    /// returned every recent interaction in the tenant would pass the test above.
    /// </remarks>
    [Fact]
    public async Task AnotherPersonsOverviewDoesNotCarryThem()
    {
        (HttpClient client, string root, _, Guid bystander, Guid interaction) =
            await SetUpAsync("f07-notes-negative");

        ClientOverviewResponse overview = await GetAsync<ClientOverviewResponse>(
            client, $"{root}/talent/{bystander}/overview");

        Assert.DoesNotContain(overview.RecentInteractions, x => x.Id == interaction);
    }

    /// <summary>
    /// Two people with talent profiles, and a call with the first of them.
    /// </summary>
    private async Task<(HttpClient Client, string Root, Guid Person, Guid Bystander, Guid Interaction)>
        SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        Guid person = await TalentAsync(client, root, "Ravensworth", "Adeyemi-Lindqvist");
        Guid bystander = await TalentAsync(client, root, "Thessaly", "Vane");

        RecordInteractionResponse recorded = await CreatedAsync<RecordInteractionResponse>(
            client.PostAsJsonAsync(
                $"{root}/interactions",
                new RecordInteractionRequest(
                    "Call",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    "Spoke about the Marin lead",
                    [new InteractionParticipantRequest(new PartyRefRequest("Person", person), "Talent")],
                    Notes)));

        return (client, root, person, bystander, recorded.InteractionId);
    }

    private static async Task<Guid> TalentAsync(
        HttpClient client, string root, string given, string family)
    {
        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync($"{root}/people", new CreatePersonRequest(given, family)));

        await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent",
                new CreateTalentProfileRequest(
                    person.Person.Id, "Established", Disciplines: ["Actor"])));

        return person.Person.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
