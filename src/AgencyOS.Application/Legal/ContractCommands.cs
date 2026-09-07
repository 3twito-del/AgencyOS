using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Legal;

// -------------------------------------------------------------------- commands

/// <param name="DealId">The negotiation this papers. Required.</param>
/// <param name="AcceptedOfferId">
/// The commercial snapshot it implements. Must be the deal's accepted offer at the
/// time the contract is opened.
/// </param>
public sealed record CreateContractCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    OfferId AcceptedOfferId,
    string Title,
    ContractKind Kind,
    UserId OwnerUserId,
    string? Reference,
    string? Summary,
    string? LegalAnalysis,
    string? StrategyNotes,
    PrivilegeClass Privilege);

/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateContractCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    string Title,
    ContractKind Kind,
    UserId OwnerUserId,
    PrivilegeClass Privilege,
    string? Reference,
    string? Summary,
    string? LegalAnalysis,
    string? StrategyNotes,
    int ExpectedVersion);

/// <summary>
/// Moves a contract through its drafting lifecycle.
/// </summary>
/// <remarks>
/// Deliberately cannot reach <see cref="ContractStatus.PartiallyExecuted"/> or
/// <see cref="ContractStatus.Executed"/>. Those follow from recording signatures,
/// and a status command that could set them would let a contract claim execution
/// with nobody's signature behind it (ADR-0022).
/// </remarks>
public sealed record ChangeContractStatusCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractTransition Transition,
    string? Reason,
    DateOnly? TerminatedOn,
    int ExpectedVersion);

public sealed record RecordEffectiveDateCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    DateOnly EffectiveOn,
    int ExpectedVersion);

public sealed record AddContractPartyCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractPartyRef Party,
    ContractPartyRole Role,
    bool IsRequiredSignatory,
    string? Notes,
    int ExpectedVersion);

/// <summary>
/// Records that a party signed.
/// </summary>
/// <remarks>
/// The only route to execution. AgencyOS implements no electronic signature and
/// verifies nothing: this records a person's assertion that a party signed.
/// </remarks>
public sealed record RecordSignatureCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    Guid ContractPartyId,
    DateOnly SignedOn,
    SignatureMethod Method,
    string? ExternalReference,
    string? Notes,
    int ExpectedVersion);

public sealed record RecordContractRelationshipCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractId RelatedContractId,
    ContractRelationshipKind Kind,
    string? Notes);

/// <param name="Label">What the agency calls this draft.</param>
/// <param name="Terms">The terms read out of it, if any have been read yet.</param>
/// <param name="ExpectedVersion">The contract's version. Required.</param>
public sealed record RecordContractVersionCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    string Label,
    VersionDirection Direction,
    int ExpectedVersion,
    DateOnly? ReceivedOn = null,
    DateOnly? SentOn = null,
    string? ExternalReference = null,
    string? SourceSystem = null,
    string? DisplayFileName = null,
    string? MediaType = null,
    string? Notes = null,
    IReadOnlyList<ContractTermInput>? Terms = null);

/// <param name="Code">The term, from the controlled vocabulary.</param>
/// <param name="Value">Its value. Exactly one shape, matching the catalog.</param>
/// <param name="ClauseReference">Where in the document it came from.</param>
/// <param name="Privilege">How sensitive it is. Assigned, never inferred.</param>
public sealed record ContractTermInput(
    ContractTermCode Code,
    DealTermValue Value,
    string? ClauseReference = null,
    string? Label = null,
    string? Notes = null,
    PrivilegeClass Privilege = PrivilegeClass.Ordinary);

/// <summary>Adds, replaces or removes a term on a draft version.</summary>
/// <param name="Value">Null removes the term.</param>
/// <param name="ExpectedVersion">The <em>version's</em> concurrency token.</param>
public sealed record ChangeContractTermCommand(
    OrganizationId OrganizationId,
    ContractVersionId ContractVersionId,
    ContractTermCode Code,
    DealTermValue? Value,
    int ExpectedVersion,
    string? ClauseReference = null,
    string? Label = null,
    string? Notes = null,
    PrivilegeClass Privilege = PrivilegeClass.Ordinary);

