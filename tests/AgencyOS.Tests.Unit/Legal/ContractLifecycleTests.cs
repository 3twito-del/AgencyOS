using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Legal;

/// <summary>
/// The contract state machine, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// All 64 status/transition pairs, against a table written here rather than read
/// from the kernel. The illegal ones carry the weight: a contract that could be
/// marked executed without a signature, or resurrected after abandonment, would
/// let the system assert legal facts nobody established (ADR-0022).
/// </remarks>
public sealed class ContractTransitionTests
{
    private static readonly Dictionary<(ContractStatus From, ContractTransition Trigger), ContractStatus>
        Legal = new()
        {
            [(ContractStatus.Draft, ContractTransition.SentForReview)] = ContractStatus.UnderReview,
            [(ContractStatus.Draft, ContractTransition.ApprovedForSignature)] =
                ContractStatus.ApprovedForExecution,
            [(ContractStatus.Draft, ContractTransition.DraftingAbandoned)] = ContractStatus.Abandoned,

            [(ContractStatus.UnderReview, ContractTransition.ReturnedToDrafting)] = ContractStatus.Draft,
            [(ContractStatus.UnderReview, ContractTransition.ApprovedForSignature)] =
                ContractStatus.ApprovedForExecution,
            [(ContractStatus.UnderReview, ContractTransition.DraftingAbandoned)] = ContractStatus.Abandoned,

            [(ContractStatus.ApprovedForExecution, ContractTransition.ReturnedToDrafting)] =
                ContractStatus.Draft,
            [(ContractStatus.ApprovedForExecution, ContractTransition.SentForReview)] =
                ContractStatus.UnderReview,
            [(ContractStatus.ApprovedForExecution, ContractTransition.SignatureRecorded)] =
                ContractStatus.PartiallyExecuted,
            [(ContractStatus.ApprovedForExecution, ContractTransition.ExecutionCompleted)] =
                ContractStatus.Executed,
            [(ContractStatus.ApprovedForExecution, ContractTransition.DraftingAbandoned)] =
                ContractStatus.Abandoned,

            [(ContractStatus.PartiallyExecuted, ContractTransition.SignatureRecorded)] =
                ContractStatus.PartiallyExecuted,
            [(ContractStatus.PartiallyExecuted, ContractTransition.ExecutionCompleted)] =
                ContractStatus.Executed,
            [(ContractStatus.PartiallyExecuted, ContractTransition.ReturnedToDrafting)] =
                ContractStatus.Draft,
            [(ContractStatus.PartiallyExecuted, ContractTransition.DraftingAbandoned)] =
                ContractStatus.Abandoned,

            [(ContractStatus.Executed, ContractTransition.ReplacedByAnother)] = ContractStatus.Superseded,
            [(ContractStatus.Executed, ContractTransition.TerminationRecorded)] = ContractStatus.Terminated,
        };

