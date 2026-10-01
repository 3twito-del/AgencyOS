using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Releases;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using AgencyOS.Contracts.Sync;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// An in-memory API, so view models can be tested without a server.
/// </summary>
/// <remarks>
/// The client is a real seam rather than a mock-shaped one: everything the Windows
/// UI does goes through <see cref="IAgencyOsApi"/>, which is why substituting it
/// exercises the actual workflow.
/// </remarks>
internal sealed partial class FakeAgencyOsApi : IAgencyOsApi
{
    public List<PersonSummaryResponse> People { get; } = [];

    public List<CompanySummaryResponse> Companies { get; } = [];

    public CommandCenterResponse CommandCenter { get; set; } =
        new([], [], [], [], 0, 0, 0);

    public RecordInteractionRequest? LastInteraction { get; private set; }

    public Exception? NextFailure { get; set; }

    public int CompletedTasks { get; private set; }

    /// <summary>Search results the fake returns, in the order given.</summary>
    public List<SearchHit> SearchHits { get; } = [];

    public List<SavedViewResponse> SavedViews { get; } = [];

    /// <summary>Change-feed pages the fake serves, one per call.</summary>
    public Queue<SyncChangesResponse> SyncPages { get; } = [];

    /// <summary>Idempotency keys presented on every mutating call, in order.</summary>
    public List<string?> IdempotencyKeys { get; } = [];

    /// <summary>Failures queued per call, so a retry can be made to behave differently.</summary>
    public Queue<Exception> Failures { get; } = [];

    /// <summary>Mutating calls that actually reached the fake, by idempotency key.</summary>
    public Dictionary<string, int> Effects { get; } = [];

    // ---- Talent and representation (M4) ----

    public List<TalentSummaryResponse> Talent { get; } = [];

    public List<ProspectResponse> Prospects { get; } = [];

    public List<CreditResponse> Credits { get; } = [];

    public List<MaterialResponse> Materials { get; } = [];

    /// <summary>The overview the fake returns, when a test sets one.</summary>
    public ClientOverviewResponse? Overview { get; set; }

    /// <summary>Filters the last talent list call was made with, so a test can assert them.</summary>
    public (bool ClientsOnly, bool FormerOnly, string? Discipline, string? Search) LastTalentFilter
    { get; private set; }

    /// <summary>Representations the fake has created, keyed by prospect.</summary>
    public Dictionary<Guid, RepresentationResponse> Conversions { get; } = [];

    public Task<HandshakeResponse> HandshakeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PersonSummaryResponse>> ListPeopleAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<PersonSummaryResponse> matches = string.IsNullOrWhiteSpace(search)
            ? People
            : People.Where(p => p.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<PersonSummaryResponse>>([.. matches]);
    }

    public Task<PersonDetailResponse> GetPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Throw();

        PersonSummaryResponse summary = People.First(p => p.Id == personId);

