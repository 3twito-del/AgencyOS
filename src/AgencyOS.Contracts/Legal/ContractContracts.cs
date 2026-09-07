using AgencyOS.Contracts.Deals;

namespace AgencyOS.Contracts.Legal;

// ------------------------------------------------------------------- requests

/// <summary>
/// When something is due, as the contract expresses it.
/// </summary>
/// <remarks>
/// <para>
/// Three shapes and no fourth. <c>Absolute</c> carries a date. <c>Relative</c>
/// carries an anchor event, an offset and a unit. <c>Unstructured</c> carries the
/// clause's own wording and nothing else, and is the honest answer for
/// "promptly following delivery".
/// </para>
/// <para>
/// <paramref name="Basis"/> may be <c>BusinessDays</c>, and AgencyOS will store
/// that faithfully and then decline to compute a date from it, because it holds no
/// holiday calendar. Counting business days as calendar days would produce a legal
/// deadline that looks authoritative and is wrong (ADR-0022).
/// </para>
/// </remarks>
/// <param name="Kind">Absolute, Relative or Unstructured.</param>
/// <param name="On">The stated date, for an absolute rule.</param>
/// <param name="Anchor">
/// OnExecution, OnEffective, OnDelivery, OnNotice, OnCommencement, OnFirstRelease,
/// OnOptionWindowOpen or Other.
/// </param>
/// <param name="Offset">How many units. Positive.</param>
/// <param name="Unit">Days, Weeks, Months or Years.</param>
/// <param name="Before">Whether the offset runs backwards from the anchor.</param>
/// <param name="Basis">CalendarDays or BusinessDays. Defaults to CalendarDays.</param>
/// <param name="Description">The clause's own wording. Required for Unstructured.</param>
public sealed record DeadlineRuleRequest(
    string Kind,
    DateOnly? On = null,
    string? Anchor = null,
    int? Offset = null,
    string? Unit = null,
    bool Before = false,
    string? Basis = null,
    string? Description = null);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="AssignedTo">Who should do it. Defaults to the caller.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record LegalFollowUpRequest(
    string Title,
    DateTimeOffset? DueAt = null,
    Guid? AssignedTo = null,
    string? Notes = null);

/// <param name="Code">The term, from the controlled vocabulary.</param>
/// <param name="Value">Its value. The same shapes M7 uses, deliberately.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="Label">A display override, when the catalog name is not enough.</param>
/// <param name="Notes">Context that is not part of the value.</param>
/// <param name="Privilege">
/// Ordinary, Confidential, LegalStrategy or AttorneyClientPrivileged. Assigned by
/// the caller and never inferred: AgencyOS does not decide what is privileged.
/// </param>
public sealed record ContractTermRequest(
    string Code,
    TermValueRequest Value,
    string? ClauseReference = null,
    string? Label = null,
    string? Notes = null,
    string? Privilege = null);

/// <summary>Opens a contract against a negotiation whose terms are agreed.</summary>
/// <param name="DealId">The negotiation being papered. Required.</param>
/// <param name="AcceptedOfferId">
/// The agreed commercial position this contract papers. Required and fixed for the
/// contract's life: it is what reconciliation compares against, and a movable
/// baseline would make "the draft matches what we agreed" unanswerable.
/// </param>
/// <param name="Title">What the instrument is called.</param>
/// <param name="Kind">
/// LongForm, ShortForm, SideLetter, Amendment, Rider, LoanOut, ServicesAgreement
/// or Other.
/// </param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="Reference">The agency's internal handle, if it uses one.</param>
/// <param name="Summary">A factual description of the instrument.</param>
/// <param name="LegalAnalysis">What counsel thinks. Privileged content.</param>
/// <param name="StrategyNotes">The agency's own strategy on the paper.</param>
/// <param name="Privilege">How the content is classified. Defaults to Ordinary.</param>
public sealed record CreateContractRequest(
    Guid DealId,
    Guid AcceptedOfferId,
    string Title,
    string Kind,
    Guid OwnerUserId,
    string? Reference = null,
    string? Summary = null,
    string? LegalAnalysis = null,
    string? StrategyNotes = null,
    string? Privilege = null);

/// <param name="ExpectedVersion">The contract's version. Required (ADR-0014).</param>
public sealed record UpdateContractRequest(
    string Title,
    string Kind,
    Guid OwnerUserId,
    int ExpectedVersion,
    string? Reference = null,
    string? Summary = null,
    string? LegalAnalysis = null,
    string? StrategyNotes = null,
    string? Privilege = null);

/// <summary>
/// Moves a contract through its drafting lifecycle.
/// </summary>
/// <remarks>
/// Deliberately not an arbitrary status patch. The accepted transitions are
/// SentForReview, ReturnedToDrafting, ApprovedForSignature, DraftingAbandoned,
/// ReplacedByAnother and TerminationRecorded. PartiallyExecuted and Executed are
/// unreachable here: they follow from recording signatures, and a route that could
/// set them would let a contract claim execution with nobody's signature behind it
/// (ADR-0022).
/// </remarks>
/// <param name="Transition">What is happening to the paper.</param>
/// <param name="ExpectedVersion">The contract's version. Required.</param>
/// <param name="Reason">Why, for the legal history.</param>
/// <param name="TerminatedOn">Required for TerminationRecorded, ignored otherwise.</param>
public sealed record ChangeContractStatusRequest(
    string Transition,
    int ExpectedVersion,
    string? Reason = null,
    DateOnly? TerminatedOn = null);

/// <summary>Records when the agreement takes effect.</summary>
/// <remarks>
/// Separate from execution and never derived from it. A date before execution is
/// accepted, because an agreement effective as of January and signed in March is
/// ordinary (ADR-0022).
/// </remarks>
public sealed record RecordEffectiveDateRequest(DateOnly EffectiveOn, int ExpectedVersion);

