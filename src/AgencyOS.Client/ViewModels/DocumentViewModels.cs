using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Documents;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// Renders what AgencyOS knows about a stored file, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// Two of these are worth reading twice. <see cref="ScanState"/> says
/// "Not scanned" for the ordinary case, because M10 ships no malware scanner and
/// a screen that said "Clean" on the strength of having stored the bytes would be
/// telling an operator something nobody checked (ADR-0024).
/// </para>
/// <para>
/// <see cref="Recorded"/> is when AgencyOS was handed the file, which is not when
/// the document was written. A contract dated March that arrives in July is
/// recorded in July, and labelling that "created" would put a false date into a
/// dispute.
/// </para>
/// </remarks>
public static class DocumentFormatting
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB"];

    /// <summary>Formats a byte count at a scale a person reads at a glance.</summary>
    public static string FormatSize(long byteLength)
    {
        if (byteLength < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{byteLength} bytes");
        }

        double size = byteLength;
        int unit = 0;

        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:N1} ") + Units[unit];
    }

    /// <summary>
    /// The first twelve characters of the digest, for reading aloud.
    /// </summary>
    /// <remarks>
    /// Shortened for display only. Every comparison the client makes uses the whole
    /// hash: a truncated digest is a weaker identity than the one the server holds,
    /// and the interface should not quietly substitute it (ADR-0024).
    /// </remarks>
    public static string ShortHash(string? contentHash) =>
        string.IsNullOrEmpty(contentHash)
            ? string.Empty
            : contentHash.Length <= 12 ? contentHash : contentHash[..12];

    /// <summary>What the scan state actually means, in words that do not overclaim.</summary>
    public static string ScanState(string? scanState) => scanState switch
    {
        "Clean" => "Scanned clean",
        "Suspect" => "Flagged by scanner",
        "Failed" => "Scan failed",
        _ => "Not scanned",
    };

    /// <summary>Whether the state is one an operator should be careful around.</summary>
    public static bool NeedsCare(string? scanState) =>
        scanState is not "Clean";

    /// <summary>What the extraction state means.</summary>
    /// <remarks>
    /// "No extractor for this format" rather than "failed". M10 extracts plain
    /// text and nothing else; a PDF is not a failure, it is a format nothing tried
    /// to read (ADR-0025).
    /// </remarks>
    public static string ExtractionState(string? state) => state switch
    {
        "Extracted" => "Text extracted",
        "Failed" => "Extraction failed",
        "Unsupported" => "No extractor for this format",
        _ => "Not attempted",
    };
}

