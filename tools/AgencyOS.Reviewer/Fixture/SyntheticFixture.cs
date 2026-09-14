using System.Globalization;
using System.Net.Http;
using AgencyOS.Client;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Reviewer.Fixture;

/// <summary>What the fixture built, and what it could not.</summary>
/// <param name="OrganizationId">The synthetic tenant.</param>
/// <param name="OwnerUserId">The owner the fixture acts as.</param>
/// <param name="Created">One line per record created.</param>
/// <param name="Refused">One line per step the server refused, with its reason.</param>
public sealed record FixtureReport(
    Guid OrganizationId,
    Guid OwnerUserId,
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Refused);

/// <summary>
/// Builds a disposable review tenant through the product's own client.
/// </summary>
/// <remarks>
/// <para>
/// Every record is created by calling <see cref="AgencyOsApiClient"/> - the same
/// class the Windows client uses - so the fixture exercises the canonical command
/// path and cannot manufacture domain state the product would refuse. §7 of the
/// review brief requires exactly that: no direct writes to PostgreSQL.
/// </para>
/// <para>
/// The data is chosen to stress the interface rather than to look plausible.
/// Long names, one-character names, Hebrew and Japanese text, punctuation that
/// breaks naive truncation, absent optional values, a hundred-row list and a
/// one-row list are all here because each is a layout the screenshots should
/// show and a normal demo dataset would never produce.
/// </para>
/// <para>
/// A refusal is recorded rather than thrown. A server that declines a fixture
/// step is telling the review something, and stopping would lose it.
/// </para>
/// </remarks>
public sealed class SyntheticFixture
{
    private readonly IAgencyOsApi _api;
    private readonly List<string> _created = [];
    private readonly List<string> _refused = [];

    /// <summary>Creates a fixture that writes through one client session.</summary>
    public SyntheticFixture(IAgencyOsApi api) => _api = api;

    /// <summary>Names chosen to break layout rather than to look real.</summary>
    private static readonly (string First, string Last, string? Title, string? Email, string? Notes)[] People =
    [
        ("A", null!, null, null, null),
        ("Rivka", "בן־שמעון", "סוכנת בכירה", "rivka@review.invalid", "שם בעברית כדי לבדוק כיווניות"),
        ("千尋", "荻野", "Producer", "chihiro@review.invalid", "Japanese name, no Latin fallback"),
        ("Bartholomew-Fitzgerald", "Pemberton-Featherstonehaugh III", "Executive Vice President of Global Scripted Content and Strategic Partnerships", "b.p.f.iii@review.invalid", null),
        ("O'Brien", "D'Angelo-Smith", "Agent, Talent", "obrien@review.invalid", "Apostrophes and hyphens"),
        ("Zoë", "Ångström", "Head of Casting", null, null),
        ("Sam", "Nolan", null, null, null),
        ("Priya", "Raghunathan", "Literary Agent", "priya@review.invalid", null),
        ("Tomasz", "Wiśniewski", "Director of Development", "tomasz@review.invalid", null),
        ("Иван", "Достоевский", "Distribution", null, null),
    ];

    private static readonly (string Name, string Type, string? Website)[] Companies =
    [
        ("A24", "Studio", "https://review.invalid/a24"),
        ("Very Long Production Company Name That Will Not Fit In A Narrow Column Limited", "ProductionCompany", null),
        ("שידורי קשת", "Network", null),
        ("東宝株式会社", "Studio", null),
        ("Ø", "Other", null),
        ("Smith & Jones, LLP (Legal)", "LawFirm", "https://review.invalid/sj"),
        ("Northlight Management", "ManagementCompany", null),
        ("Pinewood Streaming Group", "Streamer", null),
    ];