/// <summary>
/// Adds a party to a contract.
/// </summary>
/// <remarks>
/// Exactly one of <paramref name="PersonId"/>, <paramref name="CompanyId"/> and
/// <paramref name="ExternalName"/>. Where AgencyOS already knows the party, use
/// its identifier: a free-text name recorded beside a real record is how a party
/// silently stops being the same party (ADR-0022).
/// </remarks>
/// <param name="Role">
/// Artist, Producer, Studio, Network, Employer, Lender, LoanOut, Licensor,
/// Licensee, Guarantor, Agency or Other. What they are doing in this agreement,
/// which is not the same as what kind of record they are.
/// </param>
/// <param name="PersonId">The person, when AgencyOS knows them.</param>
/// <param name="CompanyId">The company, when AgencyOS knows them.</param>
/// <param name="ExternalName">Their name as written, when it does not.</param>
/// <param name="Provenance">Where that name came from.</param>
/// <param name="IsRequiredSignatory">Whether the agreement needs their signature.</param>
/// <param name="Notes">Context.</param>
/// <param name="ExpectedVersion">The contract's version. Required.</param>
public sealed record AddContractPartyRequest(
    string Role,
    int ExpectedVersion,
    Guid? PersonId = null,
    Guid? CompanyId = null,
    string? ExternalName = null,
    string? Provenance = null,
    bool IsRequiredSignatory = true,
    string? Notes = null);

/// <summary>
/// Records that a party signed.
/// </summary>
/// <remarks>
/// The only route to execution, and an assertion rather than a verification.
/// AgencyOS implements no electronic signature, holds no certificate and checks
/// nothing cryptographically: this records that a person says a party signed on a
/// date by a method (ADR-0022).
/// </remarks>
/// <param name="ContractPartyId">Which party signed.</param>
/// <param name="SignedOn">The day they signed, as reported.</param>
/// <param name="Method">Wet, Electronic, Counterpart or Other.</param>
/// <param name="ExternalReference">Where the signed copy lives, if anywhere.</param>
/// <param name="Notes">Context.</param>
/// <param name="ExpectedVersion">The contract's version. Required.</param>
public sealed record RecordSignatureRequest(
    Guid ContractPartyId,
    DateOnly SignedOn,
    string Method,
    int ExpectedVersion,
    string? ExternalReference = null,
    string? Notes = null);

/// <summary>
/// Records how one contract relates to another.
/// </summary>
/// <remarks>
/// An amendment is a separate executed instrument, not version five of the paper
/// it changes. This is how the two stay connected without either pretending to be
/// a draft of the other (ADR-0022).
/// </remarks>
/// <param name="RelatedContractId">The other instrument.</param>
/// <param name="Kind">AmendmentOf, Supersedes, SideLetterTo, Restates or RelatedTo.</param>
/// <param name="Notes">Context.</param>
public sealed record RecordContractRelationshipRequest(
    Guid RelatedContractId,
    string Kind,
    string? Notes = null);

/// <summary>
/// Records a drafting version.
/// </summary>
/// <remarks>
/// AgencyOS does not hold the document. The reference fields say where the file
/// actually lives - a matter number, a document-management identifier, a filename
/// somebody will recognise - and there is no content hash, because a hash of bytes
/// the system has never seen would be a claim about identity it cannot support.
/// Canonical document storage arrives in M10 (ADR-0022).
/// </remarks>
/// <param name="Label">What the agency calls this draft.</param>
/// <param name="Direction">Inbound, Outbound or Internal.</param>
/// <param name="ExpectedVersion">The contract's version. Required.</param>
/// <param name="ReceivedOn">When it arrived, for an inbound draft.</param>
/// <param name="SentOn">When it went out, for an outbound one.</param>
/// <param name="ExternalReference">Where the document lives.</param>
/// <param name="SourceSystem">Which system that reference belongs to.</param>
/// <param name="DisplayFileName">The filename as a person would recognise it.</param>
/// <param name="MediaType">The document's media type, as reported.</param>
/// <param name="Notes">What changed, factually.</param>
/// <param name="Terms">The terms read out of it, if any have been read yet.</param>
public sealed record RecordContractVersionRequest(
    string Label,
    string Direction,
    int ExpectedVersion,
    DateOnly? ReceivedOn = null,
    DateOnly? SentOn = null,
    string? ExternalReference = null,
    string? SourceSystem = null,
    string? DisplayFileName = null,
    string? MediaType = null,
    string? Notes = null,
    IReadOnlyList<ContractTermRequest>? Terms = null);

/// <summary>Adds, replaces or removes a term on a draft version.</summary>
/// <param name="Value">Omit to remove the term.</param>
/// <param name="ExpectedVersion">The <em>version's</em> concurrency token.</param>
public sealed record ChangeContractTermRequest(
    string Code,
    int ExpectedVersion,
    TermValueRequest? Value = null,
    string? ClauseReference = null,
    string? Label = null,
    string? Notes = null,
    string? Privilege = null);

/// <summary>Records a draft as a drafting milestone, freezing its terms.</summary>
public sealed record FinaliseContractVersionRequest(int ExpectedVersion);

