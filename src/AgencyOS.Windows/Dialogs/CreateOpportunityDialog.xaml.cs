using System;
using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a new pursuit.</summary>
/// <remarks>
/// The subject field changes meaning with the kind, and the hint says which record
/// is wanted. Each kind requires the subject that makes it that kind, so asking for
/// "a subject" without saying which would produce a refusal the user cannot act on
/// (ADR-0020).
/// </remarks>
public sealed partial class CreateOpportunityDialog : ContentDialog
{
    public CreateOpportunityDialog()
    {
        InitializeComponent();

        KindBox.SelectedIndex = 1;
        UpdateHint();
    }

    public CreateOpportunityRequest ToRequest()
    {
        string kind = SelectedTag(KindBox) ?? "Other";

        OpportunitySubjectRequest[] subjects =
            Guid.TryParse(SubjectIdBox.Text.Trim(), out Guid subject)
                ? [new OpportunitySubjectRequest(SubjectKindFor(kind), subject, "Primary")]
                : [];

        return new CreateOpportunityRequest(
            NameBox.Text.Trim(),
            kind,
            Guid.TryParse(OwnerIdBox.Text.Trim(), out Guid owner) ? owner : Guid.Empty,
            OpenedOn: null,
            SelectedTag(PriorityBox),
            Empty(DescriptionBox.Text),
            Empty(StrategyBox.Text),
            subjects);
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHint();
        Validate();
    }

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void UpdateHint()
    {
        if (SubjectHint is null)
        {
            return;
        }

        SubjectHint.Text = SelectedTag(KindBox) switch
        {
            "TalentEngagement" => "The talent profile being placed.",
            "ProjectMarket" => "The project being taken out.",
            "PackageMarket" => "The package being taken out.",
            "Staffing" => "The project role being filled.",
            _ => "Any project, package or talent profile this pursuit is about.",
        };
    }

    private void Validate() =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(NameBox.Text)
            && Guid.TryParse(OwnerIdBox.Text.Trim(), out _)
            && Guid.TryParse(SubjectIdBox.Text.Trim(), out _);

    private static string SubjectKindFor(string kind) => kind switch
    {
        "TalentEngagement" => "TalentProfile",
        "PackageMarket" => "Package",
        "Staffing" => "ProjectRole",
        _ => "Project",
    };

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
