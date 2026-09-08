using System.Text;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Documents;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What the document surfaces say, and what they refuse to say.
/// </summary>
/// <remarks>
/// Most of these are about wording. A screen that says "Clean" for a file nothing
/// scanned, or "created" for a date that is really "received", is a screen that
/// puts a false fact into somebody's head at the moment they are relying on it
/// (ADR-0024).
/// </remarks>
public sealed class DocumentViewModelTests
{
    [Fact]
    public async Task TheList_LoadsAndSummarizesWhatItLoaded()
    {
        FakeAgencyOsApi api = new();

        api.Documents.Add(FakeAgencyOsApi.DocumentSummary(
            "Executed agreement", "Contract", sensitivity: "Confidential", linkCount: 2));
        api.Documents.Add(FakeAgencyOsApi.DocumentSummary(
            "Counsel note", "Statement", sensitivity: "Privileged"));
        api.Documents.Add(FakeAgencyOsApi.DocumentSummary(
            "Mentioned only", "Statement", holdsContent: false));

        DocumentListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(3, view.Documents.Count);
        Assert.Equal(2, view.WithContent);
        Assert.Equal(1, view.Sensitive);
        Assert.Equal(2, view.Unfiled);
        Assert.False(view.IsEmpty);
    }

    /// <summary>The filters reach the server rather than being applied locally.</summary>
    /// <remarks>
    /// It matters which end filters. A client that fetched everything and hid rows
    /// would have already received the privileged documents it was hiding.
    /// </remarks>
    [Fact]
    public async Task TheFilters_AreSentToTheServer()
    {
        FakeAgencyOsApi api = new();

        DocumentListViewModel view = new(api)
        {
            Kind = "Contract",
            Status = "Archived",
            Sensitivity = "Privileged",
            WithContentOnly = true,
            Search = "  undertow  ",
        };

        await view.LoadAsync();

        Assert.Equal("Contract", api.LastDocumentFilter.Kind);
        Assert.Equal("Archived", api.LastDocumentFilter.Status);
        Assert.Equal("Privileged", api.LastDocumentFilter.Sensitivity);
        Assert.True(api.LastDocumentFilter.HasContent);
        Assert.Equal("undertow", api.LastDocumentFilter.Search);
    }

    /// <summary>The screen says what the search box actually searches.</summary>
    [Fact]
    public void TheSearchScope_IsStated()
    {
        Assert.Contains(
            "File contents are not searched",
            DocumentListViewModel.SearchScopeNotice,
            StringComparison.Ordinal);
    }

    /// <summary>An empty list is distinguishable from a list nobody loaded.</summary>
    [Fact]
    public async Task AnEmptyResult_IsNotTheSameAsNotLoaded()
    {
        FakeAgencyOsApi api = new();

        DocumentListViewModel view = new(api);

        Assert.False(view.IsEmpty);

        await view.LoadAsync();

        Assert.True(view.IsEmpty);
    }