/// <summary>
/// Records a grant of rights a contract makes.
/// </summary>
/// <remarks>
/// A record of what the instrument says, and nothing more. It is not a finding
/// that the grantor held the rights, not a chain of title and not a clearance
/// (ADR-0022).
/// </remarks>
/// <param name="ContractVersionId">The version the grant was read out of.</param>
/// <param name="GrantorPartyId">Who grants. Must be a party to this contract.</param>
/// <param name="GranteePartyId">Who receives.</param>
/// <param name="RightType">
/// Production, Distribution, Exhibition, Adaptation, Sequel, Prequel, Remake,
/// Spinoff, Merchandising, Promotional, Publishing, Ancillary or Other.
/// </param>
/// <param name="Medium">
/// AllMedia, Film, Television, Streaming, Theatrical, Digital, Audio, Podcast,
/// Publishing, Stage, Interactive or Other.
/// </param>
/// <param name="Territory">Worldwide, UnitedStates, NorthAmerica or Specified.</param>
/// <param name="Exclusivity">Exclusive, SoleExclusive or NonExclusive.</param>
/// <param name="PeriodKind">
/// Perpetual, Fixed, OpenEnded or Unstated. Unstated exists so a contract that
/// says nothing this build can structure is recorded as saying nothing, rather
/// than as running forever.
/// </param>
/// <param name="StartsOn">When the grant runs from.</param>
/// <param name="EndsOn">When it runs to, for a fixed period.</param>
/// <param name="TerritoryDetail">The clause's own words, when Specified.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="SourcePropertyId">The underlying material, when it names one.</param>
/// <param name="ProjectId">The project, when it names one.</param>
/// <param name="Reservations">What the grantor holds back, as written.</param>
/// <param name="Notes">Context.</param>
/// <param name="SupersedesGrantId">The grant this one replaces, for an amendment.</param>
/// <param name="SupersededExpectedVersion">That grant's version, when superseding one.</param>
public sealed record RecordRightsGrantRequest(
    Guid ContractVersionId,
    Guid GrantorPartyId,
    Guid GranteePartyId,
    string RightType,
    string Medium,
    string Territory,
    string Exclusivity,
    string PeriodKind,
    DateOnly? StartsOn = null,
    DateOnly? EndsOn = null,
    string? TerritoryDetail = null,
    string? ClauseReference = null,
    Guid? SourcePropertyId = null,
    Guid? ProjectId = null,
    string? Reservations = null,
    string? Notes = null,
    Guid? SupersedesGrantId = null,
    int? SupersededExpectedVersion = null);

/// <summary>Records that a grant stopped running.</summary>
public sealed record EndRightsGrantRequest(DateOnly EndedOn, int ExpectedVersion);

/// <summary>
/// Records an election a contract creates.
/// </summary>
/// <remarks>
/// Recording an option never exercises it and never lapses it. Both are acts
/// somebody performs and records, because inferring an exercise from a payment or
/// an expiry from a clock would put a legal position in the system that nobody
/// took (ADR-0022).
/// </remarks>
/// <param name="ContractVersionId">The version the option was read out of.</param>
/// <param name="Kind">
/// Employment, Renewal, Sequel, Extension, Purchase, Rights or Other.
/// </param>
/// <param name="HolderPartyId">Who holds the election.</param>
/// <param name="Subject">What it is over.</param>
/// <param name="Deadline">When it must be exercised by, as the contract expresses it.</param>
/// <param name="AnchorDate">
/// The date the deadline hangs off, when it is already known. Omitted, the
/// deadline stays unresolved rather than being invented.
/// </param>
/// <param name="WindowOpensOn">When the election first becomes available.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="ExerciseMethod">How it must be exercised, as written.</param>
/// <param name="ProjectId">The project, when it names one.</param>
/// <param name="SourcePropertyId">The underlying material, when it names one.</param>
/// <param name="EconomicsTermId">The term carrying its price, when one was recorded.</param>
/// <param name="Notes">Context.</param>
public sealed record RecordOptionRequest(
    Guid ContractVersionId,
    string Kind,
    Guid HolderPartyId,
    string Subject,
    DeadlineRuleRequest Deadline,
    DateOnly? AnchorDate = null,
    DateOnly? WindowOpensOn = null,
    string? ClauseReference = null,
    string? ExerciseMethod = null,
    Guid? ProjectId = null,
    Guid? SourcePropertyId = null,
    Guid? EconomicsTermId = null,
    string? Notes = null);

/// <summary>Records what became of an option.</summary>
/// <param name="Outcome">
/// Exercised, Declined, Waived, Expired or Cancelled. Expired means somebody
/// recorded that the stated deadline passed, never that a clock noticed.
/// </param>
/// <param name="ExpectedVersion">The option's version. Required.</param>
/// <param name="OccurredOn">The day it happened. Required except for Expired and Cancelled.</param>
/// <param name="Reason">Context, for the history.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
public sealed record ResolveOptionRequest(
    string Outcome,
    int ExpectedVersion,
    DateOnly? OccurredOn = null,
    string? Reason = null,
    LegalFollowUpRequest? FollowUp = null);

/// <summary>
/// Records something a party has to do.
/// </summary>
/// <param name="ContractVersionId">The version the obligation was read out of.</param>
/// <param name="ObligorPartyId">Who must do it.</param>
/// <param name="ObligeePartyId">Who is owed it.</param>
/// <param name="Kind">
/// Delivery, Services, Payment, Notice, Approval, Insurance, Credit, Restraint,
/// OptionExercise, Availability or Other.
/// </param>
/// <param name="Description">What is required, in the contract's own terms.</param>
/// <param name="Due">When, as the contract expresses it.</param>
/// <param name="AnchorDate">The anchor date, when it is already known.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="RelatedOptionId">The option it belongs to, when it belongs to one.</param>
/// <param name="RelatedRightsGrantId">The grant it belongs to, when it belongs to one.</param>
/// <param name="Notes">Context.</param>
/// <param name="Privilege">How sensitive it is. Assigned, never inferred.</param>
public sealed record RecordObligationRequest(
    Guid ContractVersionId,
    Guid ObligorPartyId,
    Guid ObligeePartyId,
    string Kind,
    string Description,
    DeadlineRuleRequest Due,
    DateOnly? AnchorDate = null,
    string? ClauseReference = null,
    Guid? RelatedOptionId = null,
    Guid? RelatedRightsGrantId = null,
    string? Notes = null,
    string? Privilege = null);

/// <summary>
/// Records what became of an obligation.
/// </summary>
/// <remarks>
/// <c>Breached</c> requires a reason, and is never reached by a date passing.
/// "Past due" is a fact about a date and AgencyOS derives it; breach is a legal
/// determination a person makes and records (ADR-0022).
/// </remarks>
/// <param name="Outcome">Satisfied, Waived, Breached, Reinstated or Cancelled.</param>
/// <param name="ExpectedVersion">The obligation's version. Required.</param>
/// <param name="OccurredOn">The day it happened. Required for Satisfied and Waived.</param>
/// <param name="Reason">The determination behind it. Required for Breached.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
public sealed record ResolveObligationRequest(
    string Outcome,
    int ExpectedVersion,
    DateOnly? OccurredOn = null,
    string? Reason = null,
    LegalFollowUpRequest? FollowUp = null);

