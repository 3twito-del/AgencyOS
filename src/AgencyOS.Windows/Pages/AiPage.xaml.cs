using System;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Ai;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The AI workspace: asking, approving, and seeing what happened.
/// </summary>
/// <remarks>
/// <para>
/// Five surfaces over one idea. Ask starts a task and shows what came back;
/// Approvals is where a person decides one exact proposed action; Runs is the
/// caller's own history; Trace is what the model asked for beside what AgencyOS
/// did; Policy is what may be transmitted at all.
/// </para>
/// <para>
/// Approving is two acts, not one. The decision is recorded, and then the approved
/// request is executed as a separate call which re-derives the fingerprint,
/// re-checks the permission and may still be refused. That is why a refusal here
/// is shown as an outcome rather than as an error: somebody allowed an attempt,
/// and the domain declined it.
/// </para>
/// <para>
/// Model output is labelled as model output everywhere it appears. Nothing on this
/// page files anything against a person, a deal or a contract, and there is no
/// button that would.
/// </para>
/// </remarks>
public sealed partial class AiPage : Page, IPaletteCommandTarget
{
    private readonly AgentRunViewModel? _run;
    private readonly AgentRunListViewModel? _runs;
    private readonly AiApprovalViewModel? _approvals;
    private readonly AiProviderPolicyViewModel? _policy;

    public AiPage()
    {
        InitializeComponent();

        foreach (string kind in AgentRunViewModel.Kinds)
        {
            AgentBox.Items.Add(new ComboBoxItem { Content = kind, Tag = kind });
        }

        AgentBox.SelectedIndex = 0;

        foreach (string subject in AgentRunViewModel.SubjectKinds)
        {
            SubjectKindBox.Items.Add(new ComboBoxItem { Content = subject, Tag = subject });
        }

        SubjectKindBox.SelectedIndex = 0;

        RunStatusFilter.Items.Add(new ComboBoxItem { Content = "Any status", Tag = string.Empty });

        foreach (string status in AgentRunListViewModel.Statuses)
        {
            RunStatusFilter.Items.Add(new ComboBoxItem
            {
                Content = AiFormatting.Status(status),
                Tag = status,
            });
        }

        RunStatusFilter.SelectedIndex = 0;

        foreach (string ceiling in AiProviderPolicyViewModel.Ceilings)
        {
            PolicyCeiling.Items.Add(new ComboBoxItem
            {
                Content = AiFormatting.Ceiling(ceiling),
                Tag = ceiling,
            });
        }

        if (AppServices.Api is not { } api)
        {
            return;
        }

        _run = new AgentRunViewModel(api);
        _run.PropertyChanged += (_, _) => RenderRun();

        _runs = new AgentRunListViewModel(api);
        _runs.PropertyChanged += (_, _) => RenderRuns();

        _approvals = new AiApprovalViewModel(api);
        _approvals.PropertyChanged += (_, _) => RenderApprovals();

        _policy = new AiProviderPolicyViewModel(api);
        _policy.PropertyChanged += (_, _) => RenderPolicy();

        ApprovalList.ItemsSource = _approvals.Pending;
        RunList.ItemsSource = _runs.Runs;
        StepList.ItemsSource = _run.Steps;
        PolicyList.ItemsSource = _policy.Policies;
        ToolList.ItemsSource = _policy.Tools;
        ModelList.ItemsSource = _policy.Models;
    }

    /// <inheritdoc />
    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "ai.ask":
                Tabs.SelectedIndex = 0;
                TaskBox.Focus(FocusState.Programmatic);
                return;

            case "ai.approvals":
                Tabs.SelectedIndex = 1;
                _ = _approvals?.LoadAsync();
                return;

            case "ai.runs":
                Tabs.SelectedIndex = 2;
                _ = _runs?.LoadAsync();
                return;

