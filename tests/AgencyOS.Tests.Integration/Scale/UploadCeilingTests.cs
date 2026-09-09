using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration.Scale;

/// <summary>
/// That an upload too large is refused, and told why.
/// </summary>
/// <remarks>
/// <para>
/// Until M14 this limit was inherited rather than chosen. Kestrel stops a body at
/// roughly 28.6 MiB and multipart buffering at 128 MiB, so AgencyOS had a maximum
/// document size nobody had decided, that appeared in no document, and that two
/// framework defaults disagreed about — and the resulting exception was not mapped,
/// so a person filing a large contract met an unhandled failure rather than an
/// answer (§27, ADR-0037).
/// </para>
/// <para>
/// The test host declares a 1 MB ceiling. Reaching the real 256 MB limit would mean
/// pushing 256 MB through a CI runner to learn what 2 MB proves exactly as well.
/// </para>
/// <para>
/// The refusal is made by <c>UploadLimitMiddleware</c> rather than by a framework
/// limit, and the first attempt at these tests is why. Kestrel's
/// <c>MaxRequestBodySize</c> is not honoured by <c>TestServer</c> at all, and
/// setting the multipart limit to the same number made the form reader trip first
/// and report an oversized upload as a malformed body — a 400 saying the wrong
/// thing. An explicit check answers the same way under both servers.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class UploadCeilingTests
{
    /// <summary>The ceiling the test host declares.</summary>
    /// <remarks>
    /// Set on the shared host rather than on a second one built for these tests.
    /// A second host would be a second set of startup assumptions to keep in step
    /// with the first, and the suite's largest upload anywhere is sixteen bytes,
    /// so one megabyte constrains nothing else.
    /// </remarks>
    private const int Ceiling = 1024 * 1024;

    private readonly AgencyOsTestFixture _fixture;

    public UploadCeilingTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Over the ceiling is refused with the ceiling.
    /// </summary>
    /// <remarks>
    /// The status matters and so does the body. "Request entity too large" tells
    /// somebody their document did not file; it does not tell them what would, and
    /// a person who has just failed to file a contract needs the number.
    /// </remarks>
    [Fact]
    public async Task AnUploadOverTheCeilingIsRefusedAndSaysTheCeiling()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-upload-big");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await UploadAsync(
            client, actor, new byte[Ceiling * 2], "oversized.txt");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "Upload too large", problem.RootElement.GetProperty("title").GetString());

        Assert.Equal(
            Ceiling,
            problem.RootElement.GetProperty("maximumUploadBytes").GetInt64());

        // The refusal names a size a person can act on.
        Assert.Contains(
            "accepts uploads up to",
            problem.RootElement.GetProperty("detail").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Under the ceiling still files.
    /// </summary>
    /// <remarks>
    /// The other half, and the one that would catch a limit applied so eagerly
    /// that ordinary documents stopped working. A ceiling nobody can reach is not
    /// a ceiling; a ceiling everybody hits is an outage.
    /// </remarks>
    [Fact]
    public async Task AnUploadUnderTheCeilingIsAccepted()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-upload-ok");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        _fixture.Factory.Logs.Clear();

        using HttpResponseMessage response = await UploadAsync(
            client, actor, new byte[64 * 1024], "ordinary.txt");

        Assert.True(
            response.IsSuccessStatusCode,
            $"An ordinary document was refused with {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync()
                + Environment.NewLine
                + "Server said: " + _fixture.Factory.Logs.Describe());
    }

    // ------------------------------------------------------------- helpers

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        SeededActor actor,
        byte[] content,
        string fileName)
    {
        using MultipartFormDataContent form = [];

        ByteArrayContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);

        form.Add(new StringContent(fileName), "title");
        form.Add(new StringContent("Other"), "kind");
        form.Add(new StringContent("Internal"), "sensitivity");

        return await client.PostAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/documents", form);
    }
}
