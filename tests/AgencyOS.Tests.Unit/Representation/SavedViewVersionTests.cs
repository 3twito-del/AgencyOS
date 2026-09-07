using AgencyOS.Domain.Common;
using AgencyOS.Domain.SavedViews;
using Xunit;

namespace AgencyOS.Tests.Unit.Representation;

/// <summary>
/// The saved-view definition format across versions.
/// </summary>
/// <remarks>
/// M3 shipped definition version 1 and refused anything else. M4 adds targets and
/// filters, which makes version 2 - and the interesting question is what happens
/// to the views somebody already saved. Refusing them would have broken every
/// saved view in the product for no reason; guessing at a version from the future
/// would defeat the point of versioning at all. These tests pin both halves.
/// </remarks>
public sealed class SavedViewVersionTests
{
    /// <summary>A view saved under M3 still works, and still means what it meant.</summary>
    [Fact]
    public void AVersionOneDocument_IsStillUnderstood()
    {
        SavedViewDefinition definition = new(
            1,
            SavedViewTarget.People,
            new SavedViewFilters(Status: "Active"),
            new SavedViewSort("DisplayName", SavedViewSortDirection.Ascending));

        // No exception: version 1 is read as it always was.
        definition.Validate();
    }

    [Fact]
    public void AVersionTwoDocument_IsUnderstood()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Talent,
            new SavedViewFilters(ClientsOnly: true, Discipline: "Writer"));

        definition.Validate();
    }

    /// <summary>A version from the future is refused rather than guessed at.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(99)]
    public void AnUnknownVersion_IsRefused(int version)
    {
        SavedViewDefinition definition = new(
            version,
            SavedViewTarget.People,
            new SavedViewFilters());

        DomainException failure = Assert.Throws<DomainException>(definition.Validate);

        Assert.Contains("not supported", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The understood range is contiguous and includes what M3 wrote.</summary>
    /// <remarks>
    /// Pinned deliberately. Raising the minimum is how views saved by an earlier
    /// build stop working, so it should be a visible decision rather than a
    /// side effect of adding a target.
    /// </remarks>
    [Fact]
    public void TheUnderstoodRange_CoversEveryVersionEverShipped()
    {
        Assert.Equal(1, SavedViewDefinition.MinimumUnderstoodVersion);
        Assert.Equal(4, SavedViewDefinition.CurrentDefinitionVersion);
    }

    /// <summary>
    /// Every target says which definition version it arrived in.
    /// </summary>
    /// <remarks>
    /// A missing entry would throw at validation time for any view naming that
    /// target, which is a runtime failure for something knowable now.
    /// </remarks>
    [Fact]
    public void EveryTarget_SaysWhenItArrived()
    {
        foreach (SavedViewTarget target in Enum.GetValues<SavedViewTarget>())
        {
            Assert.True(
                SavedViewDefinition.TargetIntroducedIn.ContainsKey(target),
                $"{target} does not say which definition version introduced it.");

            int introduced = SavedViewDefinition.TargetIntroducedIn[target];

            Assert.InRange(
                introduced,
                SavedViewDefinition.MinimumUnderstoodVersion,
                SavedViewDefinition.CurrentDefinitionVersion);
        }
    }

    /// <summary>
    /// A target cannot be claimed at a version that predates it.
    /// </summary>
    /// <remarks>
    /// Otherwise the version number describes nothing: a document could say it was
    /// written by a build that had never heard of the target it names.
    /// </remarks>
    [Fact]
    public void ATargetOlderThanItsVersion_IsRefused()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Projects,
            new SavedViewFilters());

        DomainException failure = Assert.Throws<DomainException>(definition.Validate);

        Assert.Contains("did not exist", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A target at or after the version it arrived in is fine.</summary>
    [Fact]
    public void ATargetAtItsOwnVersion_IsAccepted()
    {
        foreach (SavedViewTarget target in Enum.GetValues<SavedViewTarget>())
        {
            SavedViewDefinition definition = new(
                SavedViewDefinition.TargetIntroducedIn[target],
                target,
                new SavedViewFilters());

            definition.Validate();
        }
    }

    /// <summary>Every target has an explicit sortable-field allow-list.</summary>
    /// <remarks>
    /// A missing entry would throw at validation time for any view that sorts,
    /// which is a runtime failure for something knowable at compile time.
    /// </remarks>
    [Fact]
    public void EveryTarget_HasSortableFields()
    {
        foreach (SavedViewTarget target in Enum.GetValues<SavedViewTarget>())
        {
            Assert.True(
                SavedViewDefinition.SortableFields.ContainsKey(target),
                $"{target} has no sortable-field allow-list.");

            Assert.NotEmpty(SavedViewDefinition.SortableFields[target]);
        }
    }

    /// <summary>A sort field outside the allow-list never reaches a query.</summary>
    [Fact]
    public void AnUnknownSortField_IsRefused()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Talent,
            new SavedViewFilters(),
            new SavedViewSort("; DROP TABLE talent_profiles", SavedViewSortDirection.Ascending));

        Assert.Throws<DomainException>(definition.Validate);
    }

    /// <summary>Representation filters are meaningless on the M2 targets and are refused.</summary>
    [Theory]
    [InlineData(SavedViewTarget.People)]
    [InlineData(SavedViewTarget.Companies)]
    [InlineData(SavedViewTarget.Tasks)]
    public void RepresentationFilters_AreRefusedOnOlderTargets(SavedViewTarget target)
    {
        SavedViewDefinition definition = new(
            2,
            target,
            new SavedViewFilters(Discipline: "Writer"));

        Assert.Throws<DomainException>(definition.Validate);
    }

    /// <summary>Prospect filters are meaningless on a talent view and are refused.</summary>
    [Fact]
    public void ProspectFilters_AreRefusedOnATalentView()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Talent,
            new SavedViewFilters(ProspectStage: "Courting"));

        Assert.Throws<DomainException>(definition.Validate);
    }

    /// <summary>
    /// Asking for current and former clients at once is refused.
    /// </summary>
    /// <remarks>
    /// The pair is contradictory and would return nothing, which reads as a broken
    /// view rather than an impossible question.
    /// </remarks>
    [Fact]
    public void CurrentAndFormerTogether_IsRefused()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Talent,
            new SavedViewFilters(ClientsOnly: true, FormerClientsOnly: true));

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void AnImplausibleFollowUpWindow_IsRefused()
    {
        SavedViewDefinition definition = new(
            2,
            SavedViewTarget.Prospects,
            new SavedViewFilters(FollowUpWithinDays: -1));

        Assert.Throws<DomainException>(definition.Validate);
    }

    /// <summary>The practical representation views the milestone asks for all validate.</summary>
    [Fact]
    public void ThePracticalRepresentationViews_AreExpressible()
    {
        SavedViewDefinition[] views =
        [
            // Active clients.
            new(2, SavedViewTarget.Talent, new SavedViewFilters(ClientsOnly: true)),

            // Clients by discipline.
            new(2, SavedViewTarget.Talent, new SavedViewFilters(ClientsOnly: true, Discipline: "Director")),

            // Clients by represented area.
            new(2, SavedViewTarget.Talent, new SavedViewFilters(ClientsOnly: true, ScopeArea: "Television")),

            // Clients by lead representative.
            new(2, SavedViewTarget.Talent, new SavedViewFilters(ClientsOnly: true, LeadUserId: Guid.NewGuid())),

            // Former clients.
            new(2, SavedViewTarget.Talent, new SavedViewFilters(FormerClientsOnly: true)),

            // Recently signed, by ordering rather than by an invented score.
            new(
                2,
                SavedViewTarget.Talent,
                new SavedViewFilters(ClientsOnly: true),
                new SavedViewSort("UpdatedAt", SavedViewSortDirection.Descending)),

            // Prospects by stage.
            new(2, SavedViewTarget.Prospects, new SavedViewFilters(ProspectStage: "Courting")),

            // Prospects by internal owner.
            new(2, SavedViewTarget.Prospects, new SavedViewFilters(OwnerUserId: Guid.NewGuid())),

            // Prospects awaiting follow-up.
            new(2, SavedViewTarget.Prospects, new SavedViewFilters(FollowUpWithinDays: 7)),
        ];

        foreach (SavedViewDefinition definition in views)
        {
            definition.Validate();
        }
    }
}
