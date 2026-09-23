using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Projects;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The slate: every project the agency is working, with the filters an agent uses.
/// </summary>
/// <remarks>
/// One list with filters rather than separate screens per stage. A project moving
/// from development to production is the same project, and separate screens would
/// make it look like a different record each time it moved.
/// </remarks>
public sealed class ProjectListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status = "Active";
    private string? _stage;
    private string? _type;
    private string? _missingRole;
    private string _search = string.Empty;

    public ProjectListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ProjectSummaryResponse> Projects { get; } = [];

    /// <summary>Restrict to one operational status. Defaults to what is being worked.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Stage
    {
        get => _stage;
        set => Set(ref _stage, value);
    }

    public string? Type
    {
        get => _type;
        set => Set(ref _type, value);
    }

    /// <summary>
    /// Show only projects nobody currently holds this role on.
    /// </summary>
    /// <remarks>
    /// "Which of my projects still need a director" is the question this answers,
    /// and the rule behind it is stated on the server rather than guessed here.
    /// </remarks>
    public string? MissingRole
    {
        get => _missingRole;
        set => Set(ref _missingRole, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Projects.Count == 0;
    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many of the listed projects still have an unfilled role.</summary>
    public int WithOpenRoles => Projects.Count(x => x.OpenRoleCount > 0);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ProjectSummaryResponse> projects = await _api
                .ListProjectsAsync(
                    Status,
                    Stage,
                    Type,
                    leadUserId: null,
                    MissingRole,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    token)
                .ConfigureAwait(true);

            Projects.Clear();

            foreach (ProjectSummaryResponse project in projects)
            {
                Projects.Add(project);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(WithOpenRoles));
        }, cancellationToken);
}

/// <summary>
/// One project's working surface: roles, attachments, companies, source, materials, packages.
/// </summary>
/// <remarks>
/// Backed by one server read rather than assembled from several calls, so the
/// screen shows one coherent answer rather than a set of reads that can disagree
/// with each other.
/// </remarks>
public sealed class ProjectDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ProjectDetailResponse? _project;
    private bool _loaded;

    public ProjectDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ProjectDetailResponse? Project
    {
        get => _project;
        private set => Set(ref _project, value);
    }

    public ObservableCollection<ProjectRoleResponse> Roles { get; } = [];

    public ObservableCollection<ProjectCompanyResponse> Companies { get; } = [];

    public ObservableCollection<SourcePropertyResponse> SourceProperties { get; } = [];

    public ObservableCollection<ProjectMaterialResponse> Materials { get; } = [];

    public ObservableCollection<PackageSummaryResponse> Packages { get; } = [];

    public ObservableCollection<ProjectHistoryEntryResponse> History { get; } = [];

    /// <summary>Who is on this project, across every role.</summary>
    /// <remarks>
    /// The attachments already arrive nested inside each role, and the Attachments
    /// tab was bound to nothing at all — a named tab that could never show anything,
    /// while the project page stayed unable to say who was on the project. Flattened
    /// here rather than fetched, because the data was already in hand.
    /// </remarks>
    public ObservableCollection<AttachmentResponse> Attachments { get; } = [];

    /// <summary>The pursuits that carry this project as their subject.</summary>
    /// <remarks>
    /// <para>
    /// A project listed its roles, companies, materials and packages and said
    /// nothing about the commercial work they exist for, so an operator standing
    /// on it had to remember a name and search another workspace to get anywhere.
    /// Phase III called it a destination that points nowhere (F-17).
    /// </para>
    /// <para>
    /// The relationships were already stored and the endpoints already accepted
    /// the filter. Nothing asked. These read the shipped lists through their own
    /// published <c>projectId</c> parameter rather than enriching the project
    /// read model, so no published shape changes and the destination stays the
    /// authority on its own rows.
    /// </para>
    /// </remarks>
    public ObservableCollection<OpportunitySummaryResponse> Pursuits { get; } = [];

    /// <inheritdoc cref="Pursuits" />
    public ObservableCollection<DealSummaryResponse> Deals { get; } = [];

    /// <inheritdoc cref="Pursuits" />
    public ObservableCollection<ContractSummaryResponse> Contracts { get; } = [];

    public override bool IsEmpty => _loaded && Project is null;

    /// <summary>Roles nothing currently holds. What the project still needs.</summary>
    public IReadOnlyList<ProjectRoleResponse> Gaps =>
        [.. Roles.Where(x => x.Status != "Closed" && !x.Attachments.Any(a => a.HoldsTheRole))];

    /// <summary>A one-line summary of where the project stands, in plain words.</summary>
    public string Standing
    {
        get
        {
            if (Project is not { } detail)
            {
                return string.Empty;
            }

            int gaps = Gaps.Count;

            string roles = gaps switch
            {
                0 => "no roles outstanding",
                1 => "1 role outstanding",
                _ => string.Create(CultureInfo.InvariantCulture, $"{gaps} roles outstanding"),
            };

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{detail.Project.Status} - {detail.Project.Stage} - {roles}");
        }
    }

    public Task LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ProjectDetailResponse detail = await _api
                .GetProjectAsync(projectId, token)
                .ConfigureAwait(true);

            Project = detail;

            Replace(Roles, detail.Roles);
            Replace(
                Attachments,
                [.. detail.Roles
                    .SelectMany(role => role.Attachments)
                    .OrderByDescending(x => x.HoldsTheRole)
                    .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)]);
            Replace(Companies, detail.Companies);
            Replace(SourceProperties, detail.SourceProperties);
            Replace(Materials, detail.Materials);
            Replace(Packages, detail.Packages);

            IReadOnlyList<ProjectHistoryEntryResponse> history = await _api
                .GetProjectHistoryAsync(projectId, token)
                .ConfigureAwait(true);

            Replace(History, history);

            // Unfiltered on purpose. A project's commercial history includes the
            // pursuits that closed and the contracts that have run out, and an
            // operator asking what became of it is asking about those too.
            Replace(
                Pursuits,
                await _api
                    .ListOpportunitiesAsync(projectId: projectId, cancellationToken: token)
                    .ConfigureAwait(true));

            Replace(
                Deals,
                await _api
                    .ListDealsAsync(projectId: projectId, cancellationToken: token)
                    .ConfigureAwait(true));

            Replace(
                Contracts,
                await _api
                    .ListContractsAsync(projectId: projectId, cancellationToken: token)
                    .ConfigureAwait(true));

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Gaps));
            OnPropertyChanged(nameof(Standing));
        }, cancellationToken);

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();

        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>
