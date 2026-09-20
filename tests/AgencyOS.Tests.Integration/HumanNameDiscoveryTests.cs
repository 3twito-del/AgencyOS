using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Finding a person's work by typing their name.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 78 began with one human name and found
/// nothing: searching Deals, Contracts and Projects for the represented client all
/// returned zero. The case was found only because the deal's title string happened
/// to contain her surname.
/// </para>
/// <para>
/// Each list searched only its own text. These cover the relationships the domain
/// genuinely carries — a negotiation's counterparty and the client its pursuit is
/// about, a contract's parties, and the people currently attached to a project —
/// and prove that widening them did not make the lists promiscuous.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class HumanNameDiscoveryTests
{
    private const string ClientSurname = "Hollowbrook-Nakagawa";
    private const string BuyerSurname = "Thorsdottir-Achebe";
    private const string Unrelated = "Pemberton-Vasquez";

    private readonly AgencyOsTestFixture _fixture;

    public HumanNameDiscoveryTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>The client's name reaches the negotiation about them.</summary>
    [Fact]
    public async Task AClientsNameFindsTheirDeal()
    {
        Scene s = await SetUpAsync("name-deal");

        Assert.Contains(await DealsAsync(s, ClientSurname), x => x.Id == s.DealId);
    }

    /// <summary>The counterparty's name reaches the negotiation with them.</summary>
    [Fact]
    public async Task ACounterpartysNameFindsTheDeal()
    {
        Scene s = await SetUpAsync("name-counterparty");

        Assert.Contains(await DealsAsync(s, BuyerSurname), x => x.Id == s.DealId);
    }

    /// <summary>The client's name reaches the paper they signed.</summary>
    [Fact]
    public async Task AClientsNameFindsTheirContract()
    {
        Scene s = await SetUpAsync("name-contract");

        Assert.Contains(await ContractsAsync(s, ClientSurname), x => x.Id == s.ContractId);
    }

    /// <summary>A person attached to a project is found by name.</summary>
    [Fact]
    public async Task AnAttachedPersonsNameFindsTheProject()
    {
        Scene s = await SetUpAsync("name-project");

        Assert.Contains(await ProjectsAsync(s, ClientSurname), x => x.Id == s.ProjectId);
    }

    /// <summary>
    /// Somebody else in the same tenant does not drag the case up with them.
    /// </summary>
    /// <remarks>
    /// The risk of widening a search is that it stops narrowing. This is the guard.
    /// </remarks>
    [Fact]
    public async Task AnUnrelatedPersonMatchesNothing()
    {
        Scene s = await SetUpAsync("name-unrelated");

        Assert.Empty(await DealsAsync(s, Unrelated));
        Assert.Empty(await ContractsAsync(s, Unrelated));
        Assert.Empty(await ProjectsAsync(s, Unrelated));
    }

    /// <summary>A name in another tenant never reaches across.</summary>
    [Fact]
    public async Task ANameCannotReachIntoAnotherTenant()
    {
        Scene mine = await SetUpAsync("name-tenant-a");
        Scene theirs = await SetUpAsync("name-tenant-b");

        Assert.DoesNotContain(await DealsAsync(mine, ClientSurname), x => x.Id == theirs.DealId);
        Assert.DoesNotContain(
            await ContractsAsync(mine, ClientSurname), x => x.Id == theirs.ContractId);
        Assert.DoesNotContain(
            await ProjectsAsync(mine, ClientSurname), x => x.Id == theirs.ProjectId);
    }

    /// <summary>Searching by title still works exactly as it did.</summary>
    [Fact]
    public async Task OrdinaryTitleSearchIsUnchanged()
    {
        Scene s = await SetUpAsync("name-title");

        Assert.Contains(await DealsAsync(s, "Tidewater"), x => x.Id == s.DealId);
        Assert.Contains(await ContractsAsync(s, "Tidewater"), x => x.Id == s.ContractId);
        Assert.Contains(await ProjectsAsync(s, "Tidewater"), x => x.Id == s.ProjectId);
    }

    /// <summary>Case does not matter, as it did not before.</summary>
    [Fact]
    public async Task TheSearchIsCaseInsensitive()
    {
        Scene s = await SetUpAsync("name-case");

        Assert.Contains(
            await DealsAsync(s, ClientSurname.ToUpperInvariant()), x => x.Id == s.DealId);
        Assert.Contains(
            await DealsAsync(s, ClientSurname.ToLowerInvariant()), x => x.Id == s.DealId);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<IReadOnlyList<DealSummaryResponse>> DealsAsync(Scene s, string q) =>
        await ListAsync<DealSummaryResponse>(s, $"deals?search={Uri.EscapeDataString(q)}");

    private static async Task<IReadOnlyList<ContractSummaryResponse>> ContractsAsync(
        Scene s, string q) =>
        await ListAsync<ContractSummaryResponse>(s, $"contracts?search={Uri.EscapeDataString(q)}");

    private static async Task<IReadOnlyList<ProjectSummaryResponse>> ProjectsAsync(
        Scene s, string q) =>
        await ListAsync<ProjectSummaryResponse>(s, $"projects?search={Uri.EscapeDataString(q)}");

    private static async Task<IReadOnlyList<T>> ListAsync<T>(Scene s, string path)
    {
        using HttpResponseMessage response = await s.Client.GetAsync($"{s.Root}/{path}");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"'{path}' answered {(int)response.StatusCode}.");

        return (await response.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private async Task<Scene> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        PersonDetailResponse client_ = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Marisol", ClientSurname)));

        PersonDetailResponse buyer = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Ingeborg", BuyerSurname)));

        // Present in the tenant and connected to nothing.
        await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"{root}/people", new CreatePersonRequest("Casimir", Unrelated)));

        TalentDetailResponse talent = await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent", new CreateTalentProfileRequest(client_.Person.Id)));

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/projects",
                new CreateProjectRequest("Tidewater Road", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest("Ninebark Pictures", Type: "Studio")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "Tidewater Road to market", "TalentEngagement", actor.User.Id.Value,
                    Subjects:
                    [
                        new OpportunitySubjectRequest("TalentProfile", talent.Talent.Id, "Primary"),
                        new OpportunitySubjectRequest("Project", project.Project.Id, "Context"),
                    ])));

        Guid opportunityId = opportunity.Opportunity.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        OpportunityDetailResponse active = await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{opportunityId}");

        // The counterparty is a person, so the counterparty-name path is exercised.
        Guid targetId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/targets",
            new AddOpportunityTargetRequest(
                active.Opportunity.Version, PersonId: buyer.Person.Id)));

        foreach (string stage in (string[])["Approved", "Contacted", "Engaged", "Interested"])
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                client, $"{root}/opportunity-targets/{targetId}");

            await NoContentAsync(client.PostAsJsonAsync(
                $"{root}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(stage, target.Version)));
        }

        DealDetailResponse deal = await CreatedAsync<DealDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/deals",
                new CreateDealRequest(
                    opportunityId, targetId, "Tidewater Road engagement",
                    "TalentEmployment", actor.User.Id.Value)));

        RecordOfferResponse offer = await CreatedAsync<RecordOfferResponse>(
            client.PostAsJsonAsync(
                $"{root}/deals/{deal.Deal.Id}/offers",
                new RecordOfferRequest(
                    "Inbound",
                    [
                        new OfferTermRequest(
                            "Fee", new TermValueRequest("Money", Amount: 90_000m, Currency: "USD")),
                    ],
                    deal.Deal.Version)));

        OfferResponse recorded = await GetAsync<OfferResponse>(
            client, $"{root}/offers/{offer.OfferId}");

        using (HttpResponseMessage answered = await client.PostAsJsonAsync(
            $"{root}/offers/{offer.OfferId}/answer",
            new AnswerOfferRequest("Accept", recorded.Version)))
        {
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        }

        ContractDetailResponse contract = await CreatedAsync<ContractDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/contracts",
                new CreateContractRequest(
                    deal.Deal.Id, offer.OfferId, "Tidewater Road engagement — long form",
                    "LongForm", actor.User.Id.Value)));

        // The client signs, so the contract is reachable by her name through parties.
        using (HttpResponseMessage party = await client.PostAsJsonAsync(
            $"{root}/contracts/{contract.Contract.Id}/parties",
            new AddContractPartyRequest(
                "Artist", contract.Contract.Version, PersonId: client_.Person.Id)))
        {
            Assert.Equal(HttpStatusCode.OK, party.StatusCode);
        }

        // And she is attached to the project, which is the project model's own
        // relationship to a person.
        ProjectDetailResponse current = await GetAsync<ProjectDetailResponse>(
            client, $"{root}/projects/{project.Project.Id}");

        Guid roleId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/projects/{project.Project.Id}/roles",
            new CreateProjectRoleRequest("Director", current.Project.Version, Label: "Director")));

        current = await GetAsync<ProjectDetailResponse>(
            client, $"{root}/projects/{project.Project.Id}");

        using (HttpResponseMessage attached = await client.PostAsJsonAsync(
            $"{root}/projects/{project.Project.Id}/roles/{roleId}/attachments",
            new AttachToRoleRequest(
                "Attached",
                DateOnly.FromDateTime(DateTime.UtcNow.Date),
                current.Project.Version,
                PersonId: client_.Person.Id)))
        {
            Assert.True(
                attached.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Attaching answered {(int)attached.StatusCode}: "
                    + await attached.Content.ReadAsStringAsync());
        }

        return new Scene(
            client, root, deal.Deal.Id, contract.Contract.Id, project.Project.Id);
    }

    private sealed record Scene(
        HttpClient Client, string Root, Guid DealId, Guid ContractId, Guid ProjectId);

    private sealed record CreatedId(Guid Id);

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Expected a created id, got {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

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
}
