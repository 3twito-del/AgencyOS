using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// What has to be true before a contract can be signed, and before it can owe money.
/// </summary>
/// <remarks>
/// <para>
/// Two invariants the operational-alpha evaluation demonstrated were missing on
/// ALPHA 0.1.0 (<c>d8a8bc56</c>).
/// </para>
/// <para>
/// <strong>Signatures need paper.</strong> A contract with no recorded version took
/// signatures, derived <c>PartiallyExecuted</c> and then <c>Executed</c> from them,
/// and was thereafter unable to accept a version at all — leaving an executed
/// instrument with signatures and nothing signed, permanently unable to carry its
/// own money.
/// </para>
/// <para>
/// <strong>An effective date is not execution.</strong> A draft carrying a version
/// and an effective date could raise a collectible obligation, so money attached to
/// paper nobody had signed. An effective date says from when terms apply; only
/// execution says there is operative paper to collect under (ADR-0040).
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ContractOperativenessTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private readonly AgencyOsTestFixture _fixture;

    public ContractOperativenessTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------- A. signatures need paper

    /// <summary>A contract with no version cannot be signed.</summary>
    [Fact]
    public async Task AContractWithNoVersionRefusesASignature()
    {
        Scene s = await SetUpAsync("operative-nopaper");

        await ApproveForSignatureAsync(s);

        using HttpResponseMessage response = await SignAsync(s, s.ArtistPartyId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("no recorded version", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The refusal happens before anything changes.</summary>
    /// <remarks>
    /// The point of the invariant is that the instrument is left alone, not merely
    /// that the call returns 400: a signature recorded and then rejected would still
    /// have moved the contract towards execution.
    /// </remarks>
    [Fact]
    public async Task ARefusedSignatureChangesNothing()
    {
        Scene s = await SetUpAsync("operative-nopaper-state");

        await ApproveForSignatureAsync(s);

        ContractDetailResponse before = await ContractAsync(s);

        using HttpResponseMessage refused = await SignAsync(s, s.ArtistPartyId);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        ContractDetailResponse after = await ContractAsync(s);

        Assert.Equal(before.Contract.Status, after.Contract.Status);
        Assert.Equal(before.Contract.Version, after.Contract.Version);
        Assert.All(after.Parties, party => Assert.Null(party.SignedOn));
    }

    /// <summary>The whole lawful path, in order.</summary>
    [Fact]
    public async Task TheVersionedExecutionLifecycleSucceeds()
    {
        Scene s = await SetUpAsync("operative-lifecycle");

        await RecordVersionAsync(s);
        await ApproveForSignatureAsync(s);

        await SignOkAsync(s, s.ArtistPartyId);
        Assert.Equal("PartiallyExecuted", (await ContractAsync(s)).Contract.Status);

        await SignOkAsync(s, s.StudioPartyId);
        Assert.Equal("Executed", (await ContractAsync(s)).Contract.Status);
    }

    // ------------------------------ B. an effective date is not legal operativeness

    /// <summary>A draft with a version and an effective date owes nothing yet.</summary>
    [Fact]
    public async Task ADraftWithAnEffectiveDateCannotOweMoney()
    {
        Scene s = await SetUpAsync("operative-effective-only");

        Guid versionId = await RecordVersionAsync(s);
        await SetEffectiveDateAsync(s, Today.AddDays(-10));

        Assert.Equal("Draft", (await ContractAsync(s)).Contract.Status);

        using HttpResponseMessage response = await RecordObligationAsync(s, versionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("not executed", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An executed contract with a version owes money, and can be collected.</summary>
    [Fact]
    public async Task AnExecutedContractCanOweMoneyAndRaiseAReceivable()
    {
        Scene s = await SetUpAsync("operative-executed");

        Guid versionId = await ExecuteAsync(s);

        using HttpResponseMessage obligation = await RecordObligationAsync(s, versionId);

        Assert.Equal(HttpStatusCode.OK, obligation.StatusCode);

        Guid obligationId =
            (await obligation.Content.ReadFromJsonAsync<RecordMonetaryObligationResponse>())!
                .ObligationId;

        using HttpResponseMessage receivable = await s.Client.PostAsJsonAsync(
            $"{s.Root}/monetary-obligations/{obligationId}/receivables",
            new RaiseReceivableRequest(
                "Client",
                new MoneyRequest(450_000m, "USD"),
                Today.AddDays(120),
                ClientPersonId: s.ArtistPersonId,
                Reference: "OPERATIVE-001"));

        Assert.Equal(HttpStatusCode.OK, receivable.StatusCode);
    }

    /// <summary>
    /// An effective date that precedes execution survives it, and does not block it.
    /// </summary>
    /// <remarks>
    /// The invariant narrows what makes money collectible; it does not remove
    /// retroactive effectiveness, which is ordinary in this industry.
    /// </remarks>
    [Fact]
    public async Task AnEffectiveDateBeforeExecutionIsPreserved()
    {
        Scene s = await SetUpAsync("operative-retroactive");

        DateOnly effective = Today.AddDays(-60);

        Guid versionId = await RecordVersionAsync(s);
        await SetEffectiveDateAsync(s, effective);
        await ApproveForSignatureAsync(s);

        foreach (Guid party in (Guid[])[s.ArtistPartyId, s.StudioPartyId])
        {
            await SignOkAsync(s, party);
        }

        ContractDetailResponse executed = await ContractAsync(s);

        Assert.Equal("Executed", executed.Contract.Status);
        Assert.Equal(effective, executed.Contract.EffectiveOn);

        Assert.Equal(
            HttpStatusCode.OK, (await RecordObligationAsync(s, versionId)).StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Guid> ExecuteAsync(Scene s)
    {
        Guid versionId = await RecordVersionAsync(s);

        await ApproveForSignatureAsync(s);

        foreach (Guid party in (Guid[])[s.ArtistPartyId, s.StudioPartyId])
        {
            await SignOkAsync(s, party);
        }

        return versionId;
    }

    private async Task<Guid> RecordVersionAsync(Scene s)
    {
        ContractDetailResponse current = await ContractAsync(s);

        RecordContractVersionResponse version =
            await CreatedAsync<RecordContractVersionResponse>(s.Client.PostAsJsonAsync(
                $"{s.Root}/contracts/{s.ContractId}/versions",
                new RecordContractVersionRequest(
                    "Execution copy", "Inbound", current.Contract.Version)));

        return version.VersionId;
    }

    private async Task ApproveForSignatureAsync(Scene s)
    {
        foreach (string step in (string[])["SentForReview", "ApprovedForSignature"])
        {
            ContractDetailResponse current = await ContractAsync(s);

            await NoContentAsync(s.Client.PostAsJsonAsync(
                $"{s.Root}/contracts/{s.ContractId}/status",
                new ChangeContractStatusRequest(step, current.Contract.Version)));
        }
    }

    private async Task SetEffectiveDateAsync(Scene s, DateOnly on)
    {
        ContractDetailResponse current = await ContractAsync(s);

        await NoContentAsync(s.Client.PostAsJsonAsync(
            $"{s.Root}/contracts/{s.ContractId}/effective-date",
            new RecordEffectiveDateRequest(on, current.Contract.Version)));
    }

    /// <summary>Signs, and says why if the server refuses.</summary>
    private async Task SignOkAsync(Scene s, Guid partyId)
    {
        using HttpResponseMessage response = await SignAsync(s, partyId);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Signing was refused with {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SignAsync(Scene s, Guid partyId)
    {
        ContractDetailResponse current = await ContractAsync(s);

        return await s.Client.PostAsJsonAsync(
            $"{s.Root}/contracts/{s.ContractId}/signatures",
            new RecordSignatureRequest(partyId, Today, "Wet", current.Contract.Version));
    }

    private Task<HttpResponseMessage> RecordObligationAsync(Scene s, Guid versionId) =>
        s.Client.PostAsJsonAsync(
            $"{s.Root}/contracts/{s.ContractId}/monetary-obligations",
            new RecordMonetaryObligationRequest(
                versionId,
                s.StudioPartyId,
                s.ArtistPartyId,
                "Compensation",
                "Fixed",
                new DueRuleRequest("Absolute", Today.AddDays(120)),
                new MoneyRequest(450_000m, "USD"),
                Description: "Lead fee."));

    private Task<ContractDetailResponse> ContractAsync(Scene s) =>
        GetAsync<ContractDetailResponse>(s.Client, $"{s.Root}/contracts/{s.ContractId}");

    private async Task<Scene> SetUpAsync(string label)
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

        PersonDetailResponse artist = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Tobias", "Ferreira-Nakamura")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Subjects:
                    [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        Guid opportunityId = opportunity.Opportunity.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        OpportunityDetailResponse active = await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{opportunityId}");

        Guid targetId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/targets",
            new AddOpportunityTargetRequest(
                active.Opportunity.Version, CompanyId: studio.Company.Id)));

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
                    opportunityId, targetId, "The Undertow to Northgate",
                    "ProjectSale", actor.User.Id.Value)));

        RecordOfferResponse offer = await CreatedAsync<RecordOfferResponse>(
            client.PostAsJsonAsync(
                $"{root}/deals/{deal.Deal.Id}/offers",
                new RecordOfferRequest(
                    "Inbound",
                    [
                        new OfferTermRequest(
                            "Fee", new TermValueRequest("Money", Amount: 450_000m, Currency: "USD")),
                    ],
                    deal.Deal.Version)));

        OfferResponse recorded = await GetAsync<OfferResponse>(
            client, $"{root}/offers/{offer.OfferId}");

        await OkAsync(client.PostAsJsonAsync(
            $"{root}/offers/{offer.OfferId}/answer",
            new AnswerOfferRequest("Accept", recorded.Version)));

        ContractDetailResponse contract = await CreatedAsync<ContractDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/contracts",
                new CreateContractRequest(
                    deal.Deal.Id, offer.OfferId, "The Undertow — Northgate",
                    "LongForm", actor.User.Id.Value)));

        Guid studioParty = await AddPartyAsync(
            client, root, contract.Contract.Id,
            new AddContractPartyRequest(
                "Producer", contract.Contract.Version, CompanyId: studio.Company.Id));

        contract = await GetAsync<ContractDetailResponse>(
            client, $"{root}/contracts/{contract.Contract.Id}");

        Guid artistParty = await AddPartyAsync(
            client, root, contract.Contract.Id,
            new AddContractPartyRequest(
                "Artist", contract.Contract.Version, PersonId: artist.Person.Id));

        return new Scene(
            client, root, contract.Contract.Id, studioParty, artistParty, artist.Person.Id);
    }

    private static async Task<Guid> AddPartyAsync(
        HttpClient client, string root, Guid contractId, AddContractPartyRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{root}/contracts/{contractId}/parties", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AddContractPartyResponse>())!
            .ContractPartyId;
    }

    private sealed record Scene(
        HttpClient Client,
        string Root,
        Guid ContractId,
        Guid StudioPartyId,
        Guid ArtistPartyId,
        Guid ArtistPersonId);

    private sealed record CreatedId(Guid Id);

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

    private static async Task OkAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