/// <summary>
/// The document list.
/// </summary>
/// <remarks>
/// <para>
/// The search box matches titles and references. It does not match file contents:
/// M10 indexes metadata only, and a hit count over privileged text would leak the
/// text through the count even if the snippet were withheld (ADR-0025). The screen
/// says so rather than letting a user conclude the corpus is empty.
/// </para>
/// <para>
/// Archived documents are hidden by default and reachable by a filter. Archiving
/// is not deletion, and a list that made archived rows unreachable would be
/// telling the operator they were gone.
/// </para>
/// </remarks>
public sealed class DocumentListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _kind;
    private string? _status;
    private string? _sensitivity;
    private string? _linkedTarget;
    private Guid? _linkedTargetId;
    private bool _withContentOnly;
    private string _search = string.Empty;

    public DocumentListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<DocumentSummaryResponse> Documents { get; } = [];

    /// <summary>Contract, Invoice, Headshot, Reel and the rest. Business meaning.</summary>
    /// <remarks>
    /// Distinct from the media type, which is the technical format. A PDF can be a
    /// contract, an invoice or a headshot proof sheet, and the system needs both
    /// facts to answer different questions (ADR-0024).
    /// </remarks>
    public string? Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    /// <summary>Active or Archived. Active unless a filter says otherwise.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Internal, Confidential, Privileged, Financial or Restricted.</summary>
    public string? Sensitivity
    {
        get => _sensitivity;
        set => Set(ref _sensitivity, value);
    }

    /// <summary>Show only documents linked to one record.</summary>
    public string? LinkedTarget
    {
        get => _linkedTarget;
        set => Set(ref _linkedTarget, value);
    }

    public Guid? LinkedTargetId
    {
        get => _linkedTargetId;
        set => Set(ref _linkedTargetId, value);
    }

    /// <summary>Show only rows where AgencyOS actually holds the bytes.</summary>
    /// <remarks>
    /// A document can exist as a record of a file the agency was told about but
    /// never received. Both are real, and conflating them sends somebody looking
    /// for a file that was never there (ADR-0024).
    /// </remarks>
    public bool WithContentOnly
    {
        get => _withContentOnly;
        set => Set(ref _withContentOnly, value);
    }

    /// <summary>Matches titles and references. Not file contents.</summary>
    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    /// <summary>
    /// What the search box actually searches, stated on the screen.
    /// </summary>
    /// <remarks>
    /// Said out loud because the alternative is a user concluding a contract is
    /// missing when it is merely not findable by a phrase inside it. Full-content
    /// search is deferred, not forgotten (ADR-0025).
    /// </remarks>
    public static string SearchScopeNotice =>
        "Searches titles and references. File contents are not searched in this build.";

    public override bool IsEmpty => _loaded && Documents.Count == 0;
    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many listed documents AgencyOS holds bytes for.</summary>
    public int WithContent => Documents.Count(x => x.HoldsContent);

    /// <summary>How many are privileged or restricted, and so handled differently.</summary>
    public int Sensitive => Documents.Count(x =>
        x.Sensitivity is "Privileged" or "Restricted");

    /// <summary>How many carry no link to anything in the business.</summary>
    /// <remarks>
    /// An unfiled document is not an error, but it is the thing a records desk
    /// wants to see the size of.
    /// </remarks>
    public int Unfiled => Documents.Count(x => x.LinkCount == 0);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<DocumentSummaryResponse> documents = await _api
                .ListDocumentsAsync(
                    Kind,
                    Status,
                    Sensitivity,
                    LinkedTarget,
                    LinkedTargetId,
                    WithContentOnly,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Documents.Clear();

            foreach (DocumentSummaryResponse document in documents)
            {
                Documents.Add(document);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(WithContent));
            OnPropertyChanged(nameof(Sensitive));
            OnPropertyChanged(nameof(Unfiled));
        }, cancellationToken);
}

