using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That the material pickers ask for the materials of a person, not of a profile.
/// </summary>
/// <remarks>
/// <c>AOS-R002-023</c>, found by Repair Wave 003A.1 while completing Repair Wave
/// 003B's runtime verification. A pursuit's talent subject carries a talent
/// <em>profile</em> identifier; materials are listed by <em>person</em>. 003B asked
/// with the profile's, the server answered with nothing, and the pickers in
/// <c>RecordPitchDialog</c> and <c>RecordSubmissionDialog</c> offered only
/// "nothing" for every pursuit.
/// </remarks>
public sealed class SubjectMaterialsTests
{
    private static readonly Guid Profile = Guid.CreateVersion7();
    private static readonly Guid Person = Guid.CreateVersion7();

    /// <summary>A talent subject resolves to the person behind the profile.</summary>
    [Fact]
    public void ATalentSubjectIsAskedAboutByPerson()
    {
        IReadOnlyList<Guid> people = SubjectMaterials.People(
            [Subject("TalentProfile", Profile)],
            [Talent(Profile, Person)]);

        Guid only = Assert.Single(people);

        Assert.Equal(Person, only);
        Assert.NotEqual(Profile, only);
    }

    /// <summary>Subjects that are not somebody have no materials to ask about.</summary>
    [Theory]
    [InlineData("Project")]
    [InlineData("Package")]
    [InlineData("ProjectRole")]
    public void ASubjectThatIsNotSomebodyIsSkipped(string kind)
    {
        OpportunitySubjectResponse[] subjects = [Subject(kind, Profile)];

        Assert.False(SubjectMaterials.NamesTalent(subjects));
        Assert.Empty(SubjectMaterials.People(subjects, [Talent(Profile, Person)]));
    }

    /// <summary>A profile the roster does not list is left out, not guessed at.</summary>
    /// <remarks>
    /// Guessing would mean asking with the profile identifier again, which is the
    /// defect.
    /// </remarks>
    [Fact]
    public void AProfileTheRosterDoesNotListIsLeftOut()
    {
        Assert.Empty(SubjectMaterials.People(
            [Subject("TalentProfile", Profile)],
            [Talent(Guid.CreateVersion7(), Guid.CreateVersion7())]));
    }

    /// <summary>A person named twice is asked about once.</summary>
    [Fact]
    public void APersonNamedTwiceIsAskedAboutOnce()
    {
        IReadOnlyList<Guid> people = SubjectMaterials.People(
            [Subject("TalentProfile", Profile), Subject("TalentProfile", Profile)],
            [Talent(Profile, Person)]);

        Assert.Single(people);
    }

    /// <summary>A pursuit with talent among its subjects is recognised as one.</summary>
    [Fact]
    public void APursuitWithTalentNamesTalent() =>
        Assert.True(SubjectMaterials.NamesTalent(
            [Subject("Project", Guid.CreateVersion7()), Subject("TalentProfile", Profile)]));

    private static OpportunitySubjectResponse Subject(string kind, Guid target) =>
        new(Guid.CreateVersion7(), kind, "Primary", target, "Ada Sallow", null, null);

    private static TalentSummaryResponse Talent(Guid profile, Guid person) =>
        new(profile, person, "Ada Sallow", "Established", [], null, true, null, null, [],
            DateTimeOffset.UtcNow, 1);
}
