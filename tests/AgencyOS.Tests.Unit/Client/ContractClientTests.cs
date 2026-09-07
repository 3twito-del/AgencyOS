using Xunit;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Legal;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The contract surfaces show what the server said and never more.
/// </summary>
/// <remarks>
/// Every test here is about the same thing in a different place: the Windows
/// client must not turn an absence into a claim, a date into a breach, or a
/// difference into a judgement. Those are the three ways a legal screen goes
/// quietly wrong (ADR-0022).
/// </remarks>
public sealed class ContractListViewModelTests
{
    [Fact]
    public async Task ListSummarisesWhatItLoaded()
    {
        FakeAgencyOsApi api = new();

        api.Contracts.Add(FakeAgencyOsApi.Contract(
            "Writer agreement", "PartiallyExecuted", outstandingSignatures: 1));
        api.Contracts.Add(FakeAgencyOsApi.Contract(
            "Option agreement", "Executed", effective: true, differences: 2));

        ContractListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(2, view.Contracts.Count);
        Assert.Equal(1, view.Unsigned);
        Assert.Equal(1, view.Effective);
        Assert.Equal(1, view.WithDifferences);
    }

    /// <summary>
    /// A contract with no resolvable deadline is not counted as due.
    /// </summary>
    /// <remarks>
    /// The honest case. A clause measured from a delivery that has not happened has
    /// no date, and counting it as due this week would put a deadline in front of a
    /// lawyer that the contract never set (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task ContractsWithoutAResolvableDeadlineAreNotDue()
    {
        FakeAgencyOsApi api = new();

        api.Contracts.Add(FakeAgencyOsApi.Contract("No deadline", nextDeadline: null));
        api.Contracts.Add(FakeAgencyOsApi.Contract(
            "Due soon", nextDeadline: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2)));

        ContractListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(1, view.DueThisWeek);
    }

    [Fact]
    public async Task FiltersReachTheServer()
    {
        FakeAgencyOsApi api = new();

        ContractListViewModel view = new(api)
        {
            Status = "UnderReview",
            AwaitingSignature = true,
            DifferencesOnly = true,
            Search = "  Northgate  ",
        };

        await view.LoadAsync();

        Assert.Equal("UnderReview", api.LastContractFilter.Status);
        Assert.True(api.LastContractFilter.Awaiting);
        Assert.True(api.LastContractFilter.Differing);
        Assert.Equal("Northgate", api.LastContractFilter.Search);
    }
}

