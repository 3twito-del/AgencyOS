using AgencyOS.Api.Endpoints;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Common;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That an incomplete request is refused as a request, not as a crash.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-025</c>. <c>POST /interactions</c> with
/// <c>participants: [{ "role": "Contact" }]</c> — no <c>party</c> — answered
/// <c>500</c> with a trace id. The body bound correctly; <c>Party</c> was simply
/// null, and the first thing to dereference it threw
/// <c>ArgumentNullException</c>, which is the exception for a programmer's
/// mistake rather than a caller's.
/// </para>
/// <para>
/// The published contract was already right: <c>InteractionParticipantRequest</c>
/// declares <c>party</c> required. Only the server disagreed.
/// </para>
/// <para>
/// No database: the mapping helpers are exercised directly, so these run
/// anywhere.
/// </para>
/// </remarks>
public sealed class MissingNestedObjectTests
{
    /// <summary>The canonical case from the finding.</summary>
    [Fact]
    public void AMissingPartyIsRefusedAsARequest()
    {
        DomainException refused = Assert.Throws<DomainException>(
            () => EndpointParsing.ToEndpoint(null!, "participants[0].party"));

        Assert.Contains("participants[0].party", refused.Message, StringComparison.Ordinal);
        Assert.Contains("required", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// It is not an <see cref="ArgumentNullException"/> any more.
    /// </summary>
    /// <remarks>
    /// The distinction that decides the status code. <c>ArgumentNullException</c>
    /// says a caller inside this process passed nothing where something was
    /// required, and the handler is right to treat that as a `500`. A missing
    /// field in a request body is not that.
    /// </remarks>
    [Fact]
    public void AMissingPartyIsNotAProgrammerError()
    {
        Exception thrown = Record.Exception(
            () => EndpointParsing.ToEndpoint(null!, "participants[0].party"))!;

        Assert.IsNotType<ArgumentNullException>(thrown);
        Assert.IsType<DomainException>(thrown);
    }

    /// <summary>A present party still maps, unchanged.</summary>
    [Fact]
    public void APresentPartyStillMaps()
    {
        Guid person = Guid.NewGuid();

        Domain.Relationships.RelationshipEndpoint mapped = EndpointParsing.ToEndpoint(
            new PartyRefRequest("Person", person), "participants[0].party");

        Assert.True(mapped.IsPerson);
        Assert.Equal(person, mapped.AsPerson?.Value);
    }

    /// <summary>
    /// A malformed party is still refused the way it always was.
    /// </summary>
    /// <remarks>
    /// The repair is about absence. A party that is present but names a kind the
    /// domain does not have was already a clean refusal, and still is.
    /// </remarks>
    [Fact]
    public void AMalformedPartyIsStillRefusedForItsKind()
    {
        DomainException refused = Assert.Throws<DomainException>(
            () => EndpointParsing.ToEndpoint(
                new PartyRefRequest("Elephant", Guid.NewGuid()), "participants[0].party"));

        Assert.Contains("Elephant", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Expected one of", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>An optional party that is absent is still absent, not a refusal.</summary>
    /// <remarks>
    /// <c>ToEndpointOrNull</c> exists because some parties genuinely are optional.
    /// Making absence a refusal everywhere would have broken those.
    /// </remarks>
    [Fact]
    public void AnOptionalPartyMayStillBeOmitted() =>
        Assert.Null(EndpointParsing.ToEndpointOrNull(null, "subject"));

    // The contract-side half of this finding — that InteractionParticipantRequest
    // already declares `party` required — is asserted in OpenApiContractTests
    // against the document the API serves. It was here, reading the generated
    // artifacts/openapi/AgencyOS.Api.json, which only exists after the contract
    // gate has run in another CI job; these tests deliberately need no host and no
    // build output, and that one quietly needed both.
}