/// <summary>Records a draft version as a drafting milestone, freezing its terms.</summary>
public sealed record FinaliseContractVersionCommand(
    OrganizationId OrganizationId,
    ContractVersionId ContractVersionId,
    int ExpectedVersion);

/// <param name="ContractId">The contract this version was recorded, and its identifier.</param>
public sealed record RecordContractVersionResult(ContractVersionId VersionId, int VersionNumber);

// -------------------------------------------------------------------- handlers

/// <summary>Opens and maintains contracts.</summary>
public sealed class ContractHandler
{
    private readonly IContractRepository _contracts;
    private readonly IDealRepository _deals;
    private readonly IOfferRepository _offers;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ContractHandler(
        IContractRepository contracts,
        IDealRepository deals,
        IOfferRepository offers,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _deals = deals;
        _offers = offers;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Opens a contract against a negotiation whose terms are agreed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires the deal to be at TermsAgreed and the offer to be its accepted one.
    /// A contract drafted from a snapshot nobody accepted is a contract nothing can
    /// reconcile against.
    /// </para>
    /// <para>
    /// The link is fixed at this moment. If the negotiation later reopens and the
    /// offer becomes superseded, the contract still records what it was drafted to
    /// implement (ADR-0022).
    /// </para>
    /// </remarks>
    public async Task<ContractId> HandleAsync(
        CreateContractCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await _deals
            .FindAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Deal), command.DealId.ToString());

        if (deal.Status != DealStatus.TermsAgreed)
        {
            throw new DomainException(
                $"This deal is {deal.Status.ToString().ToLowerInvariant()}. A contract papers agreed "
                + "commercial terms, so there must be an accepted offer to draft from.");
        }

        IReadOnlyList<Offer> thread = await _offers
            .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        Offer accepted = thread.FirstOrDefault(offer => offer.Status == OfferStatus.Accepted)
            ?? throw new DomainException(
                "This deal says its terms are agreed but no offer records the agreement, "
                + "so there is nothing to draft from.");

        if (accepted.Id != command.AcceptedOfferId)
        {
            throw new DomainException(
                "That offer is not the one this deal accepted, so a contract cannot claim to "
                + "implement it.");
        }

        Contract contract = Contract.Open(
            command.OrganizationId,
            deal.Id,
            accepted.Id,
            command.Title,
            command.Kind,
            command.OwnerUserId,
            actor,
            _clock.UtcNow,
            command.Reference,
            command.Summary,
            command.LegalAnalysis,
            command.StrategyNotes,
            command.Privilege);

        _contracts.Add(contract);

        _audit.Record(
            AuditAction.ContractOpened,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                contract.Title,
                Kind = contract.Kind.ToString(),
                DealId = deal.Id.ToString(),
                AcceptedOfferId = accepted.Id.ToString(),
                Privilege = contract.Privilege.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return contract.Id;
    }