            default:
                return;
        }
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_runs is null || _approvals is null || _policy is null)
        {
            Error("This build is not configured to reach an AgencyOS server.");
            return;
        }

        await _runs.LoadAsync().ConfigureAwait(true);
        await _approvals.LoadAsync().ConfigureAwait(true);
        await _policy.LoadAsync().ConfigureAwait(true);

        PopulateModels();
    }

    // ------------------------------------------------------------------ ask

    private void OnAgentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgentBox.SelectedItem is not ComboBoxItem { Tag: string kind })
        {
            return;
        }

        if (_run is not null)
        {
            _run.Kind = kind;
        }

        AgentDescription.Text = kind switch
        {
            "ResearchCopilot" =>
                "Reads a research case and drafts what it found. Anything it wants to "
                    + "record is proposed to you.",
            "RelationshipBrief" =>
                "Summarizes what AgencyOS holds about one person or company.",
            "DealBrief" => "Summarizes one deal from its own record.",
            "ContractBrief" => "Summarizes one contract from its recorded terms.",
            "FinanceBrief" => "Summarizes receivables and commissions you may read.",
            "CommunicationDraft" =>
                "Drafts a message. It is a draft: AgencyOS sends nothing, ever, "
                    + "from an AI run.",
            _ => string.Empty,
        };
    }

    private void OnTaskChanged(object sender, TextChangedEventArgs e)
    {
        if (_run is not null)
        {
            _run.Task = TaskBox.Text ?? string.Empty;
        }

        StartButton.IsEnabled = !string.IsNullOrWhiteSpace(TaskBox.Text);
    }

    private void OnSubjectKindChanged(object sender, SelectionChangedEventArgs e) =>
        SyncSubject();

    private void OnSubjectIdChanged(object sender, TextChangedEventArgs e) => SyncSubject();

    /// <summary>
    /// Carries the subject across to the view model.
    /// </summary>
    /// <remarks>
    /// An unparseable identifier reads as none rather than as an error. The subject
    /// is optional, and a half-typed GUID is somebody mid-keystroke rather than
    /// somebody making a mistake.
    /// </remarks>
    private void SyncSubject()
    {
        if (_run is null)
        {
            return;
        }

        if (SubjectKindBox.SelectedItem is ComboBoxItem { Tag: string subject })
        {
            _run.SubjectKind = subject;
        }

        _run.SubjectId = Guid.TryParse(SubjectIdBox.Text, out Guid id) ? id : null;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_run is null)
        {
            return;
        }

        if (ModelBox.SelectedItem is ComboBoxItem { Tag: string model } && model.Length > 0)
        {
            _run.ModelKey = model;
        }

        await _run.StartAsync().ConfigureAwait(true);

        // A run that ended waiting for a decision is not finished, and the person
        // who started it is the one who has to be told.
        if (_approvals is not null)
        {
            await _approvals.LoadAsync().ConfigureAwait(true);
        }

        if (_runs is not null)
        {
            await _runs.LoadAsync().ConfigureAwait(true);
        }
    }

    private async void OnCancelRun(object sender, RoutedEventArgs e)
    {
        if (_run is null)
        {
            return;
        }

        await _run.CancelAsync().ConfigureAwait(true);

        if (_runs is not null)
        {
            await _runs.LoadAsync().ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------ approvals

    private void OnApprovalSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_approvals is null)
        {
            return;
        }

        _approvals.Selected = ApprovalList.SelectedItem as AiApprovalResponse;
        RenderSelectedApproval();
    }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (_approvals?.Selected is not { } approval)
        {
            return;
        }

        ApproveAiActionDialog dialog = new(approval, DateTimeOffset.UtcNow)
        {
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();

        // Only Primary approves. Secondary rejects and Close does nothing, which is
        // what "decide later" has to mean for an approval that expires on its own.
        if (result == ContentDialogResult.Primary)
        {
            await _approvals.DecideAsync(approve: true, dialog.Reason).ConfigureAwait(true);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await _approvals.DecideAsync(approve: false, dialog.Reason).ConfigureAwait(true);
        }

        RenderSelectedApproval();
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if (_approvals?.Selected is null)
        {
            return;
        }

        await _approvals.DecideAsync(approve: false).ConfigureAwait(true);
        RenderSelectedApproval();
    }

    // ----------------------------------------------------------------- runs

    private async void OnRunFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_runs is null)
        {
            return;
        }

        _runs.Status = RunStatusFilter.SelectedItem is ComboBoxItem { Tag: string status }
            && status.Length > 0
                ? status
                : null;

        await _runs.LoadAsync().ConfigureAwait(true);
    }

    private async void OnRefreshRuns(object sender, RoutedEventArgs e)
    {
        if (_runs is not null)
        {
            await _runs.LoadAsync().ConfigureAwait(true);
        }
    }

    private async void OnRunSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_run is null || RunList.SelectedItem is not AgentRunResponse selected)
        {
            return;
        }

        await _run.LoadAsync(selected.Id).ConfigureAwait(true);
        Tabs.SelectedIndex = 3;
    }

    // --------------------------------------------------------------- policy

    private void OnPolicySelected(object sender, SelectionChangedEventArgs e)
    {
        if (PolicyList.SelectedItem is not AiProviderPolicyResponse policy)
        {
            PolicySave.IsEnabled = false;
            return;
        }

        PolicyEnabled.IsOn = policy.IsEnabled;
        PolicyWrites.IsChecked = policy.AllowsCanonicalWriteProposals;

        for (int index = 0; index < PolicyCeiling.Items.Count; index++)
        {
            if (PolicyCeiling.Items[index] is ComboBoxItem { Tag: string ceiling }
                && ceiling == policy.MaximumSensitivity)
            {
                PolicyCeiling.SelectedIndex = index;
                break;
            }
        }

        PolicySave.IsEnabled = true;
    }

    private async void OnSavePolicy(object sender, RoutedEventArgs e)
    {
        if (_policy is null
            || PolicyList.SelectedItem is not AiProviderPolicyResponse policy
            || PolicyCeiling.SelectedItem is not ComboBoxItem { Tag: string ceiling })
        {
            return;
        }

        await _policy
            .SaveAsync(policy, PolicyEnabled.IsOn, ceiling, PolicyWrites.IsChecked == true)
            .ConfigureAwait(true);
    }

    // -------------------------------------------------------------- render

    private void RenderRun()
    {
        if (_run is null)
        {
            return;
        }

        Busy.Visibility = _run.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = _run.CanStart;
        CancelButton.IsEnabled = _run.IsActive;

        if (_run.HasError)
        {
            Error(_run.ErrorMessage ?? string.Empty);
        }

        RunStatusText.Text = _run.StatusText;

        RunDetailText.Text = _run.Run is { } run
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{run.Run.ModelInvocationCount} model turns, {run.Run.ToolCallCount} lookups, "
                    + $"prompt {run.PromptTemplateId} v{run.PromptTemplateVersion}")
            : string.Empty;

        if (_run.FailureText.Length > 0)
        {
            Error(_run.FailureText);
        }

        ResultNotice.IsOpen = _run.HasResult;
        ResultText.Text = _run.Result ?? string.Empty;
    }

    private void RenderRuns()
    {
        if (_runs is null)
        {
            return;
        }

        Busy.Visibility = _runs.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        if (_runs.HasError)
        {
            Error(_runs.ErrorMessage ?? string.Empty);
        }

        SummaryText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{_runs.Runs.Count} of your runs   {_runs.Active.Count} still going");
    }

    private void RenderApprovals()
    {
        if (_approvals is null)
        {
            return;
        }

        Busy.Visibility = _approvals.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ApprovalsEmpty.IsOpen = _approvals.IsEmpty;

        if (_approvals.HasError)
        {
            Error(_approvals.ErrorMessage ?? string.Empty);
        }

        ApprovalBar.IsOpen = _approvals.Pending.Count > 0;
        ApprovalBar.Message = string.Create(
            CultureInfo.CurrentCulture,
            $"{_approvals.Pending.Count} proposed actions are waiting. Each one expires on its own, and nothing happens until you decide.");

        if (_approvals.LastExecution is { } execution)
        {
            ExecutionBar.IsOpen = true;
            ExecutionBar.Severity = execution.Succeeded
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;

            // A refusal is not an error. The approval permitted an attempt and
            // AgencyOS declined it, which is the system working.
            ExecutionBar.Title = execution.Succeeded ? "Done" : "AgencyOS refused it";
            ExecutionBar.Message = execution.Succeeded
                ? execution.Content ?? string.Empty
                : execution.Failure ?? string.Empty;
        }

        RenderSelectedApproval();
    }

    private void RenderSelectedApproval()
    {
        if (_approvals?.Selected is not { } approval)
        {
            ApprovalHeading.Text = "Select something to decide.";
            ApprovalEffect.Text = string.Empty;
            ApprovalExpiry.Text = string.Empty;
            ApprovalArguments.Text = string.Empty;
            ApproveButton.IsEnabled = false;
            RejectButton.IsEnabled = false;
            return;
        }

        ApprovalHeading.Text = approval.Summary;

        ApprovalEffect.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{approval.ToolName}, requested by a {approval.AgentKind} run");

        ApprovalExpiry.Text = AiFormatting.Remaining(approval, DateTimeOffset.UtcNow);
        ApprovalArguments.Text = approval.Arguments;

        ApproveButton.IsEnabled = _approvals.CanDecide;
        RejectButton.IsEnabled = _approvals.CanDecide;
    }

    private void RenderPolicy()
    {
        if (_policy is null)
        {
            return;
        }

        Busy.Visibility = _policy.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        if (_policy.HasError)
        {
            Error(_policy.ErrorMessage ?? string.Empty);
        }
    }

    private void PopulateModels()
    {
        if (_policy is null)
        {
            return;
        }

        ModelBox.Items.Clear();
        ModelBox.Items.Add(new ComboBoxItem { Content = "Configured default", Tag = string.Empty });

        foreach (AiModelDescriptorResponse model in _policy.Models)
        {
            ModelBox.Items.Add(new ComboBoxItem { Content = model.Key, Tag = model.Key });
        }

        ModelBox.SelectedIndex = 0;
    }

    private void Error(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = message.Length > 0;
    }
}
