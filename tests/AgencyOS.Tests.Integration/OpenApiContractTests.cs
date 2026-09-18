using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The versioned, machine-readable API contract.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> principle 6 requires API contracts to be versioned and
/// explicit. A document that is valid but incomplete would satisfy a schema
/// validator and still fail the purpose, so these tests assert the surface it
/// describes, not merely that it parses.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed partial class OpenApiContractTests
{
    private readonly AgencyOsTestFixture _fixture;

    public OpenApiContractTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Contract_IsServedForVersionOne()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ASP.NET Core 10 emits OpenAPI 3.1 natively, so the target version needs no
    /// custom serialization code. This asserts that remains true.
    /// </summary>
    [Fact]
    public async Task Contract_IsOpenApi31()
    {
        using JsonDocument document = await GetContractAsync();

        string version = document.RootElement.GetProperty("openapi").GetString()!;

        Assert.StartsWith("3.1", version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contract_IdentifiesItself()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement info = document.RootElement.GetProperty("info");

        Assert.Equal("AgencyOS API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
    }

    /// <summary>
    /// The document must describe every implemented route: version identity and the
    /// release handshake from M1, the people slice from M2, search, saved views
    /// and synchronization from M3, talent, prospects, representation, credits and
    /// materials from M4, projects, source properties, roles, attachments and
    /// packages from M5, opportunities, targets, submissions and pitches from M6,
    /// and deals, offers and the term catalog from M7, contracts, versions,
    /// reconciliation, rights, options, obligations and notices from M8, and
    /// monetary obligations, receivables, invoices, payments, allocations,
    /// commissions, the ledger and reconciliation from M9.
    /// </summary>
    /// <remarks>
    /// A contract that silently stopped describing a route would still be valid
    /// OpenAPI, and a client generated from it would simply not know the route
    /// exists. Listing them is the only way that failure is visible.
    /// </remarks>
    [Theory]
    [InlineData("/version")]
    [InlineData("/api/v1/release/handshake")]
    [InlineData("/api/v1/system/status")]
    [InlineData("/api/v1/organizations")]
    [InlineData("/api/v1/organizations/{id}")]
    [InlineData("/api/v1/organizations/{id}/memberships")]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/organizations/{organizationId}/people")]
    [InlineData("/api/v1/organizations/{organizationId}/people/{personId}")]
    [InlineData("/api/v1/organizations/{organizationId}/people/{personId}/timeline")]
    [InlineData("/api/v1/organizations/{organizationId}/companies")]
    [InlineData("/api/v1/organizations/{organizationId}/companies/{companyId}")]
    [InlineData("/api/v1/organizations/{organizationId}/companies/{companyId}/timeline")]
    [InlineData("/api/v1/organizations/{organizationId}/relationships")]
    [InlineData("/api/v1/organizations/{organizationId}/relationships/{relationshipId}/end")]
    [InlineData("/api/v1/organizations/{organizationId}/interactions")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks/{taskId}/complete")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks/{taskId}/reopen")]
    [InlineData("/api/v1/organizations/{organizationId}/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/search")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views/{savedViewId}")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views/{savedViewId}/results")]
    [InlineData("/api/v1/organizations/{organizationId}/sync/changes")]
    [InlineData("/api/v1/organizations/{organizationId}/sync/head")]
    [InlineData("/api/v1/organizations/{organizationId}/talent")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/overview")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/credits")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/materials")]
    [InlineData("/api/v1/organizations/{organizationId}/talent-profiles/{talentProfileId}")]
    [InlineData("/api/v1/organizations/{organizationId}/talent-profiles/{talentProfileId}/disciplines")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}/advance")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}/convert")]
    [InlineData("/api/v1/organizations/{organizationId}/representations")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/transition")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/scopes")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/team")]
    [InlineData("/api/v1/organizations/{organizationId}/credits")]
    [InlineData("/api/v1/organizations/{organizationId}/credits/{creditId}")]
    [InlineData("/api/v1/organizations/{organizationId}/materials")]
    [InlineData("/api/v1/organizations/{organizationId}/materials/{materialId}")]
    [InlineData("/api/v1/organizations/{organizationId}/projects")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/stage")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/attachments")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/roles")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/roles/{roleId}/change")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/roles/{roleId}/attachments")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/attachments/{attachmentId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/companies")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/source-properties")]
    [InlineData("/api/v1/organizations/{organizationId}/projects/{projectId}/materials")]
    [InlineData("/api/v1/organizations/{organizationId}/source-properties")]
    [InlineData("/api/v1/organizations/{organizationId}/source-properties/{sourcePropertyId}")]
    [InlineData("/api/v1/organizations/{organizationId}/packages")]
    [InlineData("/api/v1/organizations/{organizationId}/packages/{packageId}")]
    [InlineData("/api/v1/organizations/{organizationId}/packages/{packageId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/packages/{packageId}/elements")]
    [InlineData("/api/v1/organizations/{organizationId}/credits/{creditId}/project")]
    [InlineData("/api/v1/organizations/{organizationId}/project-command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/subjects")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/subjects/{subjectId}/remove")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/targets")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/stage")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/responses")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/submissions")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/pitches")]
    [InlineData("/api/v1/organizations/{organizationId}/submissions")]
    [InlineData("/api/v1/organizations/{organizationId}/submissions/{submissionId}")]
    [InlineData("/api/v1/organizations/{organizationId}/pitches")]
    [InlineData("/api/v1/organizations/{organizationId}/pipeline")]
    [InlineData("/api/v1/organizations/{organizationId}/opportunity-command-center")]
    [InlineData("/api/v1/deal-terms")]
    [InlineData("/api/v1/organizations/{organizationId}/deals")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/close")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/reopen")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/offers")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/draft-offers")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/current-offer")]
    [InlineData("/api/v1/organizations/{organizationId}/deals/{dealId}/comparison")]
    [InlineData("/api/v1/organizations/{organizationId}/offers")]
    [InlineData("/api/v1/organizations/{organizationId}/offers/{offerId}")]
    [InlineData("/api/v1/organizations/{organizationId}/offers/{offerId}/terms")]
    [InlineData("/api/v1/organizations/{organizationId}/offers/{offerId}/record")]
    [InlineData("/api/v1/organizations/{organizationId}/offers/{offerId}/answer")]
    [InlineData("/api/v1/organizations/{organizationId}/deal-pipeline")]
    [InlineData("/api/v1/organizations/{organizationId}/deal-command-center")]
    [InlineData("/api/v1/contract-terms/catalog")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/effective-date")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/parties")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/signatures")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/relationships")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/versions")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/rights-grants")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/options")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/obligations")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/notice-requirements")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/notices")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/tasks")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/versions/{versionId}/reconciliation")]
    [InlineData("/api/v1/organizations/{organizationId}/contract-versions/{versionId}")]
    [InlineData("/api/v1/organizations/{organizationId}/contract-versions/{versionId}/terms")]
    [InlineData("/api/v1/organizations/{organizationId}/contract-versions/{versionId}/record")]
    [InlineData("/api/v1/organizations/{organizationId}/rights-grants")]
    [InlineData("/api/v1/organizations/{organizationId}/rights-grants/{grantId}/end")]
    [InlineData("/api/v1/organizations/{organizationId}/contract-options")]
    [InlineData("/api/v1/organizations/{organizationId}/contract-options/{optionId}/resolve")]
    [InlineData("/api/v1/organizations/{organizationId}/obligations")]
    [InlineData("/api/v1/organizations/{organizationId}/obligations/{obligationId}/resolve")]
    [InlineData("/api/v1/organizations/{organizationId}/legal/deadlines")]
    [InlineData("/api/v1/organizations/{organizationId}/legal/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/quantify")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/release")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/receivables")]
    [InlineData("/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/commission")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/monetary-obligations")]
    [InlineData("/api/v1/organizations/{organizationId}/contracts/{contractId}/invoices")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables/{receivableId}")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables/{receivableId}/write-off")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables/{receivableId}/cancel")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables/{receivableId}/adjustments")]
    [InlineData("/api/v1/organizations/{organizationId}/receivables/{receivableId}/reconciliation")]
    [InlineData("/api/v1/organizations/{organizationId}/invoices")]
    [InlineData("/api/v1/organizations/{organizationId}/invoices/{invoiceId}")]
    [InlineData("/api/v1/organizations/{organizationId}/invoices/{invoiceId}/issue")]
    [InlineData("/api/v1/organizations/{organizationId}/invoices/{invoiceId}/void")]
    [InlineData("/api/v1/organizations/{organizationId}/payments")]
    [InlineData("/api/v1/organizations/{organizationId}/payments/{paymentId}")]
    [InlineData("/api/v1/organizations/{organizationId}/payments/{paymentId}/allocations")]
    [InlineData("/api/v1/organizations/{organizationId}/payments/{paymentId}/allocations/reverse")]
    [InlineData("/api/v1/organizations/{organizationId}/payments/{paymentId}/reverse")]
    [InlineData("/api/v1/organizations/{organizationId}/commission-rules")]
    [InlineData("/api/v1/organizations/{organizationId}/commission-rules/{ruleId}/end")]
    [InlineData("/api/v1/organizations/{organizationId}/commissions")]
    [InlineData("/api/v1/organizations/{organizationId}/commissions/{commissionId}")]
    [InlineData("/api/v1/organizations/{organizationId}/commissions/{commissionId}/adjustments")]
    [InlineData("/api/v1/organizations/{organizationId}/ledger/accounts")]
    [InlineData("/api/v1/organizations/{organizationId}/ledger/balances")]
    [InlineData("/api/v1/organizations/{organizationId}/ledger/entries")]
    [InlineData("/api/v1/organizations/{organizationId}/ledger/entries/{entryId}")]
    [InlineData("/api/v1/organizations/{organizationId}/ledger/entries/{entryId}/reverse")]
    [InlineData("/api/v1/organizations/{organizationId}/finance/history")]
    [InlineData("/api/v1/organizations/{organizationId}/finance/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/documents")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/versions")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/update")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/links")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/links/{linkId}")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/archive")]
    [InlineData("/api/v1/organizations/{organizationId}/documents/{documentId}/restore")]
    [InlineData("/api/v1/organizations/{organizationId}/document-versions/{versionId}/content")]
    [InlineData("/api/v1/organizations/{organizationId}/communication-providers")]
    [InlineData("/api/v1/organizations/{organizationId}/communication-accounts")]
    [InlineData("/api/v1/organizations/{organizationId}/communication-accounts/{accountId}/disconnect")]
    [InlineData("/api/v1/organizations/{organizationId}/communication-accounts/{accountId}/visibility")]
    [InlineData("/api/v1/organizations/{organizationId}/messages")]
    [InlineData("/api/v1/organizations/{organizationId}/messages/{messageId}")]
    [InlineData("/api/v1/organizations/{organizationId}/messages/{messageId}/links")]
    [InlineData("/api/v1/organizations/{organizationId}/messages/{messageId}/links/{linkId}")]
    [InlineData("/api/v1/organizations/{organizationId}/messages/{messageId}/participants/{participantId}")]
    [InlineData("/api/v1/organizations/{organizationId}/participant-suggestions")]
    [InlineData("/api/v1/organizations/{organizationId}/message-attachments/{attachmentId}/ingest")]
    [InlineData("/api/v1/organizations/{organizationId}/outbound-messages")]
    [InlineData("/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}")]
    [InlineData("/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}/queue")]
    [InlineData("/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}/cancel")]
    [InlineData("/api/v1/organizations/{organizationId}/communications/history")]
    [InlineData("/api/v1/organizations/{organizationId}/communications/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/sources")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/sources/{sourceId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/sources/{sourceId}/reliability")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/verification")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/evidence")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/evidence/{evidenceId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/subjects")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/subjects/{subjectRowId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/activate")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/revisions")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/close")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/evidence")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/evidence/{evidenceId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/subjects")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/calibration")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/revisions")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/resolve")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/cancel")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/subjects")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/activity")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/entries")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/entries/{entryId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/review")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/archive")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/review")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/dismiss")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/convert")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/status")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/links")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/links/{linkId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/subjects")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/relationships/{kind}/{subjectId}")]
    [InlineData("/api/v1/organizations/{organizationId}/intelligence/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/runs")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/runs/{runId}")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/runs/{runId}/cancel")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/approvals")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/approvals/{approvalId}")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/approvals/{approvalId}/decision")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/tool-requests/{toolRequestId}/execute")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/agents")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/agents/{kind}/tools")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/models")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/policies")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/policies/{providerKey}")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/runs/{runId}/local-lease")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/runs/{runId}/local-result")]
    [InlineData("/api/v1/organizations/{organizationId}/ai/execution-targets")]
    public async Task Contract_DescribesTheImplementedSurface(string path)
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out _),
            $"The OpenAPI document does not describe '{path}'.");
    }

    /// <summary>
    /// A timestamp is still published as a date-time string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repair Wave 003A.2 reads incoming timestamps as UTC instants through a
    /// custom converter (<c>AOS-R002-001</c>), and the schema generator cannot see
    /// through a custom converter: without help it published every timestamp as
    /// "any value at all", losing the type, the format and the nullability. The
    /// document is the contract, and clients are generated from it.
    /// </para>
    /// <para>
    /// The contract gate counts paths and schemas, and both counts were unchanged
    /// by that degradation. This is the assertion that would have caught it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("RecordSubmissionRequest", "sentAt", true)]
    [InlineData("RecordPitchRequest", "occurredAt", true)]
    [InlineData("OpportunityFollowUpRequest", "dueAt", true)]
    [InlineData("RecordInteractionRequest", "occurredAt", false)]
    public async Task Contract_PublishesTimestampsAsDateTimeStrings(
        string schemaName, string property, bool nullable)
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schema = document.RootElement
            .GetProperty("components").GetProperty("schemas")
            .GetProperty(schemaName).GetProperty("properties").GetProperty(property);

        Assert.Equal("date-time", schema.GetProperty("format").GetString());

        JsonElement type = schema.GetProperty("type");

        string[] declared = type.ValueKind == JsonValueKind.Array
            ? [.. type.EnumerateArray().Select(x => x.GetString()!)]
            : [type.GetString()!];

        Assert.Contains("string", declared);
        Assert.Equal(nullable, declared.Contains("null"));
    }

    /// <summary>
    /// A calendar date is still published as a date, not as a date-time.
    /// </summary>
    /// <remarks>
    /// The distinction the same request carries: "reply expected by" is a business
    /// date and must not acquire a time zone on the way to a client.
    /// </remarks>
    [Fact]
    public async Task Contract_PublishesCalendarDatesAsDates()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schema = document.RootElement
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("RecordSubmissionRequest").GetProperty("properties")
            .GetProperty("responseExpectedBy");

        Assert.Equal("date", schema.GetProperty("format").GetString());
    }

    /// <summary>
    /// The bootstrap route is absent here because this host has no bootstrap token
    /// configured, so it is genuinely not mapped. The published contract is
    /// generated with a throwaway token by
    /// <c>scripts/Invoke-AgencyOS.ps1 contract</c> so it documents the full surface.
    /// </summary>
    [Fact]
    public async Task Contract_OmitsBootstrapWhenItIsNotEnabled()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.False(paths.TryGetProperty("/api/v1/system/bootstrap", out _));
    }

    /// <summary>
    /// No two paths may differ only in what their template parameters are called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OpenAPI 3.1 forbids it, and for a good reason: <c>/talent/{personId}</c> and
    /// <c>/talent/{talentProfileId}</c> are the same path as far as a router or a
    /// generated client is concerned, so a document containing both describes two
    /// resources that a caller has no way to tell apart.
    /// </para>
    /// <para>
    /// ASP.NET Core will happily route them, because it separates them by HTTP
    /// method. The contract cannot. M4 introduced exactly this pair before the
    /// talent-profile routes were moved to their own resource, and nothing in the
    /// build noticed, which is why this test exists rather than a note in a review.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Contract_HasNoPathsThatDifferOnlyByParameterName()
    {
        using JsonDocument document = await GetContractAsync();

        Dictionary<string, List<string>> byShape = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            // Reduce every template parameter to a placeholder, so paths that a
            // caller could not distinguish collapse onto the same key.
            string shape = TemplateParameter().Replace(path.Name, "{}");

            if (!byShape.TryGetValue(shape, out List<string>? paths))
            {
                byShape[shape] = paths = [];
            }

            paths.Add(path.Name);
        }

        KeyValuePair<string, List<string>>[] ambiguous = [.. byShape.Where(x => x.Value.Count > 1)];

        Assert.True(
            ambiguous.Length == 0,
            "The contract contains paths that differ only by parameter name: "
                + string.Join("; ", ambiguous.Select(x => string.Join(" and ", x.Value))));
    }

    /// <summary>
    /// Money crosses the wire as an amount and a currency, never as a bare number.
    /// </summary>
    /// <remarks>
    /// The rule the whole finance milestone rests on, checked where a generated
    /// client would read it. A figure that travelled without its currency is one
    /// somebody would assume is dollars, and AgencyOS holds no exchange rate that
    /// could correct the assumption later (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task Contract_DescribesMoneyAsAnAmountAndACurrency()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(
            schemas.TryGetProperty("MoneyRequest", out JsonElement money),
            "The contract does not describe MoneyRequest.");

        JsonElement properties = money.GetProperty("properties");

        Assert.True(properties.TryGetProperty("amount", out JsonElement amount));
        Assert.True(properties.TryGetProperty("currency", out _));

        // Both are required. An amount without a currency is the failure this
        // shape exists to prevent, so neither may be omitted.
        string[] required =
            [.. money.GetProperty("required").EnumerateArray().Select(x => x.GetString()!)];

        Assert.Contains("amount", required);
        Assert.Contains("currency", required);

        // The amount is emitted with the exact-decimal pattern rather than as a
        // plain JSON number, so a generated client that reads it as a float has to
        // do so deliberately.
        Assert.True(amount.TryGetProperty("pattern", out _));
    }

    /// <summary>
    /// No finance request carries a naked monetary number.
    /// </summary>
    /// <remarks>
    /// The check that matters more than the shape of <c>MoneyRequest</c> itself: a
    /// contract can define money correctly and then take a bare <c>amount</c>
    /// somewhere, and the bare one is what a caller would fill in wrongly
    /// (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task NoFinanceRequest_CarriesANakedMonetaryNumber()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        string[] monetary =
            ["amount", "unitAmount", "fixedAmount", "total", "outstanding", "balance"];

        List<string> naked = [];

        foreach (JsonProperty schema in schemas.EnumerateObject())
        {
            // MoneyRequest is the shape itself, and the rate is a percentage
            // rather than a sum, so neither is in scope here.
            if (schema.Name == "MoneyRequest"
                || !schema.Value.TryGetProperty("properties", out JsonElement properties))
            {
                continue;
            }

            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (!monetary.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Either the value is a money object, or the schema carries the
                // currency beside it. M7 states a term value the second way, and
                // that is not a naked number: the currency travels with it.
                bool carriesCurrency =
                    Describes(property.Value, "MoneyRequest")
                    || properties.EnumerateObject().Any(
                        x => string.Equals(x.Name, "currency", StringComparison.OrdinalIgnoreCase));

                if (!carriesCurrency)
                {
                    naked.Add($"{schema.Name}.{property.Name}");
                }
            }
        }

        Assert.True(
            naked.Count == 0,
            "These contract properties carry a monetary value without its currency: "
                + string.Join(", ", naked));
    }

    /// <summary>
    /// Whether a schema node is that reference, directly or through a nullable
    /// union the generator emits for an optional value.
    /// </summary>
    private static bool Describes(JsonElement node, string schemaName)
    {
        if (node.TryGetProperty("$ref", out JsonElement reference)
            && reference.GetString() is { } target
            && target.EndsWith($"/{schemaName}", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (string keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (node.TryGetProperty(keyword, out JsonElement branches)
                && branches.EnumerateArray().Any(x => Describes(x, schemaName)))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task Contract_DefinesResponseSchemas()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.NotEmpty(schemas.EnumerateObject());
    }

    /// <summary>The published contract requires an interaction participant's party.</summary>
    /// <remarks>
    /// <para>
    /// <c>AOS-R002-025</c>'s other half. The server answered <c>500</c> to a
    /// participant with no <c>party</c>; the contract already said the field was
    /// required, so the repair belonged in the server. Recorded as a test because
    /// the alternative reading — that the contract was wrong and the server right —
    /// would have called for a contract change instead.
    /// </para>
    /// <para>
    /// It lives here, against the document the API serves, rather than beside the
    /// mapping tests: those deliberately need no host, and an earlier version of
    /// this assertion read <c>artifacts/openapi/AgencyOS.Api.json</c> from disk. That
    /// file is written by the contract gate, which runs in a different CI job, so
    /// the test passed on a developer's machine and failed in CI on a missing file.
    /// A test that depends on another job's output is testing the build, not the
    /// product.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Contract_RequiresAnInteractionParticipantParty()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("InteractionParticipantRequest");

        Assert.Contains(
            schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()),
            x => x == "party");
    }

    private async Task<JsonDocument> GetContractAsync()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        await using Stream stream = await client.GetStreamAsync("/openapi/v1.json");

        return await JsonDocument.ParseAsync(stream);
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex TemplateParameter();
}