    public static TheoryData<ContractStatus, ContractTransition> EveryPair()
    {
        TheoryData<ContractStatus, ContractTransition> data = [];

        foreach (ContractStatus status in Enum.GetValues<ContractStatus>())
        {
            foreach (ContractTransition transition in Enum.GetValues<ContractTransition>())
            {
                data.Add(status, transition);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryTransition_MatchesThePublishedTable(
        ContractStatus from,
        ContractTransition trigger)
    {
        bool expected = Legal.ContainsKey((from, trigger));

        Assert.Equal(expected, Contract.Permits(from, trigger));

        if (expected)
        {
            Assert.Contains(Legal[(from, trigger)], Contract.ReachableFrom(from));
        }
    }

    [Fact]
    public void TheTableIsCompletelyEnumerated()
    {
        int pairs = Enum.GetValues<ContractStatus>().Length * Enum.GetValues<ContractTransition>().Length;

        Assert.Equal(64, pairs);
        Assert.Equal(17, Legal.Count);
    }

    [Theory]
    [InlineData(ContractStatus.Abandoned)]
    [InlineData(ContractStatus.Superseded)]
    [InlineData(ContractStatus.Terminated)]
    public void FinishedInstruments_AcceptNothing(ContractStatus terminal)
    {
        Assert.Empty(Contract.ReachableFrom(terminal));
        Assert.Contains(terminal, Contract.TerminalStatuses);
    }

    /// <summary>
    /// The invariant the milestone rests on: nothing a caller can ask for reaches
    /// execution. It follows from signatures or it does not happen.
    /// </summary>
    [Theory]
    [InlineData(ContractTransition.SentForReview, true)]
    [InlineData(ContractTransition.ReturnedToDrafting, true)]
    [InlineData(ContractTransition.ApprovedForSignature, true)]
    [InlineData(ContractTransition.DraftingAbandoned, true)]
    [InlineData(ContractTransition.ReplacedByAnother, true)]
    [InlineData(ContractTransition.TerminationRecorded, true)]
    [InlineData(ContractTransition.SignatureRecorded, false)]
    [InlineData(ContractTransition.ExecutionCompleted, false)]
    public void ExecutionIsNotCallerRequestable(ContractTransition transition, bool requestable)
    {
        Assert.Equal(
            requestable, DealRules.IsCallerRequestableContractTrigger((int)transition));
    }

    [Fact]
    public void ExecutedIsReachableOnlyBySigning()
    {
        // Two states reach it - approved and partially executed - but through the
        // one trigger, which is what makes signing the only route.
        IEnumerable<ContractTransition> routes = Legal
            .Where(entry => entry.Value == ContractStatus.Executed)
            .Select(entry => entry.Key.Trigger)
            .Distinct();

        Assert.Equal(ContractTransition.ExecutionCompleted, Assert.Single(routes));
    }

    /// <summary>Signatures only make sense once the paper is cleared for them.</summary>
    [Theory]
    [InlineData(ContractStatus.Draft, false)]
    [InlineData(ContractStatus.UnderReview, false)]
    [InlineData(ContractStatus.ApprovedForExecution, true)]
    [InlineData(ContractStatus.PartiallyExecuted, true)]
    [InlineData(ContractStatus.Executed, false)]
    [InlineData(ContractStatus.Abandoned, false)]
    public void SignaturesAreAcceptedOnlyWhileTheContractIsBeingExecuted(
        ContractStatus status,
        bool accepts)
    {
        Assert.Equal(accepts, DealRules.ContractAcceptsSignatures((int)status));
    }

    /// <summary>
    /// The vocabulary stops at execution. Whether money moved is M9's question and
    /// a status here that named it would be a claim M8 cannot substantiate.
    /// </summary>
    [Fact]
    public void TheStatusVocabulary_StopsBeforeFinance()
    {
        string[] names = [.. Enum.GetNames<ContractStatus>()];

        foreach (string forbidden in new[]
        {
            "Paid", "Invoiced", "Commissioned", "Billed", "Settled", "Reconciled",
        })
        {
            Assert.DoesNotContain(forbidden, names);
        }

        Assert.Contains("Executed", names);
    }
}

/// <summary>The contract aggregate: parties, signatures, execution and effect.</summary>
public sealed class ContractTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly DealId Deal = DealId.New();
    private static readonly OfferId AcceptedOffer = OfferId.New();
    private static readonly DateTimeOffset Now = new(2027, 4, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewContract_OpensAsADraftAgainstItsAcceptedOffer()
    {
        Contract contract = Open();

        Assert.Equal(ContractStatus.Draft, contract.Status);
        Assert.Equal(Deal, contract.DealId);
        Assert.Equal(AcceptedOffer, contract.AcceptedOfferId);
        Assert.Equal(PrivilegeClass.Ordinary, contract.Privilege);
        Assert.False(contract.IsFullyExecuted);

        ContractEvent opened = Assert.Single(contract.Events);
        Assert.Equal(ContractEventKind.Opened, opened.Kind);
    }

    /// <summary>
    /// Recording signatures is the only way to execution, and the last one closes it.
    /// </summary>
    [Fact]
    public void ExecutionFollowsFromTheSignaturesItRequires()
    {
        Contract contract = Approved(out Guid artist, out Guid studio);

        contract.RecordSignature(
            artist, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version);

        Assert.Equal(ContractStatus.PartiallyExecuted, contract.Status);
        Assert.False(contract.IsFullyExecuted);
        Assert.Single(contract.OutstandingSignatories);
        Assert.Null(contract.ExecutedOn);

        contract.RecordSignature(
            studio, new DateOnly(2027, 3, 5), SignatureMethod.Electronic, Now, Actor, contract.Version);

        Assert.Equal(ContractStatus.Executed, contract.Status);
        Assert.True(contract.IsFullyExecuted);
        Assert.Empty(contract.OutstandingSignatories);

        // The day the last required signature was given, not the day it was typed.
        Assert.Equal(new DateOnly(2027, 3, 5), contract.ExecutedOn);
    }

    /// <summary>
    /// A contract nobody has to sign cannot become executed. Naming the signatories
    /// is the work, and asserting execution without it records a conclusion nobody
    /// reached.
    /// </summary>
    [Fact]
    public void AContractWithNoRequiredSignatory_CannotBecomeExecuted()
    {
        Contract contract = Open();

        Guid observer = contract
            .AddParty(
                ContractPartyRef.ForExternal("Outside counsel"),
                ContractPartyRole.Other,
                isRequiredSignatory: false,
                Now,
                contract.Version)
            .Id;

        contract.ChangeStatus(
            ContractTransition.ApprovedForSignature, Now, Actor, contract.Version);

        contract.RecordSignature(
            observer, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version);

        Assert.Equal(ContractStatus.PartiallyExecuted, contract.Status);
        Assert.False(contract.IsFullyExecuted);
        Assert.Empty(contract.RequiredSignatories);
    }

    [Fact]
    public void APartyCannotSignTwice()
    {
        Contract contract = Approved(out Guid artist, out _);

        contract.RecordSignature(
            artist, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version);

        Assert.Throws<DomainException>(() =>
            contract.RecordSignature(
                artist, new DateOnly(2027, 3, 3), SignatureMethod.Wet, Now, Actor, contract.Version));
    }

    [Fact]
    public void ASignatureCannotBeGivenInTheFuture()
    {
        Contract contract = Approved(out Guid artist, out _);

        Assert.Throws<DomainException>(() =>
            contract.RecordSignature(
                artist, new DateOnly(2099, 1, 1), SignatureMethod.Wet, Now, Actor, contract.Version));
    }

    [Fact]
    public void ADraftContract_RefusesSignatures()
    {
        Contract contract = Open();

        Guid artist = contract
            .AddParty(
                ContractPartyRef.ForExternal("Priya Raghunathan"),
                ContractPartyRole.Artist,
                isRequiredSignatory: true,
                Now,
                contract.Version)
            .Id;

        Assert.Throws<DomainException>(() =>
            contract.RecordSignature(
                artist, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version));
    }

    /// <summary>
    /// Execution and effectiveness are separate facts. A contract signed in March
    /// and effective from January is ordinary, and assuming otherwise would misdate
    /// every deadline measured from it.
    /// </summary>
    [Fact]
    public void EffectivenessIsRecordedSeparatelyFromExecution()
    {
        Contract contract = Approved(out Guid artist, out Guid studio);

        contract.RecordSignature(
            artist, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version);
        contract.RecordSignature(
            studio, new DateOnly(2027, 3, 5), SignatureMethod.Wet, Now, Actor, contract.Version);

        Assert.True(contract.IsFullyExecuted);

        // Executed, and not yet effective: nothing inferred one from the other.
        Assert.Null(contract.EffectiveOn);
        Assert.False(contract.IsEffectiveOn(new DateOnly(2027, 3, 5)));

        contract.RecordEffectiveDate(new DateOnly(2027, 1, 1), Now, Actor, contract.Version);

        // Retroactive effectiveness is accepted, because contracts do that.
        Assert.True(contract.IsEffectiveOn(new DateOnly(2027, 1, 1)));
        Assert.False(contract.IsEffectiveOn(new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void ATerminatedContract_StopsBeingEffective()
    {
        Contract contract = Executed();

        contract.RecordEffectiveDate(new DateOnly(2027, 1, 1), Now, Actor, contract.Version);

        contract.ChangeStatus(
            ContractTransition.TerminationRecorded,
            Now,
            Actor,
            contract.Version,
            reason: "Terminated by agreement.",
            terminatedOn: new DateOnly(2027, 6, 30));

        Assert.True(contract.IsEffectiveOn(new DateOnly(2027, 6, 30)));
        Assert.False(contract.IsEffectiveOn(new DateOnly(2027, 7, 1)));
    }

    [Fact]
    public void TerminatingWithoutADate_IsRefused()
    {
        Contract contract = Executed();

        Assert.Throws<DomainException>(() =>
            contract.ChangeStatus(
                ContractTransition.TerminationRecorded, Now, Actor, contract.Version));
    }

    /// <summary>Execution is refused as a status command, and the refusal says why.</summary>
    [Fact]
    public void AskingForExecutionDirectly_IsRefused()
    {
        Contract contract = Approved(out _, out _);

        DomainException failure = Assert.Throws<DomainException>(() =>
            contract.ChangeStatus(
                ContractTransition.ExecutionCompleted, Now, Actor, contract.Version));

        Assert.Contains("signatures", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APartyMustBeExactlyOneKindOfThing()
    {
        Contract contract = Open();

        Assert.Throws<DomainException>(() =>
            contract.AddParty(
                new ContractPartyRef(),
                ContractPartyRole.Artist,
                isRequiredSignatory: true,
                Now,
                contract.Version));

        Assert.Throws<DomainException>(() =>
            contract.AddParty(
                new ContractPartyRef(
                    PersonId: new Domain.People.PersonId(Guid.CreateVersion7()),
                    ExternalName: "Also a name"),
                ContractPartyRole.Artist,
                isRequiredSignatory: true,
                Now,
                contract.Version));
    }

    /// <summary>
    /// Privilege is assigned by a person. Nothing infers it, and the default is
    /// ordinary so nothing becomes privileged by accident.
    /// </summary>
    [Fact]
    public void PrivilegeIsAssignedRatherThanInferred()
    {
        Contract contract = Open();

        Assert.Equal(PrivilegeClass.Ordinary, contract.Privilege);

        contract.UpdateMetadata(
            contract.Title,
            contract.Kind,
            Actor,
            PrivilegeClass.AttorneyClientPrivileged,
            Now,
            Actor,
            contract.Version,
            legalAnalysis: "Counsel considers the exclusivity overbroad.");

        Assert.Equal(PrivilegeClass.AttorneyClientPrivileged, contract.Privilege);
    }

    [Fact]
    public void AStaleChange_IsRefused()
    {
        Contract contract = Open();

        Assert.Throws<ConcurrencyConflictException>(() =>
            contract.ChangeStatus(
                ContractTransition.SentForReview, Now, Actor, contract.Version - 1));
    }

    internal static Contract Open() =>
        Contract.Open(
            Tenant,
            Deal,
            AcceptedOffer,
            "The Undertow - writing services",
            ContractKind.LongForm,
            Actor,
            Actor,
            Now,
            summary: "Long-form writing agreement.");

    private static Contract Approved(out Guid artist, out Guid studio)
    {
        Contract contract = Open();

        artist = contract
            .AddParty(
                ContractPartyRef.ForExternal("Priya Raghunathan"),
                ContractPartyRole.Artist,
                isRequiredSignatory: true,
                Now,
                contract.Version)
            .Id;

        studio = contract
            .AddParty(
                ContractPartyRef.ForExternal("Northgate Pictures"),
                ContractPartyRole.Studio,
                isRequiredSignatory: true,
                Now,
                contract.Version)
            .Id;

        contract.ChangeStatus(ContractTransition.ApprovedForSignature, Now, Actor, contract.Version);

        return contract;
    }

    private static Contract Executed()
    {
        Contract contract = Approved(out Guid artist, out Guid studio);

        contract.RecordSignature(
            artist, new DateOnly(2027, 3, 2), SignatureMethod.Wet, Now, Actor, contract.Version);
        contract.RecordSignature(
            studio, new DateOnly(2027, 3, 5), SignatureMethod.Wet, Now, Actor, contract.Version);

        return contract;
    }
}
