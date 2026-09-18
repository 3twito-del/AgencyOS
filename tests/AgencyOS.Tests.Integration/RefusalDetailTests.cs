using System.Net;
using System.Text.Json;
using AgencyOS.Api.Http;
using AgencyOS.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That a refusal is written for the person who reads it.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-008</c>. A body the framework could not bind was refused with the
/// framework's own sentence — <c>Failed to read parameter "CreateDealRequest
/// request" from the request body as JSON</c> — which names an internal class,
/// says nothing about which field was wrong, and says nothing about what was
/// expected. Every domain refusal in this product does the opposite.
/// </para>
/// <para>
/// No database: the handler is exercised directly, so these run anywhere.
/// </para>
/// </remarks>
public sealed class RefusalDetailTests
{
    /// <summary>The refusal names the field the caller sent.</summary>
    [Fact]
    public async Task AFieldThatCouldNotBeReadIsNamed()
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.ownerUserId", "The JSON value could not be converted to System.Guid."));

        Assert.Equal(400, problem.Status);
        Assert.Contains("ownerUserId", problem.Detail, StringComparison.Ordinal);
        Assert.Contains("an identifier", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>A nested field is named as the caller wrote it.</summary>
    [Fact]
    public async Task ANestedFieldKeepsItsPath()
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.participants[0].party", "The JSON value could not be converted to System.Guid."));

        Assert.Contains("participants[0].party", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>What the field should have looked like, for the shapes we know.</summary>
    [Theory]
    [InlineData("System.DateTimeOffset", "a timestamp")]
    [InlineData("System.DateOnly", "a date")]
    [InlineData("System.Int32", "a whole number")]
    [InlineData("System.Boolean", "true or false")]
    public async Task TheExpectedShapeIsDescribedWithoutNamingAType(string type, string expectation)
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.sentAt", $"The JSON value could not be converted to {type}."));

        Assert.Contains(expectation, problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>A type we have no words for goes undescribed rather than guessed at.</summary>
    [Fact]
    public async Task AnUnfamiliarShapeIsNotGuessedAt()
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.kind", "The JSON value could not be converted to AgencyOS.Domain.Deals.OfferKind."));

        Assert.Contains("kind", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("AgencyOS.Domain", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>No refusal names the type the endpoint declares.</summary>
    /// <remarks>
    /// The defect itself: the class name reached the caller, and it is the one
    /// thing in that sentence the caller has no business seeing.
    /// </remarks>
    [Fact]
    public async Task TheRefusalNeverNamesTheRequestClass()
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.ownerUserId", "The JSON value could not be converted to System.Guid."));

        Assert.DoesNotContain("CreateDealRequest", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to read parameter", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed body with no field to name still refuses cleanly.
    /// </summary>
    /// <remarks>
    /// Broken JSON has no path, so there is nothing to quote back. It stays a 400
    /// with the framework's own sentence rather than becoming a 500 or an empty
    /// refusal.
    /// </remarks>
    [Fact]
    public async Task ABodyWithNoReadableFieldIsStillARefusal()
    {
        ProblemShape problem = await RefuseAsync(new BadHttpRequestException(
            "Failed to read parameter \"CreateDealRequest request\" from the request body as JSON.",
            StatusCodes.Status400BadRequest,
            new JsonException("Expected a value.")));

        Assert.Equal(400, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    /// <summary>
    /// The shape the serializer actually produces for a record request.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed: for a record with a parameterized
    /// constructor the serializer reports the *request* type, whichever property
    /// failed — "could not be converted to
    /// AgencyOS.Contracts.Projects.AttachToRoleRequest" — so the member's type has
    /// to be looked up on the contract. The request class must still never reach
    /// the caller.
    /// </remarks>
    [Theory]
    [InlineData("personId", "an identifier")]
    [InlineData("startsOn", "a date")]
    [InlineData("expectedVersion", "a whole number")]
    public async Task ARecordRequestStillDescribesTheField(string field, string expectation)
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            $"$.{field}",
            "The JSON value could not be converted to "
                + "AgencyOS.Contracts.Projects.AttachToRoleRequest."));

        Assert.Contains(field, problem.Detail, StringComparison.Ordinal);
        Assert.Contains(expectation, problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachToRoleRequest", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("AgencyOS.Contracts", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>A field the contract does not have goes undescribed.</summary>
    [Fact]
    public async Task AFieldTheContractDoesNotHaveIsNotDescribed()
    {
        ProblemShape problem = await RefuseAsync(BindingFailure(
            "$.whatever",
            "The JSON value could not be converted to "
                + "AgencyOS.Contracts.Projects.AttachToRoleRequest."));

        Assert.Contains("whatever", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected", problem.Detail, StringComparison.Ordinal);
    }

    private static BadHttpRequestException BindingFailure(string path, string message) =>
        new(
            "Failed to read parameter \"CreateDealRequest request\" from the request body as JSON.",
            StatusCodes.Status400BadRequest,
            new JsonException(message, path, lineNumber: 0, bytePositionInLine: 40));

    /// <summary>Runs one exception through the handler and reads what it wrote.</summary>
    private static async Task<ProblemShape> RefuseAsync(Exception exception)
    {
        AgencyOsExceptionHandler handler = new(
            NullLogger<AgencyOsExceptionHandler>.Instance, new UploadLimit(1024));

        DefaultHttpContext context = new();
        using MemoryStream body = new();

        context.Response.Body = body;
        context.Request.Path = "/api/v1/organizations/x/deals";

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        body.Position = 0;

        using JsonDocument written = await JsonDocument.ParseAsync(body);

        return new ProblemShape(
            context.Response.StatusCode,
            written.RootElement.TryGetProperty("detail", out JsonElement detail)
                ? detail.GetString() ?? string.Empty
                : string.Empty,
            written.RootElement.TryGetProperty("title", out JsonElement title)
                ? title.GetString() ?? string.Empty
                : string.Empty);
    }

    private sealed record ProblemShape(int Status, string Detail, string Title);
}