/// <summary>Records a notice the contract requires.</summary>
/// <param name="ContractVersionId">The version it was read out of.</param>
/// <param name="ObligorPartyId">Who must give it.</param>
/// <param name="RecipientPartyId">Who must receive it.</param>
/// <param name="Description">What must be notified.</param>
/// <param name="Due">When, as the contract expresses it.</param>
/// <param name="Method">Written, Email, Courier, RegisteredPost, HandDelivery or Other.</param>
/// <param name="AnchorDate">The anchor date, when it is already known.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="AddressReference">Where the notice address is recorded.</param>
/// <param name="RelatedOptionId">The option it serves, when it serves one.</param>
/// <param name="RelatedObligationId">The obligation it serves, when it serves one.</param>
/// <param name="Notes">Context.</param>
public sealed record RecordNoticeRequirementRequest(
    Guid ContractVersionId,
    Guid ObligorPartyId,
    Guid RecipientPartyId,
    string Description,
    DeadlineRuleRequest Due,
    string Method,
    DateOnly? AnchorDate = null,
    string? ClauseReference = null,
    string? AddressReference = null,
    Guid? RelatedOptionId = null,
    Guid? RelatedObligationId = null,
    string? Notes = null);

/// <summary>
/// Records that a notice passed between the parties.
/// </summary>
/// <remarks>
/// <strong>AgencyOS does not send notices.</strong> It has no outbound transport
/// and cannot confirm that anything reached anybody. This records a person's
/// assertion that a notice was given or received, exactly as M6 records that
/// material went out (ADR-0022).
/// </remarks>
/// <param name="Direction">Given or Received.</param>
/// <param name="SenderPartyId">Who gave it.</param>
/// <param name="RecipientPartyId">Who received it.</param>
/// <param name="OccurredOn">The day it passed, as reported.</param>
/// <param name="Method">Written, Email, Courier, RegisteredPost, HandDelivery or Other.</param>
/// <param name="NoticeRequirementId">The requirement it answers, when it answers one.</param>
/// <param name="ExternalReference">Where a copy lives, if anywhere.</param>
/// <param name="Summary">What it said, factually.</param>
/// <param name="Notes">Context.</param>
public sealed record RecordNoticeRequest(
    string Direction,
    Guid SenderPartyId,
    Guid RecipientPartyId,
    DateOnly OccurredOn,
    string Method,
    Guid? NoticeRequirementId = null,
    string? ExternalReference = null,
    string? Summary = null,
    string? Notes = null);

/// <summary>
/// Creates a task to manage a piece of contract work.
/// </summary>
/// <remarks>
/// Explicit, never automatic. Turning every obligation into a task would fill the
/// list with entries nobody has to act on this week, and the list would stop being
/// read (ADR-0022).
/// </remarks>
/// <param name="FollowUp">The task to create.</param>
/// <param name="ObligationId">The obligation it manages, when it manages one.</param>
/// <param name="ContractOptionId">The option it manages, when it manages one.</param>
public sealed record CreateContractTaskRequest(
    LegalFollowUpRequest FollowUp,
    Guid? ObligationId = null,
    Guid? ContractOptionId = null);

// ------------------------------------------------------------------ responses

/// <param name="Code">The controlled term code.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="ValueKind">Which shape the value takes.</param>
/// <param name="IsEconomic">Whether reading it required <c>deals.economics.read</c>.</param>
/// <param name="IsCommercial">Whether it has a negotiated counterpart to reconcile against.</param>
/// <param name="Amount">Money amount.</param>
/// <param name="Currency">Currency of the amount.</param>
/// <param name="Number">Percentage or decimal value.</param>
/// <param name="Whole">Integer, duration length or count quantity.</param>
/// <param name="Text">Text value.</param>
/// <param name="Flag">Boolean value.</param>
/// <param name="Date">Business date value.</param>
/// <param name="Unit">Unit for durations and counts.</param>
/// <param name="DisplayValue">
/// A rendering for display only. The typed fields above are canonical; nothing
/// parses this back.
/// </param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="Sequence">Display order within the version.</param>
/// <param name="Notes">Context that is not part of the value.</param>
public sealed record ContractTermResponse(
    string Code,
    string DisplayName,
    string ValueKind,
    bool IsEconomic,
    bool IsCommercial,
    decimal? Amount,
    string? Currency,
    decimal? Number,
    long? Whole,
    string? Text,
    bool? Flag,
    DateOnly? Date,
    string? Unit,
    string DisplayValue,
    string? ClauseReference,
    int Sequence,
    string? Notes);

/// <param name="Id">Version identifier.</param>
/// <param name="ContractId">The instrument it belongs to.</param>
/// <param name="VersionNumber">Position in the drafting sequence.</param>
/// <param name="Label">What the agency calls it.</param>
/// <param name="Direction">Inbound, Outbound or Internal.</param>
/// <param name="Status">Draft, Recorded or Superseded.</param>
/// <param name="RecordedAt">When AgencyOS was told.</param>
/// <param name="ReceivedOn">When it arrived.</param>
/// <param name="SentOn">When it went out.</param>
/// <param name="RecordedByDisplayName">Who recorded it.</param>
/// <param name="ExternalReference">Where the document actually lives.</param>
/// <param name="SourceSystem">Which system that reference belongs to.</param>
/// <param name="DisplayFileName">The filename as a person would recognise it.</param>
/// <param name="MediaType">The document's media type, as reported.</param>
/// <param name="HoldsDocument">
/// Whether AgencyOS holds the file itself. Always <c>false</c> in this contract
/// version: M8 records a reference to a document it has never seen, and says so
/// rather than letting a client assume otherwise.
/// </param>
/// <param name="Notes">What changed, factually.</param>
/// <param name="Terms">
/// The terms read out of it. Absent entirely without <c>contracts.terms.read</c>;
/// economic terms absent without <c>deals.economics.read</c>; privileged terms
/// absent without <c>contracts.privileged.read</c>. Absent is deliberately
/// indistinguishable from empty.
/// </param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ContractVersionResponse(
    Guid Id,
    Guid ContractId,
    int VersionNumber,
    string Label,
    string Direction,
    string Status,
    DateTimeOffset RecordedAt,
    DateOnly? ReceivedOn,
    DateOnly? SentOn,
    string? RecordedByDisplayName,
    string? ExternalReference,
    string? SourceSystem,
    string? DisplayFileName,
    string? MediaType,
    bool HoldsDocument,
    string? Notes,
    IReadOnlyList<ContractTermResponse> Terms,
    int Version);

