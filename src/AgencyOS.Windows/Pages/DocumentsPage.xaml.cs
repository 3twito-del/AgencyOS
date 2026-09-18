using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Documents;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The document workspace: the files the agency holds, and every version of them.
/// </summary>
/// <remarks>
/// <para>
/// The organizing rule is that a document is a name for a series of immutable
/// versions. Uploading again adds version N+1; there is no command anywhere on this
/// page that replaces the bytes of a version that already exists, because the
/// reason for keeping them is that somebody can go back and read what was actually
/// signed (ADR-0024).
/// </para>
/// <para>
/// There is no editor and no preview of formats this build cannot read. Opening a
/// file hands it to whatever Windows uses. A half-working document surface invites
/// people to edit a contract in a box that quietly loses its formatting, and the
/// milestone that stores files should not also be the milestone that renders them.
/// </para>
/// <para>
/// Nothing here is cached. Documents are ONLINE_ONLY in
/// <c>docs/13_OFFLINE_CLASSIFICATION.md</c>: a privileged contract copied into a
/// local SQLite file is a privileged contract on a laptop, and revoking somebody's
/// access afterwards would not take it back.
/// </para>
/// </remarks>
public sealed partial class DocumentsPage : Page, IPaletteCommandTarget
{
    private readonly DocumentListViewModel? _list;
    private readonly DocumentDetailViewModel? _detail;

