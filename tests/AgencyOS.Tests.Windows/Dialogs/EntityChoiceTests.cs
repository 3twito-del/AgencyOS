using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That a person can tell two records apart, and that the identifier survives.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of <c>AOS-R001-006</c>'s repair. A label an operator can act on
/// is the point of the change; an identifier that arrives at the server exactly as
/// the server issued it is the thing that must not break while making it.
/// </para>
/// <para>
/// §8 is the reason the labels carry more than a name. Two people called Ada
/// Sallow at different companies are two different people, and a picker that shows
/// the name alone has moved the ambiguity rather than removed it.
/// </para>
/// </remarks>
public sealed class EntityChoiceTests
{
    /// <summary>The identifier is carried through untouched.</summary>
    [Fact]
    public void TheIdentifierIsTheOneTheServerIssued()
    {
        Guid id = Guid.CreateVersion7();

        EntityChoice choice = Assert.Single(EntityChoice.ForCompanies([Company(id, "Northgate")]));

        Assert.Equal(id, choice.Id);
    }

    /// <summary>Two people of the same name are told apart by where they work.</summary>
    [Fact]
    public void TwoPeopleOfOneNameAreDistinguishable()
    {
        IReadOnlyList<EntityChoice> choices = EntityChoice.ForPeople(
        [
            Person(Guid.CreateVersion7(), "Ada Sallow", company: "Northgate Pictures"),
            Person(Guid.CreateVersion7(), "Ada Sallow", company: "Harbour Films"),
        ]);

        Assert.Equal(2, choices.Count);
        Assert.NotEqual(choices[0].Label, choices[1].Label);
        Assert.Contains("Northgate Pictures", choices[0].Label, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person with nothing to distinguish them still gets a usable label.
    /// </summary>
    /// <remarks>
    /// The empty parts drop out rather than leaving a trailing separator, because a
    /// label reading "Ada Sallow · " is a label with a bug in it.
    /// </remarks>
    [Fact]
    public void APersonWithNoCompanyStillReadsCleanly()
    {
        EntityChoice choice = Assert.Single(
            EntityChoice.ForPeople([Person(Guid.CreateVersion7(), "Ada Sallow", company: null)]));

        Assert.Equal("Ada Sallow", choice.Label);
    }

    /// <summary>Two drafts of one script are not the same thing to send.</summary>
    [Fact]
    public void MaterialsAreToldApartByVersion()
    {
        IReadOnlyList<EntityChoice> choices = EntityChoice.ForMaterials(
        [
            Material(Guid.CreateVersion7(), "The Undertow", "Screenplay", "First draft"),
            Material(Guid.CreateVersion7(), "The Undertow", "Screenplay", "Second draft"),
        ]);

        Assert.NotEqual(choices[0].Label, choices[1].Label);
        Assert.Contains("First draft", choices[0].Label, StringComparison.Ordinal);
    }

    /// <summary>A deal is told apart by who is on the other side of it.</summary>
    [Fact]
    public void DealsAreToldApartByCounterparty()
    {
        EntityChoice choice = Assert.Single(EntityChoice.ForDeals(
            [Deal(Guid.CreateVersion7(), "Writer agreement", "Northgate Pictures")]));

        Assert.Contains("Northgate Pictures", choice.Label, StringComparison.Ordinal);
    }

    /// <summary>No label is ever an identifier.</summary>
    /// <remarks>
    /// The rule the whole finding turns on. A picker that showed the GUID in its
    /// label would satisfy every other assertion here and none of the point.
    /// </remarks>
    [Fact]
    public void NoLabelContainsAnIdentifier()
    {
        Guid id = Guid.CreateVersion7();

        IReadOnlyList<EntityChoice> choices =
        [
            .. EntityChoice.ForPeople([Person(id, "Ada Sallow", "Northgate")]),
            .. EntityChoice.ForCompanies([Company(id, "Northgate")]),
            .. EntityChoice.ForProjects([Project(id, "The Undertow")]),
            .. EntityChoice.ForDeals([Deal(id, "Writer agreement", "Northgate")]),
            .. EntityChoice.ForMaterials([Material(id, "The Undertow", "Screenplay", "First")]),
        ];

        Assert.All(choices, x =>
        {
            Assert.DoesNotContain(id.ToString(), x.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(id.ToString("N"), x.Label, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>What a combo box announces is the label, not the record.</summary>
    /// <remarks>
    /// §14. A <c>ComboBox</c> falls back to <c>ToString()</c> when a template does
    /// not apply, and a record's generated <c>ToString()</c> prints every property —
    /// including the identifier, which is how Audit 001 found identifiers being read
    /// aloud to screen-reader users in the first place.
    /// </remarks>
    [Fact]
    public void ToStringIsTheLabel()
    {
        EntityChoice choice = new(Guid.CreateVersion7(), "Ada Sallow · Northgate Pictures");

        Assert.Equal("Ada Sallow · Northgate Pictures", choice.ToString());
    }

    /// <summary>Only roles nobody fills are offered as open roles.</summary>
    [Fact]
    public void OnlyUnfilledRolesAreOpen()
    {
        Guid filled = Guid.CreateVersion7();
        Guid open = Guid.CreateVersion7();

        PackageElementSources sources = PackageElementSources.From(
            ProjectWith(
            [
                Role(filled, "Director", attached: true),
                Role(open, "Writer", attached: false),
            ]),
            [],
            []);

        EntityChoice only = Assert.Single(sources.Roles);

        Assert.Equal(open, only.Id);
        Assert.Single(sources.Attachments);
    }

    // ------------------------------------------------------------- fixtures

    private static PersonSummaryResponse Person(Guid id, string name, string? company) =>
        new(id, name, null, null, null, "Active", null, company, DateTimeOffset.UtcNow, 1);

    private static CompanySummaryResponse Company(Guid id, string name) =>
        new(id, name, null, "Studio", "Active", null, DateTimeOffset.UtcNow, 1);

    private static ProjectSummaryResponse Project(Guid id, string title) =>
        new(id, title, null, "Feature", "Active", "Development", 2026, null, null, null, null,
            0, 0, 0, DateTimeOffset.UtcNow, 1);

    private static DealSummaryResponse Deal(Guid id, string name, string counterparty) =>
        new(id, name, null, "Writing", "Open", Guid.CreateVersion7(), "A pursuit",
            Guid.CreateVersion7(), counterparty, null, null, null, Guid.CreateVersion7(), null,
            DateOnly.FromDateTime(DateTime.UtcNow), null, 0, null, null, null, false, null,
            null, 0, null, DateTimeOffset.UtcNow, 1);

    private static MaterialResponse Material(Guid id, string title, string type, string version) =>
        new(id, Guid.CreateVersion7(), title, type, "Current", version, null, null, null, null, 1);

    private static ProjectRoleResponse Role(Guid id, string type, bool attached) =>
        new(id, type, null, "Open", false, null,
            attached
                ? [new AttachmentResponse(
                    Guid.CreateVersion7(), id, type, null, Guid.CreateVersion7(), null,
                    "Somebody", "Attached", DateOnly.FromDateTime(DateTime.UtcNow), null,
                    true, null, null, DateTimeOffset.UtcNow, 1)]
                : []);

    private static ProjectDetailResponse ProjectWith(IReadOnlyList<ProjectRoleResponse> roles) =>
        new(Project(Guid.CreateVersion7(), "The Undertow"), null, null, null,
            roles, [], [], [], [], DateTimeOffset.UtcNow);
}