/// <param name="Id">Party identifier.</param>
/// <param name="Role">What they are doing in the agreement.</param>
/// <param name="DisplayName">Their name, joined for a known party or as written.</param>
/// <param name="PersonId">The person, when AgencyOS knows them.</param>
/// <param name="CompanyId">The company, when AgencyOS knows them.</param>
/// <param name="IsResolved">Whether AgencyOS has a record for them.</param>
/// <param name="Provenance">Where an external name came from.</param>
/// <param name="IsRequiredSignatory">Whether the agreement needs their signature.</param>
/// <param name="HasSigned">Whether a signature has been recorded. Derived.</param>
/// <param name="SignedOn">When, as reported.</param>
public sealed record ContractPartyResponse(
    Guid Id,
    string Role,
    string DisplayName,
    Guid? PersonId,
    Guid? CompanyId,
    bool IsResolved,
    string? Provenance,
    bool IsRequiredSignatory,
    bool HasSigned,
    DateOnly? SignedOn);

/// <param name="Id">Contract identifier.</param>
/// <param name="Title">What the instrument is called.</param>
/// <param name="Reference">The agency's internal handle.</param>
/// <param name="Kind">What kind of instrument.</param>
/// <param name="Status">
/// Draft, UnderReview, ApprovedForExecution, PartiallyExecuted, Executed,
/// Abandoned, Superseded or Terminated.
/// </param>
/// <param name="DealId">The negotiation it papers.</param>
/// <param name="DealName">That negotiation's name.</param>
/// <param name="AcceptedOfferId">The agreed position it papers.</param>
/// <param name="SubjectDisplayName">What the pursuit behind it is about.</param>
/// <param name="CounterpartyDisplayName">Who is on the other side, read through M6.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OwnerDisplayName">Their name.</param>
/// <param name="ExecutedOn">When the last required signature was given.</param>
/// <param name="EffectiveOn">When the agreement takes effect. Separate from execution.</param>
/// <param name="TerminatedOn">When it ended, as recorded.</param>
/// <param name="IsEffective">Whether it is in force today. Derived from its dates.</param>
/// <param name="VersionCount">How many drafting versions exist. Derived.</param>
/// <param name="LatestVersionNumber">The newest version's number. Derived.</param>
/// <param name="LatestVersionLabel">What that version is called.</param>
/// <param name="LatestVersionId">Its identifier.</param>
/// <param name="OutstandingSignatureCount">Required signatures not yet recorded. Derived.</param>
/// <param name="PartyCount">How many parties. Derived.</param>
/// <param name="RightsGrantCount">How many grants recorded. Derived.</param>
/// <param name="OpenOptionCount">Options still available. Derived.</param>
/// <param name="OutstandingObligationCount">Obligations still pending. Derived.</param>
/// <param name="OverdueObligationCount">
/// Obligations past their resolved due date and still outstanding. A fact about
/// dates, and explicitly not a count of breaches.
/// </param>
/// <param name="NextDeadlineOn">
/// The earliest legal date still ahead. Derived, and null when every deadline on
/// the contract hangs off an event that has not happened.
/// </param>
/// <param name="NextDeadlineDescription">What that deadline is.</param>
/// <param name="UnresolvedDifferenceCount">
/// How many terms differ between the agreed position and the newest version.
/// Recomputed on every read; null when no version has been recorded.
/// </param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ContractSummaryResponse(
    Guid Id,
    string Title,
    string? Reference,
    string Kind,
    string Status,
    Guid DealId,
    string DealName,
    Guid AcceptedOfferId,
    string? SubjectDisplayName,
    string CounterpartyDisplayName,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    DateOnly? ExecutedOn,
    DateOnly? EffectiveOn,
    DateOnly? TerminatedOn,
    bool IsEffective,
    int VersionCount,
    int? LatestVersionNumber,
    string? LatestVersionLabel,
    Guid? LatestVersionId,
    int OutstandingSignatureCount,
    int PartyCount,
    int RightsGrantCount,
    int OpenOptionCount,
    int OutstandingObligationCount,
    int OverdueObligationCount,
    DateOnly? NextDeadlineOn,
    string? NextDeadlineDescription,
    int? UnresolvedDifferenceCount,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Grant identifier.</param>
/// <param name="ContractId">The instrument recording it.</param>
/// <param name="ContractVersionId">The version it was read out of.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="GrantorPartyId">Who grants.</param>
/// <param name="GrantorDisplayName">Their name.</param>
/// <param name="GranteePartyId">Who receives.</param>
/// <param name="GranteeDisplayName">Their name.</param>
/// <param name="RightType">The right.</param>
/// <param name="Medium">The medium.</param>
/// <param name="Territory">The territory.</param>
/// <param name="TerritoryDetail">The clause's own words, when Specified.</param>
/// <param name="Exclusivity">Exclusive, SoleExclusive or NonExclusive.</param>
/// <param name="PeriodKind">Perpetual, Fixed, OpenEnded or Unstated.</param>
/// <param name="StartsOn">When it runs from.</param>
/// <param name="EndsOn">When it runs to.</param>
/// <param name="IsCurrent">
/// Whether the grant is running today, as the contract describes it. Not a finding
/// that the grantor held the right, and not a clearance.
/// </param>
/// <param name="SourcePropertyId">The underlying material, when it names one.</param>
/// <param name="SourcePropertyTitle">That material's title.</param>
/// <param name="ProjectId">The project, when it names one.</param>
/// <param name="ProjectTitle">That project's title.</param>
/// <param name="Reservations">What the grantor holds back, as written.</param>
/// <param name="Notes">Context.</param>
/// <param name="Status">Active, Superseded or Ended.</param>
/// <param name="SupersededByGrantId">The grant that replaced it.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record RightsGrantResponse(
    Guid Id,
    Guid ContractId,
    Guid ContractVersionId,
    string? ClauseReference,
    Guid GrantorPartyId,
    string GrantorDisplayName,
    Guid GranteePartyId,
    string GranteeDisplayName,
    string RightType,
    string Medium,
    string Territory,
    string? TerritoryDetail,
    string Exclusivity,
    string PeriodKind,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    bool IsCurrent,
    Guid? SourcePropertyId,
    string? SourcePropertyTitle,
    Guid? ProjectId,
    string? ProjectTitle,
    string? Reservations,
    string? Notes,
    string Status,
    Guid? SupersededByGrantId,
    int Version);

