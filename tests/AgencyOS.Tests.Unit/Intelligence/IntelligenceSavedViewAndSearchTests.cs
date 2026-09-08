using AgencyOS.Application.Authorization;
using AgencyOS.Application.SavedViews;
using AgencyOS.Application.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.SavedViews;
using Xunit;

namespace AgencyOS.Tests.Unit.Intelligence;

/// <summary>
/// What a saved view of intelligence may ask for, and what it may not.
/// </summary>
/// <remarks>
/// A saved view is a query somebody else may later run. Every milestone since M7
/// has kept the things that reveal a secret out of the filter list for that reason:
/// no compensation figure in M7, no balance in M9, no document text in M10. M11's
/// version of the same rule is the probability (§31, ADR-0030).
/// </remarks>
public sealed class IntelligenceSavedViewTests
{
    [Theory]
    [InlineData(SavedViewTarget.Signals)]
    [InlineData(SavedViewTarget.Theses)]
    [InlineData(SavedViewTarget.Predictions)]
    [InlineData(SavedViewTarget.TalentRadar)]
    public void AnIntelligenceTarget_ArrivedInVersionNine(SavedViewTarget target)
    {
        Assert.Equal(9, SavedViewDefinition.TargetIntroducedIn[target]);

        new SavedViewDefinition(9, target, new SavedViewFilters()).Validate();

        // A document claiming version 8 while naming a version 9 target is
        // internally inconsistent, and accepting it would make the version number
        // describe nothing.
        Assert.Throws<DomainException>(
            new SavedViewDefinition(8, target, new SavedViewFilters()).Validate);
    }

    /// <summary>
    /// No filter narrows by probability.
    /// </summary>
    /// <remarks>
    /// The direct endpoint offers one, because it runs under the caller's own
    /// grants and answers only them. A saved view is a query somebody else may run,
    /// and a predicate reading "probability above eighty percent" would tell its
    /// reader the forecast whether or not they may open the prediction.
    /// </remarks>
    [Fact]
    public void NoSavedViewFilter_NarrowsByAForecast()
    {
        string[] names =
        [
            .. typeof(SavedViewFilters)
                .GetProperties()
                .Select(x => x.Name),
        ];

        Assert.DoesNotContain(names, x => x.Contains("Probability", StringComparison.Ordinal));
        Assert.DoesNotContain(names, x => x.Contains("Brier", StringComparison.Ordinal));
        Assert.DoesNotContain(names, x => x.Contains("Calibration", StringComparison.Ordinal));
    }

    /// <summary>Nothing sorts by a forecast either, for the same reason.</summary>
    [Fact]
    public void NoIntelligenceTarget_SortsByAForecast()
    {
        foreach (SavedViewTarget target in new[]
        {
            SavedViewTarget.Signals,
            SavedViewTarget.Theses,
            SavedViewTarget.Predictions,
            SavedViewTarget.TalentRadar,
        })
        {
            IReadOnlySet<string> fields = SavedViewDefinition.SortableFields[target];

            Assert.NotEmpty(fields);
            Assert.DoesNotContain(fields, x => x.Contains("Probability", StringComparison.Ordinal));
            Assert.DoesNotContain(fields, x => x.Contains("Brier", StringComparison.Ordinal));

            // Nor by classification: ordering a list by sensitivity tells the reader
            // which rows are the protected ones, which is most of what the
            // classification protects.
            Assert.DoesNotContain(fields, x => x.Contains("Sensitivity", StringComparison.Ordinal));
        }
    }

    /// <summary>Every intelligence target is gated by the intelligence floor.</summary>
    [Theory]
    [InlineData(SavedViewTarget.Signals)]
    [InlineData(SavedViewTarget.Theses)]
    [InlineData(SavedViewTarget.Predictions)]
    [InlineData(SavedViewTarget.TalentRadar)]
    public void AnIntelligenceView_RequiresTheIntelligenceGrant(SavedViewTarget target)
    {
        Assert.Equal(
            Permission.IntelligenceRead,
            SavedViewService.RequiredPermissions[target]);
    }

    /// <summary>A filter from another milestone is refused on an intelligence view.</summary>
    /// <remarks>
    /// The accept-list is per target. Without it a view could carry a finance filter
    /// on a signals query, which would be meaningless at best and a way to probe
    /// another surface at worst.
    /// </remarks>
    [Fact]
    public void AForeignFilter_IsRefusedOnAnIntelligenceView()
    {
        SavedViewDefinition definition = new(
            9,
            SavedViewTarget.Signals,
            new SavedViewFilters(ReceivableStatus: "Overdue"));

        Assert.Throws<DomainException>(definition.Validate);
    }

    /// <summary>The intelligence filters are accepted on the targets that use them.</summary>
    [Fact]
    public void TheIntelligenceFilters_AreAcceptedWhereTheyBelong()
    {
        new SavedViewDefinition(
                9,
                SavedViewTarget.Signals,
                new SavedViewFilters(
                    SignalKind: "PersonnelMove",
                    SignalVerification: "Disputed",
                    IntelligenceSensitivity: "Internal",
                    SubjectKind: "Person",
                    SubjectId: Guid.CreateVersion7()))
            .Validate();

        new SavedViewDefinition(
                9,
                SavedViewTarget.Predictions,
                new SavedViewFilters(
                    PredictionStatus: "AwaitingResolution",
                    PredictionOutcome: "Unresolvable",
                    OwnerUserId: Guid.CreateVersion7()))
            .Validate();

        new SavedViewDefinition(
                9,
                SavedViewTarget.TalentRadar,
                new SavedViewFilters(RadarStatus: "ReadyForReview", RadarPriority: "High"))
            .Validate();
    }
}

/// <summary>
/// What global search may find.
/// </summary>
/// <remarks>
/// The palette shows results beside people and projects, and its result count is
/// visible before anything is opened. That makes it the wrong surface for anything
/// above Internal — the count alone would answer "is there something about this
/// person" (§28, ADR-0030).
/// </remarks>
public sealed class IntelligenceSearchTests
{
    /// <summary>Signals are gated by the intelligence floor, on their own.</summary>
    /// <remarks>
    /// Holding documents, contracts or finance access confers nothing here, and
    /// holding the elevated intelligence grant widens nothing: the branch is
    /// narrowed to Internal claims for everybody.
    /// </remarks>
    [Fact]
    public void SearchingSignals_RequiresTheIntelligenceGrant()
    {
        Assert.Equal(
            Permission.IntelligenceRead,
            SearchService.RequiredPermissions[SearchEntityType.Signal]);
    }

    /// <summary>Every searchable type says which permission it needs.</summary>
    /// <remarks>
    /// The table this checks is a parallel map. A type missing from it is silently
    /// unsearchable rather than loudly wrong, which is the worse failure.
    /// </remarks>
    [Fact]
    public void EverySearchableType_NamesItsPermission()
    {
        foreach (SearchEntityType type in Enum.GetValues<SearchEntityType>())
        {
            Assert.True(
                SearchService.RequiredPermissions.ContainsKey(type),
                $"{type} does not say which permission it requires.");

            Assert.Contains(SearchService.RequiredPermissions[type], Permission.All);
        }
    }
}