    public async Task HandleAsync(
        UpdateContractCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await RequireAsync(
            command.OrganizationId, command.ContractId, cancellationToken).ConfigureAwait(false);

        contract.UpdateMetadata(
            command.Title,
            command.Kind,
            command.OwnerUserId,
            command.Privilege,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Reference,
            command.Summary,
            command.LegalAnalysis,
            command.StrategyNotes);

        _audit.Record(
            AuditAction.ContractUpdated,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,

            // The classification, never the classified text. An audit row carrying
            // counsel's analysis would be a second copy with none of the
            // permissions guarding the first.
            semanticDelta: new
            {
                contract.Title,
                Kind = contract.Kind.ToString(),
                Privilege = contract.Privilege.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ChangeContractStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await RequireAsync(
            command.OrganizationId, command.ContractId, cancellationToken).ConfigureAwait(false);

        contract.ChangeStatus(
            command.Transition,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Reason,
            command.TerminatedOn);

        _audit.Record(
            AuditAction.ContractStatusChanged,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                Status = contract.Status.ToString(),
                Transition = command.Transition.ToString(),
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records when the agreement takes effect.</summary>
    /// <remarks>
    /// Separate from execution and never derived from it. A contract effective from
    /// January and signed in March is ordinary.
    /// </remarks>
    public async Task HandleAsync(
        RecordEffectiveDateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await RequireAsync(
            command.OrganizationId, command.ContractId, cancellationToken).ConfigureAwait(false);

        contract.RecordEffectiveDate(
            command.EffectiveOn, _clock.UtcNow, actor, command.ExpectedVersion);

        _audit.Record(
            AuditAction.ContractEffectiveDateRecorded,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new { EffectiveOn = command.EffectiveOn.ToString("O") });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddContractPartyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await RequireAsync(
            command.OrganizationId, command.ContractId, cancellationToken).ConfigureAwait(false);

        ContractParty party = contract.AddParty(
            command.Party,
            command.Role,
            command.IsRequiredSignatory,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.Notes);

        _audit.Record(
            AuditAction.ContractPartyAdded,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                PartyId = party.Id.ToString(),
                Role = party.Role.ToString(),
                party.IsRequiredSignatory,
                party.IsResolved,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return party.Id;
    }

    /// <summary>Records a signature, advancing execution when it was the last one.</summary>
    public async Task<ContractStatus> HandleAsync(
        RecordSignatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await RequireAsync(
            command.OrganizationId, command.ContractId, cancellationToken).ConfigureAwait(false);

        contract.RecordSignature(
            command.ContractPartyId,
            command.SignedOn,
            command.Method,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.ExternalReference,
            command.Notes);

        _audit.Record(
            AuditAction.ContractSignatureRecorded,
            entityType: nameof(Contract),
            entityId: contract.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                PartyId = command.ContractPartyId.ToString(),
                SignedOn = command.SignedOn.ToString("O"),
                Method = command.Method.ToString(),
                Status = contract.Status.ToString(),
                Outstanding = contract.OutstandingSignatories.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return contract.Status;
    }

    /// <summary>Records how one instrument relates to another.</summary>
    /// <remarks>
    /// An amendment is a separate contract pointing back at what it amends, never a
    /// version of it. Both instruments keep their own drafting history, signatures
    /// and effective dates, because legally they have them.
    /// </remarks>
    public async Task HandleAsync(
        RecordContractRelationshipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await RequireAsync(command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        if (!await _contracts
                .ExistsAsync(command.OrganizationId, command.RelatedContractId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new EntityNotFoundException(
                nameof(Contract), command.RelatedContractId.ToString());
        }

        _contracts.AddRelationship(ContractRelationship.Create(
            command.OrganizationId,
            command.ContractId,
            command.RelatedContractId,
            command.Kind,
            actor,
            _clock.UtcNow,
            command.Notes));

        _audit.Record(
            AuditAction.ContractRelationshipRecorded,
            entityType: nameof(Contract),
            entityId: command.ContractId.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                Kind = command.Kind.ToString(),
                Related = command.RelatedContractId.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Contract> RequireAsync(
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken) =>
        await _contracts.FindAsync(organizationId, contractId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Contract), contractId.ToString());
}

/// <summary>Records drafting versions and the terms read out of them.</summary>
public sealed class ContractVersionHandler
{
    private readonly IContractRepository _contracts;
    private readonly IContractVersionRepository _versions;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ContractVersionHandler(
        IContractRepository contracts,
        IContractVersionRepository versions,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _versions = versions;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records a drafting version, superseding the one before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version number comes from the sequence rather than from the caller, so
    /// two people recording a draft at the same time cannot both claim v4. A unique
    /// index settles the race the moment they try.
    /// </para>
    /// <para>
    /// AgencyOS records where the document lives, not the document. Nothing here
    /// stores bytes, hashes anything, or claims a file is immutable because its
    /// metadata is.
    /// </para>
    /// </remarks>
    public async Task<RecordContractVersionResult> HandleAsync(
        RecordContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await _contracts
            .FindAsync(command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Contract), command.ContractId.ToString());

        if (!contract.AcceptsNewVersions)
        {
            throw new DomainException(
                $"This contract is {contract.Status.ToString().ToLowerInvariant()}, so a new drafting "
                + "version cannot be recorded against it. An amendment is a separate instrument.");
        }

        IReadOnlyList<ContractVersion> existing = await _versions
            .ListForContractAsync(command.OrganizationId, contract.Id, cancellationToken)
            .ConfigureAwait(false);

        int next = existing.Count == 0 ? 1 : existing.Max(version => version.VersionNumber) + 1;

        ContractVersion version = ContractVersion.Start(
            command.OrganizationId,
            contract.Id,
            next,
            command.Label,
            command.Direction,
            actor,
            _clock.UtcNow,
            command.ReceivedOn,
            command.SentOn,
            command.ExternalReference,
            command.SourceSystem,
            command.DisplayFileName,
            command.MediaType,
            command.Notes);

        foreach (ContractTermInput term in command.Terms ?? [])
        {
            version.AddTerm(
                term.Code,
                term.Value,
                _clock.UtcNow,
                version.Version,
                term.ClauseReference,
                term.Label,
                term.Notes,
                term.Privilege);
        }

        // Recorded straight away when terms came with it, because the caller has
        // told us what the draft says. A version left as a draft is one somebody is
        // still transcribing.
        if (command.Terms is { Count: > 0 })
        {
            version.Record(_clock.UtcNow, version.Version);
        }

        _versions.Add(version);

        // The previous milestone is superseded, so "which version is current" has
        // one answer rather than being inferred from the highest number.
        foreach (ContractVersion earlier in existing.Where(
            x => x.Status == ContractVersionStatus.Recorded))
        {
            earlier.NoteSuperseded(_clock.UtcNow);
        }

        contract.NoteVersionRecorded(version.Label, _clock.UtcNow, actor);

        _audit.Record(
            AuditAction.ContractVersionRecorded,
            entityType: nameof(ContractVersion),
            entityId: version.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,

            // The count of terms, never their values.
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                version.VersionNumber,
                version.Label,
                Direction = version.Direction.ToString(),
                TermCount = version.Terms.Count,
                HasExternalReference = version.ExternalReference is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordContractVersionResult(version.Id, version.VersionNumber);
    }

    /// <summary>Adds, replaces or removes a term while the version is a draft.</summary>
    public async Task HandleAsync(
        ChangeContractTermCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractVersion version = await RequireVersionAsync(
            command.OrganizationId, command.ContractVersionId, cancellationToken).ConfigureAwait(false);

        if (command.Value is null)
        {
            version.RemoveTerm(command.Code, _clock.UtcNow, command.ExpectedVersion);
        }
        else if (version.Terms.Any(term => term.Code == command.Code))
        {
            version.UpdateTerm(
                command.Code,
                command.Value,
                _clock.UtcNow,
                command.ExpectedVersion,
                command.ClauseReference,
                command.Label,
                command.Notes,
                command.Privilege);
        }
        else
        {
            version.AddTerm(
                command.Code,
                command.Value,
                _clock.UtcNow,
                command.ExpectedVersion,
                command.ClauseReference,
                command.Label,
                command.Notes,
                command.Privilege);
        }

        _audit.Record(
            AuditAction.ContractTermChanged,
            entityType: nameof(ContractVersion),
            entityId: version.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ContractsWrite,
            semanticDelta: new
            {
                Term = command.Code.ToString(),
                Action = command.Value is null ? "removed" : "set",
                Privilege = command.Privilege.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a draft version as a milestone, freezing its terms.</summary>
    public async Task HandleAsync(
        FinaliseContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractVersion version = await RequireVersionAsync(
            command.OrganizationId, command.ContractVersionId, cancellationToken).ConfigureAwait(false);

        version.Record(_clock.UtcNow, command.ExpectedVersion);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContractVersion> RequireVersionAsync(
        OrganizationId organizationId,
        ContractVersionId versionId,
        CancellationToken cancellationToken) =>
        await _versions.FindAsync(organizationId, versionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(ContractVersion), versionId.ToString());
}