/// <param name="Id">Option identifier.</param>
/// <param name="ContractId">The instrument recording it.</param>
/// <param name="ContractVersionId">The version it was read out of.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="Kind">What kind of election.</param>
/// <param name="HolderPartyId">Who holds it.</param>
/// <param name="HolderDisplayName">Their name.</param>
/// <param name="Subject">What it is over.</param>
/// <param name="ProjectId">The project, when it names one.</param>
/// <param name="ProjectTitle">That project's title.</param>
/// <param name="WindowOpensOn">When the election first becomes available.</param>
/// <param name="DeadlineOn">
/// The deadline, once it is a date anybody can work out. Null is honest rather
/// than missing: the anchor may not have happened, or the clause may count
/// business days AgencyOS has no calendar for.
/// </param>
/// <param name="DeadlineUnresolvedReason">Which of those applies, when it is null.</param>
/// <param name="DeadlineDescription">The clause's own wording, always.</param>
/// <param name="ExerciseMethod">How it must be exercised, as written.</param>
/// <param name="Status">Available, Exercised, Declined, Expired, Waived or Cancelled.</param>
/// <param name="ResolvedOn">When it was settled.</param>
/// <param name="IsExercisable">Whether the election could be made today. Derived.</param>
/// <param name="IsPastDeadline">
/// Whether the deadline has passed with the option still available. A fact about a
/// date, and explicitly not the same as expired, which somebody records.
/// </param>
/// <param name="EconomicsTermId">The term carrying its price, when one was recorded.</param>
/// <param name="NoticeRequirementId">The notice it requires, when it requires one.</param>
/// <param name="Notes">Context.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ContractOptionResponse(
    Guid Id,
    Guid ContractId,
    Guid ContractVersionId,
    string? ClauseReference,
    string Kind,
    Guid HolderPartyId,
    string HolderDisplayName,
    string Subject,
    Guid? ProjectId,
    string? ProjectTitle,
    DateOnly? WindowOpensOn,
    DateOnly? DeadlineOn,
    string? DeadlineUnresolvedReason,
    string? DeadlineDescription,
    string? ExerciseMethod,
    string Status,
    DateOnly? ResolvedOn,
    bool IsExercisable,
    bool IsPastDeadline,
    Guid? EconomicsTermId,
    Guid? NoticeRequirementId,
    string? Notes,
    int Version);

/// <param name="Id">Obligation identifier.</param>
/// <param name="ContractId">The instrument recording it.</param>
/// <param name="ContractVersionId">The version it was read out of.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="ObligorPartyId">Who must do it.</param>
/// <param name="ObligorDisplayName">Their name.</param>
/// <param name="ObligeePartyId">Who is owed it.</param>
/// <param name="ObligeeDisplayName">Their name.</param>
/// <param name="Kind">What kind of duty.</param>
/// <param name="Description">What is required, in the contract's own terms.</param>
/// <param name="DueOn">The due date, once it is a date anybody can work out.</param>
/// <param name="DueUnresolvedReason">Why it is not a date, when it is not.</param>
/// <param name="DueDescription">The clause's own wording, always.</param>
/// <param name="Status">Pending, Satisfied, Waived, Breached or Cancelled.</param>
/// <param name="ResolvedOn">When it was settled.</param>
/// <param name="IsPastDue">
/// Whether the due date has passed with the obligation outstanding. Derived, and
/// deliberately not the same as breached: past due is a fact about a date, breach
/// is a legal determination a person records.
/// </param>
/// <param name="RelatedOptionId">The option it belongs to, when it belongs to one.</param>
/// <param name="RelatedRightsGrantId">The grant it belongs to, when it belongs to one.</param>
/// <param name="Notes">Context.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ObligationResponse(
    Guid Id,
    Guid ContractId,
    Guid ContractVersionId,
    string? ClauseReference,
    Guid ObligorPartyId,
    string ObligorDisplayName,
    Guid ObligeePartyId,
    string ObligeeDisplayName,
    string Kind,
    string Description,
    DateOnly? DueOn,
    string? DueUnresolvedReason,
    string? DueDescription,
    string Status,
    DateOnly? ResolvedOn,
    bool IsPastDue,
    Guid? RelatedOptionId,
    Guid? RelatedRightsGrantId,
    string? Notes,
    int Version);

/// <param name="Id">Requirement identifier.</param>
/// <param name="ContractId">The instrument recording it.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="ObligorPartyId">Who must give it.</param>
/// <param name="ObligorDisplayName">Their name.</param>
/// <param name="RecipientPartyId">Who must receive it.</param>
/// <param name="RecipientDisplayName">Their name.</param>
/// <param name="Description">What must be notified.</param>
/// <param name="DueOn">When, once it is a date anybody can work out.</param>
/// <param name="DueUnresolvedReason">Why it is not a date, when it is not.</param>
/// <param name="DueDescription">The clause's own wording, always.</param>
/// <param name="Method">How it must be given.</param>
/// <param name="AddressReference">Where the notice address is recorded.</param>
/// <param name="RelatedOptionId">The option it serves, when it serves one.</param>
/// <param name="RelatedObligationId">The obligation it serves, when it serves one.</param>
/// <param name="RecordedNoticeCount">How many notices have been recorded against it.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record NoticeRequirementResponse(
    Guid Id,
    Guid ContractId,
    string? ClauseReference,
    Guid ObligorPartyId,
    string ObligorDisplayName,
    Guid RecipientPartyId,
    string RecipientDisplayName,
    string Description,
    DateOnly? DueOn,
    string? DueUnresolvedReason,
    string? DueDescription,
    string Method,
    string? AddressReference,
    Guid? RelatedOptionId,
    Guid? RelatedObligationId,
    int RecordedNoticeCount,
    int Version);

