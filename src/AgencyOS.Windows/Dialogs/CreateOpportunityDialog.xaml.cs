using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private readonly IAgencyOsApi _api;
    private readonly IReadOnlyList<EntityChoice> _projects;
    private readonly IReadOnlyList<EntityChoice> _packages;
    private readonly IReadOnlyList<EntityChoice> _talent;

    /// <param name="api">Used to fetch a project's roles when the kind is staffing.</param>
    /// <param name="projects">Projects this organization holds.</param>
    /// <param name="packages">Packages it holds.</param>
    /// <param name="talent">Talent profiles it holds.</param>
    /// <remarks>
    /// The subject is a different kind of record for each kind of pursuit, which is
    /// why there are three lists and not one. Staffing is the odd one: its subject
    /// is a project role, roles have no tenant-wide list of their own, and so the
    /// project is chosen first and its roles are fetched (<c>AOS-R001-006</c>).
    /// </remarks>
    public CreateOpportunityDialog(
        IAgencyOsApi api,
        IReadOnlyList<ProjectSummaryResponse> projects,
        IReadOnlyList<PackageSummaryResponse> packages,
        IReadOnlyList<TalentSummaryResponse> talent,
        IReadOnlyList<OrganizationMemberResponse> members)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(talent);
        ArgumentNullException.ThrowIfNull(members);

        InitializeComponent();

        // The hint under this field describes it. Declaring that lets a screen
        // reader reach the explanation from the field, instead of the reader
        // having to find it by looking (AOS-R002-011).
        AutomationProperties.GetDescribedBy(SubjectBox).Add(SubjectHint);

        // Preselect whoever is signed in, proved by the directory's own IsSelf
        // rather than guessed; they can still choose somebody else.
        OwnerPicker(members);

        _api = api;
        _projects = EntityChoice.ForProjects(projects);
        _packages = EntityChoice.ForPackages(packages);
        _talent = EntityChoice.ForTalent(talent);

        KindBox.SelectedIndex = 1;
        UpdateHint();
        ApplySubjectKind();
    }

    /// <summary>The subject the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => SubjectBox.SelectedItem as EntityChoice;

    public CreateOpportunityRequest ToRequest()
    {
        string kind = SelectedTag(KindBox) ?? "Other";

        OpportunitySubjectRequest[] subjects = Chosen() is { } subject
            ? [new OpportunitySubjectRequest(SubjectKindFor(kind), subject.Id, "Primary")]
            : [];

        return new CreateOpportunityRequest(
            NameBox.Text.Trim(),
            kind,
            Chosen(OwnerIdBox),
            OpenedOn: null,
            SelectedTag(PriorityBox),
            Empty(DescriptionBox.Text),
            Empty(StrategyBox.Text),
            subjects);
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHint();
        ApplySubjectKind();
    }

    private void OnRequiredChanged(object sender, object e) => Validate();

    private void OnSubjectProjectChanged(object sender, SelectionChangedEventArgs e) =>
        _ = LoadRolesAsync();

    /// <summary>
    /// Points the subject picker at the records this kind of pursuit is about, and
    /// drops whatever was chosen under the previous kind.
    /// </summary>
    /// <remarks>
    /// A package chosen while the kind was "package market" is not a talent profile,
    /// and carrying it across would send the server an identifier of the wrong kind
    /// for the subject it declares (§12).
    /// </remarks>
    private void ApplySubjectKind()
    {
        if (SubjectBox is null)
        {
            return;
        }

        string kind = SelectedTag(KindBox) ?? "Other";
        bool staffing = kind == "Staffing";

        SubjectProjectBox.Visibility = staffing ? Visibility.Visible : Visibility.Collapsed;

        SubjectBox.SelectedItem = null;

        if (staffing)
        {
            SubjectProjectBox.ItemsSource = _projects;
            SubjectBox.Header = "Role";
            SubjectBox.ItemsSource = Array.Empty<EntityChoice>();
        }
        else
        {
            SubjectProjectBox.SelectedItem = null;

            (SubjectBox.ItemsSource, SubjectBox.Header) = kind switch
            {
                "TalentEngagement" => (_talent, "Talent"),
                "PackageMarket" => (_packages, "Package"),
                _ => (_projects, "Project"),
            };
        }

        Validate();
    }

    /// <summary>Fetches the chosen project's roles, and says while it is happening.</summary>
    private async Task LoadRolesAsync()
    {
        if (SubjectProjectBox.SelectedItem is not EntityChoice project)
        {
            SubjectBox.ItemsSource = Array.Empty<EntityChoice>();
            Validate();

            return;
        }

        SubjectBox.SelectedItem = null;
        SubjectBusy.Visibility = Visibility.Visible;
        Validate();

        try
        {
            ProjectDetailResponse detail = await _api
                .GetProjectAsync(project.Id).ConfigureAwait(true);

            SubjectBox.ItemsSource = EntityChoice.ForProjectRoles(detail.Roles);
        }
        catch (AgencyOsApiException)
        {
            // The picker says nothing rather than claiming the project has no
            // roles. The primary button stays off, so nothing can be submitted
            // against a list that failed to load (§15).
            SubjectBox.ItemsSource = Array.Empty<EntityChoice>();
        }
        finally
        {
            SubjectBusy.Visibility = Visibility.Collapsed;
            Validate();
        }
    }

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
            && Chosen(OwnerIdBox) != Guid.Empty
            && Chosen() is not null;

    private static string SubjectKindFor(string kind) => kind switch
    {
        "TalentEngagement" => "TalentProfile",
        "PackageMarket" => "Package",
        "Staffing" => "ProjectRole",
        _ => "Project",
    };

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    /// <summary>
    /// Offers the organization's people, with whoever is signed in preselected.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-006</c>. The directory says which member is the caller through
    /// <c>IsSelf</c>, so the default is proved rather than assumed — and it is only
    /// a default: every other member stays selectable.
    /// </remarks>
    private void OwnerPicker(IReadOnlyList<OrganizationMemberResponse> members)
    {
        IReadOnlyList<EntityChoice> choices = EntityChoice.ForMembers(members);

        OwnerIdBox.ItemsSource = choices;

        if (members.FirstOrDefault(x => x.IsSelf) is { } self)
        {
            OwnerIdBox.SelectedItem = choices.FirstOrDefault(x => x.Id == self.UserId);
        }
    }

    /// <summary>The member chosen in a picker, or empty while none is.</summary>
    private static Guid Chosen(ComboBox box) =>
        (box.SelectedItem as EntityChoice)?.Id ?? Guid.Empty;

}
