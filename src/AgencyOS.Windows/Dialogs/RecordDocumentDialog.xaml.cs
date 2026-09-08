using System;
using System.Collections.Generic;
using System.IO;
using AgencyOS.Client.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a document and its first version.
/// </summary>
/// <remarks>
/// <para>
/// Three things on this dialog are deliberate. Sensitivity has no default, because
/// a default is a guess and privilege is a legal conclusion (ADR-0025). The kind
/// is a business classification and is asked separately from the file's format,
/// which the system reads from the bytes: a PDF can be a contract, an invoice or a
/// headshot proof, and the questions they answer are different (ADR-0024).
/// </para>
/// <para>
/// And the dialog says out loud that nothing scans the file. An operator who
/// assumes a stored file was vetted will forward it, and AgencyOS ships no
/// scanner - saying so is cheaper than the alternative.
/// </para>
/// </remarks>
public sealed partial class RecordDocumentDialog : ContentDialog
{
    public RecordDocumentDialog()
    {
        InitializeComponent();

        foreach (string kind in DocumentUploadViewModel.Kinds)
        {
            KindBox.Items.Add(kind);
        }

        KindBox.SelectedItem = "Other";

        foreach (string sensitivity in DocumentUploadViewModel.Sensitivities)
        {
            SensitivityBox.Items.Add(sensitivity);
        }
    }

    /// <summary>The chosen file's full path, or null when nothing was chosen.</summary>
    public string? FilePath { get; private set; }

    public string FileName =>
        FilePath is null ? string.Empty : Path.GetFileName(FilePath);

    public long ByteLength { get; private set; }

    /// <summary>The document title. Named apart from the dialog's own Title.</summary>
    public string DocumentTitle => (TitleBox.Text ?? string.Empty).Trim();

    public string Kind => KindBox.SelectedItem as string ?? "Other";

    public string Sensitivity => SensitivityBox.SelectedItem as string ?? "Internal";

    public string? Reference =>
        string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text.Trim();

    public string? Description =>
        string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

    /// <summary>Opens the chosen file for reading.</summary>
    public Stream OpenContent() =>
        File.OpenRead(FilePath ?? throw new InvalidOperationException("No file was chosen."));

    private async void OnChooseFile(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        IReadOnlyList<string> chosen = await FilePicking
            .PickFilesAsync(allowMultiple: false)
            .ConfigureAwait(true);

        if (chosen.Count == 0)
        {
            return;
        }

        FilePath = chosen[0];
        ByteLength = new FileInfo(FilePath).Length;

        FileText.Text = $"{Path.GetFileName(FilePath)}  ({DocumentFormatting.FormatSize(ByteLength)})";

        // A title the operator can change, not one the system insists on.
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            TitleBox.Text = Path.GetFileNameWithoutExtension(FilePath);
        }

        TooLargeBar.IsOpen = ByteLength > DocumentUploadViewModel.MaximumByteLength;

        Update();
    }

    private void OnChanged(object sender, object e) => Update();

    private void Update() =>
        IsPrimaryButtonEnabled =
            FilePath is not null
            && ByteLength <= DocumentUploadViewModel.MaximumByteLength
            && !string.IsNullOrWhiteSpace(TitleBox.Text)
            && SensitivityBox.SelectedItem is not null;
}
