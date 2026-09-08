using AgencyOS.Client.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Pulls an attachment's bytes into the canonical document store.
/// </summary>
/// <remarks>
/// Explicit rather than automatic. A mailbox synchronization that downloaded every
/// attachment it saw would pull gigabytes of signatures, logos and cloud
/// placeholders into canonical storage, and the sensitivity of each one would have
/// been decided by nobody. Somebody says which attachment matters and what it is
/// (ADR-0024, ADR-0026).
/// </remarks>
public sealed partial class IngestAttachmentDialog : ContentDialog
{
    public IngestAttachmentDialog(string fileName, string sizeText)
    {
        InitializeComponent();

        HeadlineText.Text = $"{fileName}  ({sizeText})";
        TitleBox.Text = fileName;

        foreach (string kind in DocumentUploadViewModel.Kinds)
        {
            KindBox.Items.Add(kind);
        }

        KindBox.SelectedItem = "Other";

        foreach (string sensitivity in DocumentUploadViewModel.Sensitivities)
        {
            SensitivityBox.Items.Add(sensitivity);
        }

        Update();
    }

    public string DocumentTitle => (TitleBox.Text ?? string.Empty).Trim();

    public string Kind => KindBox.SelectedItem as string ?? "Other";

    public string Sensitivity => SensitivityBox.SelectedItem as string ?? "Internal";

    private void OnChanged(object sender, object e) => Update();

    private void Update() =>
        IsPrimaryButtonEnabled =
            DocumentTitle.Length > 0 && SensitivityBox.SelectedItem is not null;
}