    /// <summary>Adding a version appends. It never replaces one.</summary>
    [Fact]
    public async Task AddingAVersion_Appends()
    {
        FakeAgencyOsApi api = new();

        RecordDocumentResponse recorded = await api.RecordDocumentAsync(
            new RecordDocumentRequest("Agreement", "Contract", "Confidential"),
            new MemoryStream(Encoding.UTF8.GetBytes("draft one")),
            "agreement.txt");

        DocumentDetailViewModel view = new(api) { DocumentId = recorded.DocumentId };

        await view.LoadAsync();

        Assert.Single(view.Versions);
        Assert.Equal(1, view.ExpectedVersion);

        await view.AddVersionAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("draft two")), "agreement-2.txt");

        Assert.Null(view.ErrorMessage);
        Assert.Equal(2, view.Versions.Count);

        // Newest first, and the earlier one is still there with its own bytes.
        Assert.Equal(2, view.Versions[0].Sequence);
        Assert.Equal(1, view.Versions[1].Sequence);
        Assert.NotEqual(view.Versions[0].ContentHash, view.Versions[1].ContentHash);
    }

    /// <summary>A stale version is refused, and the refusal is shown rather than thrown.</summary>
    [Fact]
    public async Task AStaleVersion_SurfacesAsARefusal()
    {
        FakeAgencyOsApi api = new();

        RecordDocumentResponse recorded = await api.RecordDocumentAsync(
            new RecordDocumentRequest("Agreement", "Contract", "Confidential"),
            new MemoryStream(Encoding.UTF8.GetBytes("one")),
            "one.txt");

        DocumentDetailViewModel first = new(api) { DocumentId = recorded.DocumentId };
        DocumentDetailViewModel second = new(api) { DocumentId = recorded.DocumentId };

        await first.LoadAsync();
        await second.LoadAsync();

        await first.AddVersionAsync(new MemoryStream(Encoding.UTF8.GetBytes("two")), "two.txt");

        Assert.Null(first.ErrorMessage);

        // The second screen is now holding a version that no longer exists.
        await second.AddVersionAsync(new MemoryStream(Encoding.UTF8.GetBytes("three")), "three.txt");

        Assert.NotNull(second.ErrorMessage);
        Assert.Contains("changed", second.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Archiving does not remove anything.</summary>
    [Fact]
    public async Task Archiving_KeepsEveryVersion()
    {
        FakeAgencyOsApi api = new();

        RecordDocumentResponse recorded = await api.RecordDocumentAsync(
            new RecordDocumentRequest("Agreement", "Contract", "Confidential"),
            new MemoryStream(Encoding.UTF8.GetBytes("one")),
            "one.txt");

        DocumentDetailViewModel view = new(api) { DocumentId = recorded.DocumentId };

        await view.LoadAsync();
        await view.ArchiveAsync("Superseded");

        Assert.True(view.IsArchived);
        Assert.False(view.CanAddVersion);
        Assert.Single(view.Versions);

        await view.RestoreAsync();

        Assert.False(view.IsArchived);
        Assert.True(view.CanAddVersion);
    }

    /// <summary>A download is written where the caller says, under the server's name.</summary>
    [Fact]
    public async Task Downloading_WritesTheBytesTheServerSent()
    {
        FakeAgencyOsApi api = new();

        byte[] bytes = Encoding.UTF8.GetBytes("the executed agreement");

        RecordDocumentResponse recorded = await api.RecordDocumentAsync(
            new RecordDocumentRequest("Agreement", "Contract", "Confidential"),
            new MemoryStream(bytes),
            "agreement.txt");

        DocumentDetailViewModel view = new(api) { DocumentId = recorded.DocumentId };

        await view.LoadAsync();

        MemoryStream destination = new();

        string? savedAs = await view.DownloadVersionAsync(
            recorded.VersionId, _ => new NonClosingStream(destination));

        Assert.Equal("agreement.txt", savedAs);
        Assert.Equal(bytes, destination.ToArray());
    }

    /// <summary>A file nothing scanned is never described as clean.</summary>
    [Theory]
    [InlineData("Unscanned", "Not scanned")]
    [InlineData(null, "Not scanned")]
    [InlineData("Pending", "Not scanned")]
    [InlineData("Clean", "Scanned clean")]
    [InlineData("Suspect", "Flagged by scanner")]
    [InlineData("Failed", "Scan failed")]
    public void TheScanState_NeverOverclaims(string? state, string expected) =>
        Assert.Equal(expected, DocumentFormatting.ScanState(state));

    /// <summary>Anything but a real clean result asks the reader for care.</summary>
    [Theory]
    [InlineData("Unscanned", true)]
    [InlineData("Suspect", true)]
    [InlineData("Failed", true)]
    [InlineData(null, true)]
    [InlineData("Clean", false)]
    public void CareIsAskedForWheneverNothingScannedIt(string? state, bool care) =>
        Assert.Equal(care, DocumentFormatting.NeedsCare(state));

    /// <summary>An unreadable format is reported as unread, not as a failure.</summary>
    [Theory]
    [InlineData("Extracted", "Text extracted")]
    [InlineData("Unsupported", "No extractor for this format")]
    [InlineData("Failed", "Extraction failed")]
    [InlineData("Pending", "Not attempted")]
    [InlineData(null, "Not attempted")]
    public void TheExtractionState_SaysWhatHappened(string? state, string expected) =>
        Assert.Equal(expected, DocumentFormatting.ExtractionState(state));

    /// <summary>Sizes are readable, and exact where exactness matters.</summary>
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    public void SizesAreFormattedForReading(long bytes, string expected) =>
        Assert.Equal(expected, DocumentFormatting.FormatSize(bytes));

    /// <summary>The digest is shortened for display and never for comparison.</summary>
    [Fact]
    public void TheShortHash_IsAPrefixOfTheWholeDigest()
    {
        const string digest = "a3f1c0de9b8877665544332211aabbccddeeff00112233445566778899aabbcc";

        string shortened = DocumentFormatting.ShortHash(digest);

        Assert.Equal(12, shortened.Length);
        Assert.StartsWith(shortened, digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The upload form will not submit without a stated sensitivity.
    /// </summary>
    /// <remarks>
    /// No default, on purpose. A default is a guess, and privilege is a legal
    /// conclusion a person draws (ADR-0025).
    /// </remarks>
    [Fact]
    public void TheUploadForm_RequiresAStatedSensitivity()
    {
        FakeAgencyOsApi api = new();

        DocumentUploadViewModel view = new(api)
        {
            Title = "Executed agreement",
            FileName = "agreement.pdf",
            ByteLength = 2048,
        };

        Assert.Null(view.Sensitivity);
        Assert.False(view.CanSubmit);

        view.Sensitivity = "Confidential";

        Assert.True(view.CanSubmit);
    }

    /// <summary>An oversize file is refused before the upload begins.</summary>
    [Fact]
    public void TheUploadForm_RefusesAnOversizeFileBeforeSending()
    {
        FakeAgencyOsApi api = new();

        DocumentUploadViewModel view = new(api)
        {
            Title = "A screener",
            Sensitivity = "Internal",
            FileName = "screener.mp4",
            ByteLength = DocumentUploadViewModel.MaximumByteLength + 1,
        };

        Assert.True(view.IsTooLarge);
        Assert.False(view.CanSubmit);
    }

    /// <summary>Every kind the form offers is one the server understands.</summary>
    /// <remarks>
    /// Offering a kind the server refuses turns a menu choice into a failed upload
    /// after the file has already been read.
    /// </remarks>
    [Fact]
    public void EveryOfferedKind_IsOneTheServerKnows()
    {
        string[] known = [.. Enum.GetNames<AgencyOS.Domain.Documents.DocumentKind>()];

        Assert.All(
            DocumentUploadViewModel.Kinds,
            kind => Assert.Contains(kind, known));
    }

    /// <summary>Every sensitivity the form offers is one the server understands.</summary>
    [Fact]
    public void EveryOfferedSensitivity_IsOneTheServerKnows()
    {
        string[] known = [.. Enum.GetNames<AgencyOS.Domain.Documents.DocumentSensitivity>()];

        Assert.All(
            DocumentUploadViewModel.Sensitivities,
            sensitivity => Assert.Contains(sensitivity, known));
    }

    /// <summary>A stream the view model must not close on the test's behalf.</summary>
    private sealed class NonClosingStream : Stream
    {
        private readonly Stream _inner;

        public NonClosingStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // Deliberately not disposed: the test still has to read what was written.
        }
    }
}