    public DocumentsPage()
    {
        InitializeComponent();

        SearchScopeText.Text = DocumentListViewModel.SearchScopeNotice;

        if (AppServices.Api is not { } api)
        {
            return;
        }

        _list = new DocumentListViewModel(api);
        _list.PropertyChanged += (_, _) => RenderList();

        _detail = new DocumentDetailViewModel(api);
        _detail.PropertyChanged += (_, _) => RenderDetail();

        DocumentList.ItemsSource = _list.Documents;
        VersionList.ItemsSource = _detail.Versions;
        LinkList.ItemsSource = _detail.Links;
        HistoryList.ItemsSource = _detail.History;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "document.record":
                _ = RecordAsync();
                break;

            case "document.version.add":
                _ = AddVersionAsync();
                break;

            case "document.download":
                _ = DownloadAsync();
                break;

            case "document.link":
                _ = LinkAsync();
                break;

            case "document.unlink":
                _ = UnlinkAsync();
                break;

            case "document.archive":
                _ = ArchiveAsync();
                break;

            case "document.restore":
                _ = RestoreAsync();
                break;

            case "go.documents.unfiled":
                // The server has no unfiled filter, so this is a client-side view
                // over what was loaded rather than a query. Said plainly in the
                // summary line rather than presented as a filtered search.
                ShowUnfiled();
                break;

            default:
                break;
        }
    }

    private Task LoadAsync() => _list is null ? Task.CompletedTask : _list.LoadAsync();

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilters();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_list is not null)
        {
            _list.Search = sender.Text ?? string.Empty;
            _ = _list.LoadAsync();
        }
    }

    private void ApplyFilters()
    {
        if (_list is null)
        {
            return;
        }

        _list.Kind = SelectedTag(KindBox);
        _list.Sensitivity = SelectedTag(SensitivityBox);
        _list.Status = SelectedTag(StatusBox);
        _list.WithContentOnly = HasContentBox.IsChecked == true;

        _ = _list.LoadAsync();
    }

    /// <summary>
    /// The selected item's tag, or null when it is the "any" entry.
    /// </summary>
    /// <remarks>
    /// Named apart from <c>FrameworkElement.Tag</c>, which a page inherits. A
    /// helper called <c>Tag</c> compiles and hides it, and the next person to
    /// write <c>Tag = something</c> on the page gets a puzzle.
    /// </remarks>
    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null;

    private void OnDocumentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || DocumentList.SelectedItem is not DocumentSummaryResponse selected)
        {
            return;
        }

        _detail.DocumentId = selected.Id;
        _ = _detail.LoadAsync();
    }

    private void OnVersionSelected(object sender, SelectionChangedEventArgs e) =>
        DownloadButton.IsEnabled = VersionList.SelectedItem is DocumentVersionResponse;

    private void OnRecordDocument(object sender, RoutedEventArgs e) => _ = RecordAsync();

    private void OnAddVersion(object sender, RoutedEventArgs e) => _ = AddVersionAsync();

    private void OnDownload(object sender, RoutedEventArgs e) => _ = DownloadAsync();

    private void OnLink(object sender, RoutedEventArgs e) => _ = LinkAsync();

    private void OnUnlink(object sender, RoutedEventArgs e) => _ = UnlinkAsync();

    private void OnArchive(object sender, RoutedEventArgs e) => _ = ArchiveAsync();

    private void OnRestore(object sender, RoutedEventArgs e) => _ = RestoreAsync();

    private async Task RecordAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        RecordDocumentDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(async () =>
        {
            RecordDocumentRequest request = new(
                dialog.DocumentTitle,
                dialog.Kind,
                dialog.Sensitivity,
                dialog.Reference,
                dialog.Description);

            await using Stream content = dialog.OpenContent();

            RecordDocumentResponse recorded = await api
                .RecordDocumentAsync(
                    request,
                    content,
                    dialog.FileName,
                    mediaType: null,
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);

            // Deduplication is a storage fact, and worth saying, because an
            // operator who sees one copy where they uploaded two should know the
            // system recognized the bytes rather than lost a file (ADR-0024).
            if (recorded.Deduplicated)
            {
                Notice(
                    "The organization already held these bytes",
                    "The file was stored once and this document points at it. It is a separate document with its own permissions.");
            }
        }).ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AddVersionAsync()
    {
        if (_detail?.Document is not { } document)
        {
            DetailError("Select a document first.");
            return;
        }

        if (AppServices.Api is not { } api)
        {
            return;
        }

        System.Collections.Generic.IReadOnlyList<string> chosen = await FilePicking
            .PickFilesAsync()
            .ConfigureAwait(true);

        if (chosen.Count == 0)
        {
            return;
        }

        string path = chosen[0];

        await Guarded(async () =>
        {
            await using Stream content = File.OpenRead(path);

            await _detail
                .AddVersionAsync(
                    content,
                    Path.GetFileName(path),
                    mediaType: null,
                    notes: null,
                    Guid.NewGuid().ToString("N"))
                .ConfigureAwait(true);
        }).ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Saves a version's bytes to a file the operator chooses.
    /// </summary>
    /// <remarks>
    /// The only route to stored content. There is no public URL and no path in any
    /// response: the server streams the bytes to an authorized caller and nothing
    /// else can reach them (ADR-0024).
    /// </remarks>
    private async Task DownloadAsync()
    {
        if (_detail is null || VersionList.SelectedItem is not DocumentVersionResponse version)
        {
            DetailError("Select a version first.");
            return;
        }

        string? destination = await FilePicking
            .PickSaveAsync(version.DisplayFileName)
            .ConfigureAwait(true);

        if (destination is null)
        {
            return;
        }

        await Guarded(async () =>
        {
            await _detail
                .DownloadVersionAsync(
                    version.Id,
                    _ => File.Create(destination))
                .ConfigureAwait(true);

            Notice(
                "Saved",
                $"{Path.GetFileName(destination)}  ({DocumentFormatting.FormatSize(version.ByteLength)}). "
                + $"Digest {DocumentFormatting.ShortHash(version.ContentHash)}.");
        }).ConfigureAwait(true);
    }

    private async Task LinkAsync()
    {
        if (_detail?.Document is not { } document)
        {
            DetailError("Select a document first.");
            return;
        }

        if (AppServices.Api is not { } api)
        {
            return;
        }

        LinkRecordDialog dialog = new(api, $"Link {document.Document.Title}")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _detail.LinkAsync(
                new LinkDocumentRequest(dialog.Target, dialog.TargetId, dialog.Note),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task UnlinkAsync()
    {
        if (_detail is null || LinkList.SelectedItem is not DocumentLinkResponse link)
        {
            DetailError("Select a link first.");
            return;
        }

        await Guarded(() => _detail.UnlinkAsync(link.Id)).ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ArchiveAsync()
    {
        if (_detail?.Document is not { } document)
        {
            DetailError("Select a document first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Archive this document",
            document.Document.Title,
            "Archiving takes the document out of ordinary use. Nothing is deleted: every version and every byte stays exactly where it is, and the document can be restored.",
            "Archive")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => _detail.ArchiveAsync(dialog.Reason, Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RestoreAsync()
    {
        if (_detail?.Document is null)
        {
            DetailError("Select a document first.");
            return;
        }

        await Guarded(() => _detail.RestoreAsync(Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private void ShowUnfiled()
    {
        if (_list is null)
        {
            return;
        }

        SummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_list.Unfiled} of {_list.Documents.Count} loaded documents are linked to nothing.");
    }

    private async Task Guarded(Func<Task> action)
    {
        try
        {
            DetailBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            DetailError(failure.Detail ?? failure.Message);
        }
    }

    private void DetailError(string message)
    {
        DetailBar.Title = "That did not happen";
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Error;
        DetailBar.IsOpen = true;
    }

    private void Notice(string title, string message)
    {
        DetailBar.Title = title;
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Informational;
        DetailBar.IsOpen = true;
    }

    private void RenderList()
    {
        if (_list is null)
        {
            return;
        }

        ListBusy.Visibility = _list.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        ListError.IsOpen = _list.HasError;
        ListError.Message = _list.ErrorMessage ?? string.Empty;

        ListEmpty.IsOpen = _list.IsEmpty;

        SummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_list.Documents.Count} documents   {_list.WithContent} with the file held   "
            + $"{_list.Sensitive} privileged or restricted   {_list.Unfiled} unfiled");
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        DetailBusy.Visibility = _detail.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        if (_detail.HasError)
        {
            DetailError(_detail.ErrorMessage ?? string.Empty);
        }

        if (_detail.Document is not { } detail)
        {
            DetailTitle.Text = "Select a document";
            DetailStanding.Text = string.Empty;
            DetailDates.Text = string.Empty;
            AddVersionButton.IsEnabled = false;
            LinkButton.IsEnabled = false;
            ArchiveButton.IsEnabled = false;
            RestoreButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            ScanBar.IsOpen = false;
            ArchivedBar.IsOpen = false;
            return;
        }

        DocumentSummaryResponse document = detail.Document;

        DetailTitle.Text = document.Title;

        DetailStanding.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{document.Kind}   {document.Sensitivity}   {document.VersionCount} versions   "
            + $"{document.LinkCount} links");

        // Recorded, deliberately, and never "created". AgencyOS knows when it was
        // handed the file and does not know when the document was written.
        DetailDates.Text = document.CurrentVersion is { } current
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Latest version recorded {current.RecordedAt:u}   ")
                + DocumentFormatting.FormatSize(current.ByteLength)
                + $"   {DocumentFormatting.ScanState(current.ScanState)}"
            : "AgencyOS holds no file for this document.";

        ScanBar.IsOpen = document.CurrentVersion is { } version
            && DocumentFormatting.NeedsCare(version.ScanState);

        ScanBar.Message = document.CurrentVersion is { } scanned
            ? DocumentFormatting.ScanState(scanned.ScanState)
                + ". AgencyOS runs no malware scanner and will not describe an unscanned file as safe."
            : string.Empty;

        ArchivedBar.IsOpen = _detail.IsArchived;

        AddVersionButton.IsEnabled = _detail.CanAddVersion;
        LinkButton.IsEnabled = true;
        ArchiveButton.IsEnabled = !_detail.IsArchived;
        RestoreButton.IsEnabled = _detail.IsArchived;

        ExtractedTextBox.Text = detail.ExtractedText ?? string.Empty;

        ExtractionBar.Message = _detail.HasExtractedText
            ? DocumentDetailViewModel.ExtractedTextNotice
            : "No text was extracted from this document. M10 extracts plain text formats only; a PDF is not a failure, it is a format nothing tried to read.";
    }
}
