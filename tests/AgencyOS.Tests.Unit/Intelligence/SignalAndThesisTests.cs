using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Intelligence;

/// <summary>
/// What a signal will and will not claim about itself.
/// </summary>
/// <remarks>
/// The epistemic rules, tested at the level where they are enforced. Most of these
/// would be invisible if they broke: a signal that quietly promoted itself to
/// corroborated reads exactly like one a person assessed (ADR-0030).
/// </remarks>
public sealed class SignalTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly UserId Analyst = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Nothing starts stronger than "somebody recorded it".</summary>
    [Fact]
    public void ANewSignal_IsUnverified()
    {
        Signal signal = Record();

        Assert.Equal(SignalVerification.Unverified, signal.Verification);
        Assert.True(signal.IsStandingClaim);
    }

    /// <summary>There is no way to say a claim was verified.</summary>
    /// <remarks>
    /// AgencyOS cannot verify anything about the outside world. The strongest
    /// honest word is Corroborated, and it means a person looked and said so.
    /// </remarks>
    [Fact]
    public void ThereIsNoVerifiedStatus()
    {
        string[] names = Enum.GetNames<SignalVerification>();

        Assert.DoesNotContain("Verified", names);
        Assert.DoesNotContain("Confirmed", names);
        Assert.DoesNotContain("True", names);
        Assert.Contains("Corroborated", names);
    }

    /// <summary>Corroboration is an act, never a consequence of the evidence count.</summary>
    /// <remarks>
    /// Two outlets running one wire story are one source wearing two hats, and only
    /// a person can tell that from two newsrooms that each made a call.
    /// </remarks>
    [Fact]
    public void AddingEvidence_DoesNotCorroborate()
    {
        Signal signal = Record();

        signal.AddEvidence(IntelligenceSourceId.New(), SignalEvidenceRole.Corroborating, Analyst, Now);
        signal.AddEvidence(IntelligenceSourceId.New(), SignalEvidenceRole.Corroborating, Analyst, Now);

        Assert.Equal(3, signal.Evidence.Count);
        Assert.Equal(SignalVerification.Unverified, signal.Verification);

        signal.Corroborate(Analyst, Now, signal.Version, "Two independent newsrooms.");

        Assert.Equal(SignalVerification.Corroborated, signal.Verification);
        Assert.Equal(Analyst, signal.VerificationChangedBy);
    }

    /// <summary>A signal can never be left with no provenance.</summary>
    /// <remarks>
    /// The invariant the whole model rests on. A claim nobody can trace is a rumour
    /// that has been promoted to a record.
    /// </remarks>
    [Fact]
    public void ASignalKeepsAtLeastOneSource()
    {
        Signal signal = Record();

        Guid onlyEvidence = signal.Evidence[0].Id;

        DomainException failure = Assert.Throws<DomainException>(
            () => signal.RemoveEvidence(onlyEvidence));

        Assert.Contains("provenance", failure.Message, StringComparison.OrdinalIgnoreCase);

        // With a second source, the first can go.
        signal.AddEvidence(IntelligenceSourceId.New(), SignalEvidenceRole.Corroborating, Analyst, Now);
        signal.RemoveEvidence(onlyEvidence);

        Assert.Single(signal.Evidence);
    }

    /// <summary>Retraction is terminal and is not a deletion.</summary>
    [Fact]
    public void Retracting_KeepsTheRowAndClosesTheClaim()
    {
        Signal signal = Record();

        signal.Retract(Analyst, Now, signal.Version, "The subject denied it on the record.");

        Assert.Equal(SignalVerification.Retracted, signal.Verification);
        Assert.False(signal.IsStandingClaim);

        // Everything that was recorded is still there.
        Assert.Single(signal.Evidence);
        Assert.NotEmpty(signal.Claim);

        // And a withdrawn claim is not edited back into use.
        Assert.Throws<DomainException>(() => signal.Corroborate(Analyst, Now, signal.Version));
        Assert.Throws<DomainException>(() => signal.Dispute(Analyst, Now, signal.Version, "no"));
    }

    /// <summary>A signal cannot have been observed before the thing happened.</summary>
    [Fact]
    public void ObservationCannotPrecedeTheEvent()
    {
        DomainException failure = Assert.Throws<DomainException>(() => Signal.Record(
            Org,
            "Studio X appointed a Head of Drama",
            "Studio X appointed Jane Doe as Head of Drama.",
            SignalKind.PersonnelMove,
            IntelligenceSensitivity.Internal,
            Analyst,
            Now,
            occurredAt: Now.AddDays(5),
            observedAt: Now));

        Assert.Contains("observed", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unknown event date stays unknown.</summary>
    /// <remarks>
    /// A hiring reported on Friday may have happened weeks earlier. Defaulting the
    /// event date to the observation would date the agency's own record wrongly.
    /// </remarks>
    [Fact]
    public void AnUnknownEventDate_StaysNull()
    {
        Signal signal = Record();

        Assert.Null(signal.OccurredAt);
        Assert.Equal(Now, signal.ObservedAt);
    }

    /// <summary>The same subject is not named twice.</summary>
    [Fact]
    public void DuplicateSubjects_AreRefused()
    {
        Signal signal = Record();
        Guid company = Guid.CreateVersion7();

        signal.AddSubject(IntelligenceSubjectKind.Company, company, Analyst, Now);

        Assert.Throws<DomainException>(
            () => signal.AddSubject(IntelligenceSubjectKind.Company, company, Analyst, Now));

        // A different kind with the same identifier is a different subject.
        signal.AddSubject(IntelligenceSubjectKind.Project, company, Analyst, Now);

        Assert.Equal(2, signal.Subjects.Count);
    }

    /// <summary>Many subjects, none of them privileged over the others.</summary>
    /// <remarks>
    /// "Netflix acquires Project X" concerns a company and a project. Forcing one
    /// primary subject would lose half the fact.
    /// </remarks>
    [Fact]
    public void ASignalCanConcernSeveralThings()
    {
        Signal signal = Record();

        signal.AddSubject(IntelligenceSubjectKind.Company, Guid.CreateVersion7(), Analyst, Now);
        signal.AddSubject(IntelligenceSubjectKind.Project, Guid.CreateVersion7(), Analyst, Now);
        signal.AddSubject(IntelligenceSubjectKind.Person, Guid.CreateVersion7(), Analyst, Now);

        Assert.Equal(3, signal.Subjects.Count);

        // There is no primary-subject concept to get wrong.
        Assert.DoesNotContain(
            typeof(Signal).GetProperties(),
            p => p.Name.Contains("Primary", StringComparison.OrdinalIgnoreCase));
    }

    private static Signal Record()
    {
        Signal signal = Signal.Record(
            Org,
            "Studio X appointed a Head of Drama",
            "Studio X appointed Jane Doe as Head of Drama.",
            SignalKind.PersonnelMove,
            IntelligenceSensitivity.Internal,
            Analyst,
            Now);

        signal.AddEvidence(
            IntelligenceSourceId.New(), SignalEvidenceRole.Primary, Analyst, Now);

        return signal;
    }
}

/// <summary>
/// What a thesis promises about its own reasoning.
/// </summary>
public sealed class ThesisTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly UserId Analyst = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A thesis is never true or false.</summary>
    /// <remarks>
    /// An interpretation does not become a historical falsehood. It stops being
    /// held, is overtaken, or is replaced by a better reading of the same facts.
    /// </remarks>
    [Fact]
    public void ThereIsNoTrueOrFalseStatus()
    {
        string[] names = Enum.GetNames<ThesisStatus>();

        Assert.DoesNotContain("True", names);
        Assert.DoesNotContain("False", names);
        Assert.DoesNotContain("Confirmed", names);
        Assert.DoesNotContain("Disproved", names);
    }

    /// <summary>The opening position is a revision, so the history is complete.</summary>
    [Fact]
    public void ANewThesis_RecordsItsOpeningPosition()
    {
        Thesis thesis = Create();

        ThesisRevision opening = Assert.Single(thesis.Revisions);

        Assert.Equal(1, opening.Sequence);
        Assert.Equal(thesis.Proposition, opening.Proposition);
        Assert.Equal(ThesisStatus.Draft, thesis.Status);
    }

    /// <summary>Revising appends, and the earlier reasoning survives.</summary>
    /// <remarks>
    /// So the system can answer "what did we believe six months ago" rather than
    /// only "what does it say now".
    /// </remarks>
    [Fact]
    public void Revising_PreservesTheEarlierReasoning()
    {
        Thesis thesis = Create();

        thesis.Activate(Now.AddDays(1), thesis.Version);

        thesis.Revise(
            "Studio X is rebuilding around outside packages, not in-house development.",
            ThesisConfidence.High,
            Analyst,
            Now.AddDays(60),
            thesis.Version,
            rationale: "Three of four recent orders came from outside packages.",
            changeNote: "Sharpened after the Q3 orders.");

        Assert.Equal(2, thesis.Revisions.Count);

        Assert.Equal(
            "Studio X is rebuilding its prestige-drama slate around creator-driven projects.",
            thesis.Revisions[0].Proposition);

        Assert.Equal(ThesisConfidence.Medium, thesis.Revisions[0].Confidence);
        Assert.Equal(ThesisConfidence.High, thesis.Revisions[1].Confidence);
        Assert.Equal(ThesisConfidence.High, thesis.Confidence);
    }

    /// <summary>Evidence carries a stance and is never counted into a verdict.</summary>
    /// <remarks>
    /// Five weak signals do not mechanically outweigh one strong contradiction, and
    /// there is no property here that pretends otherwise.
    /// </remarks>
    [Fact]
    public void EvidenceCarriesAStanceAndIsNotScored()
    {
        Thesis thesis = Create();

        thesis.AddEvidence(SignalId.New(), ThesisEvidenceStance.Supports, Analyst, Now);
        thesis.AddEvidence(SignalId.New(), ThesisEvidenceStance.Supports, Analyst, Now);
        thesis.AddEvidence(SignalId.New(), ThesisEvidenceStance.Challenges, Analyst, Now);
        thesis.AddEvidence(SignalId.New(), ThesisEvidenceStance.Context, Analyst, Now);

        Assert.Equal(4, thesis.Evidence.Count);

        // No score, no verdict, no computed truth anywhere on the aggregate.
        Assert.DoesNotContain(
            typeof(Thesis).GetProperties(),
            p => p.Name.Contains("Score", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Truth", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Verdict", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A closed thesis is not revised back into use.</summary>
    [Fact]
    public void AClosedThesis_IsNotRevised()
    {
        Thesis thesis = Create();

        thesis.Activate(Now.AddDays(1), thesis.Version);
        thesis.Retire("Overtaken by the restructure.", Now.AddDays(90), thesis.Version);

        Assert.Equal(ThesisStatus.Retired, thesis.Status);
        Assert.False(thesis.IsHeld);

        Assert.Throws<DomainException>(() => thesis.Revise(
            "still true", ThesisConfidence.High, Analyst, Now.AddDays(91), thesis.Version));

        // The reasoning stays readable.
        Assert.NotEmpty(thesis.Revisions);
        Assert.NotEmpty(thesis.Proposition);
    }

    /// <summary>Superseding names the successor.</summary>
    [Fact]
    public void Superseding_NamesItsSuccessor()
    {
        Thesis thesis = Create();
        ThesisId successor = ThesisId.New();

        thesis.Activate(Now.AddDays(1), thesis.Version);
        thesis.Supersede(successor, "Replaced by the wider slate thesis.", Now.AddDays(30), thesis.Version);

        Assert.Equal(ThesisStatus.Superseded, thesis.Status);
        Assert.Equal(successor, thesis.SupersededByThesisId);
    }

    /// <summary>A thesis cannot supersede itself.</summary>
    [Fact]
    public void AThesisCannotSupersedeItself()
    {
        Thesis thesis = Create();

        thesis.Activate(Now.AddDays(1), thesis.Version);

        Assert.Throws<DomainException>(
            () => thesis.Supersede(thesis.Id, "circular", Now.AddDays(2), thesis.Version));
    }

    private static Thesis Create() => Thesis.Create(
        Org,
        "Studio X prestige-drama strategy",
        "Studio X is rebuilding its prestige-drama slate around creator-driven projects.",
        IntelligenceSensitivity.Confidential,
        Analyst,
        Analyst,
        Now,
        ThesisConfidence.Medium,
        "Two senior hires from creator-led shops in six weeks.");
}