        return Task.FromResult(new PersonDetailResponse(
            summary, summary.DisplayName, null, null, null, null, DateTimeOffset.UtcNow, []));
    }

    public Task<PersonDetailResponse> CreatePersonAsync(
        CreatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        PersonSummaryResponse summary = new(
            Guid.NewGuid(),
            request.DisplayName ?? request.FirstName,
            request.Title,
            request.Email,
            request.Phone,
            "Active",
            request.PrimaryCompanyId,
            null,
            DateTimeOffset.UtcNow,
            1);

        People.Add(summary);

        return Task.FromResult(new PersonDetailResponse(
            summary, request.FirstName, null, request.LastName, null, request.Notes, DateTimeOffset.UtcNow, []));
    }

    public Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        PersonSummaryResponse summary = new(
            personId,
            request.DisplayName ?? request.FirstName,
            request.Title,
            request.Email,
            request.Phone,
            "Active",
            request.PrimaryCompanyId,
            null,
            DateTimeOffset.UtcNow,
            request.ExpectedVersion + 1);

        return Task.FromResult(new PersonDetailResponse(
            summary, request.FirstName, null, request.LastName, null, request.Notes, DateTimeOffset.UtcNow, []));
    }

    public Task<IReadOnlyList<TimelineEntryResponse>> GetPersonTimelineAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimelineEntryResponse>>([]);

    public Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<CompanySummaryResponse>>([.. Companies]);
    }

    public Task<CompanyDetailResponse> GetCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CompanyDetailResponse> CreateCompanyAsync(
        CreateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        CompanySummaryResponse summary = new(
            Guid.NewGuid(),
            request.Name,
            request.LegalName,
            request.Type,
            "Active",
            request.Website,
            DateTimeOffset.UtcNow,
            1);

        Companies.Add(summary);

        return Task.FromResult(new CompanyDetailResponse(summary, request.Notes, DateTimeOffset.UtcNow, [], []));
    }

    public Task<CompanyDetailResponse> UpdateCompanyAsync(
        Guid companyId,
        UpdateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        CompanySummaryResponse summary = new(
            companyId,
            request.Name,
            request.LegalName,
            request.Type,
            "Active",
            request.Website,
            DateTimeOffset.UtcNow,
            request.ExpectedVersion + 1);

        return Task.FromResult(new CompanyDetailResponse(summary, request.Notes, DateTimeOffset.UtcNow, [], []));
    }

    public Task<IReadOnlyList<TimelineEntryResponse>> GetCompanyTimelineAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimelineEntryResponse>>([]);

    public Task<Guid> CreateRelationshipAsync(
        CreateRelationshipRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());

    public Task EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<RecordInteractionResponse> RecordInteractionAsync(
        RecordInteractionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        LastInteraction = request;

        return Task.FromResult(new RecordInteractionResponse(
            Guid.NewGuid(),
            request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskResponse>>([]);

    public Task<Guid> CreateTaskAsync(
        CreateTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task CompleteTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        CompletedTasks++;
        return Task.CompletedTask;
    }

    public Task ReopenTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(CommandCenter);
    }

    // ---- Search, saved views and synchronization (M3) ----

    public Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string>? types = null,
        bool includeArchived = false,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new SearchResponse(query, [.. SearchHits], skip, take, HasMore: false));
    }

    public Task<IReadOnlyList<SavedViewResponse>> ListSavedViewsAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<SavedViewResponse>>([.. SavedViews]);
    }

    public Task<SavedViewResponse> CreateSavedViewAsync(
        CreateSavedViewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        SavedViewResponse created = new(
            Guid.NewGuid(),
            request.Name,
            request.Definition.Target,
            request.Definition,
            request.Definition.DefinitionVersion,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        SavedViews.Add(created);

        return Task.FromResult(created);
    }

    public Task<SavedViewResponse> UpdateSavedViewAsync(
        Guid savedViewId,
        UpdateSavedViewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        int index = SavedViews.FindIndex(x => x.Id == savedViewId);
        SavedViewResponse existing = SavedViews[index];

        if (existing.Version != request.ExpectedVersion)
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.Conflict,
                "Version conflict",
                "The saved view changed.",
                "version_conflict",
                request.ExpectedVersion,
                existing.Version);
        }

        SavedViewResponse updated = existing with
        {
            Name = request.Name,
            Definition = request.Definition,
            Target = request.Definition.Target,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        SavedViews[index] = updated;

        return Task.FromResult(updated);
    }

    public Task DeleteSavedViewAsync(Guid savedViewId, CancellationToken cancellationToken = default)
    {
        Throw();
        SavedViews.RemoveAll(x => x.Id == savedViewId);
        return Task.CompletedTask;
    }

    public Task<SavedViewResultsResponse> RunSavedViewAsync(
        Guid savedViewId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        SavedViewResponse view = SavedViews.Single(x => x.Id == savedViewId);

        return Task.FromResult(new SavedViewResultsResponse(
            view.Target,
            [.. People],
            [.. Companies],
            [],
            [.. Talent],
            [.. Prospects],
            [],
            [],
            [],
            [.. Deals],
            [.. Contracts],
            [.. Receivables],
            [.. Invoices],
            [.. Payments],
            [.. Documents],
            [.. Messages],
            [.. Signals],
            [.. Theses],
            [.. Predictions],
            [.. RadarEntries]));
    }

    public Task<SyncChangesResponse> ReadSyncChangesAsync(
        long cursor,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (SyncPages.Count == 0)
        {
            return Task.FromResult(new SyncChangesResponse(cursor, false, [], [], [], [], []));
        }

        return Task.FromResult(SyncPages.Dequeue());
    }

    /// <summary>
    /// Records a mutating call and applies the next queued failure, if any.
    /// </summary>
    /// <remarks>
    /// Effects are counted per idempotency key, which is what lets a test assert
    /// the property the model checks: a command retried after a lost response
    /// takes effect once, not twice.
    /// </remarks>
    private void Submit(string? idempotencyKey)
    {
        IdempotencyKeys.Add(idempotencyKey);

        if (Failures.Count > 0)
        {
            throw Failures.Dequeue();
        }

        Throw();

        if (idempotencyKey is not null)
        {
            Effects[idempotencyKey] = Effects.GetValueOrDefault(idempotencyKey) + 1;
        }
    }

    // ---- Talent and representation (M4) ----

    public Task<IReadOnlyList<TalentSummaryResponse>> ListTalentAsync(
        bool clientsOnly = false,
        bool formerClientsOnly = false,
        string? discipline = null,
        string? scope = null,
        Guid? leadUserId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastTalentFilter = (clientsOnly, formerClientsOnly, discipline, search);

        IEnumerable<TalentSummaryResponse> matches = Talent;

        if (clientsOnly)
        {
            matches = matches.Where(x => x.IsClient);
        }

        if (formerClientsOnly)
        {
            matches = matches.Where(x => !x.IsClient && x.RepresentationStatus is not null);
        }

        if (discipline is { Length: > 0 })
        {
            matches = matches.Where(x => x.Disciplines.Contains(discipline));
        }

        if (search is { Length: > 0 })
        {
            matches = matches.Where(x => x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<TalentSummaryResponse>>([.. matches]);
    }

    /// <summary>
    /// Talent reads to hold in flight, by person, until the test completes the gate.
    /// </summary>
    /// <remarks>
    /// Deterministic: a test sees the state while the read is outstanding, and decides
    /// when and how it ends, with no timing involved. A gate completed with an
    /// exception ends the read with that exception.
    /// </remarks>
    public Dictionary<Guid, TaskCompletionSource> TalentReadGates { get; } = [];

    public async Task<TalentDetailResponse> GetTalentAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        if (TalentReadGates.TryGetValue(personId, out TaskCompletionSource? gate))
        {
            await gate.Task.ConfigureAwait(false);
        }

        Throw();

        // As the server does: a person with no talent profile is not found.
        TalentSummaryResponse summary = Talent.FirstOrDefault(x => x.PersonId == personId)
            ?? throw new AgencyOsApiException(System.Net.HttpStatusCode.NotFound, "Not found");

        return new TalentDetailResponse(summary, null, null, null, null, DateTimeOffset.UtcNow);
    }

    public Task<TalentDetailResponse> CreateTalentProfileAsync(
        CreateTalentProfileRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        // As the server does: one talent profile per person.
        if (Talent.Any(x => x.PersonId == request.PersonId))
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.Conflict,
                "Already exists",
                "This person already has a talent profile.");
        }

        // A profile reads the person's representation as the talent roster does: a
        // person signed through a conversion is a client once they have a profile.
        RepresentationResponse? representation = Conversions.Values
            .FirstOrDefault(x => x.PersonId == request.PersonId);

        TalentSummaryResponse summary = new(
            Guid.NewGuid(),
            request.PersonId,
            representation?.DisplayName ?? "Created",
            request.CareerStage ?? "Unknown",
            request.Disciplines ?? [],
            representation?.Status,
            IsClient: representation is { Status: "Active" },
            null,
            null,
            representation is null ? [] : [.. representation.Scopes.Select(x => x.Area)],
            DateTimeOffset.UtcNow,
            1);

        Talent.Add(summary);

        return Task.FromResult(new TalentDetailResponse(
            summary,
            request.Summary,
            request.PositioningNotes,
            request.BaseMarket,
            request.Languages,
            DateTimeOffset.UtcNow));
    }

    public Task<ClientOverviewResponse> GetClientOverviewAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (Overview is { } overview)
        {
            return Task.FromResult(overview);
        }

        TalentSummaryResponse summary = Talent.First(x => x.PersonId == personId);

        return Task.FromResult(new ClientOverviewResponse(
            new TalentDetailResponse(summary, null, null, null, null, DateTimeOffset.UtcNow),
            null,
            [],
            [],
            [.. Credits],
            [.. Materials],
            []));
    }

    public Task<IReadOnlyList<RepresentationHistoryEntryResponse>> GetRepresentationHistoryAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<RepresentationHistoryEntryResponse>>([]);
    }

    public Task<IReadOnlyList<ProspectResponse>> ListProspectsAsync(
        bool openOnly = true,
        string? stage = null,
        Guid? ownerUserId = null,
        DateOnly? dueOnOrBefore = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<ProspectResponse> matches = Prospects;

        if (openOnly)
        {
            matches = matches.Where(x => x.Stage is "Identified" or "Contacted" or "Courting");
        }

        if (stage is { Length: > 0 })
        {
            matches = matches.Where(x => x.Stage == stage);
        }

        return Task.FromResult<IReadOnlyList<ProspectResponse>>([.. matches]);
    }

    public Task<ProspectResponse> GetProspectAsync(Guid prospectId, CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(Prospects.First(x => x.Id == prospectId));
    }

    public Task<ProspectResponse> CreateProspectAsync(
        CreateProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        ProspectResponse prospect = new(
            Guid.NewGuid(),
            request.PersonId,
            "Prospect",
            "Identified",
            request.OwnerUserId,
            "Owner",
            request.Source,
            request.StrategyNotes,
            request.IdentifiedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            request.NextFollowUpOn,
            null,
            DateTimeOffset.UtcNow,
            1);

        Prospects.Add(prospect);

        return Task.FromResult(prospect);
    }

    public Task AdvanceProspectAsync(
        Guid prospectId,
        AdvanceProspectRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        int index = Prospects.FindIndex(x => x.Id == prospectId);
        ProspectResponse existing = Prospects[index];

        if (existing.Version != request.ExpectedVersion)
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.Conflict,
                "Version conflict",
                "The prospect changed.",
                "version_conflict",
                request.ExpectedVersion,
                existing.Version);
        }

        Prospects[index] = existing with { Stage = request.Stage, Version = existing.Version + 1 };

        return Task.CompletedTask;
    }

    public Task<RepresentationResponse> ConvertProspectAsync(
        Guid prospectId,
        ConvertProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // Replays under a key that already converted, exactly as the server does.
        if (idempotencyKey is not null
            && Effects.ContainsKey(idempotencyKey)
            && Conversions.TryGetValue(prospectId, out RepresentationResponse? replayed))
        {
            return Task.FromResult(replayed);
        }

        Submit(idempotencyKey);

        int index = Prospects.FindIndex(x => x.Id == prospectId);
        ProspectResponse existing = Prospects[index];

        RepresentationResponse representation = new(
            Guid.NewGuid(),
            existing.PersonId,
            existing.DisplayName,
            "Active",
            request.StartsOn,
            null,
            request.IsExclusive,
            request.Territory,
            request.Notes,
            [.. request.Scopes.Select(x => new RepresentationScopeResponse(x, request.StartsOn, null))],
            [new RepresentationTeamMemberResponse(request.LeadUserId, "Lead", "Lead", request.StartsOn, null)],
            DateTimeOffset.UtcNow,
            1);

        Prospects[index] = existing with
        {
            Stage = "Converted",
            ConvertedToRepresentationId = representation.Id,
            Version = existing.Version + 1,
        };

        Conversions[prospectId] = representation;

        return Task.FromResult(representation);
    }

    public Task<RepresentationResponse> GetRepresentationAsync(
        Guid representationId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(Conversions.Values.First(x => x.Id == representationId));
    }

    public Task TransitionRepresentationAsync(
        Guid representationId,
        TransitionRepresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.CompletedTask;
    }

    public Task AddRepresentationScopeAsync(
        Guid representationId,
        ChangeRepresentationScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.CompletedTask;
    }

    public Task EndRepresentationScopeAsync(
        Guid representationId,
        ChangeRepresentationScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.CompletedTask;
    }

    public Task AssignRepresentationTeamMemberAsync(
        Guid representationId,
        AssignRepresentationTeamMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.CompletedTask;
    }

    public Task RemoveRepresentationTeamMemberAsync(
        Guid representationId,
        RemoveRepresentationTeamMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CreditResponse>> ListCreditsAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<CreditResponse>>([.. Credits]);
    }

    public Task<Guid> AddCreditAsync(
        AddCreditRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        Credits.Add(new CreditResponse(
            id,
            request.PersonId,
            request.Title,
            request.Role,
            request.Type,
            request.Status ?? "Released",
            request.Year,
            request.CompanyId,
            null,
            request.Source,
            request.Notes,
            null,
            1));

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<MaterialResponse>> ListMaterialsAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<MaterialResponse>>([.. Materials]);
    }

    public Task<Guid> AddMaterialAsync(
        AddMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        Materials.Add(new MaterialResponse(
            id,
            request.PersonId,
            request.Title,
            request.Type,
            request.Status ?? "Draft",
            request.VersionLabel,
            request.ExternalUri,
            request.ReceivedOn,
            request.Source,
            request.Notes,
            1));

        return Task.FromResult(id);
    }

    // ---- Projects and packaging (M5) ----

    public List<ProjectSummaryResponse> Projects { get; } = [];

    public List<PackageSummaryResponse> Packages { get; } = [];

    public List<SourcePropertyResponse> SourceProperties { get; } = [];

    /// <summary>Roles the fake has created, by project.</summary>
    public Dictionary<Guid, List<ProjectRoleResponse>> Roles { get; } = [];

    /// <summary>Attachments the fake has recorded, by project.</summary>
    public Dictionary<Guid, List<AttachmentResponse>> Attachments { get; } = [];

    /// <summary>Elements the fake holds, by package.</summary>
    public Dictionary<Guid, List<PackageElementResponse>> Elements { get; } = [];

    /// <summary>Filters the last project list call was made with, so a test can assert them.</summary>
    public (string? Status, string? Stage, string? Type, string? MissingRole, string? Search) LastProjectFilter
    { get; private set; }

    public ProjectCommandCenterResponse ProjectCommandCenter { get; set; } =
        new([], [], [], 0, 0);

    public Task<IReadOnlyList<ProjectSummaryResponse>> ListProjectsAsync(
        string? status = null,
        string? stage = null,
        string? type = null,
        Guid? leadUserId = null,
        string? missingRole = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastProjectFilter = (status, stage, type, missingRole, search);

        IEnumerable<ProjectSummaryResponse> matches = Projects;

        if (!string.IsNullOrWhiteSpace(status))
        {
            matches = matches.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            matches = matches.Where(
                x => x.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<ProjectSummaryResponse>>([.. matches]);
    }

    public Task<ProjectDetailResponse> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        ProjectSummaryResponse summary = Projects.First(x => x.Id == projectId);

        return Task.FromResult(new ProjectDetailResponse(
            summary,
            null,
            null,
            null,
            Roles.TryGetValue(projectId, out List<ProjectRoleResponse>? roles) ? roles : [],
            [],
            [],
            [],
            [.. Packages.Where(x => x.ProjectId == projectId)],
            DateTimeOffset.UtcNow));
    }

    public Task<ProjectDetailResponse> CreateProjectAsync(
        CreateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        ProjectSummaryResponse summary = new(
            Guid.NewGuid(),
            request.Title,
            request.WorkingTitle,
            request.Type,
            "Active",
            request.Stage ?? "Concept",
            request.Year,
            request.PrimaryCompanyId,
            null,
            request.LeadUserId,
            null,
            0,
            0,
            0,
            DateTimeOffset.UtcNow,
            1);

        Projects.Add(summary);

        return Task.FromResult(new ProjectDetailResponse(
            summary, request.Logline, request.Synopsis, request.Notes, [], [], [], [], [], DateTimeOffset.UtcNow));
    }

    public Task UpdateProjectAsync(
        Guid projectId,
        UpdateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task ChangeProjectStatusAsync(
        Guid projectId,
        ChangeProjectStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Projects.FindIndex(x => x.Id == projectId);

        if (index >= 0)
        {
            Projects[index] = Projects[index] with
            {
                Status = request.Status,
                Version = Projects[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task ChangeProjectStageAsync(
        Guid projectId,
        ChangeProjectStageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Projects.FindIndex(x => x.Id == projectId);

        if (index >= 0)
        {
            Projects[index] = Projects[index] with
            {
                Stage = request.Stage,
                Version = Projects[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProjectHistoryEntryResponse>> GetProjectHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectHistoryEntryResponse>>([]);

    public Task<Guid> CreateProjectRoleAsync(
        Guid projectId,
        CreateProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        if (!Roles.TryGetValue(projectId, out List<ProjectRoleResponse>? roles))
        {
            Roles[projectId] = roles = [];
        }

        roles.Add(new ProjectRoleResponse(
            id, request.Type, request.Label, "Open", request.IsExclusive, request.Notes, []));

        return Task.FromResult(id);
    }

    public Task ChangeProjectRoleAsync(
        Guid projectId,
        Guid roleId,
        ChangeProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<Guid> AttachToRoleAsync(
        Guid projectId,
        Guid roleId,
        AttachToRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        if (!Attachments.TryGetValue(projectId, out List<AttachmentResponse>? attachments))
        {
            Attachments[projectId] = attachments = [];
        }

        attachments.Add(new AttachmentResponse(
            id,
            roleId,
            "Director",
            null,
            request.PersonId,
            request.CompanyId,
            "Attached party",
            request.Status,
            request.StartsOn,
            request.EndsOn,
            request.Status is "Attached" or "Conditional",
            request.Source,
            request.Notes,
            DateTimeOffset.UtcNow,
            1));

        return Task.FromResult(id);
    }

    public Task ChangeAttachmentAsync(
        Guid projectId,
        Guid attachmentId,
        ChangeAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<Guid> AddProjectCompanyAsync(
        Guid projectId,
        AddProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task EndProjectCompanyAsync(
        Guid projectId,
        Guid participationId,
        EndProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SourcePropertyResponse>> ListSourcePropertiesAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<SourcePropertyResponse>>([.. SourceProperties]);
    }

    public Task<SourcePropertyResponse> CreateSourcePropertyAsync(
        CreateSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        SourcePropertyResponse created = new(
            Guid.NewGuid(),
            request.Title,
            request.Type,
            request.AttributedCreator,
            request.CreatorPersonId,
            request.SourceReference,
            request.Provenance,
            request.Year,
            request.Notes,
            0,
            DateTimeOffset.UtcNow,
            1);

        SourceProperties.Add(created);

        return Task.FromResult(created);
    }

    public Task LinkSourcePropertyAsync(
        Guid projectId,
        LinkSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task LinkMaterialToProjectAsync(
        Guid projectId,
        LinkProjectMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PackageSummaryResponse>> ListPackagesAsync(
        string? status = null,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<PackageSummaryResponse> matches = Packages;

        if (!string.IsNullOrWhiteSpace(status))
        {
            matches = matches.Where(x => x.Status == status);
        }

        if (projectId is { } project)
        {
            matches = matches.Where(x => x.ProjectId == project);
        }

        return Task.FromResult<IReadOnlyList<PackageSummaryResponse>>([.. matches]);
    }

    public Task<PackageDetailResponse> GetPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        PackageSummaryResponse summary = Packages.First(x => x.Id == packageId);

        return Task.FromResult(new PackageDetailResponse(
            summary,
            null,
            PackageStrategy,
            Elements.TryGetValue(packageId, out List<PackageElementResponse>? elements) ? elements : [],
            PackageGaps,
            DateTimeOffset.UtcNow));
    }

    /// <summary>Strategy the fake returns, so redaction can be simulated.</summary>
    public string? PackageStrategy { get; set; }

    /// <summary>Gaps the fake returns for a package.</summary>
    public List<ProjectRoleResponse> PackageGaps { get; } = [];

    public Task<PackageDetailResponse> CreatePackageAsync(
        CreatePackageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        PackageSummaryResponse summary = new(
            Guid.NewGuid(),
            request.ProjectId,
            Projects.FirstOrDefault(x => x.Id == request.ProjectId)?.Title ?? "(unknown)",
            request.Name,
            "Draft",
            request.LeadUserId,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            1);

        Packages.Add(summary);

        return Task.FromResult(new PackageDetailResponse(
            summary, request.Thesis, request.StrategyNotes, [], [], DateTimeOffset.UtcNow));
    }

    public Task ChangePackageStatusAsync(
        Guid packageId,
        ChangePackageStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Packages.FindIndex(x => x.Id == packageId);

        if (index >= 0)
        {
            Packages[index] = Packages[index] with
            {
                Status = request.Status,
                Version = Packages[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<Guid> AddPackageElementAsync(
        Guid packageId,
        AddPackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        if (!Elements.TryGetValue(packageId, out List<PackageElementResponse>? elements))
        {
            Elements[packageId] = elements = [];
        }

        elements.Add(new PackageElementResponse(
            id,
            request.Kind,
            request.TargetId,
            "Element",
            null,
            request.Kind == "AttachedParty",
            request.Note,
            elements.Count));

        return Task.FromResult(id);
    }

    public Task RemovePackageElementAsync(
        Guid packageId,
        Guid elementId,
        RemovePackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        if (Elements.TryGetValue(packageId, out List<PackageElementResponse>? elements))
        {
            elements.RemoveAll(x => x.Id == elementId);
        }

        return Task.CompletedTask;
    }

    public Task<ProjectCommandCenterResponse> GetProjectCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(ProjectCommandCenter);
    }

    // ---- Opportunities and submissions (M6) ----

    public List<OpportunitySummaryResponse> Opportunities { get; } = [];

    public Dictionary<Guid, List<OpportunityTargetResponse>> Targets { get; } = [];

    public List<SubmissionResponse> Submissions { get; } = [];

    public List<PitchResponse> Pitches { get; } = [];

    public List<PipelineColumnResponse> Pipeline { get; } = [];

    public List<OpportunityHistoryEntryResponse> OpportunityHistory { get; } = [];

    /// <summary>Strategy the fake hands back, so redaction can be simulated.</summary>
    public string? OpportunityStrategy { get; set; }

    /// <summary>The filter the last list call actually sent.</summary>
    public (string? Status, string? Kind, bool Awaiting, string? Search) LastOpportunityFilter
    { get; private set; }

    public OpportunityCommandCenterResponse OpportunityCommandCenter { get; set; } =
        new([], [], [], [], 0, 0);

    public Task<IReadOnlyList<OpportunitySummaryResponse>> ListOpportunitiesAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool awaitingResponse = false,
        string? search = null,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastOpportunityFilter = (status, kind, awaitingResponse, search);

        IEnumerable<OpportunitySummaryResponse> matches = Opportunities;

        if (!string.IsNullOrWhiteSpace(status))
        {
            matches = matches.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            matches = matches.Where(x => x.Kind == kind);
        }

        if (awaitingResponse)
        {
            matches = matches.Where(x => x.AwaitingResponseCount > 0);
        }

        return Task.FromResult<IReadOnlyList<OpportunitySummaryResponse>>([.. matches]);
    }

    public Task<OpportunityDetailResponse> GetOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (NextOpportunityReadFailure is { } failure)
        {
            NextOpportunityReadFailure = null;

            throw failure;
        }

        OpportunitySummaryResponse summary = Opportunities.First(x => x.Id == opportunityId);

        return Task.FromResult(new OpportunityDetailResponse(
            summary,
            "A pursuit.",
            OpportunityStrategy,
            [],
            TargetsOf(opportunityId),
            [.. Submissions.Where(x => x.OpportunityId == opportunityId)],
            [.. Pitches.Where(x => x.OpportunityId == opportunityId)],
            [],
            DateTimeOffset.UtcNow));
    }

    public Task<OpportunityDetailResponse> CreateOpportunityAsync(
        CreateOpportunityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        OpportunitySummaryResponse summary = Opportunity(
            request.Name, request.Kind, "Draft", request.Priority ?? "Normal");

        Opportunities.Add(summary);

        return Task.FromResult(new OpportunityDetailResponse(
            summary,
            request.Description,
            request.StrategyNotes,
            [],
            [],
            [],
            [],
            [],
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Fails the next pursuit detail read only - not a write - so a test can accept a
    /// command and then lose the refresh that follows it.
    /// </summary>
    public Exception? NextOpportunityReadFailure { get; set; }

    /// <summary>Every status change requested, in order, with its idempotency key.</summary>
    public List<(Guid OpportunityId, ChangeOpportunityStatusRequest Request, string? Key)> OpportunityStatusChanges { get; } = [];

    public Task ChangeOpportunityStatusAsync(
        Guid opportunityId,
        ChangeOpportunityStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        OpportunityStatusChanges.Add((opportunityId, request, idempotencyKey));

        Submit(idempotencyKey);

        int index = Opportunities.FindIndex(x => x.Id == opportunityId);

        // As the server does: the version the caller saw must still be current.
        if (index >= 0 && Opportunities[index].Version != request.ExpectedVersion)
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.Conflict,
                "Version conflict",
                "Somebody else changed this opportunity. Reload it and try again.");
        }

        if (index >= 0)
        {
            Opportunities[index] = Opportunities[index] with
            {
                Status = request.Status,
                Outcome = request.Outcome,
                Version = Opportunities[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OpportunityHistoryEntryResponse>> GetOpportunityHistoryAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<OpportunityHistoryEntryResponse>>([.. OpportunityHistory]);
    }

    public Task<Guid> AddOpportunityTargetAsync(
        Guid opportunityId,
        AddOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        if (!Targets.TryGetValue(opportunityId, out List<OpportunityTargetResponse>? targets))
        {
            Targets[opportunityId] = targets = [];
        }

        targets.Add(Target(id, "Identified", request.NextActionOn));

        return Task.FromResult(id);
    }

    public Task<OpportunityTargetResponse> GetOpportunityTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Targets.Values.SelectMany(x => x).First(x => x.Id == targetId));
    }

    public Task MoveOpportunityTargetAsync(
        Guid targetId,
        MoveOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        // As the server does: market activity needs an Active pursuit.
        Guid? owner = Targets.FirstOrDefault(x => x.Value.Any(t => t.Id == targetId)).Key;

        if (Opportunities.FirstOrDefault(x => x.Id == owner) is { Status: "Draft" })
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.BadRequest,
                "Invalid request",
                "This opportunity is still a draft. Activate it before recording market activity.");
        }

        foreach (List<OpportunityTargetResponse> targets in Targets.Values)
        {
            int index = targets.FindIndex(x => x.Id == targetId);

            if (index >= 0)
            {
                targets[index] = targets[index] with
                {
                    Stage = request.Stage,
                    IsOpen = request.Stage is not ("Passed" or "Withdrawn" or "Exhausted"),
                    Version = targets[index].Version + 1,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task RecordTargetResponseAsync(
        Guid targetId,
        RecordTargetResponseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<RecordSubmissionResponse> RecordSubmissionAsync(
        Guid targetId,
        RecordSubmissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();

        Submissions.Add(new SubmissionResponse(
            id,
            OpportunityOf(targetId),
            targetId,
            "Northgate Pictures",
            request.SentAt ?? DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            request.Channel,
            request.Subject,
            request.Notes,
            request.ResponseExpectedBy,
            request.ExternalReference,
            [],
            null,
            false,
            1));

        return Task.FromResult(new RecordSubmissionResponse(
            id, request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<RecordPitchResponse> RecordPitchAsync(
        Guid targetId,
        RecordPitchRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Guid id = Guid.NewGuid();
        Guid interaction = Guid.NewGuid();

        Pitches.Add(new PitchResponse(
            id,
            OpportunityOf(targetId),
            targetId,
            "Northgate Pictures",
            interaction,
            request.Kind,
            request.Outcome,
            request.Subject,
            request.Notes,
            request.OccurredAt ?? DateTimeOffset.UtcNow,
            [.. request.Participants.Select(x => x.Role ?? x.PartyKind)],
            [],
            1));

        return Task.FromResult(new RecordPitchResponse(
            id, interaction, request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(
        Guid? opportunityId = null,
        Guid? targetId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<SubmissionResponse> matches = Submissions;

        if (opportunityId is { } opportunity)
        {
            matches = matches.Where(x => x.OpportunityId == opportunity);
        }

        if (targetId is { } target)
        {
            matches = matches.Where(x => x.OpportunityTargetId == target);
        }

        return Task.FromResult<IReadOnlyList<SubmissionResponse>>([.. matches]);
    }

    public Task<IReadOnlyList<PipelineColumnResponse>> GetPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<PipelineColumnResponse>>([.. Pipeline]);
    }

    public Task<OpportunityCommandCenterResponse> GetOpportunityCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(OpportunityCommandCenter);
    }

    /// <summary>The targets recorded against one pursuit.</summary>
    internal List<OpportunityTargetResponse> TargetsOf(Guid opportunityId) =>
        Targets.TryGetValue(opportunityId, out List<OpportunityTargetResponse>? targets)
            ? targets
            : [];

    /// <summary>Which pursuit a target belongs to, so derived rows agree with it.</summary>
    private Guid OpportunityOf(Guid targetId) =>
        Targets.FirstOrDefault(pair => pair.Value.Any(x => x.Id == targetId)).Key;

    /// <summary>A pursuit the tests can assert against.</summary>
    internal static OpportunitySummaryResponse Opportunity(
        string name,
        string kind = "ProjectMarket",
        string status = "Active",
        string priority = "Normal",
        int awaitingResponseCount = 0,
        DateOnly? nextActionOn = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            name,
            kind,
            status,
            priority,
            Guid.NewGuid(),
            null,
            new DateOnly(2026, 1, 5),
            null,
            null,
            null,
            0,
            0,
            0,
            awaitingResponseCount,
            nextActionOn,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

    /// <summary>A target the tests can assert against.</summary>
    internal static OpportunityTargetResponse Target(
        Guid id,
        string stage,
        DateOnly? nextActionOn = null,
        int submissionCount = 0,
        DateOnly? awaitingSince = null,
        string displayName = "Northgate Pictures") =>
        new(
            id,
            Guid.NewGuid(),
            null,
            displayName,
            null,
            null,
            stage,
            stage is not ("Passed" or "Withdrawn" or "Exhausted"),
            null,
            null,
            nextActionOn,
            null,
            null,
            submissionCount,
            submissionCount > 0 ? DateTimeOffset.UtcNow : null,
            0,
            null,
            DateTimeOffset.UtcNow,
            awaitingSince,
            DateTimeOffset.UtcNow,
            1);

    // ---- Deals and offers (M7) ----

    public List<DealSummaryResponse> Deals { get; } = [];

    public Dictionary<Guid, List<OfferResponse>> Offers { get; } = [];

    public List<DealPipelineColumnResponse> DealPipeline { get; } = [];

    public List<DealHistoryEntryResponse> DealHistory { get; } = [];

    /// <summary>Strategy the fake hands back, so redaction can be simulated.</summary>
    public string? DealStrategy { get; set; }

    /// <summary>The filter the last deal list call actually sent.</summary>
    public (string? Status, string? Kind, bool Awaiting, bool Agreed, string? Search,
        bool OpenOnly) LastDealFilter
    { get; private set; }

    public DealCommandCenterResponse DealCommandCenter { get; set; } =
        new([], [], [], [], [], [], [], [], 0, 0);

    /// <summary>
    /// The catalog the fake publishes. Empty by default so a test that needs one
    /// says so.
    /// </summary>
    public List<DealTermDefinitionResponse> DealTerms { get; } = [];

    public Task<IReadOnlyList<DealSummaryResponse>> ListDealsAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool hasOpenOffer = false,
        bool termsAgreed = false,
        string? search = null,
        bool openOnly = false,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastDealFilter = (status, kind, hasOpenOffer, termsAgreed, search, openOnly);

        IEnumerable<DealSummaryResponse> matches = Deals;

        if (!string.IsNullOrWhiteSpace(status))
        {
            matches = matches.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            matches = matches.Where(x => x.Kind == kind);
        }

        if (hasOpenOffer)
        {
            matches = matches.Where(x => x.HasOpenOffer);
        }

        if (termsAgreed)
        {
            matches = matches.Where(x => x.Status == "TermsAgreed");
        }

        // The live set, as the server reads it from Deal.LiveStatuses.
        if (openOnly)
        {
            matches = matches.Where(x =>
                x.Status is "Draft" or "Negotiating" or "TermsAgreed");
        }

        return Task.FromResult<IReadOnlyList<DealSummaryResponse>>([.. matches]);
    }

    public Task<DealDetailResponse> GetDealAsync(
        Guid dealId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        DealSummaryResponse summary = Deals.First(x => x.Id == dealId);
        List<OfferResponse> offers = OffersOf(dealId);

        return Task.FromResult(new DealDetailResponse(
            summary,
            "A negotiation.",
            DealStrategy,
            offers,
            offers.FirstOrDefault(x => x.Status == "Accepted"),
            offers.FirstOrDefault(x => x.Status == "Open"),
            [],
            DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<DealHistoryEntryResponse>> GetDealHistoryAsync(
        Guid dealId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<DealHistoryEntryResponse>>([.. DealHistory]);
    }

    public Task<DealDetailResponse> CreateDealAsync(
        CreateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        DealSummaryResponse summary = Deal(request.Name, request.Kind, "Draft");

        Deals.Add(summary);

        return Task.FromResult(new DealDetailResponse(
            summary,
            request.Summary,
            request.StrategyNotes,
            [],
            null,
            null,
            [],
            DateTimeOffset.UtcNow));
    }

    public Task UpdateDealAsync(
        Guid dealId,
        UpdateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Deals.FindIndex(x => x.Id == dealId);

        if (index >= 0)
        {
            Deals[index] = Deals[index] with
            {
                Name = request.Name,
                Kind = request.Kind,
                Version = Deals[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task CloseDealAsync(
        Guid dealId,
        CloseDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Deals.FindIndex(x => x.Id == dealId);

        if (index >= 0)
        {
            Deals[index] = Deals[index] with
            {
                Status = request.Status,
                Version = Deals[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task ReopenNegotiationAsync(
        Guid dealId,
        ReopenNegotiationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Deals.FindIndex(x => x.Id == dealId);

        if (index >= 0)
        {
            Deals[index] = Deals[index] with
            {
                Status = "Negotiating",
                AcceptedOfferId = null,
                Version = Deals[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OfferResponse>> ListOffersAsync(
        Guid? dealId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<OfferResponse> matches = dealId is { } deal
            ? OffersOf(deal)
            : Offers.Values.SelectMany(x => x);

        return Task.FromResult<IReadOnlyList<OfferResponse>>([.. matches]);
    }

    public Task<OfferResponse> GetOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(Offers.Values.SelectMany(x => x).First(x => x.Id == offerId));
    }

    public Task<RecordOfferResponse> RecordOfferAsync(
        Guid dealId,
        RecordOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        List<OfferResponse> thread = OffersOf(dealId);

        // The standing offer is superseded, exactly as the server does it: a
        // negotiation holds one canonical thread.
        Guid? superseded = null;

        for (int index = 0; index < thread.Count; index++)
        {
            if (thread[index].Status == "Open")
            {
                superseded = thread[index].Id;
                thread[index] = thread[index] with { Status = "Superseded" };
            }
        }

        Guid id = Guid.NewGuid();

        thread.Add(Offer(
            id,
            dealId,
            request.Direction,
            "Open",
            thread.Count + 1,
            request.RespondsToOfferId ?? superseded,
            [.. request.Terms.Select(Term)],
            request.ExpiresAt,
            request.Summary));

        Offers[dealId] = thread;

        MarkOpenOffer(dealId, id, request.ExpiresAt);

        return Task.FromResult(new RecordOfferResponse(
            id, dealId, superseded, request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<AnswerOfferResponse> AnswerOfferAsync(
        Guid offerId,
        AnswerOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        string status = request.Answer switch
        {
            "Accept" => "Accepted",
            "Reject" => "Rejected",
            "Withdraw" => "Withdrawn",
            _ => "Expired",
        };

        Guid dealId = Guid.Empty;

        foreach ((Guid deal, List<OfferResponse> thread) in Offers)
        {
            int index = thread.FindIndex(x => x.Id == offerId);

            if (index >= 0)
            {
                dealId = deal;
                thread[index] = thread[index] with { Status = status, Version = thread[index].Version + 1 };
            }
        }

        string dealStatus = request.Answer == "Accept" ? "TermsAgreed" : "Negotiating";

        int position = Deals.FindIndex(x => x.Id == dealId);

        if (position >= 0)
        {
            Deals[position] = Deals[position] with
            {
                Status = dealStatus,
                HasOpenOffer = false,
                AcceptedOfferId = request.Answer == "Accept" ? offerId : null,
                Version = Deals[position].Version + 1,
            };
        }

        return Task.FromResult(new AnswerOfferResponse(
            offerId, dealStatus, request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<OfferComparisonResponse> CompareOffersAsync(
        Guid dealId,
        Guid previousOfferId,
        Guid currentOfferId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        List<OfferResponse> thread = OffersOf(dealId);

        OfferResponse previous = thread.First(x => x.Id == previousOfferId);
        OfferResponse current = thread.First(x => x.Id == currentOfferId);

        List<TermDifferenceResponse> differences = [];

        foreach (OfferTermResponse term in current.Terms)
        {
            OfferTermResponse? before = previous.Terms.FirstOrDefault(x => x.Code == term.Code);

            differences.Add(new TermDifferenceResponse(
                term.Code,
                term.DisplayName,
                before is null ? "Added" : before.Amount == term.Amount ? "Unchanged" : "Changed",
                before?.Amount is { } was && term.Amount is { } now
                    ? now > was ? "Increased" : now < was ? "Decreased" : "Level"
                    : "NotComparable",
                before,
                term));
        }

        foreach (OfferTermResponse term in previous.Terms
            .Where(x => current.Terms.All(y => y.Code != x.Code)))
        {
            differences.Add(new TermDifferenceResponse(
                term.Code, term.DisplayName, "Removed", "NotComparable", term, null));
        }

        return Task.FromResult(new OfferComparisonResponse(
            dealId, previousOfferId, currentOfferId, differences));
    }

    public Task<IReadOnlyList<DealPipelineColumnResponse>> GetDealPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<DealPipelineColumnResponse>>([.. DealPipeline]);
    }

    public Task<DealCommandCenterResponse> GetDealCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(DealCommandCenter);
    }

    public Task<IReadOnlyList<DealTermDefinitionResponse>> ListDealTermsAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<DealTermDefinitionResponse>>([.. DealTerms]);
    }

    /// <summary>The offers recorded against one negotiation.</summary>
    internal List<OfferResponse> OffersOf(Guid dealId) =>
        Offers.TryGetValue(dealId, out List<OfferResponse>? thread) ? thread : [];

    private void MarkOpenOffer(Guid dealId, Guid offerId, DateTimeOffset? expiresAt)
    {
        int index = Deals.FindIndex(x => x.Id == dealId);

        if (index >= 0)
        {
            Deals[index] = Deals[index] with
            {
                Status = "Negotiating",
                HasOpenOffer = true,
                LatestOfferId = offerId,
                OpenOfferExpiresAt = expiresAt,
                OfferCount = Deals[index].OfferCount + 1,
                Version = Deals[index].Version + 1,
            };
        }
    }

    /// <summary>A negotiation the tests can assert against.</summary>
    internal static DealSummaryResponse Deal(
        string name,
        string kind = "ProjectSale",
        string status = "Negotiating",
        bool hasOpenOffer = false,
        DateTimeOffset? openOfferExpiresAt = null,
        Guid? id = null,
        string counterparty = "Northgate Pictures") =>
        new(
            id ?? Guid.NewGuid(),
            name,
            null,
            kind,
            status,
            Guid.NewGuid(),
            "The Undertow - take out",
            Guid.NewGuid(),
            counterparty,
            Guid.NewGuid(),
            null,
            "The Undertow",
            Guid.NewGuid(),
            null,
            new DateOnly(2026, 2, 3),
            null,
            0,
            null,
            null,
            null,
            hasOpenOffer,
            openOfferExpiresAt,
            null,
            0,
            null,
            DateTimeOffset.UtcNow,
            1);

    /// <summary>An offer the tests can assert against.</summary>
    internal static OfferResponse Offer(
        Guid id,
        Guid dealId,
        string direction,
        string status,
        int sequence,
        Guid? respondsTo = null,
        IReadOnlyList<OfferTermResponse>? terms = null,
        DateTimeOffset? expiresAt = null,
        string? summary = null) =>
        new(
            id,
            dealId,
            direction,
            status,
            sequence,
            respondsTo,
            respondsTo is null ? null : "Counter",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            summary,
            null,
            expiresAt,
            terms ?? [],
            1);

    /// <summary>A money term the tests can assert against.</summary>
    internal static OfferTermResponse MoneyTerm(string code, decimal amount, string currency = "USD") =>
        new(
            code,
            code,
            "Money",
            true,
            amount,
            currency,
            null,
            null,
            null,
            null,
            null,
            null,
            $"{amount} {currency}",
            1,
            null);

    /// <summary>A structural term, which survives economics redaction.</summary>
    internal static OfferTermResponse StructuralTerm(string code, string value) =>
        new(code, code, "Text", false, null, null, null, null, value, null, null, null, value, 2, null);

    private static OfferTermResponse Term(OfferTermRequest request) =>
        request.Value.Kind == "Money"
            ? MoneyTerm(request.Code, request.Value.Amount ?? 0m, request.Value.Currency ?? "USD")
            : StructuralTerm(request.Code, request.Value.Text ?? string.Empty);

    // ---- Contracts, rights, options and obligations (M8) ----

    public List<ContractSummaryResponse> Contracts { get; } = [];

    public Dictionary<Guid, ContractDetailResponse> ContractDetails { get; } = [];

    public List<ContractHistoryEntryResponse> ContractHistory { get; } = [];

    public List<RightsGrantResponse> RightsGrants { get; } = [];

    public List<ContractOptionResponse> ContractOptions { get; } = [];

    public List<ObligationResponse> Obligations { get; } = [];

    public List<LegalDeadlineResponse> LegalDeadlines { get; } = [];

    /// <summary>The reconciliation the fake hands back, so a diff can be simulated.</summary>
    public ReconciliationResponse? Reconciliation { get; set; }

    /// <summary>The filter the last contract list call actually sent.</summary>
    public (string? Status, string? Kind, bool Awaiting, bool Effective, bool Differing, string? Search)
        LastContractFilter
    { get; private set; }

    public ContractCommandCenterResponse LegalCommandCenter { get; set; } =
        new([], [], [], [], [], [], [], [], [], 0, 0, 0);

    public Task<IReadOnlyList<ContractSummaryResponse>> ListContractsAsync(
        string? status = null,
        string? kind = null,
        Guid? dealId = null,
        bool awaitingSignature = false,
        bool effectiveOnly = false,
        bool hasUnresolvedReconciliation = false,
        string? search = null,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastContractFilter =
            (status, kind, awaitingSignature, effectiveOnly, hasUnresolvedReconciliation, search);

        IEnumerable<ContractSummaryResponse> contracts = Contracts;

        if (status is { Length: > 0 })
        {
            contracts = contracts.Where(x => x.Status == status);
        }

        if (awaitingSignature)
        {
            contracts = contracts.Where(x => x.OutstandingSignatureCount > 0);
        }

        if (effectiveOnly)
        {
            contracts = contracts.Where(x => x.IsEffective);
        }

        if (hasUnresolvedReconciliation)
        {
            contracts = contracts.Where(x => x.UnresolvedDifferenceCount > 0);
        }

        return Task.FromResult<IReadOnlyList<ContractSummaryResponse>>([.. contracts]);
    }

    public Task<ContractDetailResponse> GetContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(ContractDetails[contractId]);
    }

    public Task<IReadOnlyList<ContractHistoryEntryResponse>> GetContractHistoryAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<ContractHistoryEntryResponse>>([.. ContractHistory]);
    }

    public Task<ContractDetailResponse> CreateContractAsync(
        CreateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(ContractDetails.Values.First());
    }

    public Task UpdateContractAsync(
        Guid contractId,
        UpdateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ChangeContractStatusAsync(
        Guid contractId,
        ChangeContractStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task RecordContractEffectiveDateAsync(
        Guid contractId,
        RecordEffectiveDateRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<AddContractPartyResponse> AddContractPartyAsync(
        Guid contractId,
        AddContractPartyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new AddContractPartyResponse(Guid.NewGuid()));
    }

    public Task<RecordSignatureResponse> RecordContractSignatureAsync(
        Guid contractId,
        RecordSignatureRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordSignatureResponse(Guid.NewGuid(), "PartiallyExecuted", 1));
    }

    public Task RecordContractRelationshipAsync(
        Guid contractId,
        RecordContractRelationshipRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<RecordContractVersionResponse> RecordContractVersionAsync(
        Guid contractId,
        RecordContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordContractVersionResponse(Guid.NewGuid(), 1));
    }

    public Task<ContractVersionResponse> GetContractVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            ContractDetails.Values.SelectMany(x => x.Versions).First(x => x.Id == versionId));
    }

    public Task ChangeContractTermAsync(
        Guid versionId,
        ChangeContractTermRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task FinaliseContractVersionAsync(
        Guid versionId,
        FinaliseContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<ReconciliationResponse> ReconcileContractVersionAsync(
        Guid contractId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Reconciliation!);
    }

    public Task<IReadOnlyList<RightsGrantResponse>> ListRightsGrantsAsync(
        Guid? contractId = null,
        Guid? projectId = null,
        bool currentOnly = true,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<RightsGrantResponse> grants = RightsGrants;

        if (currentOnly)
        {
            grants = grants.Where(x => x.Status == "Active");
        }

        return Task.FromResult<IReadOnlyList<RightsGrantResponse>>([.. grants]);
    }

    public Task<RecordRightsGrantResponse> RecordRightsGrantAsync(
        Guid contractId,
        RecordRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordRightsGrantResponse(Guid.NewGuid()));
    }

    public Task EndRightsGrantAsync(
        Guid grantId,
        EndRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContractOptionResponse>> ListContractOptionsAsync(
        Guid? contractId = null,
        string? status = null,
        bool exercisableOnly = false,
        bool pastDeadlineOnly = false,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<ContractOptionResponse> options = ContractOptions;

        if (status is { Length: > 0 })
        {
            options = options.Where(x => x.Status == status);
        }

        if (exercisableOnly)
        {
            options = options.Where(x => x.IsExercisable);
        }

        if (pastDeadlineOnly)
        {
            options = options.Where(x => x.IsPastDeadline);
        }

        return Task.FromResult<IReadOnlyList<ContractOptionResponse>>([.. options]);
    }

    public Task<RecordOptionResponse> RecordContractOptionAsync(
        Guid contractId,
        RecordOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordOptionResponse(Guid.NewGuid()));
    }

    public Task<ResolveOptionResponse> ResolveContractOptionAsync(
        Guid optionId,
        ResolveOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new ResolveOptionResponse(optionId, request.Outcome));
    }

    public Task<IReadOnlyList<ObligationResponse>> ListObligationsAsync(
        Guid? contractId = null,
        string? status = null,
        bool outstandingOnly = false,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<ObligationResponse> obligations = Obligations;

        if (status is { Length: > 0 })
        {
            obligations = obligations.Where(x => x.Status == status);
        }

        if (outstandingOnly)
        {
            obligations = obligations.Where(x => x.Status == "Pending");
        }

        if (overdueOnly)
        {
            obligations = obligations.Where(x => x.IsPastDue);
        }

        return Task.FromResult<IReadOnlyList<ObligationResponse>>([.. obligations]);
    }

    public Task<RecordObligationResponse> RecordObligationAsync(
        Guid contractId,
        RecordObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordObligationResponse(Guid.NewGuid()));
    }

    public Task<ResolveObligationResponse> ResolveObligationAsync(
        Guid obligationId,
        ResolveObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new ResolveObligationResponse(obligationId, request.Outcome));
    }

    public Task<RecordNoticeRequirementResponse> RecordNoticeRequirementAsync(
        Guid contractId,
        RecordNoticeRequirementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordNoticeRequirementResponse(Guid.NewGuid()));
    }

    public Task<RecordNoticeResponse> RecordNoticeAsync(
        Guid contractId,
        RecordNoticeRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new RecordNoticeResponse(Guid.NewGuid()));
    }

    public Task<CreateContractTaskResponse> CreateContractTaskAsync(
        Guid contractId,
        CreateContractTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new CreateContractTaskResponse(Guid.NewGuid()));
    }

    public Task<IReadOnlyList<LegalDeadlineResponse>> ListLegalDeadlinesAsync(
        int? withinDays = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<LegalDeadlineResponse>>([.. LegalDeadlines]);
    }

    public Task<ContractCommandCenterResponse> GetLegalCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(LegalCommandCenter);
    }

    public Task<IReadOnlyList<ContractTermDefinitionResponse>> ListContractTermsAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<ContractTermDefinitionResponse>>([]);
    }

    /// <summary>A contract summary with only the fields a test cares about set.</summary>
    internal static ContractSummaryResponse Contract(
        string title,
        string status = "Draft",
        int outstandingSignatures = 0,
        bool effective = false,
        int? differences = null,
        DateOnly? nextDeadline = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            null,
            "LongForm",
            status,
            Guid.NewGuid(),
            "A negotiation",
            Guid.NewGuid(),
            null,
            "Northgate Pictures",
            Guid.NewGuid(),
            null,
            status == "Executed" ? new DateOnly(2027, 5, 1) : null,
            effective ? new DateOnly(2027, 5, 1) : null,
            null,
            effective,
            1,
            1,
            "Execution copy",
            Guid.NewGuid(),
            outstandingSignatures,
            2,
            0,
            0,
            0,
            0,
            nextDeadline,
            nextDeadline is null ? null : "Delivery",
            differences,
            DateTimeOffset.UtcNow,
            1);

    /// <summary>A term the caller may read the value of.</summary>
    internal static ContractTermResponse ContractTerm(
        string code,
        string value,
        bool economic = false) =>
        new(
            code,
            code,
            "Text",
            economic,
            true,
            null,
            null,
            null,
            null,
            value,
            null,
            null,
            null,
            value,
            null,
            1,
            null);

    // ---- Finance, commissions, receivables, payments and ledger (M9) ----
    //
    // The fake keeps the derived figures derived. Outstanding, unapplied and
    // collected are computed here from the rows, exactly as the server computes
    // them from the database, so a view model that quietly relied on a stored
    // total would fail against this rather than passing and failing in production
    // (ADR-0023).

    public List<MonetaryObligationResponse> MonetaryObligations { get; } = [];

    public List<ReceivableResponse> Receivables { get; } = [];

    public List<InvoiceResponse> Invoices { get; } = [];

    public List<PaymentResponse> Payments { get; } = [];

    public List<CommissionRuleResponse> CommissionRules { get; } = [];

    public List<CommissionEntitlementResponse> Commissions { get; } = [];

    public List<AccountResponse> LedgerAccounts { get; } = [];

    public List<AccountBalanceResponse> LedgerBalances { get; } = [];

    public List<JournalEntryResponse> JournalEntries { get; } = [];

    public List<FinanceHistoryEntryResponse> FinanceHistory { get; } = [];

    /// <summary>The reconciliation the fake hands back, so a variance can be simulated.</summary>
    public ReceivableReconciliationResponse? ReceivableReconciliation { get; set; }

    public FinanceCommandCenterResponse FinanceCommandCenter { get; set; } =
        new([], [], [], [], [], [], [], [], [], [], 0, 0);

    /// <summary>Payments the fake was asked to record, so a test can assert the request.</summary>
    public List<RecordPaymentRequest> RecordedPayments { get; } = [];

    /// <summary>The filter the last receivable list call actually sent.</summary>
    public (string? Status, string? Beneficiary, bool Overdue, bool Unreconciled, string? Currency)
        LastReceivableFilter
    { get; private set; }

    /// <summary>The filter the last payment list call actually sent.</summary>
    public (string? Direction, bool UnappliedOnly, string? Currency) LastPaymentFilter
    { get; private set; }

    public Task<IReadOnlyList<MonetaryObligationResponse>> ListMonetaryObligationsAsync(
        Guid? contractId = null,
        bool unbilledOnly = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<MonetaryObligationResponse> obligations = MonetaryObligations;

        if (contractId is { } contract)
        {
            obligations = obligations.Where(x => x.ContractId == contract);
        }

        if (unbilledOnly)
        {
            obligations = obligations.Where(x => x.IsQuantified && !x.HasReceivable);
        }

        return Task.FromResult<IReadOnlyList<MonetaryObligationResponse>>([.. obligations]);
    }

    public Task<MonetaryObligationResponse> GetMonetaryObligationAsync(
        Guid obligationId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(MonetaryObligations.Single(x => x.Id == obligationId));
    }

    public Task<RecordMonetaryObligationResponse> RecordMonetaryObligationAsync(
        Guid contractId,
        RecordMonetaryObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new RecordMonetaryObligationResponse(Guid.NewGuid()));
    }

    public Task QuantifyObligationAsync(
        Guid obligationId,
        QuantifyObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task ReleaseObligationAsync(
        Guid obligationId,
        ReleaseObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReceivableResponse>> ListReceivablesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? payerPartyId = null,
        Guid? clientPersonId = null,
        string? beneficiary = null,
        bool overdueOnly = false,
        bool unreconciledOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastReceivableFilter = (status, beneficiary, overdueOnly, unreconciledOnly, currency);

        IEnumerable<ReceivableResponse> receivables = Receivables;

        if (status is { Length: > 0 })
        {
            receivables = receivables.Where(x => x.Status == status);
        }

        if (beneficiary is { Length: > 0 })
        {
            receivables = receivables.Where(x => x.Beneficiary == beneficiary);
        }

        if (overdueOnly)
        {
            receivables = receivables.Where(x => x.IsOverdue);
        }

        if (currency is { Length: > 0 })
        {
            receivables = receivables.Where(x => x.Outstanding.Currency == currency);
        }

        return Task.FromResult<IReadOnlyList<ReceivableResponse>>([.. receivables]);
    }

    public Task<ReceivableResponse> GetReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Receivables.Single(x => x.Id == receivableId));
    }

    public Task<RaiseReceivableResponse> RaiseReceivableAsync(
        Guid obligationId,
        RaiseReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new RaiseReceivableResponse(Guid.NewGuid()));
    }

    public Task WriteOffReceivableAsync(
        Guid receivableId,
        WriteOffReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task CancelReceivableAsync(
        Guid receivableId,
        CancelReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<RecordAdjustmentResponse> RecordAdjustmentAsync(
        Guid receivableId,
        RecordAdjustmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new RecordAdjustmentResponse(Guid.NewGuid()));
    }

    public Task<ReceivableReconciliationResponse> ReconcileReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(
            ReceivableReconciliation
            ?? throw new InvalidOperationException("No reconciliation was set on the fake."));
    }

    public Task<IReadOnlyList<InvoiceResponse>> ListInvoicesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? debtorPartyId = null,
        bool overdueOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<InvoiceResponse> invoices = Invoices;

        if (status is { Length: > 0 })
        {
            invoices = invoices.Where(x => x.Status == status);
        }

        if (overdueOnly)
        {
            invoices = invoices.Where(x => x.IsOverdue);
        }

        return Task.FromResult<IReadOnlyList<InvoiceResponse>>([.. invoices]);
    }

    public Task<InvoiceResponse> GetInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Invoices.Single(x => x.Id == invoiceId));
    }

    public Task<RecordInvoiceResponse> RecordInvoiceAsync(
        Guid contractId,
        RecordInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new RecordInvoiceResponse(Guid.NewGuid()));
    }

    public Task IssueInvoiceAsync(
        Guid invoiceId,
        IssueInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(
        string? direction = null,
        string? status = null,
        Guid? payerPartyId = null,
        bool unappliedOnly = false,
        DateOnly? recordedAfter = null,
        DateOnly? recordedBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastPaymentFilter = (direction, unappliedOnly, currency);

        IEnumerable<PaymentResponse> payments = Payments;

        if (direction is { Length: > 0 })
        {
            payments = payments.Where(x => x.Direction == direction);
        }

        if (status is { Length: > 0 })
        {
            payments = payments.Where(x => x.Status == status);
        }

        if (unappliedOnly)
        {
            payments = payments.Where(x => x.Unapplied.Amount > 0m);
        }

        if (currency is { Length: > 0 })
        {
            payments = payments.Where(x => x.Amount.Currency == currency);
        }

        return Task.FromResult<IReadOnlyList<PaymentResponse>>([.. payments]);
    }

    public Task<PaymentResponse> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Payments.Single(x => x.Id == paymentId));
    }

    /// <summary>
    /// Records a payment and reports what was left over.
    /// </summary>
    /// <remarks>
    /// The residual is computed rather than echoed, so a caller that allocates less
    /// than it received gets a real unapplied figure back and a test can assert the
    /// UI reports it (ADR-0023).
    /// </remarks>
    public Task<RecordPaymentResponse> RecordPaymentAsync(
        RecordPaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        ArgumentNullException.ThrowIfNull(request);

        RecordedPayments.Add(request);

        decimal allocated = request.Allocations?.Sum(x => x.Amount.Amount) ?? 0m;
        string currency = request.Amount.Currency;

        return Task.FromResult(new RecordPaymentResponse(
            Guid.NewGuid(),
            new MoneyResponse(allocated, currency),
            new MoneyResponse(request.Amount.Amount - allocated, currency),
            [
                .. Payments
                    .Where(x =>
                        request.ExternalReference is { Length: > 0 }
                        && x.ExternalReference == request.ExternalReference)
                    .Select(x => x.Id),
            ]));
    }

    public Task<RecordPaymentResponse> AllocatePaymentAsync(
        Guid paymentId,
        AllocatePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        ArgumentNullException.ThrowIfNull(request);

        PaymentResponse payment = Payments.Single(x => x.Id == paymentId);
        decimal allocated = request.Allocations.Sum(x => x.Amount.Amount);

        return Task.FromResult(new RecordPaymentResponse(
            paymentId,
            new MoneyResponse(allocated, payment.Amount.Currency),
            new MoneyResponse(payment.Unapplied.Amount - allocated, payment.Amount.Currency),
            []));
    }

    public Task ReverseAllocationAsync(
        Guid paymentId,
        ReverseAllocationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<ReversePaymentResponse> ReversePaymentAsync(
        Guid paymentId,
        ReversePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new ReversePaymentResponse(Guid.NewGuid()));
    }

    public Task<IReadOnlyList<CommissionRuleResponse>> ListCommissionRulesAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<CommissionRuleResponse> rules = CommissionRules;

        if (clientPersonId is { } client)
        {
            rules = rules.Where(x => x.ClientPersonId == client);
        }

        return Task.FromResult<IReadOnlyList<CommissionRuleResponse>>([.. rules]);
    }

    public Task<CreateCommissionRuleResponse> CreateCommissionRuleAsync(
        CreateCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new CreateCommissionRuleResponse(Guid.NewGuid()));
    }

    public Task EndCommissionRuleAsync(
        Guid ruleId,
        EndCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CommissionEntitlementResponse>> ListCommissionsAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        Guid? representationId = null,
        string? status = null,
        bool outstandingOnly = false,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<CommissionEntitlementResponse> commissions = Commissions;

        if (clientPersonId is { } client)
        {
            commissions = commissions.Where(x => x.ClientPersonId == client);
        }

        if (status is { Length: > 0 })
        {
            commissions = commissions.Where(x => x.Status == status);
        }

        if (outstandingOnly)
        {
            commissions = commissions.Where(x => x.Outstanding.Amount > 0m);
        }

        return Task.FromResult<IReadOnlyList<CommissionEntitlementResponse>>([.. commissions]);
    }

    public Task<CommissionEntitlementResponse> GetCommissionAsync(
        Guid commissionId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Commissions.Single(x => x.Id == commissionId));
    }

    public Task<CalculateCommissionResponse> CalculateCommissionAsync(
        Guid obligationId,
        CalculateCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new CalculateCommissionResponse(Guid.NewGuid()));
    }

    public Task AdjustCommissionAsync(
        Guid commissionId,
        AdjustCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountResponse>> ListLedgerAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<AccountResponse>>([.. LedgerAccounts]);
    }

    public Task<IReadOnlyList<AccountBalanceResponse>> GetLedgerBalancesAsync(
        string? currency = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<AccountBalanceResponse> balances = LedgerBalances;

        if (currency is { Length: > 0 })
        {
            balances = balances.Where(x => x.Currency == currency);
        }

        return Task.FromResult<IReadOnlyList<AccountBalanceResponse>>([.. balances]);
    }

    public Task<IReadOnlyList<JournalEntryResponse>> ListJournalEntriesAsync(
        string? status = null,
        string? source = null,
        Guid? accountId = null,
        DateOnly? postedAfter = null,
        DateOnly? postedBefore = null,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<JournalEntryResponse> entries = JournalEntries;

        if (status is { Length: > 0 })
        {
            entries = entries.Where(x => x.Status == status);
        }

        if (source is { Length: > 0 })
        {
            entries = entries.Where(x => x.Source == source);
        }

        if (currency is { Length: > 0 })
        {
            entries = entries.Where(x => x.Currency == currency);
        }

        return Task.FromResult<IReadOnlyList<JournalEntryResponse>>([.. entries]);
    }

    public Task<JournalEntryResponse> GetJournalEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(JournalEntries.Single(x => x.Id == entryId));
    }

    public Task<PostJournalEntryResponse> PostJournalEntryAsync(
        PostJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new PostJournalEntryResponse(Guid.NewGuid()));
    }

    public Task<ReverseJournalEntryResponse> ReverseJournalEntryAsync(
        Guid entryId,
        ReverseJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new ReverseJournalEntryResponse(Guid.NewGuid()));
    }

    public Task<IReadOnlyList<FinanceHistoryEntryResponse>> GetFinanceHistoryAsync(
        Guid? contractId = null,
        Guid? receivableId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<FinanceHistoryEntryResponse>>([.. FinanceHistory]);
    }

    public Task<FinanceCommandCenterResponse> GetFinanceCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(FinanceCommandCenter);
    }

    // ---- Finance builders, so tests read as arrangements rather than as ceremony ----

    internal static MoneyResponse Money(decimal amount, string currency = "USD") =>
        new(amount, currency);

    /// <summary>A receivable with its arithmetic already consistent.</summary>
    internal static ReceivableResponse Receivable(
        decimal original,
        decimal allocated = 0m,
        decimal adjusted = 0m,
        string currency = "USD",
        string beneficiary = "Client",
        string? status = null,
        bool overdue = false,
        DateOnly? dueOn = null,
        Guid? id = null,
        Guid? contractId = null,
        string contractTitle = "Feature deal")
    {
        decimal outstanding = original - allocated - adjusted;

        return new ReceivableResponse(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            contractId ?? Guid.NewGuid(),
            contractTitle,
            Guid.NewGuid(),
            "Studio",
            beneficiary,
            Guid.NewGuid(),
            "Client",
            Money(original, currency),
            Money(allocated, currency),
            Money(adjusted, currency),
            Money(outstanding, currency),
            dueOn,
            status ?? (outstanding <= 0m ? "Paid" : allocated > 0m ? "PartiallyPaid" : "Open"),
            overdue,
            "AR-1",
            null,
            null,
            DateTimeOffset.UtcNow,
            1);
    }

    /// <summary>A payment whose unapplied figure follows from its allocations.</summary>
    internal static PaymentResponse Payment(
        decimal amount,
        decimal allocated = 0m,
        string currency = "USD",
        string status = "Recorded",
        string? externalReference = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "Incoming",
            Guid.NewGuid(),
            "Studio",
            null,
            null,
            Money(amount, currency),
            Money(allocated, currency),
            Money(amount - allocated, currency),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow,
            "BankTransfer",
            externalReference,
            null,
            status,
            null,
            null,
            null,
            "Operator",
            null,
            [],
            1);

    /// <summary>An entitlement whose three figures are kept apart.</summary>
    internal static CommissionEntitlementResponse Commission(
        decimal basis,
        decimal entitled,
        decimal collected,
        string currency = "USD",
        string status = "Calculated") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Feature deal",
            Guid.NewGuid(),
            "Client",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "GrossCompensation",
            10m,
            Money(basis, currency),
            Money(entitled, currency),
            Money(collected, currency),
            Money(0m, currency),
            Money(entitled - collected, currency),
            DateOnly.FromDateTime(DateTime.UtcNow),
            status,
            [],
            DateTimeOffset.UtcNow,
            "Operator",
            null,
            1);

    /// <summary>A balanced journal entry.</summary>
    internal static JournalEntryResponse JournalEntry(
        decimal amount,
        string currency = "USD",
        string status = "Posted",
        string source = "PaymentRecorded",
        bool balanced = true,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            status,
            source,
            "Payment recorded",
            currency,
            Money(amount, currency),
            Money(balanced ? amount : amount - 1m, currency),
            balanced,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Operator",
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            1);

    // ---- Documents and communications (M10) ----
    //
    // The fake holds real bytes and hashes them, because that is where the client
    // behaviour worth testing is: an upload that becomes version N+1 rather than
    // replacing version N, and a download whose digest can be checked. A fake that
    // returned a constant string for the hash would let a view model that ignored
    // the digest pass here and fail against a real file.

    public List<DocumentSummaryResponse> Documents { get; } = [];

    /// <summary>Details a test has arranged, when the derived one is not enough.</summary>
    public Dictionary<Guid, DocumentDetailResponse> DocumentDetails { get; } = [];

    /// <summary>Version bytes, keyed by version id.</summary>
    public Dictionary<Guid, byte[]> VersionContent { get; } = [];

    /// <summary>Versions per document, oldest first, so a sequence can be asserted.</summary>
    public Dictionary<Guid, List<DocumentVersionResponse>> DocumentVersions { get; } = [];

    public List<CommunicationAccountResponse> CommunicationAccounts { get; } = [];

    public List<MessageSummaryResponse> Messages { get; } = [];

    public Dictionary<Guid, MessageDetailResponse> MessageDetails { get; } = [];

    public List<OutboundDispatchResponse> OutboundMessages { get; } = [];

    public List<CommunicationEventResponse> CommunicationHistory { get; } = [];

    public List<ParticipantSuggestionResponse> ParticipantSuggestions { get; } = [];

    public CommunicationCommandCenterResponse CommunicationCommandCenter { get; set; } =
        new([], [], [], 0, 0, 0);

    /// <summary>Uploads that reached the fake, so a test can assert bytes and version.</summary>
    public List<(Guid DocumentId, string FileName, int ExpectedVersion, byte[] Content)> Uploads
    { get; } = [];

    /// <summary>Compose requests, so a test can assert what was actually asked for.</summary>
    public List<ComposeMessageRequest> Composed { get; } = [];

    /// <summary>Dispatches the fake was asked to queue, in order.</summary>
    public List<Guid> QueuedDispatches { get; } = [];

    /// <summary>Dispatches the fake was asked to cancel, in order.</summary>
    public List<Guid> CancelledDispatches { get; } = [];

    /// <summary>Documents the fake was asked to archive, in order.</summary>
    public List<Guid> ArchivedDocuments { get; } = [];

    /// <summary>The filter the last document list call actually sent.</summary>
    public (string? Kind, string? Status, string? Sensitivity, string? LinkedTarget,
        Guid? LinkedTargetId, bool HasContent, string? Search) LastDocumentFilter
    { get; private set; }

    /// <summary>The filter the last message list call actually sent.</summary>
    public (Guid? AccountId, string? Direction, string? LinkedTarget, Guid? LinkedTargetId,
        bool UnlinkedOnly, bool HasAttachments, string? Search) LastMessageFilter
    { get; private set; }

    public Task<IReadOnlyList<DocumentSummaryResponse>> ListDocumentsAsync(
        string? kind = null,
        string? status = null,
        string? sensitivity = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool hasContent = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastDocumentFilter =
            (kind, status, sensitivity, linkedTarget, linkedTargetId, hasContent, search);

        IEnumerable<DocumentSummaryResponse> documents = Documents;

        if (kind is not null)
        {
            documents = documents.Where(x => x.Kind == kind);
        }

        if (status is not null)
        {
            documents = documents.Where(x => x.Status == status);
        }

        if (sensitivity is not null)
        {
            documents = documents.Where(x => x.Sensitivity == sensitivity);
        }

        if (hasContent)
        {
            documents = documents.Where(x => x.HoldsContent);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            documents = documents.Where(x =>
                x.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (limit is { } take)
        {
            documents = documents.Take(take);
        }

        return Task.FromResult<IReadOnlyList<DocumentSummaryResponse>>([.. documents]);
    }

    public Task<DocumentDetailResponse> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (DocumentDetails.TryGetValue(documentId, out DocumentDetailResponse? arranged))
        {
            return Task.FromResult(arranged);
        }

        DocumentSummaryResponse summary = Documents.Single(x => x.Id == documentId);

        return Task.FromResult(new DocumentDetailResponse(
            summary,
            null,
            null,
            DocumentVersions.TryGetValue(documentId, out List<DocumentVersionResponse>? versions)
                ? [.. versions]
                : [],
            [],
            [],
            null));
    }

    public async Task<RecordDocumentResponse> RecordDocumentAsync(
        RecordDocumentRequest request,
        Stream content,
        string fileName,
        string? mediaType = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);
        string hash = Digest(bytes);

        Guid documentId = Guid.NewGuid();
        bool deduplicated = VersionContent.Values.Any(existing => Digest(existing) == hash);

        DocumentVersionResponse version = DocumentVersion(
            1, fileName, mediaType ?? "application/octet-stream", bytes, hash);

        VersionContent[version.Id] = bytes;
        DocumentVersions[documentId] = [version];
        Uploads.Add((documentId, fileName, 0, bytes));

        Documents.Add(new DocumentSummaryResponse(
            documentId,
            request.Title,
            request.Kind,
            "Active",
            request.Sensitivity ?? "Internal",
            request.Reference,
            version,
            1,
            request.Links?.Count ?? 0,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Operator",
            1));

        return new RecordDocumentResponse(
            documentId, version.Id, hash, bytes.LongLength, deduplicated);
    }

    public async Task<RecordDocumentResponse> AddDocumentVersionAsync(
        Guid documentId,
        Stream content,
        string fileName,
        int expectedVersion,
        string? mediaType = null,
        string? notes = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);
        string hash = Digest(bytes);

        int index = Documents.FindIndex(x => x.Id == documentId);
        DocumentSummaryResponse existing = Documents[index];

        // The fake refuses a stale write for the same reason the server does. A view
        // model that forgot to carry the version forward would otherwise pass here.
        if (existing.Version != expectedVersion)
        {
            throw new AgencyOsApiException(
                HttpStatusCode.Conflict,
                "The document changed since it was read.",
                code: "version_conflict",
                expectedVersion: expectedVersion,
                actualVersion: existing.Version);
        }

        List<DocumentVersionResponse> versions = DocumentVersions.TryGetValue(
            documentId, out List<DocumentVersionResponse>? held) ? held : [];

        DocumentVersionResponse version = DocumentVersion(
            versions.Count + 1,
            fileName,
            mediaType ?? "application/octet-stream",
            bytes,
            hash,
            notes);

        // Appended. The earlier versions and their bytes stay exactly where they are.
        versions.Add(version);
        DocumentVersions[documentId] = versions;
        VersionContent[version.Id] = bytes;
        Uploads.Add((documentId, fileName, expectedVersion, bytes));

        Documents[index] = existing with
        {
            CurrentVersion = version,
            VersionCount = versions.Count,
            HoldsContent = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = existing.Version + 1,
        };

        return new RecordDocumentResponse(
            documentId, version.Id, hash, bytes.LongLength, false);
    }

    public Task<DocumentContent> DownloadDocumentVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        byte[] bytes = VersionContent[versionId];

        DocumentVersionResponse version = DocumentVersions.Values
            .SelectMany(x => x)
            .Single(x => x.Id == versionId);

        return Task.FromResult(new DocumentContent(
            new MemoryStream(bytes, writable: false),
            version.DisplayFileName,
            version.MediaType,
            bytes.LongLength,
            version.ContentHash));
    }

    public Task UpdateDocumentAsync(
        Guid documentId,
        UpdateDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Documents.FindIndex(x => x.Id == documentId);

        if (index >= 0)
        {
            DocumentSummaryResponse existing = Documents[index];

            Documents[index] = existing with
            {
                Title = request.Title ?? existing.Title,
                Kind = request.Kind ?? existing.Kind,
                Sensitivity = request.Sensitivity ?? existing.Sensitivity,
                Reference = request.Reference ?? existing.Reference,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = existing.Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<LinkDocumentResponse> LinkDocumentAsync(
        Guid documentId,
        LinkDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new LinkDocumentResponse(Guid.NewGuid()));
    }

    public Task UnlinkDocumentAsync(
        Guid documentId,
        Guid linkId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ArchiveDocumentAsync(
        Guid documentId,
        ArchiveDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        ArchivedDocuments.Add(documentId);

        int index = Documents.FindIndex(x => x.Id == documentId);

        if (index >= 0)
        {
            // Archived, not destroyed: the bytes and every version stay exactly where
            // they were, which is the whole distinction M10 draws (ADR-0024).
            Documents[index] = Documents[index] with
            {
                Status = "Archived",
                Version = Documents[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task RestoreDocumentAsync(
        Guid documentId,
        RestoreDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Documents.FindIndex(x => x.Id == documentId);

        if (index >= 0)
        {
            Documents[index] = Documents[index] with
            {
                Status = "Active",
                Version = Documents[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>Providers a test has arranged, so a connect flow can be exercised.</summary>
    public List<CommunicationProviderResponse> CommunicationProviders { get; } = [];

    public Task<IReadOnlyList<CommunicationProviderResponse>> ListCommunicationProvidersAsync(
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<CommunicationProviderResponse>>(
            [.. CommunicationProviders]);
    }

    public Task<IReadOnlyList<CommunicationAccountResponse>> ListCommunicationAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<CommunicationAccountResponse>>(
            [.. CommunicationAccounts]);
    }

    public Task<ConnectMailboxResponse> ConnectMailboxAsync(
        ConnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.FromResult(new ConnectMailboxResponse(Guid.NewGuid()));
    }

    public Task DisconnectMailboxAsync(
        Guid accountId,
        DisconnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = CommunicationAccounts.FindIndex(x => x.Id == accountId);

        if (index >= 0)
        {
            CommunicationAccounts[index] = CommunicationAccounts[index] with
            {
                State = "Disconnected",
                HasStoredCredential = false,
                Version = CommunicationAccounts[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task ChangeMailboxVisibilityAsync(
        Guid accountId,
        ChangeMailboxVisibilityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = CommunicationAccounts.FindIndex(x => x.Id == accountId);

        if (index >= 0)
        {
            CommunicationAccounts[index] = CommunicationAccounts[index] with
            {
                Visibility = request.Visibility,
                Version = CommunicationAccounts[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MessageSummaryResponse>> ListMessagesAsync(
        Guid? accountId = null,
        string? direction = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool unlinkedOnly = false,
        bool hasAttachments = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastMessageFilter = (
            accountId, direction, linkedTarget, linkedTargetId,
            unlinkedOnly, hasAttachments, search);

        IEnumerable<MessageSummaryResponse> messages = Messages;

        if (accountId is { } account)
        {
            messages = messages.Where(x => x.AccountId == account);
        }

        if (direction is not null)
        {
            messages = messages.Where(x => x.Direction == direction);
        }

        if (unlinkedOnly)
        {
            messages = messages.Where(x => x.LinkCount == 0);
        }

        if (hasAttachments)
        {
            messages = messages.Where(x => x.HasAttachments);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            messages = messages.Where(x =>
                x.Subject?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (limit is { } take)
        {
            messages = messages.Take(take);
        }

        return Task.FromResult<IReadOnlyList<MessageSummaryResponse>>([.. messages]);
    }

    public Task<MessageDetailResponse> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (MessageDetails.TryGetValue(messageId, out MessageDetailResponse? arranged))
        {
            return Task.FromResult(arranged);
        }

        MessageSummaryResponse summary = Messages.Single(x => x.Id == messageId);

        return Task.FromResult(new MessageDetailResponse(
            summary, null, null, null, null, [], [], [], [summary]));
    }

    public Task<LinkMessageResponse> LinkMessageAsync(
        Guid messageId,
        LinkMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        int index = Messages.FindIndex(x => x.Id == messageId);

        if (index >= 0)
        {
            Messages[index] = Messages[index] with { LinkCount = Messages[index].LinkCount + 1 };
        }

        return Task.FromResult(new LinkMessageResponse(Guid.NewGuid()));
    }

    public Task UnlinkMessageAsync(
        Guid messageId,
        Guid linkId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.CompletedTask;
    }

    public Task ResolveParticipantAsync(
        Guid messageId,
        Guid participantId,
        ResolveParticipantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ParticipantSuggestionResponse>> SuggestParticipantsAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<ParticipantSuggestionResponse>>(
            [.. ParticipantSuggestions.Where(x =>
                string.Equals(x.MatchedAddress, address, StringComparison.OrdinalIgnoreCase))]);
    }

    public Task<IngestAttachmentResponse> IngestAttachmentAsync(
        Guid attachmentId,
        IngestAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        byte[] bytes = [1, 2, 3];

        return Task.FromResult(new IngestAttachmentResponse(
            Guid.NewGuid(), Guid.NewGuid(), Digest(bytes), bytes.LongLength));
    }

    public Task<IReadOnlyList<OutboundDispatchResponse>> ListOutboundMessagesAsync(
        string? state = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<OutboundDispatchResponse> dispatches = OutboundMessages;

        if (state is not null)
        {
            dispatches = dispatches.Where(x => x.State == state);
        }

        if (limit is { } take)
        {
            dispatches = dispatches.Take(take);
        }

        return Task.FromResult<IReadOnlyList<OutboundDispatchResponse>>([.. dispatches]);
    }

    public Task<OutboundDispatchResponse> GetOutboundMessageAsync(
        Guid dispatchId,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(OutboundMessages.Single(x => x.Id == dispatchId));
    }

    public Task<ComposeMessageResponse> ComposeMessageAsync(
        ComposeMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        Composed.Add(request);

        Guid dispatchId = Guid.NewGuid();

        // Composed, not queued. Nothing has left, which is what the interface must
        // be able to say truthfully (ADR-0028).
        OutboundMessages.Add(Dispatch("Draft", request.Subject, dispatchId));

        return Task.FromResult(new ComposeMessageResponse(dispatchId));
    }

    public Task QueueMessageAsync(
        Guid dispatchId,
        QueueMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        QueuedDispatches.Add(dispatchId);

        int index = OutboundMessages.FindIndex(x => x.Id == dispatchId);

        if (index >= 0)
        {
            OutboundMessages[index] = OutboundMessages[index] with
            {
                State = "Queued",
                Version = OutboundMessages[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task CancelMessageAsync(
        Guid dispatchId,
        CancelMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        CancelledDispatches.Add(dispatchId);

        int index = OutboundMessages.FindIndex(x => x.Id == dispatchId);

        if (index >= 0)
        {
            OutboundMessages[index] = OutboundMessages[index] with
            {
                State = "Cancelled",
                Version = OutboundMessages[index].Version + 1,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CommunicationEventResponse>> GetCommunicationHistoryAsync(
        Guid? accountId = null,
        Guid? dispatchId = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult<IReadOnlyList<CommunicationEventResponse>>(
            [.. CommunicationHistory]);
    }

    public Task<CommunicationCommandCenterResponse> GetCommunicationCommandCenterAsync(
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(CommunicationCommandCenter);
    }

    // ---- Document and communication builders ----

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();

        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    /// <summary>The real digest of the real bytes, exactly as the server computes it.</summary>
    private static string Digest(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static DocumentVersionResponse DocumentVersion(
        int sequence,
        string fileName,
        string mediaType,
        byte[] bytes,
        string hash,
        string? notes = null) =>
        new(
            Guid.NewGuid(),
            sequence,
            fileName,
            mediaType,
            bytes.LongLength,
            hash,
            "Upload",
            null,
            DateTimeOffset.UtcNow,
            "Operator",
            notes,
            "NotAttempted",
            null,
            // Unscanned, because nothing scanned it. The fake will not say Clean for
            // the same reason the server will not.
            "Unscanned");

    /// <summary>A document summary whose derived counts follow from its versions.</summary>
    internal static DocumentSummaryResponse DocumentSummary(
        string title = "Executed agreement",
        string kind = "Contract",
        string status = "Active",
        string sensitivity = "Internal",
        bool holdsContent = true,
        int versionCount = 1,
        int linkCount = 0,
        Guid? id = null)
    {
        byte[] bytes = [7, 7, 7];

        return new DocumentSummaryResponse(
            id ?? Guid.NewGuid(),
            title,
            kind,
            status,
            sensitivity,
            null,
            holdsContent
                ? DocumentVersion(versionCount, $"{title}.pdf", "application/pdf", bytes, Digest(bytes))
                : null,
            versionCount,
            linkCount,
            holdsContent,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Operator",
            1);
    }

    /// <summary>A message summary.</summary>
    internal static MessageSummaryResponse Message(
        string subject = "Re: offer",
        string direction = "Inbound",
        string from = "producer@studio.example",
        int linkCount = 0,
        int attachmentCount = 0,
        Guid? id = null,
        Guid? accountId = null) =>
        new(
            id ?? Guid.NewGuid(),
            accountId ?? Guid.NewGuid(),
            "agent@agency.example",
            direction,
            subject,
            from,
            "Producer",
            ["agent@agency.example"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            attachmentCount > 0,
            attachmentCount,
            linkCount,
            false,
            "Inbox");

    /// <summary>An outbound dispatch in a named state.</summary>
    internal static OutboundDispatchResponse Dispatch(
        string state = "Draft",
        string subject = "Offer terms",
        Guid? id = null,
        int attempts = 0,
        string? lastVerdict = null,
        bool hasProviderEvidence = false) =>
        new(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "agent@agency.example",
            state,
            subject,
            [],
            [],
            attempts,
            null,
            lastVerdict,
            null,
            state is "ProviderDraftCreated" or "SendRequested",
            hasProviderEvidence,
            null,
            state == "Sent" ? DateTimeOffset.UtcNow : null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Operator",
            state is "UnknownOutcome" or "FailedPermanent",
            1);

    private void Throw()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }
}

/// <summary>Loading, empty and error states are distinct and observable.</summary>
public sealed class ViewModelStateTests
{
    [Fact]
    public async Task PeopleList_LoadsAndReportsNotEmpty()
    {
        FakeAgencyOsApi api = new();
        api.People.Add(Person("Sarah Okonkwo"));

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.People);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    /// A successful load with no rows is not an error, and must not look like one.
    /// </summary>
    [Fact]
    public async Task PeopleList_ReportsEmptyWhenThereIsNothingToShow()
    {
        PeopleListViewModel viewModel = new(new FakeAgencyOsApi());
        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>A refusal surfaces the server's own words, not an invented message.</summary>
    [Fact]
    public async Task PeopleList_SurfacesTheServersExplanation()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'people.read' is required."),
        };

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("Permission 'people.read' is required.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>An out-of-date build is explained as such rather than as a failure.</summary>
    [Fact]
    public async Task ViewModel_ExplainsWhenTheBuildMustBeUpdated()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.UpgradeRequired,
                "Client update required",
                "Version 0.1.0 is below the minimum supported version 0.3.0."),
        };

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Contains("must be updated", viewModel.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandCenter_ProjectsTheServersBuckets()
    {
        FakeAgencyOsApi api = new()
        {
            CommandCenter = new CommandCenterResponse(
                [Task("Overdue thing")],
                [Task("Soon thing")],
                [Task("Someday thing")],
                [],
                3,
                12,
                4),
        };

        CommandCenterViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Overdue);
        Assert.Single(viewModel.DueSoon);
        Assert.Single(viewModel.Unscheduled);
        Assert.Equal(3, viewModel.OpenTaskCount);
        Assert.Equal(12, viewModel.PeopleCount);
        Assert.Equal(4, viewModel.CompanyCount);
        Assert.False(viewModel.IsEmpty);
    }

    private static PersonSummaryResponse Person(string name) =>
        new(Guid.NewGuid(), name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow, 1);

    private static TaskResponse Task(string title) =>
        new(Guid.NewGuid(), title, "Open", "Normal", null, null, null, DateTimeOffset.UtcNow, null, 1);
}

/// <summary>The interaction-plus-follow-up capture workflow.</summary>
public sealed class RecordInteractionViewModelTests
{
    [Fact]
    public void CannotSubmit_WithoutASummaryOrAParticipant()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi());

        Assert.False(viewModel.CanSubmit);

        viewModel.Summary = "Met Sarah at dinner";
        Assert.False(viewModel.CanSubmit);

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));
        Assert.True(viewModel.CanSubmit);
    }

    /// <summary>Asking for a follow-up without saying what it is cannot be submitted.</summary>
    [Fact]
    public void CannotSubmit_WithAFollowUpThatHasNoTitle()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi())
        {
            Summary = "Met Sarah at dinner",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));
        viewModel.CreateFollowUp = true;

        Assert.False(viewModel.CanSubmit);

        viewModel.FollowUpTitle = "Send Sarah the screenplay";
        Assert.True(viewModel.CanSubmit);
    }

    [Fact]
    public void AddParticipant_IgnoresDuplicates()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi());
        PartyRefRequest sarah = new("Person", Guid.NewGuid());

        viewModel.AddParticipant(sarah);
        viewModel.AddParticipant(new PartyRefRequest("Person", sarah.Id));

        Assert.Single(viewModel.Participants);
    }

    /// <summary>
    /// The whole point of the screen: one submission carries both the memory and
    /// the commitment.
    /// </summary>
    [Fact]
    public async Task Submit_SendsTheInteractionAndItsFollowUpTogether()
    {
        FakeAgencyOsApi api = new();
        Guid sarah = Guid.NewGuid();

        RecordInteractionViewModel viewModel = new(api)
        {
            InteractionType = "Meeting",
            Summary = "Met Sarah at dinner",
            CreateFollowUp = true,
            FollowUpTitle = "Send Sarah the screenplay Monday",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", sarah));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.FollowUpTaskId);

        RecordInteractionRequest sent = api.LastInteraction!;
        Assert.Equal("Meeting", sent.Type);
        Assert.Equal("Met Sarah at dinner", sent.Summary);
        Assert.Single(sent.Participants);
        Assert.Equal(sarah, sent.Participants[0].Party.Id);
        Assert.Equal("Send Sarah the screenplay Monday", sent.FollowUp!.Title);
    }

    [Fact]
    public async Task Submit_OmitsTheFollowUpWhenNoneWasAskedFor()
    {
        FakeAgencyOsApi api = new();

        RecordInteractionViewModel viewModel = new(api) { Summary = "Call with studio executive" };
        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.NotNull(result);
        Assert.Null(result.FollowUpTaskId);
        Assert.Null(api.LastInteraction!.FollowUp);
    }

    [Fact]
    public async Task Submit_ReportsFailureWithoutThrowing()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'interactions.record' is required."),
        };

        RecordInteractionViewModel viewModel = new(api) { Summary = "Met Sarah" };
        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.Null(result);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public void Reset_ClearsTheFormForTheNextCapture()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi())
        {
            Summary = "Met Sarah",
            CreateFollowUp = true,
            FollowUpTitle = "Send screenplay",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        viewModel.Reset();

        Assert.Empty(viewModel.Summary);
        Assert.Empty(viewModel.Participants);
        Assert.False(viewModel.CreateFollowUp);
        Assert.False(viewModel.CanSubmit);
    }
}

/// <summary>The keyboard-first command palette.</summary>
public sealed class CommandPaletteTests
{
    [Fact]
    public void OffersEveryImplementedCommandByDefault()
    {
        CommandPaletteViewModel palette = new();

        Assert.NotEmpty(palette.Results);
        Assert.Equal(palette.AllCommands.Count, palette.Results.Count);
    }

    [Fact]
    public void FiltersOnTitleCategoryAndIdentifier()
    {
        CommandPaletteViewModel palette = new();

        palette.Query = "interaction";
        Assert.Contains(palette.Results, c => c.Id == "interaction.record");

        palette.Query = "Navigate";
        Assert.All(palette.Results, c => Assert.Equal("Navigate", c.Category));

        palette.Query = "person.create";
        Assert.Single(palette.Results);
    }

    [Fact]
    public void ReportsEmptyWhenNothingMatches()
    {
        CommandPaletteViewModel palette = new();
        palette.Query = "zzzz-not-a-command";

        Assert.Empty(palette.Results);
        Assert.True(palette.IsEmpty);
        Assert.Null(palette.Selected);
    }

    /// <summary>
    /// Selection wraps. On a keyboard surface, an arrow key that stops responding
    /// at the end of a short list reads as a bug.
    /// </summary>
    [Fact]
    public void SelectionWrapsAtBothEnds()
    {
        CommandPaletteViewModel palette = new(
        [
            new PaletteCommand("a", "Alpha", "Test"),
            new PaletteCommand("b", "Beta", "Test"),
        ]);

        Assert.Equal("a", palette.Selected!.Id);

        palette.MoveSelection(1);
        Assert.Equal("b", palette.Selected!.Id);

        palette.MoveSelection(1);
        Assert.Equal("a", palette.Selected!.Id);

        palette.MoveSelection(-1);
        Assert.Equal("b", palette.Selected!.Id);
    }
}

/// <summary>
/// The client's dependency boundary.
/// </summary>
/// <remarks>
/// M0 established that the Windows client cannot depend on persistence. This keeps
/// that true as the client grows: it is a compile-time fact, and this test makes it
/// a failing build rather than a discovery.
/// </remarks>
public sealed class ClientBoundaryTests
{
    [Theory]
    [InlineData("AgencyOS.Domain")]
    [InlineData("AgencyOS.Application")]
    [InlineData("AgencyOS.Infrastructure")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    public void ClientDoesNotReferenceServerOrPersistenceAssemblies(string assemblyName)
    {
        IEnumerable<string?> referenced = typeof(AgencyOsApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain(assemblyName, referenced);
    }

    /// <summary>
    /// The offline queue may only carry reversible, low-risk commands.
    /// </summary>
    /// <remarks>
    /// The allow-list is closed by construction, and this pins it. Queueing a
    /// privileged operation would mean deciding offline that it is permitted;
    /// possession of a cached record is not permission to change anything, and the
    /// server re-authorizes every queued command when it finally runs. Nothing
    /// touching authorization, release policy, bootstrap, deletion, deals or money
    /// belongs here - adding one should require editing this test and saying why.
    /// </remarks>
    [Fact]
    public void TheOfflineQueueCarriesOnlyReversibleLowRiskCommands()
    {
        string[] permitted =
        [
            nameof(QueuedOperation.CreatePerson),
            nameof(QueuedOperation.UpdatePerson),
            nameof(QueuedOperation.CreateCompany),
            nameof(QueuedOperation.UpdateCompany),
            nameof(QueuedOperation.CreateTask),
            nameof(QueuedOperation.CompleteTask),
            nameof(QueuedOperation.ReopenTask),
            nameof(QueuedOperation.RecordInteraction),
        ];

        Assert.Equal(permitted.Order(), Enum.GetNames<QueuedOperation>().Order());
    }

    /// <summary>
    /// The client cannot reach the audit trail, so it cannot record a cache
    /// operation as a business event.
    /// </summary>
    /// <remarks>
    /// Reading a record into a local cache is not a consequential business fact,
    /// and recording it would dilute the trail that matters. The commands the queue
    /// submits are audited by the server when they actually run. This is a
    /// compile-time fact rather than a convention.
    /// </remarks>
    [Fact]
    public void ClientCannotReachTheAuditTrail()
    {
        Assert.DoesNotContain(
            typeof(AgencyOS.Client.Cache.LocalCache).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "AgencyOS.Domain" || reference.Name == "AgencyOS.Application");
    }

    /// <summary>
    /// The synchronization seam ADR-0013 names is real, not just described.
    /// </summary>
    /// <remarks>
    /// The ADR says a future Rust engine could replace <c>IWriteQueue</c> and
    /// <c>ISyncEngine</c> without touching the domain, the API contract or the UI.
    /// That claim is only true if the interfaces exist and the UI actually binds to
    /// them; a documented boundary nothing depends on is a boundary in name only.
    /// </remarks>
    [Fact]
    public void TheSynchronizationSeamIsReal()
    {
        Assert.True(typeof(AgencyOS.Client.Sync.ISyncEngine).IsAssignableFrom(typeof(AgencyOS.Client.Sync.SyncEngine)));
        Assert.True(typeof(AgencyOS.Client.Sync.IWriteQueue).IsAssignableFrom(typeof(LocalCache)));

        // The offline surface takes the seam, not the implementation, so replacing
        // the engine does not touch the UI.
        Assert.Contains(
            typeof(SyncStatusViewModel).GetConstructors(),
            constructor => constructor
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(AgencyOS.Client.Sync.ISyncEngine)));
    }

    [Fact]
    public void ClientDependsOnTheVersionedContracts()
    {
        IEnumerable<string?> referenced = typeof(AgencyOsApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.Contains("AgencyOS.Contracts", referenced);
    }
}