/// <summary>A notice somebody recorded as given or received.</summary>
/// <remarks>AgencyOS did not send it. This records that a person says it passed.</remarks>
public sealed record NoticeRecordResponse(
    Guid Id,
    Guid ContractId,
    Guid? NoticeRequirementId,
    string Direction,
    string SenderDisplayName,
    string RecipientDisplayName,
    DateOnly OccurredOn,
    string Method,
    string? ExternalReference,
    string? Summary,
    string? RecordedByDisplayName);

/// <param name="Id">Relationship identifier.</param>
/// <param name="Kind">AmendmentOf, Supersedes, SideLetterTo, Restates or RelatedTo.</param>
/// <param name="RelatedContractId">The other instrument.</param>
/// <param name="RelatedContractTitle">What it is called.</param>
/// <param name="RelatedContractStatus">Where it stands.</param>
/// <param name="Notes">Context.</param>
public sealed record ContractRelationshipResponse(
    Guid Id,
    string Kind,
    Guid RelatedContractId,
    string RelatedContractTitle,
    string RelatedContractStatus,
    string? Notes);

/// <param name="Id">Task identifier.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="State">Open or Completed.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="ObligationId">The obligation it manages, when it manages one.</param>
/// <param name="ContractOptionId">The option it manages, when it manages one.</param>
public sealed record ContractTaskResponse(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    Guid? ObligationId,
    Guid? ContractOptionId);

/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Kind">Contract, Signature, Notice, Option or Obligation.</param>
/// <param name="Summary">A readable sentence.</param>
/// <param name="Detail">Secondary context.</param>
/// <param name="ActorDisplayName">Who recorded it.</param>
public sealed record ContractHistoryEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <param name="Code">The term compared.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="Result">
/// Matched, Changed, MissingFromContract, AddedInContract or NotComparable. Five
/// factual outcomes and no judgement: whether a change is acceptable is a legal
/// question about a document AgencyOS has not read.
/// </param>
/// <param name="Direction">
/// Increased, Decreased, Level or NotComparable. Movement, never merit.
/// </param>
/// <param name="Negotiated">What the accepted offer said, absent when the contract added it.</param>
/// <param name="Contracted">What the version says, absent when the contract omits it.</param>
public sealed record ReconciliationLineResponse(
    string Code,
    string DisplayName,
    string Result,
    string Direction,
    ContractTermResponse? Negotiated,
    ContractTermResponse? Contracted);

/// <param name="ContractId">The instrument.</param>
/// <param name="ContractVersionId">The version compared.</param>
/// <param name="AcceptedOfferId">The agreed position compared against.</param>
/// <param name="Lines">Every term on either side, exactly once.</param>
/// <param name="DifferenceCount">How many lines are not a plain match.</param>
/// <param name="IsFaithful">Whether the draft says exactly what was negotiated.</param>
public sealed record ReconciliationResponse(
    Guid ContractId,
    Guid ContractVersionId,
    Guid AcceptedOfferId,
    IReadOnlyList<ReconciliationLineResponse> Lines,
    int DifferenceCount,
    bool IsFaithful);

/// <param name="Contract">Headline fields.</param>
/// <param name="Summary">Factual description of the instrument.</param>
/// <param name="LegalAnalysis">
/// What counsel thinks. Absent without <c>contracts.privileged.read</c> when the
/// contract is classified above ordinary, and absent is deliberately
/// indistinguishable from empty.
/// </param>
/// <param name="StrategyNotes">The agency's own strategy on the paper. Same rule.</param>
/// <param name="Privilege">How the content is classified. Assigned by a person.</param>
/// <param name="Parties">Who is party to it, and who still has to sign.</param>
/// <param name="Versions">The drafting history, newest first.</param>
/// <param name="Relationships">Amendments, side letters and what this supersedes.</param>
/// <param name="RightsGrants">What the contract records as granted.</param>
/// <param name="Options">The elections it creates.</param>
/// <param name="Obligations">
/// What the parties must do. Privileged obligations are absent without
/// <c>contracts.privileged.read</c>.
/// </param>
/// <param name="NoticeRequirements">The notices it requires.</param>
/// <param name="RecordedNotices">Notices somebody recorded as given or received.</param>
/// <param name="OpenTasks">Outstanding linked tasks.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record ContractDetailResponse(
    ContractSummaryResponse Contract,
    string? Summary,
    string? LegalAnalysis,
    string? StrategyNotes,
    string Privilege,
    IReadOnlyList<ContractPartyResponse> Parties,
    IReadOnlyList<ContractVersionResponse> Versions,
    IReadOnlyList<ContractRelationshipResponse> Relationships,
    IReadOnlyList<RightsGrantResponse> RightsGrants,
    IReadOnlyList<ContractOptionResponse> Options,
    IReadOnlyList<ObligationResponse> Obligations,
    IReadOnlyList<NoticeRequirementResponse> NoticeRequirements,
    IReadOnlyList<NoticeRecordResponse> RecordedNotices,
    IReadOnlyList<ContractTaskResponse> OpenTasks,
    DateTimeOffset CreatedAt);

