using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Deals;

/// <summary>Opaque, immutable identifier for an <see cref="Offer"/>.</summary>
public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for an <see cref="OfferTerm"/>.</summary>
public readonly record struct OfferTermId(Guid Value)
{
    public static OfferTermId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Which side put the terms forward.
/// </summary>
/// <remarks>
/// AgencyOS records that an offer was made or received. It has no outbound
/// transport, holds no credentials and knows nothing about delivery, so outbound
/// means "our side proposed these terms", never "the system sent them". The
/// commands and the screens say "record", for the same reason M6's submissions do
/// (ADR-0020, ADR-0021).
/// </remarks>
public enum OfferDirection
{
    /// <summary>The counterparty made or provided these terms.</summary>
    Inbound = 1,

    /// <summary>Our side made or provided these terms.</summary>
    Outbound = 2,
}

/// <summary>
/// Where one offer stands.
/// </summary>
/// <remarks>
/// A draft is a working document whose terms may be edited. Every other status
/// describes something that happened in the world, and its terms are frozen from
/// that moment: rewriting them would change what the agency is recorded as having
/// proposed, or as having been offered (ADR-0021).
/// </remarks>
public enum OfferStatus
{
    /// <summary>Being prepared. Terms editable; nothing communicated.</summary>
    Draft = 1,

    /// <summary>Made or received, awaiting an answer. Terms frozen.</summary>
    Open = 2,

    /// <summary>Accepted. The agreed commercial snapshot.</summary>
    Accepted = 3,

    /// <summary>The receiving side said no.</summary>
    Rejected = 4,

    /// <summary>The proposing side pulled it.</summary>
    Withdrawn = 5,

    /// <summary>A known expiration was recorded as having passed.</summary>
    Expired = 6,

    /// <summary>Answered by a later offer, or unwound by a reopened negotiation.</summary>
    Superseded = 7,
}

/// <summary>What causes an offer to change status.</summary>
public enum OfferTransition
{
    /// <summary>A draft was recorded as actually made or received.</summary>
    Opened = 1,

    /// <summary>Accepted by the receiving side.</summary>
    Accepted = 2,

    /// <summary>Rejected by the receiving side.</summary>
    Rejected = 3,

    /// <summary>Withdrawn by the proposing side.</summary>
    Withdrawn = 4,

    /// <summary>A known expiration was recorded as having passed.</summary>
    Expired = 5,

    /// <summary>A later offer answered this one.</summary>
    AnsweredByCounter = 6,

    /// <summary>A reopened negotiation unwound this acceptance.</summary>
    UnwoundByReopen = 7,
}

/// <summary>How a later offer relates to the one it answers.</summary>
public enum OfferResponseKind
{
    /// <summary>The other side answered with different terms.</summary>
    Counter = 1,

    /// <summary>The same side replaced its own earlier terms.</summary>
    Revision = 2,
}

/// <summary>
/// A term's value, before it has been checked.
/// </summary>
/// <remarks>
/// Flat and optional, mirroring both the persisted row and the shape the rules
/// kernel parses. Exactly one group of fields may be populated, and which one is
/// decided by <paramref name="Kind"/> - a combination the kernel rejects rather
/// than silently ignores.
/// </remarks>
/// <param name="Kind">Which shape the value claims to be.</param>
/// <param name="Amount">Money amount.</param>
/// <param name="Currency">ISO 4217 code, for money.</param>
/// <param name="Number">Percentage or decimal value.</param>
/// <param name="Whole">Integer, duration length or count quantity.</param>
/// <param name="Text">Text value.</param>
/// <param name="Flag">Boolean value.</param>
/// <param name="Date">Date value.</param>
/// <param name="Unit">Unit, for durations and counts.</param>
public sealed record DealTermValue(
    TermValueKind Kind,
    decimal? Amount = null,
    string? Currency = null,
    decimal? Number = null,
    long? Whole = null,
    string? Text = null,
    bool? Flag = null,
    DateOnly? Date = null,
    TermUnit? Unit = null)
{
    /// <summary>A money value.</summary>
    public static DealTermValue OfMoney(Money money) =>
        new(TermValueKind.Money, Amount: money.Amount, Currency: money.Currency.Value);

    /// <summary>A percentage value.</summary>
    public static DealTermValue OfPercentage(decimal percentage) =>
        new(TermValueKind.Percentage, Number: percentage);

    /// <summary>A count, optionally in a stated unit.</summary>
    public static DealTermValue OfCount(long quantity, TermUnit? unit = null) =>
        new(TermValueKind.Count, Whole: quantity, Unit: unit);

    /// <summary>A duration in a stated unit.</summary>
    public static DealTermValue OfDuration(long length, TermUnit unit) =>
        new(TermValueKind.Duration, Whole: length, Unit: unit);

    /// <summary>A business date.</summary>
    public static DealTermValue OfDate(DateOnly date) => new(TermValueKind.Date, Date: date);

    /// <summary>Free text.</summary>
    public static DealTermValue OfText(string text) => new(TermValueKind.Text, Text: text);
}

/// <summary>
/// One negotiated term on one offer.
/// </summary>
/// <remarks>
/// <para>
/// Structured rather than prose, because terms have to be compared between offers,
/// filtered, and later reconciled against what a contract actually says. A free
/// text blob does none of that, and an offer stored as one is an offer nobody can
/// diff.
/// </para>
/// <para>
/// Immutable in practice: there are no setters, and the aggregate refuses to add,
/// change or remove terms once the offer has left draft. PostgreSQL refuses the
/// same thing independently (ADR-0021).
/// </para>
/// </remarks>
public sealed class OfferTerm
{
    private OfferTerm()
    {
    }

    public OfferTermId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OfferId OfferId { get; private set; }

    /// <summary>Which term this is, from the controlled vocabulary.</summary>
    public DealTermCode Code { get; private set; }

    /// <summary>The shape of the value below.</summary>
    public TermValueKind ValueKind { get; private set; }

    /// <summary>Money amount. Set only for a money term, and never a floating-point type.</summary>
    public decimal? AmountValue { get; private set; }

    /// <summary>The currency the amount is in. Set exactly when the amount is.</summary>
    public string? CurrencyCodeValue { get; private set; }

    /// <summary>Percentage or decimal value.</summary>
    public decimal? NumericValue { get; private set; }

    /// <summary>Integer, duration length or count quantity.</summary>
    public long? IntegerValue { get; private set; }

    /// <summary>Text value.</summary>
    public string? TextValue { get; private set; }

    /// <summary>Boolean value.</summary>
    public bool? BooleanValue { get; private set; }

    /// <summary>Business date value.</summary>
    public DateOnly? DateValue { get; private set; }

    /// <summary>What a duration or count is measured in.</summary>
    public TermUnit? Unit { get; private set; }

    /// <summary>Display order within the offer.</summary>
    public int Sequence { get; private set; }

    /// <summary>An override for how the term is shown, when the catalog name is not enough.</summary>
    public string? Label { get; private set; }

    /// <summary>Context that is not part of the value: the basis of a percentage, for example.</summary>
    public string? Notes { get; private set; }

    /// <summary>The money this term carries, when it carries money.</summary>
    public Money? AsMoney =>
        ValueKind == TermValueKind.Money && AmountValue is { } amount && CurrencyCodeValue is { } currency
            ? Money.Create(amount, currency)
            : null;

    /// <summary>Whether the term's value may only be read with <c>deals.economics.read</c>.</summary>
    public bool IsEconomic => DealTermCatalog.IsEconomic(Code);

    internal static OfferTerm Create(
        OrganizationId organizationId,
        OfferId offerId,
        DealKind dealKind,
        DealTermCode code,
        DealTermValue value,
        int sequence,
        string? label = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        DealTermDefinition definition = DealTermCatalog.Require(code);

        if (!definition.AppliesTo(dealKind))
        {
            throw new DomainException($"Term '{definition.DisplayName}' does not apply to a {dealKind} deal.");
        }

        if (value.Kind != definition.ValueKind)
        {
            throw new DomainException(
                $"Term '{definition.DisplayName}' is a {definition.ValueKind} term; a {value.Kind} value was supplied.");
        }

        if (value.Unit is { } unit && !definition.AllowedUnits.Contains(unit))
        {
            throw new DomainException($"Term '{definition.DisplayName}' cannot be measured in {unit}.");
        }

        // Currency membership is reference data and stays on this side; the rules
        // kernel checks only that a code is shaped like one.
        string? currency = value.Currency is null ? null : CurrencyCode.Parse(value.Currency).Value;

        OfferTerm term = new()
        {
            Id = OfferTermId.New(),
            OrganizationId = organizationId,
            OfferId = offerId,
            Code = code,
            ValueKind = value.Kind,
            AmountValue = value.Amount,
            CurrencyCodeValue = currency,
            NumericValue = value.Number,
            IntegerValue = value.Whole,
            TextValue = value.Text?.Trim(),
            BooleanValue = value.Flag,
            DateValue = value.Date,
            Unit = value.Unit,
            Sequence = sequence,
            Label = Ensure.OptionalMax(label, nameof(label), 200),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
        };

        // Structural validation is the kernel's: exactly one value shape, money
        // carries a currency, numbers are in range for their kind.
        if (DealRules.DescribeTermProblem(term.ToRulesInput()) is { } problem)
        {
            throw new DomainException(problem);
        }

        // Money is re-created so the currency's own minor units are applied once,
        // here, rather than differently by whatever renders it.
        if (term.ValueKind == TermValueKind.Money && term.AmountValue is { } amount)
        {
            term.AmountValue = Money.Create(amount, currency!).Amount;
        }

        RequireWithinCatalogBounds(definition, term);

        return term;
    }

    /// <summary>The flat shape the rules kernel parses.</summary>
    public TermInput ToRulesInput() =>
        new()
        {
            Code = (int)Code,
            Kind = (int)ValueKind,
            Amount = AmountValue,
            Currency = CurrencyCodeValue,
            Number = NumericValue,
            Whole = IntegerValue,
            Text = TextValue,
            Flag = BooleanValue,
            Date = DateValue,
            Unit = Unit is { } unit ? (int)unit : null,
            Sequence = Sequence,
        };

    /// <summary>Applies the bounds the catalog states for this particular term.</summary>
    /// <remarks>
    /// Narrower than the kernel's kind-level checks by design. A percentage may be
    /// up to 1000 as a kind, because escalations above 100% of a prior fee are
    /// ordinary; backend points may not, because a share cannot exceed the whole.
    /// </remarks>
    private static void RequireWithinCatalogBounds(DealTermDefinition definition, OfferTerm term)
    {
        decimal? value = term.ValueKind switch
        {
            TermValueKind.Money => term.AmountValue,
            TermValueKind.Percentage or TermValueKind.Decimal => term.NumericValue,
            TermValueKind.Integer or TermValueKind.Duration or TermValueKind.Count => term.IntegerValue,
            _ => null,
        };

        if (value is not { } number)
        {
            return;
        }

        if (definition.Minimum is { } minimum && number < minimum)
        {
            throw new DomainException($"Term '{definition.DisplayName}' must be at least {minimum}.");
        }

        if (definition.Maximum is { } maximum && number > maximum)
        {
            throw new DomainException($"Term '{definition.DisplayName}' must be at most {maximum}.");
        }
    }
}

/// <summary>A recorded change to an offer's status.</summary>
/// <remarks>Append-only. The offer row carries only where it is now.</remarks>
public sealed class OfferEvent
{
    private OfferEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OfferId OfferId { get; private set; }

    public OfferStatus? FromStatus { get; private set; }

    public OfferStatus ToStatus { get; private set; }

    public OfferTransition Transition { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static OfferEvent Record(
        OrganizationId organizationId,
        OfferId offerId,
        OfferStatus? fromStatus,
        OfferStatus toStatus,
        OfferTransition transition,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new OfferEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            OfferId = offerId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Transition = transition,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// One concrete proposal of commercial terms, at one moment.
/// </summary>
/// <remarks>
/// <para>
/// The unit of negotiation. A counter is another offer answering this one, never
/// an edit of it and never a separate kind of record: the chain of offers
/// <em>is</em> the negotiation, and rewriting a link in it would destroy the only
/// account of how the parties got where they are.
/// </para>
/// <para>
/// Once recorded, the commercial snapshot is immutable. If the number in the
/// record turns out to be wrong, the correction is another offer that supersedes
/// this one, so both what was written and what it was corrected to survive
/// (ADR-0021).
/// </para>
/// </remarks>
public sealed class Offer
{
    private readonly List<OfferTerm> _terms = [];
    private readonly List<OfferEvent> _events = [];

    private Offer()
    {
    }

    public OfferId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DealId DealId { get; private set; }

    public OfferDirection Direction { get; private set; }

    public OfferStatus Status { get; private set; }

    /// <summary>The offer this one answers, when it answers one.</summary>
    public OfferId? RespondsToOfferId { get; private set; }

    /// <summary>
    /// Position in the negotiation, assigned by the deal.
    /// </summary>
    /// <remarks>
    /// The canonical order. Never inferred from identifiers or insertion: version
    /// 7 GUIDs happen to sort by creation time, and a thread whose order depended
    /// on that would silently reorder the day the identifier scheme changed.
    /// </remarks>
    public int Sequence { get; private set; }

    /// <summary>When AgencyOS was told about it.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>
    /// When it was actually made or received, when that is known.
    /// </summary>
    /// <remarks>
    /// Freely backdated, because offers are usually recorded after the call. That
    /// makes the negotiation timeline a business record rather than an audit
    /// trail; the audit log holds when the row was written.
    /// </remarks>
    public DateTimeOffset? CommunicatedAt { get; private set; }

    public UserId RecordedByUserId { get; private set; }

    /// <summary>A short factual description of the proposal.</summary>
    public string? Summary { get; private set; }

    /// <summary>Narrative context for anything the structured terms do not carry.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// When the offer was stated to lapse, when a lapse was actually stated.
    /// </summary>
    /// <remarks>
    /// Nothing expires on its own. Time passing is not an event the counterparty
    /// caused, and an offer marked expired by a clock would assert something
    /// nobody said - the same reason M6 refuses to manufacture a non-response.
    /// </remarks>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<OfferTerm> Terms => _terms;

    public IReadOnlyCollection<OfferEvent> Events => _events;

    /// <summary>Gets a value indicating whether the commercial terms may still be edited.</summary>
    public bool IsEditable => DealRules.IsOfferEditable((int)Status);

    /// <summary>Gets a value indicating whether the offer is still awaiting an answer.</summary>
    public bool IsStanding => DealRules.IsOfferStanding((int)Status);

    /// <summary>Gets a value indicating whether the offer has finished.</summary>
    public bool IsTerminal => DealRules.IsOfferTerminal((int)Status);

    /// <summary>Whether a transition is legal from a status.</summary>
    public static bool Permits(OfferStatus status, OfferTransition transition) =>
        DealRules.OfferPermits((int)status, (int)transition);

    /// <summary>The statuses reachable from a status.</summary>
    public static IReadOnlySet<OfferStatus> ReachableFrom(OfferStatus status) =>
        new HashSet<OfferStatus>(
            DealRules.ReachableOfferStates((int)status).Select(code => (OfferStatus)code));

    /// <summary>How a response of one direction relates to the offer it answers.</summary>
    public static OfferResponseKind Classify(OfferDirection answered, OfferDirection answering) =>
        (OfferResponseKind)DealRules.ClassifyResponse((int)answered, (int)answering);

    /// <summary>
    /// Starts an offer as a working draft, with editable terms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A draft is not on the table: it does not supersede the standing offer and
    /// it has no bearing on the deal's status until it is recorded.
    /// </para>
    /// <para>
    /// Every offer begins here, including one being written down after the fact.
    /// There is deliberately no factory that produces an already-frozen offer:
    /// terms can only be added while a draft is editable, so a second entry point
    /// would either have to duplicate the term rules or bypass them (ADR-0021).
    /// </para>
    /// </remarks>
    public static Offer StartDraft(
        OrganizationId organizationId,
        DealId dealId,
        OfferDirection direction,
        int sequence,
        UserId recordedBy,
        DateTimeOffset now,
        OfferId? respondsToOfferId = null,
        string? summary = null,
        string? notes = null,
        DateTimeOffset? expiresAt = null) =>
        Start(
            organizationId,
            dealId,
            direction,
            OfferStatus.Draft,
            sequence,
            recordedBy,
            now,
            communicatedAt: null,
            respondsToOfferId,
            summary,
            notes,
            expiresAt);

    private static Offer Start(
        OrganizationId organizationId,
        DealId dealId,
        OfferDirection direction,
        OfferStatus status,
        int sequence,
        UserId recordedBy,
        DateTimeOffset now,
        DateTimeOffset? communicatedAt,
        OfferId? respondsToOfferId,
        string? summary,
        string? notes,
        DateTimeOffset? expiresAt)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new DomainException($"Unknown offer direction '{direction}'.");
        }

        if (sequence < 1)
        {
            throw new DomainException("An offer's position in the negotiation must be positive.");
        }

        if (communicatedAt is { } communicated && communicated > now)
        {
            throw new DomainException("An offer cannot have been communicated in the future.");
        }

        if (expiresAt is { } expiry && communicatedAt is { } made && expiry < made)
        {
            throw new DomainException("An offer cannot expire before it was made.");
        }

        Offer offer = new()
        {
            Id = OfferId.New(),
            OrganizationId = organizationId,
            DealId = dealId,
            Direction = direction,
            Status = status,
            RespondsToOfferId = respondsToOfferId,
            Sequence = sequence,
            RecordedAt = now,
            CommunicatedAt = communicatedAt,
            RecordedByUserId = recordedBy,
            Summary = Ensure.OptionalMax(summary, nameof(summary), 1000),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 8000),
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        return offer;
    }

    /// <summary>Edits a draft's descriptive fields.</summary>
    public void UpdateDraft(
        DateTimeOffset now,
        int expectedVersion,
        DateTimeOffset? communicatedAt = null,
        string? summary = null,
        string? notes = null,
        DateTimeOffset? expiresAt = null)
    {
        RequireVersion(expectedVersion);
        RequireEditable("change");

        if (communicatedAt is { } communicated && communicated > now)
        {
            throw new DomainException("An offer cannot have been communicated in the future.");
        }

        CommunicatedAt = communicatedAt;
        Summary = Ensure.OptionalMax(summary, nameof(summary), 1000);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 8000);
        ExpiresAt = expiresAt;

        Touch(now);
    }

    /// <summary>Adds a term to a draft.</summary>
    /// <remarks>
    /// One row per term code. A second bonus is a different code or a labelled
    /// <see cref="DealTermCode.OtherTerm"/>, because comparison joins on the code
    /// and two rows sharing one would make the diff ambiguous.
    /// </remarks>
    public OfferTerm AddTerm(
        DealKind dealKind,
        DealTermCode code,
        DealTermValue value,
        DateTimeOffset now,
        int expectedVersion,
        string? label = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);
        RequireEditable("add terms to");

        if (_terms.Any(term => term.Code == code))
        {
            throw new DomainException(
                $"This offer already carries '{DealTermCatalog.Require(code).DisplayName}'.");
        }

        int sequence = _terms.Count == 0 ? 1 : _terms.Max(term => term.Sequence) + 1;

        OfferTerm added = OfferTerm.Create(
            OrganizationId, Id, dealKind, code, value, sequence, label, notes);

        _terms.Add(added);

        Touch(now);

        return added;
    }

    /// <summary>Replaces a draft term's value.</summary>
    /// <remarks>
    /// The old row is discarded rather than versioned. A draft nobody has seen has
    /// no history worth keeping; the moment it is recorded, that stops being true
    /// and this method stops working.
    /// </remarks>
    public OfferTerm UpdateTerm(
        DealKind dealKind,
        DealTermCode code,
        DealTermValue value,
        DateTimeOffset now,
        int expectedVersion,
        string? label = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);
        RequireEditable("change terms on");

        OfferTerm existing = _terms.FirstOrDefault(term => term.Code == code)
            ?? throw new DomainException(
                $"This offer does not carry '{DealTermCatalog.Require(code).DisplayName}'.");

        OfferTerm replacement = OfferTerm.Create(
            OrganizationId, Id, dealKind, code, value, existing.Sequence, label, notes);

        _terms.Remove(existing);
        _terms.Add(replacement);

        Touch(now);

        return replacement;
    }

    /// <summary>Removes a term from a draft.</summary>
    public void RemoveTerm(DealTermCode code, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireEditable("remove terms from");

        OfferTerm existing = _terms.FirstOrDefault(term => term.Code == code)
            ?? throw new DomainException(
                $"This offer does not carry '{DealTermCatalog.Require(code).DisplayName}'.");

        _terms.Remove(existing);

        Touch(now);
    }

    /// <summary>
    /// Records a draft as actually made or received, freezing its terms.
    /// </summary>
    /// <remarks>
    /// An offer with no terms is refused. A proposal that proposes nothing is not
    /// a negotiating position, and recording one would put an empty snapshot in
    /// the chain that nothing downstream could compare against.
    /// </remarks>
    public void Open(DateTimeOffset now, UserId actor, int expectedVersion, DateTimeOffset? communicatedAt = null)
    {
        RequireVersion(expectedVersion);

        if (_terms.Count == 0)
        {
            throw new DomainException("An offer must carry at least one term before it can be recorded.");
        }

        if (communicatedAt is { } communicated)
        {
            if (communicated > now)
            {
                throw new DomainException("An offer cannot have been communicated in the future.");
            }

            CommunicatedAt = communicated;
        }

        CommunicatedAt ??= now;

        // Checked here as well as at construction, because a draft has no
        // communicated instant to compare an expiry against until this moment.
        if (ExpiresAt is { } expiry && expiry < CommunicatedAt)
        {
            throw new DomainException("An offer cannot expire before it was made.");
        }

        Advance(OfferTransition.Opened, now, actor, reason: null);
    }

    /// <summary>Records that the receiving side accepted it.</summary>
    public void Accept(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(OfferTransition.Accepted, now, actor, reason);
    }

    /// <summary>Records that the receiving side rejected it.</summary>
    public void Reject(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(OfferTransition.Rejected, now, actor, reason);
    }

    /// <summary>Records that the proposing side pulled it.</summary>
    public void Withdraw(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);
        Advance(OfferTransition.Withdrawn, now, actor, reason);
    }

    /// <summary>
    /// Records that a stated expiration passed.
    /// </summary>
    /// <remarks>
    /// Requires the offer to have carried an expiry in the first place, and
    /// requires that expiry to be behind us. An offer with no stated lapse cannot
    /// expire, because nobody ever said it would.
    /// </remarks>
    public void Expire(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (ExpiresAt is not { } expiry)
        {
            throw new DomainException(
                "This offer never carried an expiration, so it cannot be recorded as expired. "
                + "Withdraw it or record the counterparty's answer instead.");
        }

        if (expiry > now)
        {
            throw new DomainException("This offer has not reached its stated expiration yet.");
        }

        Advance(OfferTransition.Expired, now, actor, reason);
    }

    /// <summary>
    /// Supersedes the offer because a later one answered it.
    /// </summary>
    /// <remarks>
    /// Never requested by a caller. An offer is superseded because a counter
    /// arrived, and a command that could set it by hand would let the status
    /// detach from the event that caused it.
    /// </remarks>
    public void NoteAnsweredByCounter(DateTimeOffset now, UserId actor, OfferId counter) =>
        Advance(
            OfferTransition.AnsweredByCounter,
            now,
            actor,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Answered by offer {counter}."));

    /// <summary>Supersedes an accepted offer because the negotiation was reopened.</summary>
    public void NoteUnwoundByReopen(DateTimeOffset now, UserId actor, string? reason = null) =>
        Advance(OfferTransition.UnwoundByReopen, now, actor, reason);

    /// <summary>The flat shapes the rules kernel compares.</summary>
    public TermInput[] ToRulesTerms() =>
        [.. _terms.OrderBy(term => term.Sequence).Select(term => term.ToRulesInput())];

    /// <summary>The shape the chain rules validate.</summary>
    public OfferNode ToRulesNode() =>
        new()
        {
            OfferId = Id.Value,
            Sequence = Sequence,
            State = (int)Status,
            Direction = (int)Direction,
            RespondsToOfferId = RespondsToOfferId is { } responds ? responds.Value : null,
        };

    private void Advance(OfferTransition transition, DateTimeOffset now, UserId actor, string? reason)
    {
        int next = DealRules.NextOfferState((int)Status, (int)transition);

        if (next == 0)
        {
            throw new DomainException($"A {Status} offer cannot record '{transition}'.");
        }

        OfferStatus from = Status;

        Status = (OfferStatus)next;

        _events.Add(OfferEvent.Record(
            OrganizationId, Id, from, Status, transition, now, actor, reason));

        Touch(now);
    }

    private void RequireEditable(string action)
    {
        if (!IsEditable)
        {
            throw new DomainException(
                $"This offer was recorded as {Status.ToString().ToLowerInvariant()}, so it is not possible to {action} it. "
                + "Record a new offer that supersedes it instead.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Offer), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