public sealed class ContractDetailViewModelTests
{
    /// <summary>
    /// Absent privileged content is treated as absent, never as empty text.
    /// </summary>
    /// <remarks>
    /// The redaction removes the field rather than blanking it, and the client
    /// mirrors that: a reader without <c>contracts.privileged.read</c> sees no
    /// analysis section at all, rather than an empty one that tells them counsel
    /// wrote something (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task RedactedAnalysisIsIndistinguishableFromNone()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(id, legalAnalysis: null);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.False(view.HasLegalAnalysis);
        Assert.False(view.HasStrategy);
    }

    [Fact]
    public async Task AnalysisShowsWhenTheServerReturnedIt()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(id, legalAnalysis: "Counsel is comfortable.");

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.True(view.HasLegalAnalysis);
    }

    /// <summary>
    /// A version with no terms reads as no terms, whatever the reason.
    /// </summary>
    /// <remarks>
    /// A caller without <c>contracts.terms.read</c> and a version nobody has
    /// transcribed produce the same empty list, deliberately. A screen that
    /// distinguished them would leak the fact that terms exist.
    /// </remarks>
    [Fact]
    public async Task EmptyTermsHideTheReconciliationPath()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(id, terms: []);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.False(view.HasTerms);
        Assert.False(view.HasEconomics);
    }

    [Fact]
    public async Task EconomicsAreVisibleOnlyWhenAnEconomicTermSurvived()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            terms:
            [
                FakeAgencyOsApi.ContractTerm("GoverningLaw", "California"),
                FakeAgencyOsApi.ContractTerm("GuaranteedCompensation", "250000 USD", economic: true),
            ]);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.True(view.HasTerms);
        Assert.True(view.HasEconomics);
    }

    /// <summary>
    /// The standing sentence keeps execution and effectiveness apart.
    /// </summary>
    /// <remarks>
    /// The distinction the milestone exists to protect. An agreement effective from
    /// January and executed in March is ordinary, and a sentence that merged the
    /// two would be wrong about what was in force (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task StandingReportsExecutionAndEffectivenessSeparately()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            contract: FakeAgencyOsApi.Contract("Writer agreement", "Executed", effective: true, id: id));

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Contains("fully executed", view.Standing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("effective from", view.Standing, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An executed contract with no effective date says so, rather than guessing.
    /// </summary>
    [Fact]
    public async Task StandingSaysWhenNoEffectiveDateWasRecorded()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            contract: FakeAgencyOsApi.Contract(
                "Writer agreement", "Executed", effective: false, id: id));

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Contains(
            "no effective date recorded", view.Standing, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Past due is reported, and never as a breach.
    /// </summary>
    /// <remarks>
    /// Two facts that a screen must not merge: a date went by, and somebody
    /// determined that the failure is a breach. Only the first is derived
    /// (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task PastDueIsNotBreached()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            obligations:
            [
                Obligation("Deliver first draft", "Pending", pastDue: true),
                Obligation("Pay the advance", "Breached", pastDue: false),
            ]);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Single(view.PastDue);
        Assert.Equal("Deliver first draft", view.PastDue[0].Description);
    }

    /// <summary>
    /// Deadlines this build cannot compute are surfaced with their reason.
    /// </summary>
    /// <remarks>
    /// Shown rather than hidden. These are duties that exist and whose date nobody
    /// can work out yet, and they are exactly what a legal calendar would otherwise
    /// silently lose (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task UnresolvedDeadlinesAreListedWithTheirReason()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            obligations:
            [
                Obligation(
                    "Deliver on notice",
                    "Pending",
                    pastDue: false,
                    dueOn: null,
                    reason: "The event this is measured from has not happened yet."),
            ]);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Single(view.UnresolvedDeadlines);
        Assert.Contains("has not happened", view.UnresolvedDeadlines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The reconciliation sentence counts and never judges.
    /// </summary>
    [Fact]
    public async Task ReconciliationStandingIsFactual()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(
            id,
            contract: FakeAgencyOsApi.Contract("Writer agreement", differences: 3, id: id));

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Contains("3 term(s) differ", view.ReconciliationStanding, StringComparison.Ordinal);

        foreach (string word in new[] { "risk", "material", "unfavourable", "bad", "problem" })
        {
            Assert.DoesNotContain(
                word, view.ReconciliationStanding, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The client never claims to hold a document.</summary>
    [Fact]
    public async Task NoVersionClaimsToHoldItsDocument()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = Detail(id);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.False(view.HoldsDocuments);
    }

    internal static ContractDetailResponse Detail(
        Guid id,
        ContractSummaryResponse? contract = null,
        string? legalAnalysis = null,
        IReadOnlyList<ContractTermResponse>? terms = null,
        IReadOnlyList<ObligationResponse>? obligations = null) =>
        new(
            contract ?? FakeAgencyOsApi.Contract("Writer agreement", id: id),
            "A writer agreement.",
            legalAnalysis,
            null,
            "Ordinary",
            [],
            [Version(id, terms ?? [FakeAgencyOsApi.ContractTerm("GoverningLaw", "California")])],
            [],
            [],
            [],
            obligations ?? [],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);

    private static ContractVersionResponse Version(
        Guid contractId,
        IReadOnlyList<ContractTermResponse> terms) =>
        new(
            Guid.NewGuid(),
            contractId,
            1,
            "Execution copy",
            "Inbound",
            "Recorded",
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "MATTER-1042",
            "Counsel DMS",
            "writer-agreement-v1.docx",
            null,

            // The point of the field: M8 records a reference and has never seen
            // the file.
            false,

            null,
            terms,
            1);

    private static ObligationResponse Obligation(
        string description,
        string status,
        bool pastDue,
        DateOnly? dueOn = null,
        string? reason = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "Northgate Pictures",
            Guid.NewGuid(),
            "The writer",
            "Delivery",
            description,
            dueOn ?? (reason is null ? new DateOnly(2027, 6, 1) : null),
            reason,
            null,
            status,
            null,
            pastDue,
            null,
            null,
            null,
            1);
}

public sealed class ReconciliationViewModelTests
{
    /// <summary>
    /// The default view hides matched lines and says what is left.
    /// </summary>
    [Fact]
    public async Task DifferencesOnlyIsTheDefault()
    {
        FakeAgencyOsApi api = new();

        api.Reconciliation = Reconciliation(
        [
            Line("GuaranteedCompensation", "Matched"),
            Line("BackEndParticipation", "Changed"),
            Line("CreditObligationSummary", "AddedInContract"),
        ]);

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(2, view.Lines.Count);
        Assert.DoesNotContain(view.Lines, line => line.Result == "Matched");

        view.DifferencesOnly = false;

        Assert.Equal(3, view.Lines.Count);
    }

    /// <summary>
    /// The summary counts outcomes and reaches no conclusion.
    /// </summary>
    /// <remarks>
    /// This is the test that would fail first if somebody added a helpful adjective
    /// to the screen. Whether a change is acceptable is a legal question about a
    /// document AgencyOS has not read (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task SummaryCountsAndDoesNotJudge()
    {
        FakeAgencyOsApi api = new();

        api.Reconciliation = Reconciliation(
        [
            Line("GuaranteedCompensation", "Changed"),
            Line("BackEndParticipation", "MissingFromContract"),
            Line("CreditObligationSummary", "AddedInContract"),
            Line("GoverningLaw", "NotComparable"),
        ]);

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Contains("1 changed", view.Summary, StringComparison.Ordinal);
        Assert.Contains("1 not carried into the draft", view.Summary, StringComparison.Ordinal);
        Assert.Contains("1 added by the draft", view.Summary, StringComparison.Ordinal);
        Assert.Contains("1 not comparable", view.Summary, StringComparison.Ordinal);

        foreach (string word in new[]
        {
            "risk", "material", "unfavourable", "favourable", "adverse", "problem", "wrong",
        })
        {
            Assert.DoesNotContain(word, view.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AFaithfulDraftSaysSo()
    {
        FakeAgencyOsApi api = new();

        api.Reconciliation = Reconciliation([Line("GuaranteedCompensation", "Matched")], faithful: true);

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(view.IsFaithful);
        Assert.Equal("The draft records the terms that were agreed.", view.Summary);
    }

    private static ReconciliationResponse Reconciliation(
        IReadOnlyList<ReconciliationLineResponse> lines,
        bool faithful = false) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            lines,
            lines.Count(x => x.Result != "Matched"),
            faithful);

    private static ReconciliationLineResponse Line(string code, string result) =>
        new(code, code, result, "NotComparable", null, null);
}

public sealed class LegalWorkQueueTests
{
    /// <summary>
    /// The headline is counts, never a score.
    /// </summary>
    [Fact]
    public async Task CommandCenterHeadlineIsFactual()
    {
        FakeAgencyOsApi api = new()
        {
            LegalCommandCenter = new(
                [FakeAgencyOsApi.Contract("Under review", "UnderReview")],
                [FakeAgencyOsApi.Contract("Awaiting", "PartiallyExecuted", outstandingSignatures: 2)],
                [],
                [],
                [new(Guid.NewGuid(), "Agreed deal", "Northgate Pictures", DateTimeOffset.UtcNow)],
                [],
                [],
                [],
                [],
                1,
                1,
                3),
        };

        LegalCommandCenterViewModel view = new(api);

        await view.LoadAsync();

        Assert.Contains("1 awaiting signature", view.Headline, StringComparison.Ordinal);
        Assert.Contains("3 in force", view.Headline, StringComparison.Ordinal);
        Assert.Contains("not yet papered", view.Headline, StringComparison.Ordinal);
        Assert.Single(view.Unpapered);
    }

    /// <summary>
    /// Options past their deadline are counted separately from expired ones.
    /// </summary>
    /// <remarks>
    /// An option sitting past its date and still Available is precisely what a work
    /// queue exists to surface: nobody has dealt with it. Marking it expired is a
    /// legal position somebody takes (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task PastDeadlineIsNotExpired()
    {
        FakeAgencyOsApi api = new();

        api.ContractOptions.Add(Option("Sequel option", "Available", pastDeadline: true));
        api.ContractOptions.Add(Option("Renewal option", "Expired", pastDeadline: false));

        OptionListViewModel view = new(api) { Status = null };

        await view.LoadAsync();

        Assert.Equal(2, view.Options.Count);
        Assert.Equal(1, view.PastDeadline);
    }

    /// <summary>Unresolved deadlines are counted, not dropped.</summary>
    [Fact]
    public async Task OptionsWithoutADeadlineAreCounted()
    {
        FakeAgencyOsApi api = new();

        api.ContractOptions.Add(Option("On delivery", "Available", pastDeadline: false, deadline: null));

        OptionListViewModel view = new(api) { Status = null };

        await view.LoadAsync();

        Assert.Equal(1, view.Unresolved);
    }

    /// <summary>Grants with an unstated period are surfaced, not assumed perpetual.</summary>
    [Fact]
    public async Task UnstatedGrantPeriodsAreCounted()
    {
        FakeAgencyOsApi api = new();

        api.RightsGrants.Add(Grant("Perpetual", current: true));
        api.RightsGrants.Add(Grant("Unstated", current: false));

        RightsViewModel view = new(api) { CurrentOnly = false };

        await view.LoadAsync();

        Assert.Equal(2, view.Grants.Count);
        Assert.Equal(1, view.Running);
        Assert.Equal(1, view.UnstatedPeriods);
    }

    private static ContractOptionResponse Option(
        string subject,
        string status,
        bool pastDeadline,
        DateOnly? deadline = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "SequelOption",
            Guid.NewGuid(),
            "Northgate Pictures",
            subject,
            null,
            null,
            null,
            deadline ?? (pastDeadline ? new DateOnly(2027, 1, 1) : null),
            deadline is null && !pastDeadline
                ? "The event this is measured from has not happened yet."
                : null,
            null,
            null,
            status,
            null,
            false,
            pastDeadline,
            null,
            null,
            null,
            1);

    private static RightsGrantResponse Grant(string periodKind, bool current) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "The writer",
            Guid.NewGuid(),
            "Northgate Pictures",
            "Motion picture",
            "AllMedia",
            "Worldwide",
            null,
            "Exclusive",
            periodKind,
            null,
            null,
            current,
            null,
            null,
            null,
            null,
            null,
            null,
            "Active",
            null,
            1);
}