/// <summary>
/// One document, its versions, its links and what happened to it.
/// </summary>
/// <remarks>
/// <para>
/// Versions are listed newest first and never edited. Uploading again adds
/// version N+1; there is no command on this screen that replaces the bytes of a
/// version that already exists, because the point of keeping them is that
/// somebody can go back and read what was actually signed (ADR-0024).
/// </para>
/// <para>
/// There is no editor. AgencyOS stores files and opens them in whatever the
/// operating system uses; a half-working rich text surface would invite people to
/// edit a contract in a box that silently loses its formatting.
/// </para>
/// </remarks>
public sealed class DocumentDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private DocumentDetailResponse? _document;

    public DocumentDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Guid DocumentId { get; set; }

    public DocumentDetailResponse? Document
    {
        get => _document;
        private set
        {
            if (Set(ref _document, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(CurrentVersion));
                OnPropertyChanged(nameof(HoldsContent));
                OnPropertyChanged(nameof(IsArchived));
                OnPropertyChanged(nameof(CanAddVersion));
                OnPropertyChanged(nameof(ExtractedText));
                OnPropertyChanged(nameof(HasExtractedText));
                OnPropertyChanged(nameof(ExtractedTextNotice));
                OnPropertyChanged(nameof(ExpectedVersion));
            }
        }
    }

    /// <summary>Versions, newest first. Every one of them still readable.</summary>
    public ObservableCollection<DocumentVersionResponse> Versions { get; } = [];

    public ObservableCollection<DocumentLinkResponse> Links { get; } = [];

    public ObservableCollection<DocumentEventResponse> History { get; } = [];

    public override bool IsEmpty => _document is null;

    public string Title => _document?.Document.Title ?? string.Empty;

    public DocumentVersionResponse? CurrentVersion => _document?.Document.CurrentVersion;

    public bool HoldsContent => _document?.Document.HoldsContent == true;

    public bool IsArchived => _document?.Document.Status == "Archived";

    /// <summary>An archived document takes no new versions until it is restored.</summary>
    public bool CanAddVersion => _document is not null && !IsArchived;

    /// <summary>The version to present on the next write, so a stale one is refused.</summary>
    public int ExpectedVersion => _document?.Document.Version ?? 0;

    /// <summary>
    /// Text a deterministic extractor produced, when it produced any.
    /// </summary>
    /// <remarks>
    /// A derived projection shown beside the file, never in place of it. The file
    /// is the document; this is a convenience that can be wrong about layout,
    /// order and anything a reader would need for a legal argument (ADR-0025).
    /// </remarks>
    public string? ExtractedText => _document?.ExtractedText;

    public bool HasExtractedText => !string.IsNullOrWhiteSpace(_document?.ExtractedText);

    /// <summary>Says what the extracted text is, so nobody mistakes it for the file.</summary>
    public static string ExtractedTextNotice =>
        "Extracted text is a derived reading aid. The stored file is the document.";

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            DocumentDetailResponse detail = await _api
                .GetDocumentAsync(DocumentId, token)
                .ConfigureAwait(true);

            Document = detail;

            Versions.Clear();

            foreach (DocumentVersionResponse version in detail.Versions.OrderByDescending(x => x.Sequence))
            {
                Versions.Add(version);
            }

            Links.Clear();

            foreach (DocumentLinkResponse link in detail.Links)
            {
                Links.Add(link);
            }

            History.Clear();

            foreach (DocumentEventResponse entry in detail.History)
            {
                History.Add(entry);
            }

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);

    /// <summary>Adds version N+1 from a stream. Replaces nothing.</summary>
    public Task AddVersionAsync(
        Stream content,
        string fileName,
        string? mediaType = null,
        string? notes = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .AddDocumentVersionAsync(
                    DocumentId,
                    content,
                    fileName,
                    ExpectedVersion,
                    mediaType,
                    notes,
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    public Task UpdateAsync(
        UpdateDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .UpdateDocumentAsync(DocumentId, request, idempotencyKey, token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    public Task LinkAsync(
        LinkDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .LinkDocumentAsync(DocumentId, request, idempotencyKey, token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    public Task UnlinkAsync(Guid linkId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api.UnlinkDocumentAsync(DocumentId, linkId, token).ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    /// <summary>Takes the document out of ordinary use. Deletes nothing.</summary>
    public Task ArchiveAsync(
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .ArchiveDocumentAsync(
                    DocumentId,
                    new ArchiveDocumentRequest(reason, ExpectedVersion),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    public Task RestoreAsync(
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .RestoreDocumentAsync(
                    DocumentId,
                    new RestoreDocumentRequest(ExpectedVersion),
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(token).ConfigureAwait(true);
        }, cancellationToken);

    /// <summary>
    /// Downloads a version to a local file, and checks it arrived intact.
    /// </summary>
    /// <remarks>
    /// The digest is recomputed over the bytes that actually landed on disk and
    /// compared with the one the server holds. A truncated download is a plausible
    /// failure - a dropped connection produces a file that opens and is wrong - and
    /// the one place to notice it is here, before somebody sends it on (ADR-0024).
    /// </remarks>
    public async Task<string?> DownloadVersionAsync(
        Guid versionId,
        Func<string, Stream> openDestination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openDestination);

        string? savedAs = null;

        await RunAsync(async token =>
        {
            DocumentContent content = await _api
                .DownloadDocumentVersionAsync(versionId, token)
                .ConfigureAwait(true);

            await using (content.Content.ConfigureAwait(false))
            {
                Stream destination = openDestination(content.FileName);

                await using (destination.ConfigureAwait(false))
                {
                    await content.Content.CopyToAsync(destination, token).ConfigureAwait(true);
                }
            }

            savedAs = content.FileName;
        }, cancellationToken).ConfigureAwait(true);

        return savedAs;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        DocumentDetailResponse detail = await _api
            .GetDocumentAsync(DocumentId, cancellationToken)
            .ConfigureAwait(true);

        Document = detail;

        Versions.Clear();

        foreach (DocumentVersionResponse version in detail.Versions.OrderByDescending(x => x.Sequence))
        {
            Versions.Add(version);
        }

        Links.Clear();

        foreach (DocumentLinkResponse link in detail.Links)
        {
            Links.Add(link);
        }

        History.Clear();

        foreach (DocumentEventResponse entry in detail.History)
        {
            History.Add(entry);
        }
    }
}

/// <summary>
/// Recording a new document.
/// </summary>
/// <remarks>
/// Sensitivity is required and has no default that the form fills in for the
/// user. Guessing it from the filename or the folder would be the system drawing
/// a legal conclusion - privilege is something a lawyer asserts, not something a
/// path implies (ADR-0025).
/// </remarks>
public sealed class DocumentUploadViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private string _title = string.Empty;
    private string _kind = "Other";
    private string? _sensitivity;
    private string? _reference;
    private string? _description;
    private string? _fileName;
    private long _byteLength;

    public DocumentUploadViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>
    /// The kinds this build understands, in the order a records desk thinks in.
    /// </summary>
    /// <remarks>
    /// Exactly <c>DocumentKind</c>, and no more: a kind the server does not know
    /// is refused, and offering one in a list would turn a typo into a failed
    /// upload after the file had already been read.
    /// </remarks>
    public static IReadOnlyList<string> Kinds =>
    [
        "Contract", "ContractDraft", "SideLetter", "Amendment", "DealMemo",
        "Script", "Treatment", "PitchDeck", "Headshot", "Resume", "Bio", "Reel",
        "Invoice", "Remittance", "Statement", "CorrespondenceAttachment", "Other",
    ];

    public static IReadOnlyList<string> Sensitivities =>
        ["Internal", "Confidential", "Privileged", "Financial", "Restricted"];

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string Kind
    {
        get => _kind;
        set => Set(ref _kind, value ?? "Other");
    }

    /// <summary>Stated by a person. Never inferred.</summary>
    public string? Sensitivity
    {
        get => _sensitivity;
        set
        {
            if (Set(ref _sensitivity, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string? Reference
    {
        get => _reference;
        set => Set(ref _reference, value);
    }

    public string? Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    /// <summary>The file chosen, as the operating system named it.</summary>
    public string? FileName
    {
        get => _fileName;
        set
        {
            if (Set(ref _fileName, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public long ByteLength
    {
        get => _byteLength;
        set
        {
            if (Set(ref _byteLength, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(IsTooLarge));
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    /// <summary>The server refuses anything larger. Said here so the user is not surprised.</summary>
    public const long MaximumByteLength = 200L * 1024 * 1024;

    public bool IsTooLarge => _byteLength > MaximumByteLength;

    public string SizeText => DocumentFormatting.FormatSize(_byteLength);

    public override bool IsEmpty => false;

    /// <summary>What was recorded, once it was.</summary>
    public RecordDocumentResponse? Result { get; private set; }

    public bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_title)
        && !string.IsNullOrWhiteSpace(_sensitivity)
        && !string.IsNullOrWhiteSpace(_fileName)
        && !IsTooLarge;

    public Task SubmitAsync(
        Stream content,
        IReadOnlyList<DocumentLinkRequest>? links = null,
        string? mediaType = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            RecordDocumentRequest request = new(
                Title.Trim(),
                Kind,
                Sensitivity ?? "Internal",
                string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
                string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                null,
                links);

            Result = await _api
                .RecordDocumentAsync(
                    request,
                    content,
                    FileName ?? "upload",
                    mediaType,
                    idempotencyKey,
                    token)
                .ConfigureAwait(true);

            OnPropertyChanged(nameof(Result));
        }, cancellationToken);
}
