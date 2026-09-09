using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgencyOS.Contracts.Ai;
using AgencyOS.Contracts.Documents;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration.Security;

/// <summary>
/// When a refusal may admit that something exists, and when it may not.
/// </summary>
/// <remarks>
/// <para>
/// M13 recorded a 403/404 inconsistency between the AI and Intelligence surfaces
/// and left it open. M14's review found the situation is both wider and more
/// deliberate than that: the pattern reaches M10 Documents and M10 Communications
/// as well, and the M10 case carries a written rationale — a document list with the
/// privileged rows silently removed reads as a complete list, and somebody would
/// conclude the contract was never filed (ADR-0025).
/// </para>
/// <para>
/// So there was already a coherent rule. It had simply never been written down,
/// which is why it looked like an accident. ADR-0038 states it; this suite pins it,
/// so a future change to any one surface has to be a decision about the rule rather
/// than a local edit.
/// </para>
/// <para>
/// The four cases are not arbitrary. They differ in what the refusal itself would
/// reveal, and to whom.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ExistenceDisclosureTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ExistenceDisclosureTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Across a tenant boundary, nothing exists.
    /// </summary>
    /// <remarks>
    /// The absolute case. Another organization's identifier must be
    /// indistinguishable from one that names nothing at all, because the person
    /// asking has no standing to learn even that the record is real. A 403 here
    /// would confirm existence to a stranger.
    /// </remarks>
    [Fact]
    public async Task ACrossTenantReferenceIsNotFound()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-disc-mine");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-disc-other");

        using HttpClient mineClient = _fixture.CreateClient(mine.Subject);
        using HttpClient strangerClient = _fixture.CreateClient(stranger.Subject);

        RecordDocumentResponse document = await FileAsync(
            mineClient, mine, "Ordinary filing", "Internal");

        // The stranger asks their own tenant for an identifier belonging to another.
        using HttpResponseMessage response = await strangerClient.GetAsync(
            $"{Root(stranger)}/documents/{document.DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Without the base grant, the surface is forbidden and no record is named.
    /// </summary>
    /// <remarks>
    /// A refusal here says "you may not use this feature". It is about the caller,
    /// not about any record, so it discloses nothing — and answering 404 would be a
    /// lie that sends somebody looking for a missing document.
    /// </remarks>
    [Fact]
    public async Task MissingTheBaseGrantIsForbidden()
    {
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "m14-disc-obs");
        using HttpClient client = _fixture.CreateClient(observer.Subject);

        using HttpResponseMessage response = await client.GetAsync($"{Root(observer)}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Inside the tenant, a classification refuses rather than hides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deliberate case, and the one that looks like the inconsistency. A
    /// colleague who may read documents but not privileged ones is told the
    /// document is beyond their clearance, not that it does not exist.
    /// </para>
    /// <para>
    /// The reasoning is ADR-0025's and it is about being trusted rather than about
    /// hiding: these are business records whose existence a colleague can usually
    /// infer anyway, and a system that answered "no such contract" to somebody who
    /// knows the contract was signed teaches them the record is unreliable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClassifiedRecordInsideTheTenantIsForbiddenNotHidden()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-disc-priv");
        Actor colleague = await SameTenantAsync(owner, AgencyRole.Member, "m14-disc-member");

        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);

        RecordDocumentResponse privileged = await FileAsync(
            ownerClient, owner, "Counsel's advice", "Privileged");

        using HttpResponseMessage response = await colleague.Client.GetAsync(
            $"{colleague.Root}/documents/{privileged.DocumentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Somebody else's AI run does not exist, even to a colleague.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other deliberate case, and it points the opposite way for a reason. A
    /// run is not a shared business record: it is one person's question, and the
    /// fact that a colleague asked something is itself private.
    /// </para>
    /// <para>
    /// An approval identifier is also a bare GUID that arrives in email, so the
    /// route has to answer the same way to a colleague and to somebody following a
    /// link they were sent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SomebodyElsesRunIsNotFound()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-disc-run");
        Actor colleague = await SameTenantAsync(owner, AgencyRole.Member, "m14-disc-run-other");

        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);

        using HttpResponseMessage enabled = await ownerClient.PutAsJsonAsync(
            $"{Root(owner)}/ai/policies/fake",
            new UpdateAiProviderPolicyRequest(true, "Internal", false, ExpectedVersion: 0));

        enabled.EnsureSuccessStatusCode();

        _fixture.Factory.Services.GetRequiredService<FakeModelProvider>().Reset();
        _fixture.Factory.Services.GetRequiredService<FakeModelProvider>()
            .ScriptText("A brief nobody else may read.");

        AgentRunIdResponse run = (await (await ownerClient.PostAsJsonAsync(
            $"{Root(owner)}/ai/runs",
            new StartAgentRunRequest("ResearchCopilot", "What do we know?")))
            .Content.ReadFromJsonAsync<AgentRunIdResponse>())!;

        using HttpResponseMessage response = await colleague.Client.GetAsync(
            $"{colleague.Root}/ai/runs/{run.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------- helpers

    private static string Root(SeededActor actor) =>
        $"/api/v1/organizations/{actor.Organization.Id.Value}";

    private sealed record Actor(HttpClient Client, string Root);

    private async Task<Actor> SameTenantAsync(
        SeededActor tenant, AgencyRole role, string label)
    {
        string subject = $"{label}-{Guid.NewGuid():N}";

        Domain.Identity.User user = await _fixture
            .SeedUserAsync(subject, $"{label} {role}")
            .ConfigureAwait(false);

        await _fixture
            .SeedMembershipAsync(tenant.Organization.Id, user.Id, role, tenant.User.Id)
            .ConfigureAwait(false);

        return new Actor(
            _fixture.CreateClient(subject),
            $"/api/v1/organizations/{tenant.Organization.Id.Value}");
    }

    private static async Task<RecordDocumentResponse> FileAsync(
        HttpClient client, SeededActor actor, string title, string sensitivity)
    {
        using MultipartFormDataContent form = [];

        ByteArrayContent file = new([1, 2, 3, 4]);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "filing.txt");

        form.Add(new StringContent(title), "title");
        form.Add(new StringContent("Other"), "kind");
        form.Add(new StringContent(sensitivity), "sensitivity");

        using HttpResponseMessage response = await client.PostAsync(
            $"{Root(actor)}/documents", form);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RecordDocumentResponse>())!;
    }
}
