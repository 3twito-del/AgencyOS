using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M8 contract workflow, end to end against PostgreSQL.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class ContractTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ContractTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole instrument, in the order an agency papers one.
    /// </summary>
    /// <remarks>
    /// One test on purpose: the value of M8 is that these steps connect. Separate
    /// tests would each pass while the joins between them stayed broken - and the
    /// joins are the milestone.
    /// </remarks>
    [Fact]
    public async Task Workflow_FromAgreedTermsToExecutedContract()
    {
        Fixture f = await SetUpAsync("m8-workflow");

        // A contract papers an agreement, and names the offer it papers.
        ContractDetailResponse contract = await OpenContractAsync(f);

        Assert.Equal("Draft", contract.Contract.Status);
        Assert.Equal(f.DealId, contract.Contract.DealId);
        Assert.Equal(f.AcceptedOfferId, contract.Contract.AcceptedOfferId);
        Assert.Equal("Northgate Pictures", contract.Contract.CounterpartyDisplayName);

        // Nothing has been signed, so nothing claims execution or effectiveness.
        Assert.Null(contract.Contract.ExecutedOn);
        Assert.Null(contract.Contract.EffectiveOn);
        Assert.False(contract.Contract.IsEffective);

        // Two parties, both of whom must sign.
        Guid studio = await AddPartyAsync(
            f, contract.Contract.Id, new AddContractPartyRequest(
                "Studio", contract.Contract.Version, CompanyId: f.StudioId));

        contract = await ContractAsync(f, contract.Contract.Id);

        Guid writer = await AddPartyAsync(
            f, contract.Contract.Id, new AddContractPartyRequest(
                "Artist", contract.Contract.Version, PersonId: f.WriterId));

        contract = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal(2, contract.Parties.Count);
        Assert.Equal(2, contract.Contract.OutstandingSignatureCount);

        // The first draft arrives, transcribed as it stands. The compensation is
        // what was agreed; the billing has quietly moved.
        RecordContractVersionResponse first = await RecordVersionAsync(
            f,
            contract.Contract.Id,
            new RecordContractVersionRequest(
                "Studio first draft",
                "Inbound",
                contract.Contract.Version,
                ReceivedOn: Today.AddDays(-20),
                ExternalReference: "MATTER-1042/v1",
                SourceSystem: "Counsel DMS",
                DisplayFileName: "undertow-writer-v1.docx",
                Terms:
                [
                    Money("GuaranteedCompensation", 650_000m),
                    Text("CreditBilling", "Second position"),
                    Text("GoverningLaw", "California"),
                ]));

        // The version records where the document is, and says it does not hold it.
        ContractVersionResponse version = await GetAsync<ContractVersionResponse>(
            f.Client, $"{f.Root}/contract-versions/{first.VersionId}");

        Assert.False(version.HoldsDocument);
        Assert.Equal("MATTER-1042/v1", version.ExternalReference);
        Assert.Equal(3, version.Terms.Count);

        // Reconciliation says what the draft did to what was agreed, and nothing
        // about whether it matters.
        ReconciliationResponse reconciliation = await GetAsync<ReconciliationResponse>(
            f.Client,
            $"{f.Root}/contracts/{contract.Contract.Id}/versions/{first.VersionId}/reconciliation");

        Assert.False(reconciliation.IsFaithful);

        ReconciliationLineResponse compensation =
            reconciliation.Lines.Single(x => x.Code == "GuaranteedCompensation");

        Assert.Equal("Matched", compensation.Result);

        ReconciliationLineResponse billing =
            reconciliation.Lines.Single(x => x.Code == "CreditBilling");

        Assert.Equal("Changed", billing.Result);
        Assert.Equal("First position", billing.Negotiated!.Text);
        Assert.Equal("Second position", billing.Contracted!.Text);

        // Governing law was never negotiated, so the draft added it.
        Assert.Equal(
            "AddedInContract", reconciliation.Lines.Single(x => x.Code == "GoverningLaw").Result);

        // The summary the list shows is the same number, derived not stored.
        contract = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal(reconciliation.DifferenceCount, contract.Contract.UnresolvedDifferenceCount);

        // Rights, an option and an obligation, all read out of that version.
        await RecordRightsGrantAsync(
            f,
            contract.Contract.Id,
            new RecordRightsGrantRequest(
                first.VersionId,
                writer,
                studio,
                "Production",
                "AllMedia",
                "Worldwide",
                "Exclusive",
                "Perpetual",
                StartsOn: Today.AddDays(-30),
                ClauseReference: "3(a)"));

        Guid option = await RecordOptionAsync(
            f,
            contract.Contract.Id,
            new RecordOptionRequest(
                first.VersionId,
                "Sequel",
                studio,
                "Sequel to The Undertow",
                new DeadlineRuleRequest("Absolute", On: Today.AddDays(400)),
                ClauseReference: "9"));

        // A relative deadline with no anchor date yet: honest rather than guessed.
        Guid delivery = await RecordObligationAsync(
            f,
            contract.Contract.Id,
            new RecordObligationRequest(
                first.VersionId,
                writer,
                studio,
                "Delivery",
                "Deliver the first draft screenplay",
                new DeadlineRuleRequest(
                    "Relative",
                    Anchor: "OnDelivery",
                    Offset: 90,
                    Unit: "Days",
                    Description: "Ninety days after commencement of services")));

        contract = await ContractAsync(f, contract.Contract.Id);

        ObligationResponse unresolved = contract.Obligations.Single(x => x.Id == delivery);

        Assert.Null(unresolved.DueOn);
        Assert.NotNull(unresolved.DueUnresolvedReason);
        Assert.False(unresolved.IsPastDue);

        // The option's own date resolved, so it appears on the legal calendar.
        Assert.Single(contract.Options);
        Assert.Equal(Today.AddDays(400), contract.Options.Single().DeadlineOn);

        // Through review, approval and signature. The two statuses that mean
        // somebody signed are unreachable by this route.
        await ChangeStatusAsync(
            f, contract.Contract.Id,
            new ChangeContractStatusRequest("SentForReview", contract.Contract.Version));

        contract = await ContractAsync(f, contract.Contract.Id);

        await ChangeStatusAsync(
            f, contract.Contract.Id,
            new ChangeContractStatusRequest("ApprovedForSignature", contract.Contract.Version));

        contract = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal("ApprovedForExecution", contract.Contract.Status);

        RecordSignatureResponse partial = await RecordSignatureAsync(
            f,
            contract.Contract.Id,
            new RecordSignatureRequest(
                studio, Today.AddDays(-4), "Wet", contract.Contract.Version));

        Assert.Equal("PartiallyExecuted", partial.ContractStatus);
        Assert.Equal(1, partial.OutstandingSignatureCount);

        contract = await ContractAsync(f, contract.Contract.Id);

        // Still not executed, and the date is still absent.
        Assert.Null(contract.Contract.ExecutedOn);

        RecordSignatureResponse complete = await RecordSignatureAsync(
            f,
            contract.Contract.Id,
            new RecordSignatureRequest(
                writer, Today.AddDays(-1), "Electronic", contract.Contract.Version));

        Assert.Equal("Executed", complete.ContractStatus);
        Assert.Equal(0, complete.OutstandingSignatureCount);

        contract = await ContractAsync(f, contract.Contract.Id);

        // Dated from the last signature, not from today.
        Assert.Equal(Today.AddDays(-1), contract.Contract.ExecutedOn);

        // Effectiveness is a separate act, and may precede execution.
        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contract.Contract.Id}/effective-date",
            new RecordEffectiveDateRequest(Today.AddDays(-90), contract.Contract.Version)));

        contract = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal(Today.AddDays(-90), contract.Contract.EffectiveOn);
        Assert.Equal(Today.AddDays(-1), contract.Contract.ExecutedOn);
        Assert.True(contract.Contract.EffectiveOn < contract.Contract.ExecutedOn);

        // A notice is recorded, never sent.
        await RecordNoticeAsync(
            f,
            contract.Contract.Id,
            new RecordNoticeRequest(
                "Given",
                writer,
                studio,
                Today.AddDays(-1),
                "RegisteredPost",
                Summary: "Notice of intent to commence services"));

        // The timeline is composed from domain events, not audit rows.
        ContractHistoryEntryResponse[] history = await GetAsync<ContractHistoryEntryResponse[]>(
            f.Client, $"{f.Root}/contracts/{contract.Contract.Id}/history");

        Assert.Contains(history, x => x.Summary == "Contract opened");
        Assert.Contains(history, x => x.Summary == "Drafting version recorded");
        Assert.Contains(history, x => x.Kind == "Signature");
        Assert.Contains(history, x => x.Kind == "Notice");

        // And M7 is untouched. The contract existing changes nothing about the
        // negotiation that produced it.
        DealDetailResponse deal = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{f.DealId}");

        Assert.Equal("TermsAgreed", deal.Deal.Status);
        Assert.Equal(f.AcceptedOfferId, deal.Deal.AcceptedOfferId);
        Assert.Equal(650_000m, deal.AcceptedOffer!.Terms
            .Single(x => x.Code == "GuaranteedCompensation").Amount);
    }

    // -------------------------------------------------------------- anchoring

    /// <summary>
    /// A contract can only paper an offer that is the deal's agreement.
    /// </summary>
    [Fact]
    public async Task AContractRequiresAnAcceptedOfferFromItsOwnDeal()
    {
        Fixture f = await SetUpAsync("m8-anchor");

        // A second negotiation, whose offer this contract has nothing to do with.
        Fixture other = await SetUpAsync("m8-anchor-other");

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts",
            new CreateContractRequest(
                f.DealId,
                other.AcceptedOfferId,
                "Wrong offer",
                "LongForm",
                f.Actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Several contracts may paper one negotiation.
    /// </summary>
    /// <remarks>
    /// A long form, a side letter and an amendment are three instruments about one
    /// deal, and the model must not force them into one row (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task ADealMayCarryMoreThanOneContract()
    {
        Fixture f = await SetUpAsync("m8-multiple");

        ContractDetailResponse main = await OpenContractAsync(f, "Writer agreement");
        ContractDetailResponse side = await OpenContractAsync(f, "Side letter", "SideLetter");

        Assert.NotEqual(main.Contract.Id, side.Contract.Id);

        ContractSummaryResponse[] listed = await GetAsync<ContractSummaryResponse[]>(
            f.Client, $"{f.Root}/contracts?dealId={f.DealId}");

        Assert.Equal(2, listed.Length);
    }

    /// <summary>
    /// An amendment is linked to the paper it changes, not versioned onto it.
    /// </summary>
    [Fact]
    public async Task AnAmendmentIsASeparateInstrument()
    {
        Fixture f = await SetUpAsync("m8-amendment");

        ContractDetailResponse original = await OpenContractAsync(f, "Writer agreement");
        ContractDetailResponse amendment = await OpenContractAsync(f, "Amendment 1", "Amendment");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{amendment.Contract.Id}/relationships",
            new RecordContractRelationshipRequest(original.Contract.Id, "AmendmentOf")));

        ContractDetailResponse linked = await ContractAsync(f, amendment.Contract.Id);

        ContractRelationshipResponse relationship = Assert.Single(linked.Relationships);

        Assert.Equal("AmendmentOf", relationship.Kind);
        Assert.Equal(original.Contract.Id, relationship.RelatedContractId);

        // The original keeps its own drafting history and its own execution
        // state. It does learn that an amendment points at it - the connection
        // the table exists for - without the amendment becoming one of its
        // drafting versions (ADR-0022).
        ContractDetailResponse untouched = await ContractAsync(f, original.Contract.Id);

        Assert.Equal(0, untouched.Contract.VersionCount);
        Assert.Equal("Draft", untouched.Contract.Status);

        ContractRelationshipResponse reciprocal = Assert.Single(untouched.Relationships);

        Assert.Equal(amendment.Contract.Id, reciprocal.RelatedContractId);
    }

    // ------------------------------------------------------------- invariants

    /// <summary>
    /// Execution follows from signatures and from nothing else.
    /// </summary>
    /// <remarks>
    /// The invariant the milestone exists to protect. A status command that could
    /// set Executed would let a contract claim execution with nobody's signature
    /// behind it (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task ExecutionCannotBeSetByAStatusCommand()
    {
        Fixture f = await SetUpAsync("m8-execution");

        ContractDetailResponse contract = await OpenContractAsync(f);

        foreach (string transition in new[] { "SignatureRecorded", "ExecutionCompleted" })
        {
            using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
                $"{f.Root}/contracts/{contract.Contract.Id}/status",
                new ChangeContractStatusRequest(transition, contract.Contract.Version));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        ContractDetailResponse unchanged = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal("Draft", unchanged.Contract.Status);
        Assert.Null(unchanged.Contract.ExecutedOn);
    }

    /// <summary>
    /// A recorded version's terms are frozen, in the domain and in the database.
    /// </summary>
    [Fact]
    public async Task ARecordedVersionsTermsAreImmutable()
    {
        Fixture f = await SetUpAsync("m8-immutable");

        ContractDetailResponse contract = await OpenContractAsync(f);

        RecordContractVersionResponse version = await RecordVersionAsync(
            f,
            contract.Contract.Id,
            new RecordContractVersionRequest(
                "First draft",
                "Inbound",
                contract.Contract.Version,
                Terms: [Money("GuaranteedCompensation", 650_000m)]));

        ContractVersionResponse recorded = await GetAsync<ContractVersionResponse>(
            f.Client, $"{f.Root}/contract-versions/{version.VersionId}");

        // Recorded rather than draft, because the handler finalises what it records.
        Assert.Equal("Recorded", recorded.Status);

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contract-versions/{version.VersionId}/terms",
            new ChangeContractTermRequest(
                "GuaranteedCompensation",
                recorded.Version,
                new TermValueRequest("Money", Amount: 1_000_000m, Currency: "USD")));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        ContractVersionResponse after = await GetAsync<ContractVersionResponse>(
            f.Client, $"{f.Root}/contract-versions/{version.VersionId}");

        Assert.Equal(
            650_000m, after.Terms.Single(x => x.Code == "GuaranteedCompensation").Amount);
    }

    /// <summary>
    /// A breach needs a determination behind it. A passed date is not one.
    /// </summary>
    [Fact]
    public async Task ABreachRequiresAReasonAndIsNeverInferred()
    {
        Fixture f = await SetUpAsync("m8-breach");

        (ContractDetailResponse contract, Guid _, Guid obligor, Guid versionId) =
            await PaperedAsync(f);

        // Due in the past, and therefore past due the moment it is recorded.
        Guid obligation = await RecordObligationAsync(
            f,
            contract.Contract.Id,
            new RecordObligationRequest(
                versionId,
                obligor,
                contract.Parties.First(x => x.Id != obligor).Id,
                "Payment",
                "Pay the first instalment",
                new DeadlineRuleRequest("Absolute", On: new DateOnly(2020, 1, 1))));

        ContractDetailResponse withObligation = await ContractAsync(f, contract.Contract.Id);

        ObligationResponse overdue = withObligation.Obligations.Single(x => x.Id == obligation);

        // Past due is derived. Breached is not, and the status still says pending.
        Assert.True(overdue.IsPastDue);
        Assert.Equal("Pending", overdue.Status);

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/obligations/{obligation}/resolve",
            new ResolveObligationRequest("Breached", overdue.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        ResolveObligationResponse recorded = await ResolveObligationAsync(
            f,
            obligation,
            new ResolveObligationRequest(
                "Breached", overdue.Version, Reason: "No payment received; counsel notified."));

        Assert.Equal("Breached", recorded.Status);
    }

    /// <summary>
    /// An option past its deadline is not expired until somebody says so.
    /// </summary>
    [Fact]
    public async Task AnOptionNeverLapsesOnItsOwn()
    {
        Fixture f = await SetUpAsync("m8-option");

        (ContractDetailResponse contract, Guid holder, Guid _, Guid versionId) =
            await PaperedAsync(f);

        Guid option = await RecordOptionAsync(
            f,
            contract.Contract.Id,
            new RecordOptionRequest(
                versionId,
                "Sequel",
                holder,
                "Sequel",
                new DeadlineRuleRequest("Absolute", On: new DateOnly(2020, 1, 1))));

        ContractOptionResponse[] options = await GetAsync<ContractOptionResponse[]>(
            f.Client, $"{f.Root}/contract-options?contractId={contract.Contract.Id}");

        ContractOptionResponse lapsed = options.Single(x => x.Id == option);

        // Past its deadline, and still available. That combination is exactly what
        // a legal work queue exists to surface (ADR-0022).
        Assert.True(lapsed.IsPastDeadline);
        Assert.Equal("Available", lapsed.Status);
        Assert.False(lapsed.IsExercisable);

        ContractOptionResponse[] pastDeadline = await GetAsync<ContractOptionResponse[]>(
            f.Client, $"{f.Root}/contract-options?pastDeadlineOnly=true");

        Assert.Contains(pastDeadline, x => x.Id == option);

        // Only an explicit act moves it.
        ResolveOptionResponse resolved = await ResolveOptionAsync(
            f, option, new ResolveOptionRequest("Expired", lapsed.Version));

        Assert.Equal("Expired", resolved.Status);
    }

    /// <summary>
    /// A business-day deadline is stored faithfully and resolves to nothing.
    /// </summary>
    /// <remarks>
    /// AgencyOS holds no holiday calendar. Counting business days as calendar days
    /// would produce a legal deadline that looks authoritative and is wrong, so the
    /// clause keeps its stated intent and has no date until a calendar exists
    /// (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task ABusinessDayDeadlineIsStoredAndNotComputed()
    {
        Fixture f = await SetUpAsync("m8-business-days");

        (ContractDetailResponse contract, Guid holder, Guid obligor, Guid versionId) =
            await PaperedAsync(f);

        Guid obligation = await RecordObligationAsync(
            f,
            contract.Contract.Id,
            new RecordObligationRequest(
                versionId,
                obligor,
                holder,
                "Approval",
                "Approve the treatment",
                new DeadlineRuleRequest(
                    "Relative",
                    Anchor: "OnExecution",
                    Offset: 10,
                    Unit: "Days",
                    Basis: "BusinessDays",
                    Description: "Ten business days after execution"),
                AnchorDate: Today.AddDays(-1)));

        ContractDetailResponse detail = await ContractAsync(f, contract.Contract.Id);

        ObligationResponse stored = detail.Obligations.Single(x => x.Id == obligation);

        Assert.Null(stored.DueOn);
        Assert.Contains("business day", stored.DueUnresolvedReason!, StringComparison.OrdinalIgnoreCase);

        // The clause's own words survive, which is what somebody actually reads.
        Assert.Equal("Ten business days after execution", stored.DueDescription);

        // And it is absent from the deadline list rather than guessed onto a day.
        LegalDeadlineResponse[] deadlines = await GetAsync<LegalDeadlineResponse[]>(
            f.Client, $"{f.Root}/legal/deadlines?withinDays=365");

        Assert.DoesNotContain(deadlines, x => x.SourceId == obligation);
    }

    /// <summary>
    /// A grant is superseded rather than overwritten.
    /// </summary>
    [Fact]
    public async Task AnAmendedGrantSupersedesRatherThanOverwrites()
    {
        Fixture f = await SetUpAsync("m8-rights");

        (ContractDetailResponse contract, Guid grantee, Guid grantor, Guid versionId) =
            await PaperedAsync(f);

        Guid original = await RecordRightsGrantAsync(
            f,
            contract.Contract.Id,
            new RecordRightsGrantRequest(
                versionId, grantor, grantee, "Production", "AllMedia", "Worldwide",
                "Exclusive", "Perpetual", StartsOn: Today.AddDays(-30)));

        RightsGrantResponse[] current = await GetAsync<RightsGrantResponse[]>(
            f.Client, $"{f.Root}/rights-grants?contractId={contract.Contract.Id}");

        RightsGrantResponse before = current.Single(x => x.Id == original);

        Assert.Equal("Worldwide", before.Territory);

        await RecordRightsGrantAsync(
            f,
            contract.Contract.Id,
            new RecordRightsGrantRequest(
                versionId, grantor, grantee, "Production", "AllMedia", "UnitedStates",
                "Exclusive", "Perpetual",
                StartsOn: Today.AddDays(-30),
                SupersedesGrantId: original,
                SupersededExpectedVersion: before.Version));

        // What we believed we had before the amendment is still answerable.
        RightsGrantResponse[] all = await GetAsync<RightsGrantResponse[]>(
            f.Client, $"{f.Root}/rights-grants?contractId={contract.Contract.Id}&currentOnly=false");

        Assert.Equal(2, all.Length);

        RightsGrantResponse superseded = all.Single(x => x.Id == original);

        Assert.Equal("Superseded", superseded.Status);
        Assert.Equal("Worldwide", superseded.Territory);
        Assert.NotNull(superseded.SupersededByGrantId);

        RightsGrantResponse[] currentOnly = await GetAsync<RightsGrantResponse[]>(
            f.Client, $"{f.Root}/rights-grants?contractId={contract.Contract.Id}");

        Assert.Equal("UnitedStates", Assert.Single(currentOnly).Territory);
    }

    // ------------------------------------------------------------- redaction

    /// <summary>
    /// Terms, economics and privileged content are each gated separately.
    /// </summary>
    /// <remarks>
    /// Three rules, and this is where they are proved to be three rather than one.
    /// Absent is deliberately indistinguishable from empty throughout (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task Redaction_HidesTermsEconomicsAndPrivilegedContentSeparately()
    {
        Fixture f = await SetUpAsync("m8-redaction");

        ContractDetailResponse contract = await OpenContractAsync(
            f, analysis: "Counsel considers the indemnity unusually broad.");

        RecordContractVersionResponse version = await RecordVersionAsync(
            f,
            contract.Contract.Id,
            new RecordContractVersionRequest(
                "First draft",
                "Inbound",
                contract.Contract.Version,
                Terms:
                [
                    Money("GuaranteedCompensation", 650_000m),
                    Text("GoverningLaw", "California"),
                ]));

        // The member who recorded it sees everything.
        ContractDetailResponse mine = await ContractAsync(f, contract.Contract.Id);

        Assert.NotNull(mine.LegalAnalysis);
        Assert.Equal(2, mine.Versions.Single().Terms.Count);

        string observerSubject = $"m8-observer-{Guid.NewGuid():N}";
        Domain.Identity.User observer = await _fixture.SeedUserAsync(observerSubject, "Observer");

        await _fixture.SeedMembershipAsync(
            f.Actor.Organization.Id, observer.Id, AgencyRole.Observer, f.Actor.User.Id);

        using HttpClient observerClient = _fixture.CreateClient(observerSubject);

        ContractDetailResponse observed = await GetAsync<ContractDetailResponse>(
            observerClient, $"{f.Root}/contracts/{contract.Contract.Id}");

        // An observer holds contracts.read and nothing else of the three, so the
        // instrument is visible and its content is not.
        Assert.Equal(contract.Contract.Id, observed.Contract.Id);
        Assert.Null(observed.LegalAnalysis);
        Assert.Empty(observed.Versions.Single().Terms);

        // The direct version read applies the identical rule.
        ContractVersionResponse observedVersion = await GetAsync<ContractVersionResponse>(
            observerClient, $"{f.Root}/contract-versions/{version.VersionId}");

        Assert.Empty(observedVersion.Terms);

        // Reconciliation is refused rather than emptied: a comparison with the
        // terms removed would report the draft matched when it did not.
        using HttpResponseMessage refused = await observerClient.GetAsync(
            $"{f.Root}/contracts/{contract.Contract.Id}/versions/{version.VersionId}/reconciliation");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// Search finds a contract, never its analysis or its terms.
    /// </summary>
    /// <remarks>
    /// The leak this milestone was warned about: a redaction that leaves a search
    /// hit behind is not a redaction. A phrase that appears only in privileged
    /// content must not surface the contract to anybody (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task Search_FindsContractsButNeverTheirAnalysisOrTerms()
    {
        Fixture f = await SetUpAsync("m8-search");

        ContractDetailResponse contract = await OpenContractAsync(
            f,
            title: "Undertow writer agreement",
            analysis: "Counsel flagged the zephyrine indemnity clause.",
            strategy: "We will trade the quillon credit for the fee.");

        await RecordVersionAsync(
            f,
            contract.Contract.Id,
            new RecordContractVersionRequest(
                "First draft",
                "Inbound",
                contract.Contract.Version,
                Terms: [Money("GuaranteedCompensation", 650_000m)]));

        SearchResponse byTitle = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=Undertow&types=Contract");

        Assert.Contains(byTitle.Hits, x => x.Id == contract.Contract.Id);

        // Nothing privileged is indexed, so nothing privileged can be confirmed by
        // watching a contract surface - not even by the person who wrote it.
        foreach (string phrase in new[] { "zephyrine", "quillon", "650000" })
        {
            SearchResponse hidden = await GetAsync<SearchResponse>(
                f.Client, $"{f.Root}/search?q={phrase}&types=Contract");

            Assert.DoesNotContain(hidden.Hits, x => x.Id == contract.Contract.Id);
        }
    }

    // --------------------------------------------------------- concurrency

    /// <summary>
    /// A stale version is refused rather than silently overwriting.
    /// </summary>
    [Fact]
    public async Task AStaleVersionIsRefused()
    {
        Fixture f = await SetUpAsync("m8-concurrency");

        ContractDetailResponse contract = await OpenContractAsync(f);

        int stale = contract.Contract.Version;

        await NoContentAsync(f.Client.PutAsJsonAsync(
            $"{f.Root}/contracts/{contract.Contract.Id}",
            new UpdateContractRequest(
                "Renamed", "LongForm", f.Actor.User.Id.Value, stale)));

        using HttpResponseMessage refused = await f.Client.PutAsJsonAsync(
            $"{f.Root}/contracts/{contract.Contract.Id}",
            new UpdateContractRequest(
                "Renamed again", "LongForm", f.Actor.User.Id.Value, stale));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    /// <summary>
    /// Replaying a recorded version with the same key records it once.
    /// </summary>
    [Fact]
    public async Task ReplayingARecordedVersionIsIdempotent()
    {
        Fixture f = await SetUpAsync("m8-idempotency");

        ContractDetailResponse contract = await OpenContractAsync(f);

        string key = Guid.NewGuid().ToString("N");

        RecordContractVersionRequest request = new(
            "First draft",
            "Inbound",
            contract.Contract.Version,
            Terms: [Money("GuaranteedCompensation", 650_000m)]);

        RecordContractVersionResponse first =
            await RecordVersionAsync(f, contract.Contract.Id, request, key);

        RecordContractVersionResponse replay =
            await RecordVersionAsync(f, contract.Contract.Id, request, key);

        Assert.Equal(first.VersionId, replay.VersionId);

        ContractDetailResponse after = await ContractAsync(f, contract.Contract.Id);

        Assert.Equal(1, after.Contract.VersionCount);
    }

    // ------------------------------------------------------------ authorization

    /// <summary>
    /// An observer cannot write anything in the legal model.
    /// </summary>
    [Fact]
    public async Task AnObserverCannotWrite()
    {
        Fixture f = await SetUpAsync("m8-authorization");

        string subject = $"m8-observer-write-{Guid.NewGuid():N}";
        Domain.Identity.User observer = await _fixture.SeedUserAsync(subject, "Observer");

        await _fixture.SeedMembershipAsync(
            f.Actor.Organization.Id, observer.Id, AgencyRole.Observer, f.Actor.User.Id);

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"{f.Root}/contracts",
            new CreateContractRequest(
                f.DealId,
                f.AcceptedOfferId,
                "Not allowed",
                "LongForm",
                f.Actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ------------------------------------------------------------ command centre

    /// <summary>
    /// The command centre reports the gap between agreed terms and paper.
    /// </summary>
    [Fact]
    public async Task TheCommandCentreShowsAgreedDealsNobodyHasPapered()
    {
        Fixture f = await SetUpAsync("m8-command-centre");

        ContractCommandCenterResponse before = await GetAsync<ContractCommandCenterResponse>(
            f.Client, $"{f.Root}/legal/command-center");

        Assert.Contains(before.TermsAgreedWithoutContract, x => x.DealId == f.DealId);

        await OpenContractAsync(f);

        ContractCommandCenterResponse after = await GetAsync<ContractCommandCenterResponse>(
            f.Client, $"{f.Root}/legal/command-center");

        Assert.DoesNotContain(after.TermsAgreedWithoutContract, x => x.DealId == f.DealId);
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// The day the suite runs.
    /// </summary>
    /// <remarks>
    /// Dates are relative to it rather than fixed, because the domain refuses a
    /// version that arrived in the future and a signature dated after today. A
    /// hard-coded calendar year would make the suite pass until it quietly did not.
    /// </remarks>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Fixture(
        SeededActor Actor,
        HttpClient Client,
        string Root,
        Guid DealId,
        Guid AcceptedOfferId,
        Guid StudioId,
        Guid WriterId);

    /// <summary>
    /// Seeds an M6 pursuit, an M7 negotiation and an accepted offer.
    /// </summary>
    /// <remarks>
    /// M8 starts where M7 stops, so every test here needs a real agreement behind
    /// it. Faking one would test the contract model against a shape the rest of the
    /// system never produces.
    /// </remarks>
    private async Task<Fixture> SetUpAsync(string label)
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

        PersonDetailResponse writer = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Ada Sallow")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Description: "Take the feature out to studios.",
                    Subjects: [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunity.Opportunity.Id}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        OpportunityDetailResponse active = await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{opportunity.Opportunity.Id}");

        Guid targetId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunity.Opportunity.Id}/targets",
            new AddOpportunityTargetRequest(active.Opportunity.Version, CompanyId: studio.Company.Id)));

        foreach (string step in new[] { "Approved", "Contacted", "Engaged", "Interested" })
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                client, $"{root}/opportunity-targets/{targetId}");

            await NoContentAsync(client.PostAsJsonAsync(
                $"{root}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(step, target.Version)));
        }

        DealDetailResponse deal = await CreatedAsync<DealDetailResponse>(client.PostAsJsonAsync(
            $"{root}/deals",
            new CreateDealRequest(
                opportunity.Opportunity.Id,
                targetId,
                "The Undertow - Northgate",
                "Writing",
                actor.User.Id.Value)));

        Guid offerId = await CreatedOfferAsync(client, root, deal.Deal.Id, deal.Deal.Version);

        OfferResponse offer = await GetAsync<OfferResponse>(client, $"{root}/offers/{offerId}");

        using HttpResponseMessage accepted = await client.PostAsJsonAsync(
            $"{root}/offers/{offerId}/answer", new AnswerOfferRequest("Accept", offer.Version));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        return new Fixture(
            actor, client, root, deal.Deal.Id, offerId, studio.Company.Id, writer.Person.Id);
    }

    private static async Task<Guid> CreatedOfferAsync(
        HttpClient client,
        string root,
        Guid dealId,
        int expectedVersion)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{root}/deals/{dealId}/offers",
            new RecordOfferRequest(
                "Inbound",
                [
                    new OfferTermRequest(
                        "GuaranteedCompensation",
                        new TermValueRequest("Money", Amount: 650_000m, Currency: "USD")),
                    new OfferTermRequest(
                        "CreditBilling", new TermValueRequest("Text", Text: "First position")),
                ],
                expectedVersion));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        RecordOfferResponse recorded = (await response.Content
            .ReadFromJsonAsync<RecordOfferResponse>())!;

        return recorded.OfferId;
    }

    /// <summary>A contract with two parties and one recorded version.</summary>
    private static async Task<(ContractDetailResponse Contract, Guid Studio, Guid Writer, Guid Version)>
        PaperedAsync(Fixture f)
    {
        ContractDetailResponse contract = await OpenContractAsync(f);

        Guid studio = await AddPartyAsync(
            f, contract.Contract.Id, new AddContractPartyRequest(
                "Studio", contract.Contract.Version, CompanyId: f.StudioId));

        contract = await ContractAsync(f, contract.Contract.Id);

        Guid writer = await AddPartyAsync(
            f, contract.Contract.Id, new AddContractPartyRequest(
                "Artist", contract.Contract.Version, PersonId: f.WriterId));

        contract = await ContractAsync(f, contract.Contract.Id);

        RecordContractVersionResponse version = await RecordVersionAsync(
            f,
            contract.Contract.Id,
            new RecordContractVersionRequest(
                "First draft",
                "Inbound",
                contract.Contract.Version,
                Terms: [Money("GuaranteedCompensation", 650_000m)]));

        contract = await ContractAsync(f, contract.Contract.Id);

        return (contract, studio, writer, version.VersionId);
    }

    private static Task<ContractDetailResponse> OpenContractAsync(
        Fixture f,
        string title = "Undertow writer agreement",
        string kind = "LongForm",
        string? analysis = null,
        string? strategy = null) =>
        CreatedAsync<ContractDetailResponse>(f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts",
            new CreateContractRequest(
                f.DealId,
                f.AcceptedOfferId,
                title,
                kind,
                f.Actor.User.Id.Value,
                Summary: "The paper for the agreed terms.",
                LegalAnalysis: analysis,
                StrategyNotes: strategy,
                Privilege: analysis is null ? null : "AttorneyClientPrivileged")));

    private static Task<ContractDetailResponse> ContractAsync(Fixture f, Guid contractId) =>
        GetAsync<ContractDetailResponse>(f.Client, $"{f.Root}/contracts/{contractId}");

    private static async Task<Guid> AddPartyAsync(
        Fixture f,
        Guid contractId,
        AddContractPartyRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/parties", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AddContractPartyResponse>())!.ContractPartyId;
    }

    private static async Task<RecordContractVersionResponse> RecordVersionAsync(
        Fixture f,
        Guid contractId,
        RecordContractVersionRequest request,
        string? key = null)
    {
        using HttpRequestMessage message =
            new(HttpMethod.Post, $"{f.Root}/contracts/{contractId}/versions")
            {
                Content = JsonContent.Create(request),
            };

        if (key is not null)
        {
            message.Headers.Add(ClientHeaders.IdempotencyKey, key);
        }

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordContractVersionResponse>())!;
    }

    private static async Task<RecordSignatureResponse> RecordSignatureAsync(
        Fixture f,
        Guid contractId,
        RecordSignatureRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/signatures", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordSignatureResponse>())!;
    }

    private static async Task<Guid> RecordRightsGrantAsync(
        Fixture f,
        Guid contractId,
        RecordRightsGrantRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/rights-grants", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordRightsGrantResponse>())!.RightsGrantId;
    }

    private static async Task<Guid> RecordOptionAsync(
        Fixture f,
        Guid contractId,
        RecordOptionRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/options", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordOptionResponse>())!.ContractOptionId;
    }

    private static async Task<ResolveOptionResponse> ResolveOptionAsync(
        Fixture f,
        Guid optionId,
        ResolveOptionRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contract-options/{optionId}/resolve", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ResolveOptionResponse>())!;
    }

    private static async Task<Guid> RecordObligationAsync(
        Fixture f,
        Guid contractId,
        RecordObligationRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/obligations", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordObligationResponse>())!.ObligationId;
    }

    private static async Task<ResolveObligationResponse> ResolveObligationAsync(
        Fixture f,
        Guid obligationId,
        ResolveObligationRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/obligations/{obligationId}/resolve", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ResolveObligationResponse>())!;
    }

    private static async Task RecordNoticeAsync(
        Fixture f,
        Guid contractId,
        RecordNoticeRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/notices", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ChangeStatusAsync(
        Fixture f,
        Guid contractId,
        ChangeContractStatusRequest request)
    {
        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{contractId}/status", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static ContractTermRequest Money(string code, decimal amount, string currency = "USD") =>
        new(code, new TermValueRequest("Money", Amount: amount, Currency: currency));

    private static ContractTermRequest Text(string code, string text) =>
        new(code, new TermValueRequest("Text", Text: text));

    private sealed record CreatedId(Guid Id);

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
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
