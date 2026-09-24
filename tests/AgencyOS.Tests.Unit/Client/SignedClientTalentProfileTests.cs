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
    /// A talent read still in flight is not said as a failed one, and does not offer Create.
    /// </summary>
    /// <remarks>
    /// Held deterministically on a gate. At <c>218c6fe</c> the signed client's state was
    /// one "unknown" for both "not read yet" and "the read failed", so while the read was
    /// still running the surface said it could not be checked.
    /// </remarks>
    [Fact]
    public async Task AReadInFlight_IsNotSaidAsFailed()
    {
        FakeAgencyOsApi api = new();
        ProspectResponse prospect = Prospect("Ada Reyes");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        api.Prospects.Add(prospect);
        api.TalentReadGates[prospect.PersonId] = gate;

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Task<RepresentationResponse?> converting = viewModel.ConvertAsync(
            viewModel.Prospects[0], new DateOnly(2026, 9, 24), Guid.NewGuid(), ["Film"]);

        // Signed; the talent read is outstanding.
        Assert.False(converting.IsCompleted);
        Assert.Equal(prospect.PersonId, viewModel.Signed?.PersonId);

        ProfileNotice during = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.Equal(TalentProfileState.Checking, viewModel.ProfileState);
        Assert.Equal("Checking whether Ada Reyes already has a talent profile.", during.Message);
        Assert.DoesNotContain("could not be checked", during.Message, StringComparison.Ordinal);
        Assert.False(during.IsError);
        Assert.False(viewModel.CanCreateTalentProfile);

        gate.SetResult();

        Assert.NotNull(await converting);
        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);
    }

    private const string RefusedCreate =
        "You do not have permission to create talent profiles. O'Brien D'Angelo-Smith is still a client.";

    /// <summary>
    /// A client signed as Absent, whose explicit create was then refused, and a fresh
    /// talent read started and held in flight.
    /// </summary>
    private static async Task<(FakeAgencyOsApi Api, ProspectsViewModel ViewModel, ProspectResponse Prospect, TaskCompletionSource Gate, Task Read)>
        RefusedThenRereadingAsync()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);

        api.Failures.Enqueue(new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied"));
        await viewModel.CreateTalentProfileAsync("Unknown");

        Assert.Equal(RefusedCreate, viewModel.ProfileNotice?.Message);
        Assert.True(viewModel.ProfileNotice?.IsError);

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        api.TalentReadGates[prospect.PersonId] = gate;

        Task read = viewModel.CheckTalentProfileAsync();

        return (api, viewModel, prospect, gate, read);
    }

    /// <summary>
    /// A: a fresh read after a refused create shows the read, not the old refusal.
    /// </summary>
    [Fact]
    public async Task AfterARefusedCreate_AFreshReadInFlight_SaysChecking()
    {
        (_, ProspectsViewModel viewModel, _, TaskCompletionSource gate, Task read) = await RefusedThenRereadingAsync();

        Assert.Equal(TalentProfileState.Checking, viewModel.ProfileState);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.Equal("Checking whether O'Brien D'Angelo-Smith already has a talent profile.", notice.Message);
        Assert.Equal("Talent profile", notice.Title);
        Assert.False(notice.IsError);
        Assert.DoesNotContain("permission", notice.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanCreateTalentProfile);

        gate.SetResult();
        await read;
    }

    /// <summary>B: the fresh read finds a profile, and says so, not the old refusal.</summary>
    [Fact]
    public async Task AfterARefusedCreate_AFreshReadFindingAProfile_SaysPresent()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect, TaskCompletionSource gate, Task read) =
            await RefusedThenRereadingAsync();

        // Somebody with the permission created it meanwhile.
        await api.CreateTalentProfileAsync(new CreateTalentProfileRequest(prospect.PersonId, "Established"));

        gate.SetResult();
        await read;

        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.Equal("O'Brien D'Angelo-Smith has a talent profile and appears in Talent.", viewModel.ProfileNotice?.Message);
        Assert.False(viewModel.ProfileNotice?.IsError);
    }

    /// <summary>C: the fresh read finds none, and gives the next step, not the old refusal.</summary>
    [Fact]
    public async Task AfterARefusedCreate_AFreshReadFindingNone_SaysTheNextStep()
    {
        (_, ProspectsViewModel viewModel, _, TaskCompletionSource gate, Task read) = await RefusedThenRereadingAsync();

        gate.SetResult();
        await read;

        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.Equal("Next: create a talent profile", notice.Title);
        Assert.Equal(
            "O'Brien D'Angelo-Smith is a client, but does not appear in Talent or in talent pursuits until they have a talent profile.",
            notice.Message);
        Assert.False(notice.IsError);
    }

    /// <summary>D: the fresh read cannot answer, and says that, not the old refusal.</summary>
    [Fact]
    public async Task AfterARefusedCreate_AFreshReadThatFails_SaysUnavailable()
    {
        (_, ProspectsViewModel viewModel, _, TaskCompletionSource gate, Task read) = await RefusedThenRereadingAsync();

        gate.SetException(new AgencyOsApiException(HttpStatusCode.ServiceUnavailable, "Unavailable"));
        await read;

        Assert.Equal(TalentProfileState.Unavailable, viewModel.ProfileState);
        Assert.Equal(
            "Whether O'Brien D'Angelo-Smith has a talent profile could not be checked. Create one if they need to appear in Talent.",
            viewModel.ProfileNotice?.Message);
        Assert.False(viewModel.ProfileNotice?.IsError);
    }

    /// <summary>
    /// A successful create is said at once, and a read deliberately made afterwards
    /// then owns the status.
    /// </summary>
    [Fact]
    public async Task AfterASuccessfulCreate_ALaterReadOwnsTheStatus()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        await viewModel.CreateTalentProfileAsync("Emerging");

        Assert.Equal("Talent profile created. O'Brien D'Angelo-Smith now appears in Talent.", viewModel.ProfileNotice?.Message);
        Assert.True(viewModel.ProfileNotice?.IsSuccess);

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        api.TalentReadGates[prospect.PersonId] = gate;

        Task read = viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Checking, viewModel.ProfileState);
        Assert.Equal("Checking whether O'Brien D'Angelo-Smith already has a talent profile.", viewModel.ProfileNotice?.Message);
        Assert.False(viewModel.ProfileNotice?.IsSuccess);

        gate.SetResult();
        await read;

        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.Equal("O'Brien D'Angelo-Smith has a talent profile and appears in Talent.", viewModel.ProfileNotice?.Message);
    }

    /// <summary>
    /// A newly assigned signed client is NotChecked until a read starts, and says so
    /// without claiming a failure or an absence.
    /// </summary>
    [Fact]
    public async Task ANewlySignedClient_IsNotChecked_UntilARead()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Converted"));

        ProspectsViewModel viewModel = new(api) { OpenOnly = false };

        await viewModel.LoadAsync();

        viewModel.Selected = viewModel.Prospects[0];

        Assert.Equal(TalentProfileState.NotChecked, viewModel.ProfileState);
        Assert.False(viewModel.CanCreateTalentProfile);

        ProfileNotice notice = Assert.IsType<ProfileNotice>(viewModel.ProfileNotice);

        Assert.Equal("Ada Reyes is a client. Whether they have a talent profile has not been checked yet.", notice.Message);
        Assert.False(notice.IsError);
    }

    /// <summary>A read that finds the profile makes it Present, and Create is not offered.</summary>
    [Fact]
    public async Task ASuccessfulRead_IsPresent()
    {
        FakeAgencyOsApi api = new();
        ProspectResponse converted = Prospect("Ada Reyes", "Converted");

        api.Prospects.Add(converted);
        await api.CreateTalentProfileAsync(new CreateTalentProfileRequest(converted.PersonId, "Established"));

        ProspectsViewModel viewModel = new(api) { OpenOnly = false };

        await viewModel.LoadAsync();

        viewModel.Selected = viewModel.Prospects[0];
        await viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.False(viewModel.CanCreateTalentProfile);
        Assert.Equal("Ada Reyes has a talent profile and appears in Talent.", viewModel.ProfileNotice?.Message);
    }

    /// <summary>
    /// Where the read could not answer, Create stays offered; if a profile existed after
    /// all, the server's refusal makes the state Present and nothing is duplicated.
    /// </summary>
    [Fact]
    public async Task FromUnavailable_AConflictResolvesToPresent_WithoutADuplicate()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, ProspectResponse prospect) = await SignAsync();

        await api.CreateTalentProfileAsync(new CreateTalentProfileRequest(prospect.PersonId, "Established"));

        api.NextFailure = new AgencyOsApiException(HttpStatusCode.ServiceUnavailable, "Unavailable");
        await viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Unavailable, viewModel.ProfileState);
        Assert.True(viewModel.CanCreateTalentProfile);

        Assert.True(await viewModel.CreateTalentProfileAsync("Unknown"));

        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.Equal("Established", Assert.Single(api.Talent).CareerStage);
    }

    /// <summary>A refused create changes neither Absent nor Unavailable into anything else.</summary>
    [Fact]
    public async Task ARefusedCreate_KeepsTheStateItHad()
    {
        (FakeAgencyOsApi api, ProspectsViewModel viewModel, _) = await SignAsync();

        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);

        api.Failures.Enqueue(new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied"));
        await viewModel.CreateTalentProfileAsync("Unknown");

        Assert.Equal(TalentProfileState.Absent, viewModel.ProfileState);

        api.NextFailure = new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied");
        await viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Unavailable, viewModel.ProfileState);

        api.Failures.Enqueue(new AgencyOsApiException(HttpStatusCode.InternalServerError, "Failed"));
        await viewModel.CreateTalentProfileAsync("Unknown");

        Assert.Equal(TalentProfileState.Unavailable, viewModel.ProfileState);
        Assert.Empty(api.Talent);
    }

    /// <summary>
    /// A read started for one client cannot overwrite the state of the client the
    /// operator selected after it.
    /// </summary>
    [Fact]
    public async Task AnOlderRead_CannotOverwriteANewerClient()
    {
        FakeAgencyOsApi api = new();
        ProspectResponse first = Prospect("Ada Reyes", "Converted");
        ProspectResponse second = Prospect("Bo Ferreira", "Converted");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        api.Prospects.Add(first);
        api.Prospects.Add(second);
        api.TalentReadGates[first.PersonId] = gate;

        // The second already has a profile; the first does not.
        await api.CreateTalentProfileAsync(new CreateTalentProfileRequest(second.PersonId, "Established"));

        ProspectsViewModel viewModel = new(api) { OpenOnly = false };

        await viewModel.LoadAsync();

        viewModel.Selected = viewModel.Prospects.Single(x => x.PersonId == first.PersonId);
        Task older = viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Checking, viewModel.ProfileState);

        viewModel.Selected = viewModel.Prospects.Single(x => x.PersonId == second.PersonId);
        await viewModel.CheckTalentProfileAsync();

        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);

        // The first read now answers "no profile" - for a client no longer shown.
        gate.SetResult();
        await older;

        Assert.Equal(second.PersonId, viewModel.Signed?.PersonId);
        Assert.Equal(TalentProfileState.Present, viewModel.ProfileState);
        Assert.False(viewModel.CanCreateTalentProfile);
    }

    /// <summary>
    /// A talent read that cannot answer leaves the question unknown, never "missing".
    /// </summary>
    [Fact]
    public async Task AnUnansweredRead_IsUnavailable_NotAbsent()
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

        Assert.Equal(TalentProfileState.Unavailable, viewModel.ProfileState);
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