/// A package: what the agency has assembled, and what it is still missing.
/// </summary>
/// <remarks>
/// <para>
/// The screen's job is to keep two things apart that look alike in a list: what is
/// actually attached, and what the agency merely wants. <see cref="Attached"/> and
/// <see cref="Proposed"/> are separate collections for exactly that reason - a
/// single list with a subtle badge would eventually be read as a roster.
/// </para>
/// <para>
/// Strategy notes arrive absent for a caller without <c>packages.strategy.read</c>,
/// and the surface shows nothing rather than a placeholder, because a placeholder
/// would leak that a note exists.
/// </para>
/// </remarks>
public sealed class PackageViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private PackageDetailResponse? _package;
    private bool _loaded;

    public PackageViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public PackageDetailResponse? Package
    {
        get => _package;
        private set => Set(ref _package, value);
    }

    /// <summary>Elements that correspond to a real, current attachment.</summary>
    public ObservableCollection<PackageElementResponse> Attached { get; } = [];

    /// <summary>Everything the agency is proposing, tracking or still needs.</summary>
    public ObservableCollection<PackageElementResponse> Proposed { get; } = [];

    /// <summary>Roles on the project that nothing currently holds.</summary>
    public ObservableCollection<ProjectRoleResponse> Gaps { get; } = [];

    public override bool IsEmpty => _loaded && Package is null;

    /// <summary>Whether the caller may read the package's strategy.</summary>
    /// <remarks>
    /// Derived from the value being present rather than from a separate flag. The
    /// API returns the field absent, and absent is deliberately indistinguishable
    /// from empty, so this is true only when there is something to show.
    /// </remarks>
    public bool HasStrategy => !string.IsNullOrWhiteSpace(Package?.StrategyNotes);

    /// <summary>A plain sentence about how complete the package is.</summary>
    public string Readiness
    {
        get
        {
            if (Package is not { } detail)
            {
                return string.Empty;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{detail.Package.Status} - {Attached.Count} attached, {Proposed.Count} proposed, {Gaps.Count} still to fill");
        }
    }

    public Task LoadAsync(Guid packageId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            PackageDetailResponse detail = await _api
                .GetPackageAsync(packageId, token)
                .ConfigureAwait(true);

            Package = detail;

            Attached.Clear();
            Proposed.Clear();
            Gaps.Clear();

            foreach (PackageElementResponse element in detail.Elements)
            {
                // The split is on what is actually true, not on the element's kind:
                // an element pointing at an attachment that has since ended is no
                // longer a claim about the project, and belongs with the proposals.
                if (element.IsAttached)
                {
                    Attached.Add(element);
                }
                else
                {
                    Proposed.Add(element);
                }
            }

            foreach (ProjectRoleResponse gap in detail.Gaps)
            {
                Gaps.Add(gap);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasStrategy));
            OnPropertyChanged(nameof(Readiness));
        }, cancellationToken);
}

/// <summary>The package list, filtered by how far along each one is.</summary>
public sealed class PackageListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;

    public PackageListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<PackageSummaryResponse> Packages { get; } = [];

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public override bool IsEmpty => _loaded && Packages.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<PackageSummaryResponse> packages = await _api
                .ListPackagesAsync(Status, projectId: null, token)
                .ConfigureAwait(true);

            Packages.Clear();

            foreach (PackageSummaryResponse package in packages)
            {
                Packages.Add(package);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);
}
