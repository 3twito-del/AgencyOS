using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M5 project and packaging workflow, end to end against PostgreSQL.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class ProjectTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ProjectTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole workflow, in the order an agency performs it.
    /// </summary>
    /// <remarks>
    /// One test on purpose: the value of M5 is that these steps connect. Separate
    /// tests would each pass while the joins between them stayed broken.
    /// </remarks>
    [Fact]
    public async Task Workflow_FromProjectToPackage()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-workflow");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        // A project starts life at Concept, Active.
        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest(
                "The Undertow",
                "FeatureFilm",
                WorkingTitle: "Deep Water",
                Logline: "A salvage diver finds something that should have stayed lost.",
                Year: 2028)));

        Assert.Equal("Active", project.Project.Status);
        Assert.Equal("Concept", project.Project.Stage);

        Guid projectId = project.Project.Id;

        // It derives from a novel. The record is descriptive: it claims nothing
        // about who owns anything.
        SourcePropertyResponse source = await CreatedAsync<SourcePropertyResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/source-properties",
            new CreateSourcePropertyRequest(
                "The Undertow",
                "Book",
                AttributedCreator: "Ines Aguirre",
                SourceReference: "978-0-00-000000-0",
                Year: 2024)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/source-properties",
            new LinkSourcePropertyRequest(source.Id, project.Project.Version)));

        // The work moves on.
        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/stage",
            new ChangeProjectStageRequest("Development", project.Project.Version)));

        // A director's job exists before anybody fills it. That is the whole point
        // of a role being separate from its occupant.
        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Guid directorRole = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Director", project.Project.Version, IsExclusive: true)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal(1, project.Project.OpenRoleCount);
        Assert.Equal("Open", Assert.Single(project.Roles).Status);

        // Somebody attaches to it.
        PersonDetailResponse director = await CreatePersonAsync(client, tenant, "Ada", "Reyes");

        Guid attachment = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{directorRole}/attachments",
            new AttachToRoleRequest(
                "Attached",
                new DateOnly(2026, 3, 1),
                project.Project.Version,
                PersonId: director.Person.Id)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal(0, project.Project.OpenRoleCount);
        Assert.Equal(1, project.Project.AttachedCount);

        ProjectRoleResponse role = Assert.Single(project.Roles);

        Assert.Equal("Filled", role.Status);
        Assert.Equal("Ada Reyes", Assert.Single(role.Attachments).DisplayName);

        // A studio is involved, but not as a role anybody fills.
        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/companies",
            new AddProjectCompanyRequest(
                studio.Company.Id,
                "Studio",
                new DateOnly(2026, 4, 1),
                project.Project.Version)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal("Northgate Pictures", Assert.Single(project.Companies).CompanyName);

        // A package assembles what exists and what is wanted, kept apart.
        PackageDetailResponse package = await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(
                projectId,
                "The Undertow - lead package",
                actor.User.Id.Value,
                Thesis: "Ada plus a name in the lead.",
                StrategyNotes: "Northgate first; they owe us one.")));

        Assert.Equal("Draft", package.Package.Status);

        Guid packageId = package.Package.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{packageId}/status",
            new ChangePackageStatusRequest("Assembling", package.Package.Version)));

        package = await GetAsync<PackageDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/packages/{packageId}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{packageId}/elements",
            new AddPackageElementRequest("AttachedParty", attachment, package.Package.Version)));

        PersonDetailResponse wanted = await CreatePersonAsync(client, tenant, "Bo", "Ferreira");

        package = await GetAsync<PackageDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/packages/{packageId}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{packageId}/elements",
            new AddPackageElementRequest(
                "ProposedPerson",
                wanted.Person.Id,
                package.Package.Version,
                Note: "Would carry the international sale.")));

        package = await GetAsync<PackageDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/packages/{packageId}");

        Assert.Equal(2, package.Elements.Count);

        // The distinction the whole model exists to preserve: one of these is a
        // fact about the project, and one is a hope.
        Assert.Single(package.Elements, x => x.IsAttached);
        Assert.Single(package.Elements, x => !x.IsAttached);

        // The history says what happened, in order, and is not the audit trail.
        ProjectHistoryEntryResponse[] history = await GetAsync<ProjectHistoryEntryResponse[]>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}/history");

        Assert.Contains(history, x => x.Kind == "Created");
        Assert.Contains(history, x => x.Kind == "StageChanged");
        Assert.Contains(history, x => x.Kind == "Attachment");
        Assert.Contains(history, x => x.Kind == "Package");
    }

    /// <summary>
    /// Two clients filling the same exclusive role: exactly one succeeds.
    /// </summary>
    /// <remarks>
    /// The handler checks, the domain refuses, and a partial unique index refuses.
    /// This proves the last of those holds when the first two are raced past, which
    /// is the only proof that matters: two directors both believing they are
    /// attached is not a state worth being merely unlikely.
    /// </remarks>
    [Fact]
    public async Task ConcurrentAttachments_FillAnExclusiveRoleExactlyOnce()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-exclusive");
        using HttpClient setup = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(setup.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("Salt Road", "TelevisionSeries")));

        Guid projectId = project.Project.Id;

        Guid role = await CreatedIdAsync(setup.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Showrunner", project.Project.Version, IsExclusive: true)));

        project = await GetAsync<ProjectDetailResponse>(
            setup, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        // Eight different people, all racing for the same singular job.
        List<Guid> candidates = [];

        for (int index = 0; index < 8; index++)
        {
            PersonDetailResponse person = await CreatePersonAsync(
                setup, tenant, "Candidate", $"Number{index}");

            candidates.Add(person.Person.Id);
        }

        int version = project.Project.Version;

        List<Task<HttpResponseMessage>> attempts = [];
        List<HttpClient> clients = [];

        foreach (Guid candidate in candidates)
        {
            HttpClient client = _fixture.CreateClient(actor.Subject);
            clients.Add(client);

            attempts.Add(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
                new AttachToRoleRequest(
                    "Attached",
                    new DateOnly(2026, 5, 1),
                    version,
                    PersonId: candidate)));
        }

        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        int created = responses.Count(x => x.StatusCode == HttpStatusCode.Created);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }

        Assert.Equal(1, created);

        // And the database agrees: one holder, not eight.
        AttachmentResponse[] attachments = await GetAsync<AttachmentResponse[]>(
            setup, $"/api/v1/organizations/{tenant}/projects/{projectId}/attachments?currentOnly=true");

        Assert.Single(attachments, x => x.HoldsTheRole);
    }

    /// <summary>A non-exclusive role takes as many people as the work needs.</summary>
    [Fact]
    public async Task ANonExclusiveRole_TakesSeveralParties()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-shared-role");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("Salt Road", "TelevisionSeries")));

        Guid projectId = project.Project.Id;

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Producer", project.Project.Version)));

        for (int index = 0; index < 3; index++)
        {
            PersonDetailResponse producer = await CreatePersonAsync(
                client, tenant, "Producer", $"Number{index}");

            project = await GetAsync<ProjectDetailResponse>(
                client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

            await CreatedIdAsync(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
                new AttachToRoleRequest(
                    "Attached",
                    new DateOnly(2026, 5, 1),
                    project.Project.Version,
                    PersonId: producer.Person.Id)));
        }

        AttachmentResponse[] attachments = await GetAsync<AttachmentResponse[]>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}/attachments?currentOnly=true");

        Assert.Equal(3, attachments.Length);
    }

    /// <summary>
    /// A role reopens when its last holder leaves.
    /// </summary>
    /// <remarks>
    /// Occupancy is a consequence of the attachments that exist, not a field
    /// somebody sets. A role left marked filled after its holder withdrew would
    /// make every "missing a director" view wrong.
    /// </remarks>
    [Fact]
    public async Task WhenTheHolderWithdraws_TheRoleIsOpenAgain()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-reopen");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        Guid projectId = project.Project.Id;

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Director", project.Project.Version, IsExclusive: true)));

        PersonDetailResponse director = await CreatePersonAsync(client, tenant, "Ada", "Reyes");

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Guid attachment = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
            new AttachToRoleRequest(
                "Attached",
                new DateOnly(2026, 3, 1),
                project.Project.Version,
                PersonId: director.Person.Id)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal("Filled", Assert.Single(project.Roles).Status);

        AttachmentResponse held = Assert.Single(
            await GetAsync<AttachmentResponse[]>(
                client, $"/api/v1/organizations/{tenant}/projects/{projectId}/attachments"));

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/attachments/{attachment}/status",
            new ChangeAttachmentRequest(
                "Withdrawn", new DateOnly(2026, 8, 1), held.Version, "Took another film")));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        ProjectRoleResponse reopened = Assert.Single(project.Roles);

        Assert.Equal("Open", reopened.Status);
        Assert.Equal(1, project.Project.OpenRoleCount);

        // Nothing was erased: the attachment is still there, ended.
        AttachmentResponse past = Assert.Single(reopened.Attachments);

        Assert.Equal("Withdrawn", past.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), past.EndsOn);
        Assert.False(past.HoldsTheRole);
    }

    /// <summary>The exclusive-role invariant surfaces as an explanation, not a constraint error.</summary>
    [Fact]
    public async Task ASecondHolderOfAnExclusiveRole_IsRefusedWithAReason()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-second-holder");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        Guid projectId = project.Project.Id;

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Director", project.Project.Version, IsExclusive: true)));

        PersonDetailResponse first = await CreatePersonAsync(client, tenant, "Ada", "Reyes");
        PersonDetailResponse second = await CreatePersonAsync(client, tenant, "Bo", "Ferreira");

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
            new AttachToRoleRequest(
                "Attached", new DateOnly(2026, 3, 1), project.Project.Version, PersonId: first.Person.Id)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
            new AttachToRoleRequest(
                "Attached", new DateOnly(2026, 4, 1), project.Project.Version, PersonId: second.Person.Id));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        string body = await refused.Content.ReadAsStringAsync();

        Assert.Contains("exclusive", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Several people may be in discussion for the same exclusive role.
    /// </summary>
    /// <remarks>
    /// Being in talks is not occupancy. Treating it as such would block a
    /// legitimate record and make the role look filled when nobody has committed.
    /// </remarks>
    [Fact]
    public async Task SeveralPeople_CanBeInDiscussionForOneExclusiveRole()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-in-discussion");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        Guid projectId = project.Project.Id;

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/roles",
            new CreateProjectRoleRequest("Director", project.Project.Version, IsExclusive: true)));

        for (int index = 0; index < 3; index++)
        {
            PersonDetailResponse candidate = await CreatePersonAsync(
                client, tenant, "Candidate", $"Number{index}");

            project = await GetAsync<ProjectDetailResponse>(
                client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

            await CreatedIdAsync(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects/{projectId}/roles/{role}/attachments",
                new AttachToRoleRequest(
                    "InDiscussion",
                    new DateOnly(2026, 2, 1),
                    project.Project.Version,
                    PersonId: candidate.Person.Id)));
        }

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        // Three conversations, and the role is still unfilled.
        Assert.Equal(3, Assert.Single(project.Roles).Attachments.Count);
        Assert.Equal("Open", Assert.Single(project.Roles).Status);
        Assert.Equal(1, project.Project.OpenRoleCount);
    }

    /// <summary>A cancelled project keeps the stage it reached.</summary>
    [Fact]
    public async Task ACancelledProject_KeepsHowFarItGot()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-cancelled");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm", Stage: "PreProduction")));

        Guid projectId = project.Project.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/status",
            new ChangeProjectStatusRequest("Cancelled", project.Project.Version, "Financing collapsed")));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal("Cancelled", project.Project.Status);
        Assert.Equal("PreProduction", project.Project.Stage);

        // The stage stops moving, so the record still says where it stopped.
        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/stage",
            new ChangeProjectStageRequest("Production", project.Project.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // And it can come back, because cancelled projects do.
        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/status",
            new ChangeProjectStatusRequest("Active", project.Project.Version, "Revived")));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal("Active", project.Project.Status);
        Assert.Equal("PreProduction", project.Project.Stage);
    }

    /// <summary>A stale stage change is refused rather than applied over newer state.</summary>
    [Fact]
    public async Task AStaleStageChange_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-stale");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        Guid projectId = project.Project.Id;
        int stale = project.Project.Version;

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/stage",
            new ChangeProjectStageRequest("Development", stale)));

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/stage",
            new ChangeProjectStageRequest("Packaging", stale));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    /// <summary>A replayed create makes one project, not two.</summary>
    [Fact]
    public async Task AReplayedCreate_MakesOneProject()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-idempotent");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        string key = Guid.NewGuid().ToString("N");

        ProjectDetailResponse first = await CreateWithKeyAsync(client, tenant, key);
        ProjectDetailResponse second = await CreateWithKeyAsync(client, tenant, key);

        Assert.Equal(first.Project.Id, second.Project.Id);

        ProjectSummaryResponse[] projects = await GetAsync<ProjectSummaryResponse[]>(
            client, $"/api/v1/organizations/{tenant}/projects");

        Assert.Single(projects);
    }

    /// <summary>
    /// Adding the same element twice leaves one element.
    /// </summary>
    /// <remarks>
    /// The domain returns the existing element and a partial unique index catches
    /// two requests that race past that check. Either way the package does not end
    /// up listing the same person twice.
    /// </remarks>
    [Fact]
    public async Task AddingTheSamePackageElementTwice_LeavesOne()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-element-twice");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        PackageDetailResponse package = await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Lead package", actor.User.Id.Value)));

        PersonDetailResponse wanted = await CreatePersonAsync(client, tenant, "Bo", "Ferreira");

        Guid packageId = package.Package.Id;

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{packageId}/elements",
            new AddPackageElementRequest("ProposedPerson", wanted.Person.Id, package.Package.Version)));

        package = await GetAsync<PackageDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/packages/{packageId}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{packageId}/elements",
            new AddPackageElementRequest("ProposedPerson", wanted.Person.Id, package.Package.Version)));

        package = await GetAsync<PackageDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/packages/{packageId}");

        Assert.Single(package.Elements);
    }

    /// <summary>
    /// A package element cannot point at another tenant's record.
    /// </summary>
    /// <remarks>
    /// Elements are raw identifiers interpreted by kind, so without validation a
    /// package could reference a stranger by pasting in a GUID - and it would
    /// render, because rendering only needs the id.
    /// </remarks>
    [Fact]
    public async Task APackageElement_CannotReachIntoAnotherTenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-element-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-element-theirs");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);
        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);

        Guid myTenant = mine.Organization.Id.Value;
        Guid theirTenant = theirs.Organization.Id.Value;

        PersonDetailResponse stranger = await CreatePersonAsync(theirClient, theirTenant, "Not", "Mine");

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        PackageDetailResponse package = await CreatedAsync<PackageDetailResponse>(myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Lead package", mine.User.Id.Value)));

        using HttpResponseMessage refused = await myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/packages/{package.Package.Id}/elements",
            new AddPackageElementRequest(
                "ProposedPerson", stranger.Person.Id, package.Package.Version));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>A project cannot be read from another tenant.</summary>
    [Fact]
    public async Task Projects_NeverCrossATenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-tenant-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-tenant-theirs");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);
        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{mine.Organization.Id.Value}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        using HttpResponseMessage refused = await theirClient.GetAsync(
            $"/api/v1/organizations/{mine.Organization.Id.Value}/projects/{project.Project.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// Closing the M4 credit seam: a credit links to a project, or to nothing.
    /// </summary>
    /// <remarks>
    /// Most historical credits describe work the agency had nothing to do with and
    /// will never have a project record, so the column stays nullable. Linking is
    /// always explicit - a credit is never matched to a project because the titles
    /// look alike.
    /// </remarks>
    [Fact]
    public async Task ACredit_CanBeLinkedToAProjectAndUnlinked()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-credit-link");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Ada", "Reyes");

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(person.Person.Id, "Established", Disciplines: ["Director"])));

        Guid creditId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/credits",
            new AddCreditRequest(person.Person.Id, "The Undertow", "Directing")));

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CreditResponse[] credits = await GetAsync<CreditResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}/credits");

        CreditResponse credit = Assert.Single(credits);

        Assert.Null(credit.ProjectId);

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/credits/{creditId}/project",
            new LinkCreditToProjectRequest(project.Project.Id, credit.Version)));

        credits = await GetAsync<CreditResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}/credits");

        credit = Assert.Single(credits);

        Assert.Equal(project.Project.Id, credit.ProjectId);

        // Unlinking is the same command with no project, and the recorded title is
        // left alone: what a credit said is a fact in its own right.
        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/credits/{creditId}/project",
            new LinkCreditToProjectRequest(null, credit.Version)));

        credits = await GetAsync<CreditResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}/credits");

        credit = Assert.Single(credits);

        Assert.Null(credit.ProjectId);
        Assert.Equal("The Undertow", credit.Title);
    }

    /// <summary>A credit cannot be linked to another tenant's project.</summary>
    [Fact]
    public async Task ACredit_CannotLinkAcrossTenants()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-credit-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-credit-theirs");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);
        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);

        Guid myTenant = mine.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(myClient, myTenant, "Ada", "Reyes");

        await CreatedAsync<TalentDetailResponse>(myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/talent",
            new CreateTalentProfileRequest(person.Person.Id, "Established", Disciplines: ["Director"])));

        Guid creditId = await CreatedIdAsync(myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/credits",
            new AddCreditRequest(person.Person.Id, "The Undertow", "Directing")));

        ProjectDetailResponse theirProject = await CreatedAsync<ProjectDetailResponse>(
            theirClient.PostAsJsonAsync(
                $"/api/v1/organizations/{theirs.Organization.Id.Value}/projects",
                new CreateProjectRequest("Their Film", "FeatureFilm")));

        CreditResponse credit = Assert.Single(await GetAsync<CreditResponse[]>(
            myClient, $"/api/v1/organizations/{myTenant}/talent/{person.Person.Id}/credits"));

        using HttpResponseMessage refused = await myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{myTenant}/credits/{creditId}/project",
            new LinkCreditToProjectRequest(theirProject.Project.Id, credit.Version));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>A material serves a project without ceasing to be the talent's.</summary>
    [Fact]
    public async Task AMaterial_CanServeAProjectAndStillBelongToItsAuthor()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-material-link");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse writer = await CreatePersonAsync(client, tenant, "Priya", "Raghunathan");

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(writer.Person.Id, "Established", Disciplines: ["Writer"])));

        Guid materialId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/materials",
            new AddMaterialRequest(writer.Person.Id, "The Undertow - draft 4", "Screenplay")));

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        Guid projectId = project.Project.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/materials",
            new LinkProjectMaterialRequest(materialId, project.Project.Version)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        ProjectMaterialResponse linked = Assert.Single(project.Materials);

        Assert.Equal("The Undertow - draft 4", linked.Title);
        Assert.Equal(writer.Person.Id, linked.PersonId);

        // Still the writer's material: the link did not move it.
        MaterialResponse[] theirs = await GetAsync<MaterialResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{writer.Person.Id}/materials");

        Assert.Single(theirs);
    }

    /// <summary>The same material can serve two projects.</summary>
    [Fact]
    public async Task OneMaterial_CanServeTwoProjects()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-material-shared");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse writer = await CreatePersonAsync(client, tenant, "Priya", "Raghunathan");

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(writer.Person.Id, "Established", Disciplines: ["Writer"])));

        Guid materialId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/materials",
            new AddMaterialRequest(writer.Person.Id, "Slate lookbook", "Lookbook")));

        foreach (string title in new[] { "The Undertow", "Salt Road" })
        {
            ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects",
                new CreateProjectRequest(title, "FeatureFilm")));

            await NoContentAsync(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects/{project.Project.Id}/materials",
                new LinkProjectMaterialRequest(materialId, project.Project.Version)));

            ProjectDetailResponse reread = await GetAsync<ProjectDetailResponse>(
                client, $"/api/v1/organizations/{tenant}/projects/{project.Project.Id}");

            Assert.Single(reread.Materials);
        }
    }

    /// <summary>One source property can spawn two projects.</summary>
    [Fact]
    public async Task OneSourceProperty_CanSpawnTwoProjects()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-source-shared");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        SourcePropertyResponse source = await CreatedAsync<SourcePropertyResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/source-properties",
            new CreateSourcePropertyRequest("The Undertow", "Book", AttributedCreator: "Ines Aguirre")));

        foreach (string title in new[] { "The Undertow", "The Undertow: Series" })
        {
            ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects",
                new CreateProjectRequest(title, "FeatureFilm")));

            await NoContentAsync(client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/projects/{project.Project.Id}/source-properties",
                new LinkSourcePropertyRequest(source.Id, project.Project.Version)));
        }

        SourcePropertyResponse reread = await GetAsync<SourcePropertyResponse>(
            client, $"/api/v1/organizations/{tenant}/source-properties/{source.Id}");

        Assert.Equal(2, reread.ProjectCount);
    }

    /// <summary>Company involvement is not duplicated in the same capacity.</summary>
    [Fact]
    public async Task TheSameCompany_CannotBeRecordedTwiceInOneCapacity()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-company-twice");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        Guid projectId = project.Project.Id;

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/companies",
            new AddProjectCompanyRequest(
                studio.Company.Id, "Studio", new DateOnly(2026, 1, 1), project.Project.Version)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/companies",
            new AddProjectCompanyRequest(
                studio.Company.Id, "Studio", new DateOnly(2026, 2, 1), project.Project.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // A different capacity for the same company is a different fact, and allowed.
        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{projectId}/companies",
            new AddProjectCompanyRequest(
                studio.Company.Id, "Distributor", new DateOnly(2026, 2, 1), project.Project.Version)));

        project = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{projectId}");

        Assert.Equal(2, project.Companies.Count);
    }

    /// <summary>Projects, source properties and packages are findable.</summary>
    [Fact]
    public async Task Search_FindsTheSlate()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-search");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("Undertow", "FeatureFilm")));

        await CreatedAsync<SourcePropertyResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/source-properties",
            new CreateSourcePropertyRequest("Undertow", "Book", AttributedCreator: "Ines Aguirre")));

        await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Undertow lead package", actor.User.Id.Value)));

        SearchResponse results = await GetAsync<SearchResponse>(
            client, $"/api/v1/organizations/{tenant}/search?q=undertow");

        Assert.Contains(results.Hits, x => x.Type == "Project");
        Assert.Contains(results.Hits, x => x.Type == "SourceProperty");
        Assert.Contains(results.Hits, x => x.Type == "Package");
    }

    /// <summary>
    /// A package's strategy is not findable through search.
    /// </summary>
    /// <remarks>
    /// The redaction would be pointless if a caller could confirm what a note says
    /// by searching for a phrase and watching the package surface, so strategy is
    /// deliberately absent from the package search vector (ADR-0019).
    /// </remarks>
    [Fact]
    public async Task PackageStrategy_IsNotSearchable()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-search-strategy");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("Salt Road", "FeatureFilm")));

        await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(
                project.Project.Id,
                "Salt Road package",
                actor.User.Id.Value,
                StrategyNotes: "Northgate will almost certainly pass; keep them last.")));

        SearchResponse results = await GetAsync<SearchResponse>(
            client, $"/api/v1/organizations/{tenant}/search?q=northgate");

        Assert.DoesNotContain(results.Hits, x => x.Type == "Package");
    }

    /// <summary>The project command centre reports real operational signals.</summary>
    [Fact]
    public async Task TheCommandCentre_ReportsWhatIsMoving()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-command-centre");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest("The Undertow", "FeatureFilm")));

        PackageDetailResponse package = await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Lead package", actor.User.Id.Value)));

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{package.Package.Id}/status",
            new ChangePackageStatusRequest("Assembling", package.Package.Version)));

        ProjectCommandCenterResponse centre = await GetAsync<ProjectCommandCenterResponse>(
            client, $"/api/v1/organizations/{tenant}/project-command-center");

        Assert.Equal(1, centre.ActiveProjectCount);
        Assert.Equal(1, centre.PackagesInProgressCount);
        Assert.Single(centre.RecentlyChanged);
        Assert.Single(centre.Packages);
    }

    /// <summary>Every M5 read is refused without authentication.</summary>
    [Fact]
    public async Task TheSlate_RequiresAuthentication()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-anon");

        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private static async Task<ProjectDetailResponse> CreateWithKeyAsync(
        HttpClient client,
        Guid tenant,
        string key)
    {
        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"/api/v1/organizations/{tenant}/projects")
        {
            Content = JsonContent.Create(new CreateProjectRequest("The Undertow", "FeatureFilm")),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ProjectDetailResponse>())!;
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        HttpClient client,
        Guid tenant,
        string firstName,
        string lastName)
    {
        return await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(firstName, lastName)));
    }

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedId? created = await response.Content.ReadFromJsonAsync<CreatedId>();

        return created!.Id;
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

        return;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record CreatedId(Guid Id);
}
