using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A client signed on the prospects surface, and the talent profile that is its own step.
/// </summary>
/// <remarks>
/// <para>
/// The final-candidate RC at <c>f29a2ee</c> stopped at C7 step 2: a prospect converted
/// through the Windows client became an active client, and then could not appear in
/// Talent or be the subject of a talent pursuit, because both are built from talent
/// profiles and signing does not create one.
/// </para>
/// <para>
/// Owner decision C kept the two separate: conversion must not create a profile, and
/// creating one is an explicit operator action. These hold the client side of that:
/// the action is offered where the client was signed, it creates exactly one profile
/// for exactly that person, and a refusal changes nothing.
/// </para>
/// </remarks>
public sealed class SignedClientTalentProfileTests
{
    private static ProspectResponse Prospect(string name, string stage = "Courting") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            stage,
            Guid.NewGuid(),
            "Owner",
            null,
            null,
            new DateOnly(2026, 1, 1),
            null,
            null,
            DateTimeOffset.UtcNow,
            1);

    private static async Task<(FakeAgencyOsApi Api, ProspectsViewModel ViewModel, ProspectResponse Prospect)> SignAsync(
        string name = "O'Brien D'Angelo-Smith")
    {
        FakeAgencyOsApi api = new();
        ProspectResponse prospect = Prospect(name);

        api.Prospects.Add(prospect);

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        RepresentationResponse? representation = await viewModel.ConvertAsync(
            viewModel.Prospects[0], new DateOnly(2026, 9, 24), Guid.NewGuid(), ["Film"]);

        Assert.NotNull(representation);

        return (api, viewModel, prospect);
    }

    /// <summary>
    /// Signing alone creates no talent profile, and the operator is told the next step.
    /// </summary>
    [Fact]
    public async Task SigningAClient_CreatesNoTalentProfile_AndOffersTheNextStep()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        Assert.Equal("Active", Assert.Single(api.Conversions.Values).Status);
        Assert.Empty(api.Talent);
        Assert.Empty(await api.ListTalentAsync());

        Assert.Equal(prospect.PersonId, viewModel.Signed?.PersonId);
        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.Equal("Next: create a talent profile", notice.Title);
        Assert.Equal(
            "O'Brien D'Angelo-Smith is a client, but does not appear in Talent or in talent pursuits until they have a talent profile.",
            notice.Message);
        Assert.False(notice.IsError);
    }

    /// <summary>
    /// The explicit action creates exactly one profile, for the signed person, with the chosen stage.
    /// </summary>
    [Fact]
    public async Task CreatingTheProfile_MakesExactlyOne_ForThatPerson()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        Assert.True(await viewModel.CreateTalentProfileAsync("Emerging"));

        TalentSummaryResponse profile = Assert.Single(api.Talent);

        Assert.Equal(prospect.PersonId, profile.PersonId);
        Assert.Equal("Emerging", profile.CareerStage);
        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.False(viewModel.CanCreateTalentProfile);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.True(notice.IsSuccess);
        Assert.Equal("Talent profile created. O'Brien D'Angelo-Smith now appears in Talent.", notice.Message);

        // Offered no longer, and a second request makes nothing.
        Assert.False(await viewModel.CreateTalentProfileAsync("Veteran"));
        Assert.Single(api.Talent);
    }

    /// <summary>
    /// After the explicit step the same person is in Talent, as a client, and is a
    /// talent-pursuit subject - the C7 story the RC could not continue.
    /// </summary>
    [Fact]
    public async Task AfterTheProfile_ThePersonIsInTalentAndIsATalentPursuitSubject()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        // Before: signed, and in neither.
        Assert.DoesNotContain(await api.ListTalentAsync(), x => x.PersonId == prospect.PersonId);
        Assert.Empty(EntityChoice.ForTalent(await api.ListTalentAsync()));

        await viewModel.CreateTalentProfileAsync("Unknown");

        TalentListViewModel talent = new(api);

        await talent.LoadAsync();

        TalentSummaryResponse listed = Assert.Single(talent.Talent);

        Assert.Equal(prospect.PersonId, listed.PersonId);
        Assert.True(listed.IsClient);
        Assert.Equal(1, talent.ClientCount);

        // The talent-engagement subject picker is built from the same roster.
        EntityChoice subject = Assert.Single(EntityChoice.ForTalent(await api.ListTalentAsync()));

        Assert.Equal(listed.Id, subject.Id);
        Assert.StartsWith("O'Brien D'Angelo-Smith", subject.Label, StringComparison.Ordinal);

        // The representation is the one conversion created, unchanged.
        Assert.Equal("Active", Assert.Single(api.Conversions.Values).Status);
    }

    /// <summary>A refusal creates nothing, leaves the client signed, and says both.</summary>
    [Fact]
    public async Task ARefusal_CreatesNothing_AndLeavesTheClientSigned()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, _) = await SignAsync();

        api.Failures.Enqueue(new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied"));

        Assert.False(await viewModel.CreateTalentProfileAsync("Unknown"));

        Assert.Empty(api.Talent);
        Assert.Equal("Active", Assert.Single(api.Conversions.Values).Status);
        Assert.NotEqual(TalentProfileState.Present, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.True(notice.IsError);
        Assert.Equal("Talent profile not created", notice.Title);
        Assert.Equal(
            "You do not have permission to create talent profiles. O'Brien D'Angelo-Smith is still a client.",
            notice.Message);

        // The list's own error state is not borrowed for it.
        Assert.False(viewModel.HasError);
    }

    /// <summary>A person who already has a profile gets no second one, and is told so.</summary>
    [Fact]
    public async Task AnExistingProfile_IsNotDuplicated()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        // Created elsewhere between the read and the request.
        await api.CreateTalentProfileAsync(new CreateTalentProfileRequest(prospect.PersonId, "Established"));

        Assert.True(await viewModel.CreateTalentProfileAsync("Unknown"));

        Assert.Equal("Established", Assert.Single(api.Talent).CareerStage);
        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.Equal(
            "O'Brien D'Angelo-Smith already has a talent profile, so none was created.",
            viewModel.ProfileNotice?.Message);
    }

    /// <summary>
    /// A talent read that cannot answer leaves the question unknown, never "missing".
    /// </summary>
    [Fact]
    public async Task AnUnansweredRead_IsUnknown_NotAbsent()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.NotNull(await viewModel.ConvertAsync(
            viewModel.Prospects[0], new DateOnly(2026, 9, 24), Guid.NewGuid(), ["Film"]));

        // The talent read is then refused - no talent read permission, say.
        api.NextFailure = new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied");

        await viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Unknown, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);
        Assert.Equal(
            "Whether Ada Reyes has a talent profile could not be checked. Create one if they need to appear in Talent.",
            viewModel.ProfileNotice?.Message);
    }

    /// <summary>
    /// A converted pursuit selected later offers the same step; a live one does not.
    /// </summary>
    [Fact]
    public async Task SelectingAConvertedPursuit_OffersTheStep_AndALiveOneDoesNot()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Converted"));
        api.Prospects.Add(Prospect("Bo Ferreira", "Courting"));

        ProspectsViewModel viewModel = new(api) { OpenOnly = false };

        await viewModel.LoadAsync();

        viewModel.Selected = viewModel.Prospects.Single(x => x.Stage == "Converted");
        await viewModel.CheckTalentProfileAsync();

        Assert.Equal("Ada Reyes", viewModel.Signed?.DisplayName);
        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);

        viewModel.Selected = viewModel.Prospects.Single(x => x.Stage == "Courting");

        Assert.Null(viewModel.Signed);
        Assert.Null(viewModel.ProfileNotice);
        Assert.False(viewModel.CanCreateTalentProfile);
    }

    /// <summary>
    /// Clearing the selection after signing - the list refresh drops the converted row -
    /// keeps the signed client and the offered step.
    /// </summary>
    [Fact]
    public async Task ARefreshedList_KeepsTheClientJustSigned()
    {
        (_, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        viewModel.Selected = null;

        Assert.Equal(prospect.PersonId, viewModel.Signed?.PersonId);
        Assert.True(viewModel.CanCreateTalentProfile);
    }
}