    /// <summary>Builds the whole review profile.</summary>
    public async Task<FixtureReport> BuildAsync(
        Guid organizationId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> companyIds = await CreateCompaniesAsync(cancellationToken).ConfigureAwait(false);
        List<Guid> personIds = await CreatePeopleAsync(companyIds, cancellationToken).ConfigureAwait(false);

        await CreateRelationshipsAsync(personIds, companyIds, cancellationToken).ConfigureAwait(false);
        await CreateInteractionsAsync(personIds, cancellationToken).ConfigureAwait(false);
        await CreateTasksAsync(personIds, cancellationToken).ConfigureAwait(false);
        await CreateRepresentationAsync(personIds, ownerUserId, cancellationToken).ConfigureAwait(false);
        List<Guid> projectIds =
            await CreateProjectsAsync(companyIds, ownerUserId, cancellationToken).ConfigureAwait(false);

        await CreateDealChainAsync(personIds, companyIds, projectIds, ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        return new FixtureReport(organizationId, ownerUserId, _created, _refused);
    }

    /// <summary>
    /// The server's own explanation, not just the title.
    /// </summary>
    /// <remarks>
    /// <c>AgencyOsApiException.Message</c> is the problem-details title, which for
    /// every validation refusal is the same three words. The detail is the part
    /// that says which value was wrong, and a fixture that prints only the title
    /// reports "Invalid request" six times and teaches nobody anything.
    /// </remarks>
    private static string Explain(Exception failure) =>
        failure is AgencyOsApiException api && api.Detail is { Length: > 0 } detail
            ? detail
            : failure.Message;

    private async Task<List<Guid>> CreateCompaniesAsync(CancellationToken cancellationToken)
    {
        List<Guid> ids = [];

        foreach ((string name, string type, string? website) in Companies)
        {
            try
            {
                CompanyDetailResponse company = await _api
                    .CreateCompanyAsync(
                        new CreateCompanyRequest(name, type, Website: website),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                ids.Add(company.Company.Id);
                _created.Add("company: " + name);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("company '" + name + "': " + Explain(failure));
            }
        }

        return ids;
    }

    private async Task<List<Guid>> CreatePeopleAsync(
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [];
        int index = 0;

        foreach ((string first, string last, string? title, string? email, string? notes) in People)
        {
            Guid? company = companyIds.Count > 0 ? companyIds[index % companyIds.Count] : null;

            try
            {
                PersonDetailResponse person = await _api
                    .CreatePersonAsync(
                        new CreatePersonRequest(
                            first,
                            string.IsNullOrEmpty(last) ? null : last,
                            PrimaryCompanyId: company,
                            Title: title,
                            Email: email,
                            Notes: notes),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                ids.Add(person.Person.Id);
                _created.Add("person: " + first + " " + last);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("person '" + first + "': " + Explain(failure));
            }

            index++;
        }

        // A dense list. Forty more people so the directory is past one screen and
        // virtualization, scrolling and column behaviour are all exercised.
        for (int i = 1; i <= 40; i++)
        {
            string suffix = i.ToString("D2", CultureInfo.InvariantCulture);

            try
            {
                PersonDetailResponse person = await _api
                    .CreatePersonAsync(
                        new CreatePersonRequest(
                            "Review",
                            "Contact " + suffix,
                            Title: i % 3 == 0 ? null : "Coordinator",
                            Email: i % 4 == 0 ? null : "contact" + suffix + "@review.invalid"),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                ids.Add(person.Person.Id);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("dense person " + suffix + ": " + Explain(failure));
                break;
            }
        }

        _created.Add("dense directory: 40 additional people");

        return ids;
    }

    private async Task CreateRelationshipsAsync(
        IReadOnlyList<Guid> personIds,
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count < 2 || companyIds.Count == 0)
        {
            return;
        }

        (string Type, int From, int To)[] links =
        [
            ("Colleague", 0, 1),
            ("Collaboration", 1, 2),
            ("Introduction", 2, 3),
            ("Advisor", 3, 4),
        ];

        foreach ((string type, int from, int to) in links)
        {
            if (from >= personIds.Count || to >= personIds.Count)
            {
                continue;
            }

            try
            {
                await _api
                    .CreateRelationshipAsync(
                        new CreateRelationshipRequest(
                            new PartyRefRequest("Person", personIds[from]),
                            new PartyRefRequest("Person", personIds[to]),
                            type),
                        cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("relationship: " + type);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("relationship '" + type + "': " + Explain(failure));
            }
        }
    }

    private async Task CreateInteractionsAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return;
        }

        (string Type, string Summary, int DaysAgo)[] interactions =
        [
            ("Call", "Discussed availability for the autumn block.", 1),
            ("Email", "Sent materials — recorded only; AgencyOS did not send this.", 4),
            ("Meeting", "שיחה על תיק הלקוח", 9),
            ("Note", "Very long summary that keeps going well past any sensible column width to see whether the timeline wraps it, truncates it, or lets it overflow the panel it is in.", 30),
        ];

        int index = 0;

        foreach ((string type, string summary, int daysAgo) in interactions)
        {
            try
            {
                await _api
                    .RecordInteractionAsync(
                        new RecordInteractionRequest(
                            type,
                            DateTimeOffset.UtcNow.AddDays(-daysAgo),
                            summary,
                            [
                                new InteractionParticipantRequest(
                                    new PartyRefRequest("Person", personIds[index % personIds.Count])),
                            ]),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("interaction: " + type);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("interaction '" + type + "': " + Explain(failure));
            }

            index++;
        }
    }

    private async Task CreateTasksAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken cancellationToken)
    {
        (string Title, int? DueInDays)[] tasks =
        [
            ("Overdue: confirm the option date", -6),
            ("Overdue: chase the signed rider", -2),
            ("Due soon: return the packaging note", 1),
            ("Due soon: read the revised draft", 3),
            ("Unscheduled: think about the second-position question", null),
            ("משימה בעברית לבדיקת כיווניות", 2),
            ("A task with a title long enough to test how the command centre handles a line that will not fit in the column it is given", 5),
        ];

        foreach ((string title, int? dueInDays) in tasks)
        {
            try
            {
                await _api
                    .CreateTaskAsync(
                        new CreateTaskRequest(
                            title,
                            dueInDays is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null,
                            Subject: personIds.Count > 0
                                ? new PartyRefRequest("Person", personIds[0])
                                : null),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("task: " + title[..Math.Min(40, title.Length)]);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("task: " + Explain(failure));
            }
        }
    }

    private async Task CreateRepresentationAsync(
        IReadOnlyList<Guid> personIds,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        if (personIds.Count < 4)
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _api
                    .CreateTalentProfileAsync(
                        new CreateTalentProfileRequest(
                            personIds[i],
                            CareerStage: i switch { 0 => "Emerging", 1 => "Established", _ => null },
                            Summary: i == 2 ? null : "Review fixture talent profile.",
                            PositioningNotes: i == 0
                                ? "Sensitive positioning note — should be redacted without the grant."
                                : null,
                            Disciplines: i == 0 ? ["Actor", "Writer"] : null),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("talent profile " + i.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("talent profile: " + Explain(failure));
            }
        }

        for (int i = 3; i < Math.Min(6, personIds.Count); i++)
        {
            try
            {
                await _api
                    .CreateProspectAsync(
                        new CreateProspectRequest(
                            personIds[i],
                            ownerUserId,
                            Source: "Review fixture",
                            StrategyNotes: "Strategy note — sensitive.",
                            NextFollowUpOn: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(i - 4))),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("prospect " + i.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("prospect: " + Explain(failure));
            }
        }
    }

    private async Task<List<Guid>> CreateProjectsAsync(
        IReadOnlyList<Guid> companyIds,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [];

        (string Title, string Type, string? Stage, string? Logline)[] projects =
        [
            ("The Quiet Coast", "FeatureFilm", "Development", "A lighthouse keeper inherits a debt."),
            ("מעבר לגשר", "TelevisionSeries", "Packaging", null),
            ("Untitled", "FeatureFilm", null, null),
            ("A Project With An Unusually Long Title That Exists Entirely To Test Column Truncation In The Slate View", "LimitedSeries", "Development", "A logline that also runs on far past the point where any reasonable layout would have stopped showing it, on purpose."),
        ];

        int index = 0;

        foreach ((string title, string type, string? stage, string? logline) in projects)
        {
            try
            {
                ProjectDetailResponse created = await _api
                    .CreateProjectAsync(
                        new CreateProjectRequest(
                            title,
                            type,
                            Stage: stage,
                            Logline: logline,
                            PrimaryCompanyId: companyIds.Count > 0 ? companyIds[index % companyIds.Count] : null,
                            LeadUserId: ownerUserId),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                ids.Add(created.Project.Id);
                _created.Add("project: " + title[..Math.Min(40, title.Length)]);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
            {
                _refused.Add("project '" + title[..Math.Min(20, title.Length)] + "': " + Explain(failure));
            }

            index++;
        }

        return ids;
    }

    /// <summary>
    /// The opportunity → target → deal → offer chain.
    /// </summary>
    /// <remarks>
    /// Built in order because each stage is the previous stage's precondition, and
    /// built through the same commands an operator would use. Term codes come from
    /// the server's own catalog rather than from a guess, so a vocabulary change
    /// makes the fixture stop rather than make it wrong.
    /// </remarks>
    private async Task CreateDealChainAsync(
        IReadOnlyList<Guid> personIds,
        IReadOnlyList<Guid> companyIds,
        IReadOnlyList<Guid> projectIds,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0 || personIds.Count == 0 || projectIds.Count == 0)
        {
            _refused.Add("deal chain: no project to pursue, so nothing downstream was built.");

            return;
        }

        OpportunityDetailResponse? opportunity = null;

        try
        {
            opportunity = await _api
                .CreateOpportunityAsync(
                    new CreateOpportunityRequest(
                        "Autumn slate — lead role",
                        "ProjectMarket",
                        ownerUserId,
                        Priority: "High",
                        Description: "Review fixture opportunity.",
                        StrategyNotes: "Strategy note — sensitive.",
                        Subjects:
                        [
                            new OpportunitySubjectRequest("Project", projectIds[0], "Primary"),
                        ]),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _created.Add("opportunity: Autumn slate");
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
        {
            _refused.Add("opportunity: " + Explain(failure));

            return;
        }

        Guid targetId;

        try
        {
            targetId = await _api
                .AddOpportunityTargetAsync(
                    opportunity.Opportunity.Id,
                    new AddOpportunityTargetRequest(
                        opportunity.Opportunity.Version,
                        CompanyId: companyIds[0],
                        ContactPersonId: personIds[0],
                        NextActionOn: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _created.Add("opportunity target");
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
        {
            _refused.Add("opportunity target: " + Explain(failure));

            return;
        }

        // A deal cannot be opened from a draft pursuit, which the server says
        // plainly. Activating it here is the same step an operator takes, and
        // leaving it out would have produced a fixture with no deal and a review
        // that never saw the Deals workspace populated.
        try
        {
            // Re-read rather than guess. Adding a target may or may not bump the
            // opportunity's own version, and a fixture that assumes which is a
            // fixture that fails on a concurrency rule it was not testing.
            OpportunityDetailResponse current = await _api
                .GetOpportunityAsync(opportunity.Opportunity.Id, cancellationToken)
                .ConfigureAwait(false);

            await _api
                .ChangeOpportunityStatusAsync(
                    current.Opportunity.Id,
                    new ChangeOpportunityStatusRequest("Active", current.Opportunity.Version),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _created.Add("opportunity activated");
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
            or AgencyOsApiException)
        {
            _refused.Add("activate opportunity: " + Explain(failure));
        }

        OpportunityDetailResponse refreshed = await _api
            .GetOpportunityAsync(opportunity.Opportunity.Id, cancellationToken)
            .ConfigureAwait(false);

        // A negotiation only opens once a target is interested. Moving it is the
        // same step an agent takes after a buyer responds, and it puts the Pipeline
        // workspace into a state past its first stage, which is the state worth
        // photographing.
        // The stage machine is strict and one step at a time: Identified, Approved,
        // Contacted, Engaged, Interested. Walking it rather than jumping is both the
        // only thing the server allows and the sequence an agent actually performs.
        foreach (string stage in (string[])["Approved", "Contacted", "Engaged", "Interested"])
        {
            try
            {
                OpportunityTargetResponse target = await _api
                    .GetOpportunityTargetAsync(targetId, cancellationToken)
                    .ConfigureAwait(false);

                await _api
                    .MoveOpportunityTargetAsync(
                        targetId,
                        new MoveOpportunityTargetRequest(stage, target.Version),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _created.Add("target moved to " + stage);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
                or AgencyOsApiException)
            {
                _refused.Add("move target to " + stage + ": " + Explain(failure));

                break;
            }
        }

        DealDetailResponse? deal = null;

        try
        {
            deal = await _api
                .CreateDealAsync(
                    new CreateDealRequest(
                        refreshed.Opportunity.Id,
                        targetId,
                        "Autumn slate — lead role",
                        "TalentEmployment",
                        ownerUserId,
                        Summary: "Review fixture deal.",
                        StrategyNotes: "Strategy note — sensitive."),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _created.Add("deal: Autumn slate");
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
        {
            _refused.Add("deal: " + Explain(failure));

            return;
        }

        IReadOnlyList<DealTermDefinitionResponse> catalog;

        try
        {
            catalog = await _api.ListDealTermsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
        {
            _refused.Add("deal term catalog: " + Explain(failure));

            return;
        }

        List<OfferTermRequest> terms = [];

        foreach (DealTermDefinitionResponse definition in catalog.Take(4))
        {
            TermValueRequest? value = definition.ValueKind switch
            {
                "Money" => new TermValueRequest("Money", Amount: 125000m, Currency: "USD"),
                "Percentage" => new TermValueRequest("Percentage", Number: 10m),
                "Text" => new TermValueRequest("Text", Text: "Review fixture term"),
                "Flag" => new TermValueRequest("Flag", Flag: true),
                "Date" => new TermValueRequest("Date", Date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45))),
                "Whole" => new TermValueRequest(
                    "Whole",
                    Whole: 3,
                    Unit: definition.AllowedUnits.Count > 0 ? definition.AllowedUnits[0] : null),
                _ => null,
            };

            if (value is not null)
            {
                terms.Add(new OfferTermRequest(definition.Code, value));
            }
        }

        if (terms.Count == 0)
        {
            _refused.Add("offer: no term in the catalog had a value shape the fixture could build.");

            return;
        }

        try
        {
            await _api
                .RecordOfferAsync(
                    deal.Deal.Id,
                    new RecordOfferRequest(
                        "Inbound",
                        terms,
                        deal.Deal.Version,
                        Summary: "Opening offer, recorded — not sent."),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _created.Add("offer: inbound, " + terms.Count.ToString(CultureInfo.InvariantCulture) + " terms");
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or AgencyOsApiException)
        {
            _refused.Add("offer: " + Explain(failure));
        }
    }
}
