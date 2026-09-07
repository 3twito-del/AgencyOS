using System;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records what happened and, in the same command, what happens next.
/// </summary>
/// <remarks>
/// The dialog holds no logic of its own: it fills a
/// <see cref="RecordInteractionViewModel"/> and lets it submit. That keeps the
/// workflow testable without a window, and keeps the rule that no business logic
/// lives in code-behind.
/// </remarks>
public sealed partial class RecordInteractionDialog : ContentDialog
{
    private readonly RecordInteractionViewModel _viewModel;

    public RecordInteractionDialog(IAgencyOsApi api, Guid personId, string personName)
    {
        InitializeComponent();

        _viewModel = new RecordInteractionViewModel(api);
        _viewModel.AddParticipant(new PartyRefRequest("Person", personId));

        WithText.Text = $"With {personName}";
        OccurredPicker.Date = DateTimeOffset.Now;
        FollowUpDuePicker.Date = DateTimeOffset.Now.AddDays(3);

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    /// <summary>Gets a value indicating whether the interaction was actually recorded.</summary>
    public bool Submitted { get; private set; }

    /// <summary>Gets the follow-up task identifier, when one was created.</summary>
    public Guid? FollowUpTaskId { get; private set; }

    private void OnFollowUpToggled(object sender, RoutedEventArgs e) =>
        FollowUpPanel.Visibility = FollowUpCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Hold the dialog open until the server has answered, so a refusal is shown
        // here rather than silently discarded.
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();

        try
        {
            _viewModel.InteractionType = TypeBox.SelectedItem as string ?? "Meeting";
            _viewModel.OccurredAt = OccurredPicker.Date;
            _viewModel.Summary = SummaryBox.Text;
            _viewModel.DetailedNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text;

            _viewModel.CreateFollowUp = FollowUpCheck.IsChecked == true;

            if (_viewModel.CreateFollowUp)
            {
                _viewModel.FollowUpTitle = FollowUpTitleBox.Text;
                _viewModel.FollowUpDueAt = FollowUpDuePicker.Date;
                _viewModel.FollowUpPriority = FollowUpPriorityBox.SelectedItem as string ?? "Normal";
            }

            if (!_viewModel.CanSubmit)
            {
                ErrorBar.Message = "A summary is required, and a follow-up needs a task title.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            RecordInteractionResponse? result = await _viewModel.SubmitAsync().ConfigureAwait(true);

            if (result is null)
            {
                ErrorBar.Message = _viewModel.ErrorMessage ?? "The interaction could not be recorded.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            Submitted = true;
            FollowUpTaskId = result.FollowUpTaskId;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
