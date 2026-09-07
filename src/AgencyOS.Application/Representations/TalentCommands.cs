using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Representations;

/// <summary>Raised when a person already has the record being created.</summary>
public sealed class AlreadyExistsException : Exception
{
    public AlreadyExistsException(string message)
        : base(message)
    {
    }
}

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="PersonId">Person the profile describes.</param>
/// <param name="CareerStage">Roughly where they are in their career.</param>
/// <param name="Summary">Short factual summary.</param>
/// <param name="PositioningNotes">Internal judgment. Requires talent.notes.read to read back.</param>
/// <param name="BaseMarket">Primary market they work out of.</param>
/// <param name="Languages">Languages they work in.</param>
/// <param name="Disciplines">Disciplines to record at creation.</param>
public sealed record CreateTalentProfileCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    CareerStage CareerStage,
    string? Summary,
    string? PositioningNotes,
    string? BaseMarket,
    string? Languages,
    IReadOnlyList<ProfessionalDiscipline> Disciplines);

/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateTalentProfileCommand(
    OrganizationId OrganizationId,
    TalentProfileId TalentProfileId,
    CareerStage CareerStage,
    string? Summary,
    string? PositioningNotes,
    string? BaseMarket,
    string? Languages,
    int ExpectedVersion);

public sealed record ChangeTalentDisciplineCommand(
    OrganizationId OrganizationId,
    TalentProfileId TalentProfileId,
    ProfessionalDiscipline Discipline,
    int ExpectedVersion);

/// <summary>
/// Creates the agency's representation record about a person.
/// </summary>
/// <remarks>
/// One profile per person per tenant. A second would split the agency's view of
/// somebody across two records, and whichever one a query happened to find would
/// look complete.
/// </remarks>
public sealed class CreateTalentProfileHandler
{
    private readonly ITalentProfileRepository _profiles;
    private readonly IPersonRepository _people;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTalentProfileHandler(
        ITalentProfileRepository profiles,
        IPersonRepository people,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _profiles = profiles;
        _people = people;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<TalentProfileId> HandleAsync(
        CreateTalentProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool personExists = await _people
            .ExistsActiveAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (!personExists)
        {
            throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());
        }

        TalentProfile? existing = await _profiles
            .FindByPersonAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new AlreadyExistsException("This person already has a talent profile.");
        }

        DateTimeOffset now = _clock.UtcNow;

        TalentProfile profile = TalentProfile.Create(
            command.OrganizationId,
            command.PersonId,
            actor,
            now,
            command.CareerStage,
            command.Summary,
            command.PositioningNotes,
            command.BaseMarket,
            command.Languages);

        foreach (ProfessionalDiscipline discipline in command.Disciplines ?? [])
        {
            profile.AddDiscipline(discipline, DateOnly.FromDateTime(now.UtcDateTime), now);
        }

        _profiles.Add(profile);

