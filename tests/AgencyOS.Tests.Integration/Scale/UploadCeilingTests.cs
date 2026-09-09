using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgencyOS.Contracts;
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
/// Run against a host configured with a deliberately tiny ceiling. Reaching the
/// real 256 MB limit in a test would mean pushing 256 MB through a CI runner to
/// learn something a small number proves exactly as well.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class UploadCeilingTests
{
    /// <summary>Small enough to cross in a test, large enough to be a real body.</summary>
    private const int TinyCeiling = 8 * 1024;

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

        await using AgencyOsApiFactory host = Constrained();
        using HttpClient client = Client(host, actor);

        using HttpResponseMessage response = await UploadAsync(
            client, actor, new byte[TinyCeiling * 4], "oversized.txt");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "Upload too large", problem.RootElement.GetProperty("title").GetString());

        Assert.Equal(
            TinyCeiling,
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

        await using AgencyOsApiFactory host = Constrained();
        using HttpClient client = Client(host, actor);

        using HttpResponseMessage response = await UploadAsync(
            client, actor, new byte[TinyCeiling / 4], "ordinary.txt");

        Assert.True(
            response.IsSuccessStatusCode,
            $"An ordinary document was refused with {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------- helpers

    private AgencyOsApiFactory Constrained() =>
        new(
            _fixture.ConnectionString,
            settings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AgencyOS:Limits:MaximumUploadBytes"] =
                    TinyCeiling.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

    private static HttpClient Client(AgencyOsApiFactory host, SeededActor actor)
    {
        HttpClient client = host.CreateClient();

        client.DefaultRequestHeaders.Add(
            AgencyOS.Api.Authentication.AgencyOsAuthentication.SubjectHeader, actor.Subject);

        // The compatibility middleware refuses a request that does not say what
        // client it is. This host is built by hand, so it says so by hand.
        client.DefaultRequestHeaders.Add(ClientHeaders.Platform, AgencyOsTestFixture.Platform);
        client.DefaultRequestHeaders.Add(ClientHeaders.Channel, AgencyOsTestFixture.Channel);
        client.DefaultRequestHeaders.Add(
            ClientHeaders.ClientVersion, AgencyOsTestFixture.LatestVersion);
        client.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            ApiContract.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return client;
    }

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
