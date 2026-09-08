using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M10 document store, end to end against PostgreSQL and real files on disk.
/// </summary>
/// <remarks>
/// <para>
/// The properties under test are the ones a person would rely on in an argument:
/// the bytes that come back are the bytes that went in, an earlier version is
/// still readable after a later one exists, and nothing about a filename can make
/// the server write outside its own storage root (ADR-0024).
/// </para>
/// <para>
/// Every digest here is computed independently by the test over the bytes it sent.
/// Comparing the server's hash against itself would prove nothing.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class DocumentTests
{
    private readonly AgencyOsTestFixture _fixture;

    public DocumentTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------- the chain

    /// <summary>
    /// Record, version, download, link, archive, restore - in the order a records
    /// desk does it.
    /// </summary>
    [Fact]
    public async Task Workflow_RecordVersionDownloadLinkArchive()
    {
        Actor a = await ActorAsync("m10-doc-workflow");

        byte[] first = Bytes("The executed agreement, first draft.");

        RecordDocumentResponse recorded = await UploadAsync(
            a, first, "agreement.txt", "text/plain", "Undertow agreement", "Contract", "Confidential");

        // The digest is the digest of what the test sent, computed here.
        Assert.Equal(Digest(first), recorded.ContentHash);
        Assert.Equal(first.LongLength, recorded.ByteLength);
        Assert.False(recorded.Deduplicated);

        DocumentDetailResponse detail = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Equal("Undertow agreement", detail.Document.Title);
        Assert.Equal("Contract", detail.Document.Kind);
        Assert.Equal("Confidential", detail.Document.Sensitivity);
        Assert.Equal("Active", detail.Document.Status);
        Assert.True(detail.Document.HoldsContent);
        Assert.Equal(1, detail.Document.VersionCount);
        Assert.Equal(1, detail.Versions[0].Sequence);

        // Nothing scanned it, and the system says so rather than calling it clean.
        Assert.Equal("Unscanned", detail.Versions[0].ScanState);

        // A second upload adds a version. It does not replace the first.
        byte[] second = Bytes("The executed agreement, as signed.");

        RecordDocumentResponse added = await AddVersionAsync(
            a, recorded.DocumentId, second, "agreement-signed.txt", "text/plain",
            detail.Document.Version);

        Assert.Equal(Digest(second), added.ContentHash);
        Assert.NotEqual(recorded.VersionId, added.VersionId);

        DocumentDetailResponse twoVersions = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Equal(2, twoVersions.Document.VersionCount);
        Assert.Equal(2, twoVersions.Document.CurrentVersion!.Sequence);
        Assert.Equal(added.VersionId, twoVersions.Document.CurrentVersion.Id);

        // The point of keeping versions: the first one still reads back exactly.
        Assert.Equal(first, await DownloadAsync(a, recorded.VersionId));
        Assert.Equal(second, await DownloadAsync(a, added.VersionId));

        // Filed against a person. The link is context, and the label is a name.
        PersonDetailResponse person = await CreatePersonAsync(a, "Ada Sallow");

        LinkDocumentResponse link = await PostAsync<LinkDocumentResponse>(
            a,
            $"documents/{recorded.DocumentId}/links",
            new LinkDocumentRequest("Person", person.Person.Id, "Signatory"));

        DocumentDetailResponse linked = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Single(linked.Links);
        Assert.Equal("Person", linked.Links[0].Target);
        Assert.Equal(person.Person.Id, linked.Links[0].TargetId);
        Assert.Equal("Ada Sallow", linked.Links[0].TargetLabel);

        // Archiving takes it out of use. It destroys nothing.
        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/documents/{recorded.DocumentId}/archive",
            new ArchiveDocumentRequest("Superseded by the fully executed copy", linked.Document.Version)));

        DocumentDetailResponse archived = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Equal("Archived", archived.Document.Status);
        Assert.Equal(2, archived.Versions.Count);

        // The bytes are still there, which is the whole difference between
        // archiving and deleting.
        Assert.Equal(first, await DownloadAsync(a, recorded.VersionId));

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/documents/{recorded.DocumentId}/restore",
            new RestoreDocumentRequest(archived.Document.Version)));

        DocumentDetailResponse restored = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Equal("Active", restored.Document.Status);

        // Unlinking removes the association and nothing else.
        HttpResponseMessage unlinked = await a.Client.DeleteAsync(
            $"{a.Root}/documents/{recorded.DocumentId}/links/{link.LinkId}");

        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);

        Assert.Empty((await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}")).Links);
    }

    // -------------------------------------------------------------- integrity

    /// <summary>
    /// An earlier version is never rewritten, even by an upload of the same bytes.
    /// </summary>
    [Fact]
    public async Task Versions_AreImmutableOnceRecorded()
    {
        Actor a = await ActorAsync("m10-immutable");

        byte[] original = Bytes("Version one.");

        RecordDocumentResponse recorded = await UploadAsync(
            a, original, "one.txt", "text/plain", "Immutable", "Statement", "Internal");

        DocumentDetailResponse before = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        await AddVersionAsync(
            a, recorded.DocumentId, Bytes("Version two."), "two.txt", "text/plain",
            before.Document.Version);

        DocumentVersionResponse first = (await GetAsync<DocumentDetailResponse>(
                a, $"documents/{recorded.DocumentId}"))
            .Versions.Single(x => x.Sequence == 1);

        Assert.Equal(recorded.ContentHash, first.ContentHash);
        Assert.Equal(recorded.VersionId, first.Id);
        Assert.Equal(original, await DownloadAsync(a, first.Id));
    }

    /// <summary>A stale version number is refused rather than silently overwriting.</summary>
    [Fact]
    public async Task AddingAVersion_WithAStaleVersion_IsRefused()
    {
        Actor a = await ActorAsync("m10-stale");

        RecordDocumentResponse recorded = await UploadAsync(
            a, Bytes("One."), "one.txt", "text/plain", "Stale", "Statement", "Internal");

        DocumentDetailResponse detail = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        await AddVersionAsync(
            a, recorded.DocumentId, Bytes("Two."), "two.txt", "text/plain",
            detail.Document.Version);

        // The same expected version again, now stale.
        using MultipartFormDataContent form = Multipart(
            Bytes("Three."), "three.txt", "text/plain");

        form.Add(new StringContent(
            detail.Document.Version.ToString(CultureInfo.InvariantCulture)), "expectedVersion");

        HttpResponseMessage response = await a.Client.PostAsync(
            $"{a.Root}/documents/{recorded.DocumentId}/versions", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Identical bytes are stored once, and remain two documents with their own
    /// classifications.
    /// </summary>
    /// <remarks>
    /// The distinction that matters: deduplication is a storage fact. Two documents
    /// that happen to hold the same bytes are not the same document, and the
    /// sensitivity of one says nothing about the other (ADR-0024).
    /// </remarks>
    [Fact]
    public async Task IdenticalBytes_AreStoredOnce_AndStayDistinctDocuments()
    {
        Actor a = await ActorAsync("m10-dedup");

        byte[] bytes = Bytes("The same eleven words, byte for byte, in two documents.");

        RecordDocumentResponse first = await UploadAsync(
            a, bytes, "a.txt", "text/plain", "Ordinary copy", "Statement", "Internal");

        RecordDocumentResponse second = await UploadAsync(
            a, bytes, "b.txt", "text/plain", "Sensitive copy", "Statement", "Confidential");

        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
        Assert.Equal(first.ContentHash, second.ContentHash);

        // Two documents, two versions, one stored object.
        Assert.NotEqual(first.DocumentId, second.DocumentId);
        Assert.NotEqual(first.VersionId, second.VersionId);

        DocumentDetailResponse sensitive = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{second.DocumentId}");

        Assert.Equal("Confidential", sensitive.Document.Sensitivity);
        Assert.Equal("Internal", (await GetAsync<DocumentDetailResponse>(
            a, $"documents/{first.DocumentId}")).Document.Sensitivity);

        // Both read back the same bytes, through their own authorized routes.
        Assert.Equal(bytes, await DownloadAsync(a, first.VersionId));
        Assert.Equal(bytes, await DownloadAsync(a, second.VersionId));
    }

    /// <summary>
    /// Deduplication never crosses a tenant, so it cannot be used to ask whether
    /// another organization holds a file.
    /// </summary>
    /// <remarks>
    /// A shared content-addressed store would answer "do you already have this?"
    /// for anybody who could guess the bytes. That is a covert channel, and the
    /// answer is worth more than the disk it saves (ADR-0024).
    /// </remarks>
    [Fact]
    public async Task Deduplication_DoesNotCrossTenants()
    {
        Actor a = await ActorAsync("m10-dedup-a");
        Actor b = await ActorAsync("m10-dedup-b");

        byte[] bytes = Bytes("A file only one tenant should be known to hold.");

        RecordDocumentResponse mine = await UploadAsync(
            a, bytes, "mine.txt", "text/plain", "Mine", "Statement", "Internal");

        RecordDocumentResponse theirs = await UploadAsync(
            b, bytes, "theirs.txt", "text/plain", "Theirs", "Statement", "Internal");

        Assert.False(mine.Deduplicated);
        Assert.False(theirs.Deduplicated);

        // And neither tenant can reach the other's document or its bytes.
        HttpResponseMessage crossDocument =
            await b.Client.GetAsync($"{b.Root}/documents/{mine.DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, crossDocument.StatusCode);

        HttpResponseMessage crossContent = await b.Client.GetAsync(
            $"{b.Root}/document-versions/{mine.VersionId}/content");

        Assert.Equal(HttpStatusCode.NotFound, crossContent.StatusCode);
    }

    // -------------------------------------------------------------- hostility

    /// <summary>
    /// Hostile filenames are reduced to something safe and never reach the
    /// filesystem.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the last one: after uploading files whose
    /// names try to climb out of the storage root, every file under the root is
    /// still inside it. A test that only checked the returned display name would
    /// pass against a server that wrote to <c>C:\Windows</c> (ADR-0024).
    /// </remarks>
    [Theory]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam", "sam")]
    [InlineData("....//....//escape.txt", "escape.txt")]
    [InlineData("/absolute/path.txt", "path.txt")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts", "hosts")]
    [InlineData("...", "untitled")]
    [InlineData("..", "untitled")]
    [InlineData(".", "untitled")]
    [InlineData("con.txt", "con.txt")]
    public async Task HostileFileNames_AreReducedToSomethingSafe(string supplied, string expected)
    {
        Actor a = await ActorAsync("m10-hostile-" + Guid.NewGuid().ToString("N")[..8]);

        RecordDocumentResponse recorded = await UploadAsync(
            a, Bytes("harmless"), supplied, "text/plain", "Hostile name", "Other", "Internal");

        DocumentDetailResponse detail = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        Assert.Equal(expected, detail.Versions[0].DisplayFileName);

        // Nothing supplied by a caller becomes part of a storage location.
        Assert.DoesNotContain("..", detail.Versions[0].DisplayFileName, StringComparison.Ordinal);
    }

    /// <summary>
    /// A multipart body no well-behaved client would produce is refused, and
    /// nothing is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These bodies are written by hand, because every HTTP client refuses to put
    /// a bare newline or an unescaped quote into a header - which is the point. A
    /// hostile upload does not arrive through a client that validates it.
    /// </para>
    /// <para>
    /// The refusal is a 400 and not a 500. The framework will not parse the body,
    /// so the filename never reaches AgencyOS at all; what is under test is that
    /// this reads as the caller sending something invalid rather than as the
    /// server falling over, because otherwise every probe of this kind would
    /// register as an integrity failure (ADR-0024).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("report\r\nX-Injected: yes.txt")]
    [InlineData("file\"; rm -rf /; \"")]
    public async Task MalformedUploadBodies_AreRefusedWithoutStoringAnything(string fileName)
    {
        Actor a = await ActorAsync("m10-malformed-" + Guid.NewGuid().ToString("N")[..8]);

        long before = CountStoredFiles();

        using HttpResponseMessage response = await RawPostAsync(
            a, Bytes("harmless"), fileName, "Malformed body");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Empty(await GetAsync<DocumentSummaryResponse[]>(a, "documents"));

        // And no bytes were written on the way to refusing it.
        Assert.Equal(before, CountStoredFiles());
    }

    /// <summary>
    /// A filename the parser accepts but that carries a control character is
    /// stored with the character neutralized.
    /// </summary>
    /// <remarks>
    /// A NUL does not break the structure of a header, so the framework hands it
    /// straight through and AgencyOS is the thing that has to deal with it. The
    /// name is kept - an operator recognizes their file - with the character that
    /// would truncate a path or a header replaced (ADR-0024).
    /// </remarks>
    [Fact]
    public async Task ControlCharactersInAFileName_AreNeutralized()
    {
        Actor a = await ActorAsync("m10-control");

        using HttpResponseMessage posted = await RawPostAsync(
            a, Bytes("harmless"), "nul\u0000byte.txt", "Control character");

        posted.EnsureSuccessStatusCode();

        RecordDocumentResponse recorded =
            (await posted.Content.ReadFromJsonAsync<RecordDocumentResponse>())!;

        DocumentDetailResponse detail = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{recorded.DocumentId}");

        string stored = detail.Versions[0].DisplayFileName;

        Assert.DoesNotContain('\0', stored);
        Assert.Equal("nul_byte.txt", stored);

        // And it survives a round trip through the Content-Disposition header,
        // which is the header a raw control character would have truncated.
        using HttpResponseMessage download = await a.Client.GetAsync(
            $"{a.Root}/document-versions/{recorded.VersionId}/content");

        download.EnsureSuccessStatusCode();

        Assert.Contains(
            "nul_byte.txt",
            download.Content.Headers.ContentDisposition!.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>The download response carries the hardening headers.</summary>
    [Fact]
    public async Task DownloadResponses_CarryTheHardeningHeaders()
    {
        Actor a = await ActorAsync("m10-headers");

        RecordDocumentResponse recorded = await UploadAsync(
            a, Bytes("harmless"), "ordinary.txt", "text/plain",
            "Hardening", "Other", "Internal");

        using HttpResponseMessage response = await a.Client.GetAsync(
            $"{a.Root}/document-versions/{recorded.VersionId}/content");

        response.EnsureSuccessStatusCode();

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("X-Injected"));

        string policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every byte the server writes stays inside the storage root, whatever the
    /// upload claimed to be called.
    /// </summary>
    [Fact]
    public async Task StoredBytes_NeverEscapeTheStorageRoot()
    {
        Actor a = await ActorAsync("m10-root");

        string root = Path.GetFullPath(_fixture.Factory.BlobRoot);

        foreach (string name in new[]
        {
            "../../../escape-one.txt",
            "..\\..\\escape-two.txt",
            "/etc/escape-three.txt",
        })
        {
            await UploadAsync(
                a, Bytes("contained " + name), name, "text/plain",
                "Contained", "Other", "Internal");
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            Assert.StartsWith(root, Path.GetFullPath(file), StringComparison.Ordinal);
        }

        // Nothing landed beside the root either.
        string parent = Path.GetDirectoryName(root)!;

        foreach (string name in new[] { "escape-one.txt", "escape-two.txt", "escape-three.txt" })
        {
            Assert.False(File.Exists(Path.Combine(parent, name)));
        }
    }

    /// <summary>
    /// An HTML file is stored as an opaque stream, never as something a browser
    /// would run.
    /// </summary>
    [Fact]
    public async Task HtmlContent_IsNeverServedInline()
    {
        Actor a = await ActorAsync("m10-html");

        byte[] hostile = Bytes("<html><body><script>fetch('http://elsewhere')</script></body></html>");

        RecordDocumentResponse recorded = await UploadAsync(
            a, hostile, "page.html", "text/html", "Hostile page", "Other", "Internal");

        using HttpResponseMessage response = await a.Client.GetAsync(
            $"{a.Root}/document-versions/{recorded.VersionId}/content");

        response.EnsureSuccessStatusCode();

        // Downloaded, not displayed, and with a policy that would stop it anyway.
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        string policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("sandbox", policy, StringComparison.Ordinal);

        // The bytes come back exactly as sent. Nothing rewrote the file.
        Assert.Equal(hostile, await response.Content.ReadAsByteArrayAsync());
    }

    // ---------------------------------------------------------- authorization

    /// <summary>
    /// A privileged document is invisible to a member without the elevated grant.
    /// </summary>
    /// <remarks>
    /// Invisible, not redacted: it is absent from the list, absent from the count,
    /// and its bytes are unreachable. A hit count over privileged material leaks
    /// the material through the count (ADR-0025).
    /// </remarks>
    [Fact]
    public async Task PrivilegedDocuments_AreInvisibleWithoutTheGrant()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m10-priv-owner");

        // A second member in the same tenant, so this is about the grant and not
        // about tenancy.
        SeededActor member = await SeedIntoAsync(owner, AgencyRole.Member, "m10-priv-member");

        Actor ownerActor = Actor.For(_fixture, owner);
        Actor memberActor = Actor.For(_fixture, member, owner);

        RecordDocumentResponse ordinary = await UploadAsync(
            memberActor, Bytes("Ordinary."), "ordinary.txt", "text/plain",
            "Ordinary", "Statement", "Internal");

        // Recorded by the owner, because a writer may not file a document into a
        // classification they could not then read - and a member cannot read
        // privileged material (ADR-0025).
        RecordDocumentResponse privileged = await UploadAsync(
            ownerActor, Bytes("Privileged."), "privileged.txt", "text/plain",
            "Advice of counsel", "Statement", "Privileged");

        // Refused rather than redacted, and refused with a reason. A member who
        // followed a link to a document they may not open learns that they need
        // a grant, instead of concluding the contract was never filed. What is
        // never leaked is the list and its count, checked below (ADR-0025).
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberActor.Client.GetAsync(
                $"{memberActor.Root}/documents/{privileged.DocumentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberActor.Client.GetAsync(
                $"{memberActor.Root}/document-versions/{privileged.VersionId}/content")).StatusCode);

        IReadOnlyList<DocumentSummaryResponse> visible =
            await GetAsync<DocumentSummaryResponse[]>(memberActor, "documents");

        Assert.Contains(visible, x => x.Id == ordinary.DocumentId);
        Assert.DoesNotContain(visible, x => x.Id == privileged.DocumentId);

        // The owner holds documents.privileged.read and sees both.
        IReadOnlyList<DocumentSummaryResponse> all =
            await GetAsync<DocumentSummaryResponse[]>(ownerActor, "documents");

        Assert.Contains(all, x => x.Id == ordinary.DocumentId);
        Assert.Contains(all, x => x.Id == privileged.DocumentId);
    }

    /// <summary>
    /// Reading the record a document is filed against grants nothing about the
    /// document.
    /// </summary>
    [Fact]
    public async Task LinkingToAReadableRecord_DoesNotGrantAccess()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m10-link-owner");
        SeededActor member = await SeedIntoAsync(owner, AgencyRole.Member, "m10-link-member");

        Actor writer = Actor.For(_fixture, owner);
        Actor reader = Actor.For(_fixture, member, owner);

        PersonDetailResponse person = await CreatePersonAsync(writer, "Nell Rowan");

        RecordDocumentResponse privileged = await UploadAsync(
            writer, Bytes("Counsel note."), "note.txt", "text/plain",
            "Counsel note", "Statement", "Privileged",
            links: [new DocumentLinkRequest("Person", person.Person.Id, "Subject")]);

        // The person is perfectly readable.
        PersonDetailResponse readable = await GetAsync<PersonDetailResponse>(
            reader, $"people/{person.Person.Id}");

        Assert.Equal("Nell Rowan", readable.Person.DisplayName);

        // The document filed against them is not.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.Client.GetAsync(
                $"{reader.Root}/documents/{privileged.DocumentId}")).StatusCode);

        // And it does not appear when the list is filtered to that person.
        IReadOnlyList<DocumentSummaryResponse> filtered = await GetAsync<DocumentSummaryResponse[]>(
            reader, $"documents?linkedTarget=Person&linkedTargetId={person.Person.Id}");

        Assert.DoesNotContain(filtered, x => x.Id == privileged.DocumentId);
    }

    /// <summary>
    /// An observer sees no documents at all, not a redacted list of them.
    /// </summary>
    /// <remarks>
    /// Deliberate, and the same decision finance made in M9: read access to stored
    /// files is something somebody grants on purpose, not a consequence of being
    /// able to see the business (ADR-0025).
    /// </remarks>
    [Fact]
    public async Task Documents_RequireAGrant()
    {
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "m10-observer");

        Actor a = Actor.For(_fixture, observer);

        HttpResponseMessage listed = await a.Client.GetAsync($"{a.Root}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);

        using MultipartFormDataContent form = Multipart(Bytes("x"), "x.txt", "text/plain");

        form.Add(new StringContent("Refused"), "title");
        form.Add(new StringContent("Other"), "kind");
        form.Add(new StringContent("Internal"), "sensitivity");

        HttpResponseMessage written = await a.Client.PostAsync($"{a.Root}/documents", form);

        Assert.Equal(HttpStatusCode.Forbidden, written.StatusCode);
    }

    // -------------------------------------------------------------- behaviour

    /// <summary>
    /// A link to a record in another tenant is refused, and the refusal says
    /// nothing about whether that record exists.
    /// </summary>
    /// <remarks>
    /// Not-found rather than a validation error, on purpose. A caller who could
    /// tell "no such record" from "not yours" could enumerate another tenant by
    /// guessing identifiers (ADR-0011, ADR-0025).
    /// </remarks>
    [Fact]
    public async Task LinkingAcrossTenants_IsRefused()
    {
        Actor a = await ActorAsync("m10-link-tenant-a");
        Actor b = await ActorAsync("m10-link-tenant-b");

        PersonDetailResponse theirs = await CreatePersonAsync(b, "Someone Else");

        RecordDocumentResponse mine = await UploadAsync(
            a, Bytes("Mine."), "mine.txt", "text/plain", "Mine", "Other", "Internal");

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/documents/{mine.DocumentId}/links",
            new LinkDocumentRequest("Person", theirs.Person.Id, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A link to a record that does not exist is refused.</summary>
    [Fact]
    public async Task LinkingToAMissingRecord_IsRefused()
    {
        Actor a = await ActorAsync("m10-link-missing");

        RecordDocumentResponse mine = await UploadAsync(
            a, Bytes("Mine."), "mine.txt", "text/plain", "Mine", "Other", "Internal");

        HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/documents/{mine.DocumentId}/links",
            new LinkDocumentRequest("Deal", Guid.NewGuid(), null));

        // The same answer as a record in another tenant, which is the point: the
        // two are indistinguishable from outside.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Text extraction runs where it can and reports itself where it cannot.
    /// </summary>
    /// <remarks>
    /// Unsupported is not a failure. M10 extracts plain text and nothing else, and
    /// a PDF that reports <c>Unsupported</c> is the system saying so rather than
    /// pretending to have read it (ADR-0025).
    /// </remarks>
    [Fact]
    public async Task TextExtraction_RunsWhereItCanAndSaysSoWhereItCannot()
    {
        Actor a = await ActorAsync("m10-extract");

        RecordDocumentResponse text = await UploadAsync(
            a,
            Bytes("The party of the first part agrees to the following."),
            "terms.txt",
            "text/plain",
            "Extractable",
            "Statement",
            "Internal");

        DocumentDetailResponse extracted = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{text.DocumentId}");

        Assert.Equal("Extracted", extracted.Versions[0].ExtractionState);
        Assert.Contains("party of the first part", extracted.ExtractedText!, StringComparison.Ordinal);

        // A PDF is a format nothing tried to read, and says exactly that.
        RecordDocumentResponse pdf = await UploadAsync(
            a, Bytes("%PDF-1.7 not really"), "scan.pdf", "application/pdf",
            "Unreadable", "Statement", "Internal");

        DocumentDetailResponse unread = await GetAsync<DocumentDetailResponse>(
            a, $"documents/{pdf.DocumentId}");

        Assert.Equal("Unsupported", unread.Versions[0].ExtractionState);
        Assert.Null(unread.ExtractedText);
    }

    /// <summary>
    /// The extracted text is not searchable, and the API does not pretend it is.
    /// </summary>
    /// <remarks>
    /// Deliberately asserted. Full-content search is deferred because getting the
    /// authorization airtight matters more than the feature, and a test that
    /// records the deferral keeps a future change honest (ADR-0025).
    /// </remarks>
    [Fact]
    public async Task Search_MatchesMetadataAndNotFileContents()
    {
        Actor a = await ActorAsync("m10-search");

        string phrase = "quicksilver-" + Guid.NewGuid().ToString("N")[..8];

        await UploadAsync(
            a,
            Bytes($"This body contains the word {phrase} and the title does not."),
            "body.txt",
            "text/plain",
            "A title with no such word",
            "Statement",
            "Internal");

        IReadOnlyList<DocumentSummaryResponse> byContent =
            await GetAsync<DocumentSummaryResponse[]>(a, $"documents?search={phrase}");

        Assert.Empty(byContent);

        IReadOnlyList<DocumentSummaryResponse> byTitle =
            await GetAsync<DocumentSummaryResponse[]>(a, "documents?search=no such word");

        Assert.Single(byTitle);
    }

    /// <summary>A repeated idempotency key records one document, not two.</summary>
    [Fact]
    public async Task RepeatingAnUpload_WithTheSameKey_RecordsOneDocument()
    {
        Actor a = await ActorAsync("m10-idempotent");

        string key = Guid.NewGuid().ToString("N");
        byte[] bytes = Bytes("Recorded once.");

        RecordDocumentResponse first = await UploadAsync(
            a, bytes, "once.txt", "text/plain", "Once", "Other", "Internal", idempotencyKey: key);

        RecordDocumentResponse second = await UploadAsync(
            a, bytes, "once.txt", "text/plain", "Once", "Other", "Internal", idempotencyKey: key);

        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Equal(first.VersionId, second.VersionId);

        IReadOnlyList<DocumentSummaryResponse> documents =
            await GetAsync<DocumentSummaryResponse[]>(a, "documents?search=Once");

        Assert.Single(documents);
    }

    /// <summary>No response anywhere carries a storage path.</summary>
    /// <remarks>
    /// Checked over the raw JSON rather than the typed contract, because the
    /// guarantee is about what leaves the server and not about which properties
    /// happen to be modelled (ADR-0024).
    /// </remarks>
    [Fact]
    public async Task NoResponse_CarriesAStoragePath()
    {
        Actor a = await ActorAsync("m10-no-paths");

        RecordDocumentResponse recorded = await UploadAsync(
            a, Bytes("Stored."), "stored.txt", "text/plain", "Stored", "Other", "Internal");

        foreach (string route in new[]
        {
            "documents",
            $"documents/{recorded.DocumentId}",
        })
        {
            string json = await a.Client.GetStringAsync($"{a.Root}/{route}");

            Assert.DoesNotContain(_fixture.Factory.BlobRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", json, StringComparison.Ordinal);
        }
    }

    /// <summary>Documents appear in saved views, filtered by what the caller may read.</summary>
    [Fact]
    public async Task SavedViews_CarryDocuments()
    {
        Actor a = await ActorAsync("m10-saved-view");

        await UploadAsync(
            a, Bytes("In a view."), "view.txt", "text/plain",
            "Saved view document", "Contract", "Internal");

        SavedViewResponse view = await PostAsync<SavedViewResponse>(
            a,
            "saved-views",
            new CreateSavedViewRequest(
                "Contracts on file",
                new SavedViewDefinitionModel(
                    // Documents arrived in version 8. A document claiming an
                    // earlier version while naming this target is refused, which is
                    // the point of versioning the definition at all.
                    8,
                    "Documents",
                    new SavedViewFiltersModel(DocumentKind: "Contract"))));

        SavedViewResultsResponse results = await GetAsync<SavedViewResultsResponse>(
            a, $"saved-views/{view.Id}/results");

        Assert.Equal("Documents", results.Target);
        Assert.Contains(results.Documents, x => x.Title == "Saved view document");
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Actor(HttpClient Client, string Root)
    {
        public static Actor For(
            AgencyOsTestFixture fixture,
            SeededActor actor,
            SeededActor? tenantOwner = null) =>
            new(
                fixture.CreateClient(actor.Subject),
                $"/api/v1/organizations/{(tenantOwner ?? actor).Organization.Id.Value}");
    }

    private async Task<Actor> ActorAsync(string label, AgencyRole role = AgencyRole.Member)
    {
        SeededActor actor = await _fixture.SeedActorAsync(role, label);

        return Actor.For(_fixture, actor);
    }

    /// <summary>Adds a second member to an existing tenant.</summary>
    private async Task<SeededActor> SeedIntoAsync(
        SeededActor tenant,
        AgencyRole role,
        string label)
    {
        string subject = $"{label}-{Guid.NewGuid():N}";

        Domain.Identity.User user = await _fixture
            .SeedUserAsync(subject, $"{label} {role}")
            .ConfigureAwait(false);

        await _fixture
            .SeedMembershipAsync(tenant.Organization.Id, user.Id, role, tenant.User.Id)
            .ConfigureAwait(false);

        return new SeededActor(subject, user, tenant.Organization);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>The digest of what the test sent, computed by the test.</summary>
    private static string Digest(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static MultipartFormDataContent Multipart(
        byte[] content,
        string fileName,
        string mediaType)
    {
        MultipartFormDataContent form = [];

        ByteArrayContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        // Added by hand rather than through the helper, because the helper quotes
        // and escapes the filename and these tests are about what happens when it
        // is hostile.
        ContentDispositionHeaderValue disposition = new("form-data")
        {
            Name = "\"file\"",
            FileName = "\"" + fileName.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        };

        file.Headers.ContentDisposition = disposition;

        form.Add(file);

        return form;
    }

    /// <summary>
    /// Posts a multipart body assembled by hand, so a hostile filename survives
    /// the client intact.
    /// </summary>
    /// <remarks>
    /// Every well-behaved HTTP client refuses to put a bare newline into a header,
    /// which is exactly why this one does not use one. A hostile filename does not
    /// arrive through a client that validates it.
    /// </remarks>
    private static async Task<HttpResponseMessage> RawPostAsync(
        Actor actor,
        byte[] content,
        string fileName,
        string title)
    {
        const string boundary = "----agencyos-hostile-boundary";

        using MemoryStream body = new();

        void Write(string text) => body.Write(Encoding.UTF8.GetBytes(text));

        foreach ((string field, string value) in new[]
        {
            ("title", title),
            ("kind", "Other"),
            ("sensitivity", "Internal"),
        })
        {
            Write($"--{boundary}\r\n");
            Write($"Content-Disposition: form-data; name=\"{field}\"\r\n\r\n");
            Write(value + "\r\n");
        }

        Write($"--{boundary}\r\n");

        // Written verbatim, control characters and all. This is the string a
        // hostile client would put on the wire.
        Write($"Content-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n");
        Write("Content-Type: text/plain\r\n\r\n");

        body.Write(content);

        Write($"\r\n--{boundary}--\r\n");

        ByteArrayContent raw = new(body.ToArray());

        raw.Headers.TryAddWithoutValidation(
            "Content-Type", $"multipart/form-data; boundary={boundary}");

        return await actor.Client.PostAsync($"{actor.Root}/documents", raw);
    }

    /// <summary>How many files the content store currently holds.</summary>
    private long CountStoredFiles() =>
        Directory.Exists(_fixture.Factory.BlobRoot)
            ? Directory.EnumerateFiles(
                _fixture.Factory.BlobRoot, "*", SearchOption.AllDirectories).LongCount()
            : 0;

    private async Task<RecordDocumentResponse> UploadAsync(
        Actor actor,
        byte[] content,
        string fileName,
        string mediaType,
        string title,
        string kind,
        string sensitivity,
        IReadOnlyList<DocumentLinkRequest>? links = null,
        string? idempotencyKey = null)
    {
        using MultipartFormDataContent form = Multipart(content, fileName, mediaType);

        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent(sensitivity), "sensitivity");

        if (links is { Count: > 0 })
        {
            form.Add(
                new StringContent(JsonSerializer.Serialize(links, Web)),
                "links");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, $"{actor.Root}/documents")
        {
            Content = form,
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add(ClientHeaders.IdempotencyKey, idempotencyKey);
        }

        using HttpResponseMessage response = await actor.Client.SendAsync(request);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<RecordDocumentResponse>())!;
    }

    private static async Task<RecordDocumentResponse> AddVersionAsync(
        Actor actor,
        Guid documentId,
        byte[] content,
        string fileName,
        string mediaType,
        int expectedVersion)
    {
        using MultipartFormDataContent form = Multipart(content, fileName, mediaType);

        form.Add(
            new StringContent(expectedVersion.ToString(CultureInfo.InvariantCulture)),
            "expectedVersion");

        using HttpResponseMessage response = await actor.Client.PostAsync(
            $"{actor.Root}/documents/{documentId}/versions", form);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<RecordDocumentResponse>())!;
    }

    private static async Task<byte[]> DownloadAsync(Actor actor, Guid versionId)
    {
        using HttpResponseMessage response = await actor.Client.GetAsync(
            $"{actor.Root}/document-versions/{versionId}/content");

        await EnsureAsync(response);

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(Actor actor, string name)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/people", new CreatePersonRequest(name));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<T> GetAsync<T>(Actor actor, string route)
    {
        using HttpResponseMessage response = await actor.Client.GetAsync($"{actor.Root}/{route}");

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PostAsync<T>(Actor actor, string route, object body)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/{route}", body);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        await EnsureAsync(response);
    }

    /// <summary>Fails with the server's own explanation rather than a status code.</summary>
    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{(int)response.StatusCode} {response.StatusCode}: {body}");
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