/// <summary>
/// A date somebody has to act on.
/// </summary>
/// <remarks>
/// Unioned from the options, obligations and notice requirements that carry one.
/// A clause whose deadline could not be resolved is simply absent: inventing a
/// date for it would put a deadline in a lawyer's calendar that the contract never
/// set (ADR-0022).
/// </remarks>
/// <param name="Source">Option, Obligation or Notice.</param>
/// <param name="SourceId">The row it came from.</param>
/// <param name="ContractId">The instrument.</param>
/// <param name="ContractTitle">What it is called.</param>
/// <param name="DueOn">The date.</param>
/// <param name="Description">What is due.</param>
/// <param name="DaysRemaining">Days from today. Negative once it has passed.</param>
/// <param name="IsPast">Whether the date has passed.</param>
public sealed record LegalDeadlineResponse(
    string Source,
    Guid SourceId,
    Guid ContractId,
    string ContractTitle,
    DateOnly DueOn,
    string Description,
    int DaysRemaining,
    bool IsPast);

/// <param name="DealId">The negotiation.</param>
/// <param name="DealName">What it is called.</param>
/// <param name="CounterpartyDisplayName">Who is on the other side.</param>
/// <param name="AgreedAt">When the terms were agreed.</param>
public sealed record DealWithoutContractResponse(
    Guid DealId,
    string DealName,
    string CounterpartyDisplayName,
    DateTimeOffset AgreedAt);

/// <summary>
/// What the legal side of the desk has to look at.
/// </summary>
/// <remarks>
/// Counts, dates and lists. There is deliberately no risk score, no reading of
/// what a clause means and no expected payment: whether a difference matters is a
/// legal judgement, and money is M9's subject (ADR-0022).
/// </remarks>
/// <param name="UnderReview">Contracts circulated for review.</param>
/// <param name="AwaitingSignature">Contracts with required signatures outstanding.</param>
/// <param name="WithUnresolvedDifferences">
/// Contracts whose newest version differs from what was agreed. A count of
/// differences, not a claim that any of them is a problem.
/// </param>
/// <param name="RecentlyExecuted">Contracts executed in the last thirty days.</param>
/// <param name="TermsAgreedWithoutContract">
/// Negotiations whose terms are agreed and which nobody has papered. Not an error
/// - the paper is often days behind the handshake - but the thing a legal desk
/// most needs to see.
/// </param>
/// <param name="UpcomingDeadlines">Legal dates inside the next sixty days.</param>
/// <param name="OverdueObligations">Obligations past their date and still outstanding.</param>
/// <param name="OptionsPastDeadline">Options whose stated deadline has passed unresolved.</param>
/// <param name="OverdueTasks">Linked tasks past their date.</param>
/// <param name="UnderReviewCount">How many contracts are under review.</param>
/// <param name="AwaitingSignatureCount">How many are awaiting signature.</param>
/// <param name="EffectiveCount">How many are in force today.</param>
public sealed record ContractCommandCenterResponse(
    IReadOnlyList<ContractSummaryResponse> UnderReview,
    IReadOnlyList<ContractSummaryResponse> AwaitingSignature,
    IReadOnlyList<ContractSummaryResponse> WithUnresolvedDifferences,
    IReadOnlyList<ContractSummaryResponse> RecentlyExecuted,
    IReadOnlyList<DealWithoutContractResponse> TermsAgreedWithoutContract,
    IReadOnlyList<LegalDeadlineResponse> UpcomingDeadlines,
    IReadOnlyList<ObligationResponse> OverdueObligations,
    IReadOnlyList<ContractOptionResponse> OptionsPastDeadline,
    IReadOnlyList<ContractTaskResponse> OverdueTasks,
    int UnderReviewCount,
    int AwaitingSignatureCount,
    int EffectiveCount);

/// <param name="VersionId">The version recorded.</param>
/// <param name="VersionNumber">Its position in the drafting sequence.</param>
public sealed record RecordContractVersionResponse(Guid VersionId, int VersionNumber);

/// <param name="ContractPartyId">The party added.</param>
public sealed record AddContractPartyResponse(Guid ContractPartyId);

/// <param name="SignatureId">The signature recorded.</param>
/// <param name="ContractStatus">Where the contract stands afterwards.</param>
/// <param name="OutstandingSignatureCount">How many required signatures remain.</param>
public sealed record RecordSignatureResponse(
    Guid SignatureId,
    string ContractStatus,
    int OutstandingSignatureCount);

/// <param name="RightsGrantId">The grant recorded.</param>
public sealed record RecordRightsGrantResponse(Guid RightsGrantId);

/// <param name="ContractOptionId">The option recorded.</param>
public sealed record RecordOptionResponse(Guid ContractOptionId);

/// <param name="ContractOptionId">The option resolved.</param>
/// <param name="Status">Where it stands afterwards.</param>
public sealed record ResolveOptionResponse(Guid ContractOptionId, string Status);

/// <param name="ObligationId">The obligation recorded.</param>
public sealed record RecordObligationResponse(Guid ObligationId);

/// <param name="ObligationId">The obligation resolved.</param>
/// <param name="Status">Where it stands afterwards.</param>
public sealed record ResolveObligationResponse(Guid ObligationId, string Status);

/// <param name="NoticeRequirementId">The requirement recorded.</param>
public sealed record RecordNoticeRequirementResponse(Guid NoticeRequirementId);

/// <param name="NoticeRecordId">The notice recorded.</param>
public sealed record RecordNoticeResponse(Guid NoticeRecordId);

/// <param name="TaskId">The task created.</param>
public sealed record CreateContractTaskResponse(Guid TaskId);

/// <summary>One supported contract term, as the catalog describes it.</summary>
/// <remarks>
/// Published so a client can build a term editor without hard-coding the
/// vocabulary. The commercial half is derived from the deal catalog, so a change
/// to a negotiated term's name or kind reaches this one automatically and the two
/// cannot drift (ADR-0022).
/// </remarks>
/// <param name="Code">The term code.</param>
/// <param name="DisplayName">What a person calls it.</param>
/// <param name="ValueKind">The one shape its value may take.</param>
/// <param name="IsEconomic">Whether reading its value requires <c>deals.economics.read</c>.</param>
/// <param name="IsCommercial">Whether it has a negotiated counterpart to reconcile against.</param>
public sealed record ContractTermDefinitionResponse(
    string Code,
    string DisplayName,
    string ValueKind,
    bool IsEconomic,
    bool IsCommercial);