        _audit.Record(
            AuditAction.TalentProfileCreated,
            entityType: nameof(TalentProfile),
            entityId: profile.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new
            {
                PersonId = command.PersonId.ToString(),
                CareerStage = profile.CareerStage.ToString(),
                Disciplines = profile.CurrentDisciplines.Select(x => x.ToString()).ToArray(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return profile.Id;
    }
}

/// <summary>Replaces the descriptive fields of a talent profile.</summary>
public sealed class UpdateTalentProfileHandler
{
    private readonly ITalentProfileRepository _profiles;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTalentProfileHandler(
        ITalentProfileRepository profiles,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _profiles = profiles;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdateTalentProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        TalentProfile profile =
            await _profiles.FindAsync(command.OrganizationId, command.TalentProfileId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TalentProfile), command.TalentProfileId.ToString());

        profile.RequireVersion(command.ExpectedVersion);

        profile.Update(
            _clock.UtcNow,
            command.CareerStage,
            command.Summary,
            command.PositioningNotes,
            command.BaseMarket,
            command.Languages);

        _audit.Record(
            AuditAction.TalentProfileUpdated,
            entityType: nameof(TalentProfile),
            entityId: profile.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { CareerStage = profile.CareerStage.ToString(), profile.BaseMarket });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Adds or ends a discipline on a talent profile.</summary>
/// <remarks>
/// One handler for both directions because they are the same operation with
/// opposite signs, and splitting them would duplicate the lookup, the version
/// check and the audit shape three lines apart.
/// </remarks>
public sealed class ChangeTalentDisciplineHandler
{
    private readonly ITalentProfileRepository _profiles;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeTalentDisciplineHandler(
        ITalentProfileRepository profiles,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _profiles = profiles;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        ChangeTalentDisciplineCommand command,
        bool add,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        TalentProfile profile =
            await _profiles.FindAsync(command.OrganizationId, command.TalentProfileId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TalentProfile), command.TalentProfileId.ToString());

        profile.RequireVersion(command.ExpectedVersion);

        DateTimeOffset now = _clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        if (add)
        {
            profile.AddDiscipline(command.Discipline, today, now);
        }
        else
        {
            profile.RemoveDiscipline(command.Discipline, today, now);
        }

        _audit.Record(
            add ? AuditAction.TalentDisciplineAdded : AuditAction.TalentDisciplineRemoved,
            entityType: nameof(TalentProfile),
            entityId: profile.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { Discipline = command.Discipline.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------- credits and materials

public sealed record AddCreditCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    string Title,
    CreditType Type,
    CreditStatus Status,
    string? Role,
    int? Year,
    CompanyId? CompanyId,
    string? Source,
    string? Notes);

public sealed record UpdateCreditCommand(
    OrganizationId OrganizationId,
    CreditId CreditId,
    string Title,
    CreditType Type,
    CreditStatus Status,
    string? Role,
    int? Year,
    CompanyId? CompanyId,
    string? Source,
    string? Notes,
    int ExpectedVersion);

public sealed record AddMaterialCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    string Title,
    MaterialType Type,
    MaterialStatus Status,
    string? VersionLabel,
    string? ExternalUri,
    DateOnly? ReceivedOn,
    string? Source,
    string? Notes);

public sealed record UpdateMaterialCommand(
    OrganizationId OrganizationId,
    MaterialId MaterialId,
    string Title,
    MaterialType Type,
    MaterialStatus Status,
    string? VersionLabel,
    string? ExternalUri,
    DateOnly? ReceivedOn,
    string? Source,
    string? Notes,
    int ExpectedVersion);

/// <summary>Records a credit against a person.</summary>
public sealed class AddCreditHandler
{
    private readonly ICreditRepository _credits;
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AddCreditHandler(
        ICreditRepository credits,
        IPersonRepository people,
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _credits = credits;
        _people = people;
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<CreditId> HandleAsync(AddCreditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool personExists = await _people
            .ExistsActiveAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (!personExists)
        {
            throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());
        }

        if (command.CompanyId is { } companyId)
        {
            bool companyExists = await _companies
                .ExistsActiveAsync(command.OrganizationId, companyId, cancellationToken)
                .ConfigureAwait(false);

            if (!companyExists)
            {
                throw new EntityNotFoundException(nameof(Company), companyId.ToString());
            }
        }

        Credit credit = Credit.Create(
            command.OrganizationId,
            command.PersonId,
            command.Title,
            command.Type,
            actor,
            _clock.UtcNow,
            command.Role,
            command.Status,
            command.Year,
            command.CompanyId,
            command.Source,
            command.Notes);

        _credits.Add(credit);

        _audit.Record(
            AuditAction.CreditAdded,
            entityType: nameof(Credit),
            entityId: credit.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { credit.Title, Type = credit.Type.ToString(), credit.Year });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return credit.Id;
    }
}

/// <summary>Revises a credit.</summary>
public sealed class UpdateCreditHandler
{
    private readonly ICreditRepository _credits;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCreditHandler(
        ICreditRepository credits,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _credits = credits;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdateCreditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Credit credit =
            await _credits.FindAsync(command.OrganizationId, command.CreditId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Credit), command.CreditId.ToString());

        credit.RequireVersion(command.ExpectedVersion);

        credit.Update(
            command.Title,
            command.Type,
            command.Status,
            _clock.UtcNow,
            command.Role,
            command.Year,
            command.CompanyId,
            command.Source,
            command.Notes);

        _audit.Record(
            AuditAction.CreditUpdated,
            entityType: nameof(Credit),
            entityId: credit.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { credit.Title, Status = credit.Status.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records a material against a person.</summary>
public sealed class AddMaterialHandler
{
    private readonly IMaterialRepository _materials;
    private readonly IPersonRepository _people;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AddMaterialHandler(
        IMaterialRepository materials,
        IPersonRepository people,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _materials = materials;
        _people = people;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<MaterialId> HandleAsync(AddMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool personExists = await _people
            .ExistsActiveAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (!personExists)
        {
            throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());
        }

        Material material = Material.Create(
            command.OrganizationId,
            command.PersonId,
            command.Title,
            command.Type,
            actor,
            _clock.UtcNow,
            command.Status,
            command.VersionLabel,
            command.ExternalUri,
            command.ReceivedOn,
            command.Source,
            command.Notes);

        _materials.Add(material);

        _audit.Record(
            AuditAction.MaterialAdded,
            entityType: nameof(Material),
            entityId: material.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { material.Title, Type = material.Type.ToString(), material.VersionLabel });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return material.Id;
    }
}

/// <summary>Revises a material.</summary>
public sealed class UpdateMaterialHandler
{
    private readonly IMaterialRepository _materials;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateMaterialHandler(
        IMaterialRepository materials,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _materials = materials;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdateMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Material material =
            await _materials.FindAsync(command.OrganizationId, command.MaterialId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Material), command.MaterialId.ToString());

        material.RequireVersion(command.ExpectedVersion);

        material.Update(
            command.Title,
            command.Type,
            command.Status,
            _clock.UtcNow,
            command.VersionLabel,
            command.ExternalUri,
            command.ReceivedOn,
            command.Source,
            command.Notes);

        _audit.Record(
            AuditAction.MaterialUpdated,
            entityType: nameof(Material),
            entityId: material.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TalentWrite,
            semanticDelta: new { material.Title, Status = material.Status.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
