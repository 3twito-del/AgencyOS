using System.Reflection;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.ViewModels;
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
internal sealed class FakeAgencyOsApi : IAgencyOsApi
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
            []));
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

    public Task<TalentDetailResponse> GetTalentAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Throw();

        TalentSummaryResponse summary = Talent.First(x => x.PersonId == personId);

        return Task.FromResult(new TalentDetailResponse(summary, null, null, null, null, DateTimeOffset.UtcNow));
    }

    public Task<TalentDetailResponse> CreateTalentProfileAsync(
        CreateTalentProfileRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        TalentSummaryResponse summary = new(
            Guid.NewGuid(),
            request.PersonId,
            "Created",
            request.CareerStage ?? "Unknown",
            request.Disciplines ?? [],
            null,
            IsClient: false,
            null,
            null,
            [],
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
