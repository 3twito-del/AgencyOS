using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Contract"/> and the parties, signatures and events it
/// owns.
/// </summary>
/// <remarks>
/// The anchor to M7 is a composite foreign key over (organization, deal) and a
/// second over (organization, accepted offer), so a contract cannot paper a
/// negotiation from another tenant, and cannot cite an offer that does not exist.
/// That the offer is the deal's <em>accepted</em> one is enforced by a trigger the
/// migration adds rather than by a foreign key, because acceptance is a status
/// that the offer row carries and no key can constrain (ADR-0011, ADR-0022).
/// </remarks>
public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contracts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.DealId)
            .HasColumnName("deal_id")
            .HasConversion(id => id.Value, value => new DealId(value))
            .IsRequired();

        builder.Property(x => x.AcceptedOfferId)
            .HasColumnName("accepted_offer_id")
            .HasConversion(id => id.Value, value => new OfferId(value))
            .IsRequired();

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000);
        builder.Property(x => x.LegalAnalysis).HasColumnName("legal_analysis").HasMaxLength(8000);
        builder.Property(x => x.StrategyNotes).HasColumnName("strategy_notes").HasMaxLength(8000);
        builder.Property(x => x.Privilege).HasColumnName("privilege").HasConversion<int>().IsRequired();

        // Four distinct dates, never collapsed. A contract can be signed on one day,
        // executed when the last party signs on another, effective from a third the
        // clause names, and terminated on a fourth (ADR-0022).
        builder.Property(x => x.ExecutedOn).HasColumnName("executed_on");
        builder.Property(x => x.EffectiveOn).HasColumnName("effective_on");
        builder.Property(x => x.TerminatedOn).HasColumnName("terminated_on");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Deal>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.DealId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Offer>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.AcceptedOfferId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Parties)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Signatures)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Contract.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Contract.Parties))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Contract.Signatures))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Every one of these is worked out from the columns above. Storing an
        // "is executed" flag beside the dates and signatures that decide it would
        // give the system two facts that can disagree (ADR-0022).
        builder.Ignore(x => x.IsTerminal);
        builder.Ignore(x => x.IsFullyExecuted);
        builder.Ignore(x => x.AcceptsNewVersions);
        builder.Ignore(x => x.AcceptsSignatures);
        builder.Ignore(x => x.RequiredSignatories);
        builder.Ignore(x => x.OutstandingSignatories);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_contracts_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId })
            .HasDatabaseName("ix_contracts_organization_owner");

        builder.HasIndex(x => new { x.OrganizationId, x.UpdatedAt })
            .HasDatabaseName("ix_contracts_organization_updated");

        builder.HasIndex(x => new { x.OrganizationId, x.DealId })
            .HasDatabaseName("ix_contracts_organization_deal");

        // Several contracts may paper one negotiation - a long form, a side letter,
        // an amendment - so this is deliberately not unique.
        builder.HasIndex(x => x.AcceptedOfferId)
            .HasDatabaseName("ix_contracts_accepted_offer");
    }
}

/// <summary>Mapping for <see cref="ContractEvent"/>.</summary>
public sealed class ContractEventConfiguration : IEntityTypeConfiguration<ContractEvent>
{
    public void Configure(EntityTypeBuilder<ContractEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Transition).HasColumnName("transition").HasConversion<int?>();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1000);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ContractId, x.RecordedAt })
            .HasDatabaseName("ix_contract_events_contract_recorded");
    }
}

/// <summary>
/// Mapping for <see cref="ContractParty"/>.
/// </summary>
/// <remarks>
/// A party is either a person AgencyOS knows, a company it knows, or a name it has
/// only been told. The three are separate columns with a check constraint that
/// exactly one is populated, because a free-text name that shadows a real record
/// is how a party silently stops being the same party (ADR-0022).
/// </remarks>
public sealed class ContractPartyConfiguration : IEntityTypeConfiguration<ContractParty>
{
    public void Configure(EntityTypeBuilder<ContractParty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_parties");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        // Role, not type. The studio on one paper is the licensee on another, and
        // both are the same company record.
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.ExternalName).HasColumnName("external_name").HasMaxLength(300);
        builder.Property(x => x.Provenance).HasColumnName("provenance").HasMaxLength(500);
        builder.Property(x => x.IsRequiredSignatory).HasColumnName("is_required_signatory").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Ignore(x => x.IsResolved);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CompanyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_contract_parties_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.CompanyId })
            .HasDatabaseName("ix_contract_parties_organization_company");

        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ix_contract_parties_organization_person");
    }
}

/// <summary>
/// Mapping for <see cref="ContractSignature"/>.
/// </summary>
/// <remarks>
/// A record that somebody signed, not a signature. There is no certificate, no
/// key and no verification: AgencyOS is told a party signed on a date by a method,
/// and stores exactly that claim (ADR-0022).
/// </remarks>
public sealed class ContractSignatureConfiguration : IEntityTypeConfiguration<ContractSignature>
{
    public void Configure(EntityTypeBuilder<ContractSignature> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_signatures");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ContractPartyId).HasColumnName("contract_party_id").IsRequired();
        builder.Property(x => x.SignedOn).HasColumnName("signed_on").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(500);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<ContractParty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractPartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // One signature per party per contract. A counterpart is the same party
        // signing the same paper, so recording it twice would mean two of a party.
        builder.HasIndex(x => new { x.ContractId, x.ContractPartyId })
            .IsUnique()
            .HasDatabaseName("ux_contract_signatures_contract_party");
    }
}

/// <summary>
/// Mapping for <see cref="ContractVersion"/>.
/// </summary>
/// <remarks>
/// The document columns are references, never content. There is no hash column,
/// because AgencyOS has not seen the bytes and a hash it did not compute would be
/// a claim about identity it cannot support. M10 brings the repository these
/// columns will point into (ADR-0022).
/// </remarks>
public sealed class ContractVersionConfiguration : IEntityTypeConfiguration<ContractVersion>
{
    public void Configure(EntityTypeBuilder<ContractVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.ReceivedOn).HasColumnName("received_on");
        builder.Property(x => x.SentOn).HasColumnName("sent_on");
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(500);
        builder.Property(x => x.SourceSystem).HasColumnName("source_system").HasMaxLength(100);
        builder.Property(x => x.DisplayFileName).HasColumnName("display_file_name").HasMaxLength(300);
        builder.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(100);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Terms)
            .WithOne()
            .HasForeignKey(x => x.ContractVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ContractVersion.Terms))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsEditable);

        builder.HasIndex(x => new { x.ContractId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_contract_versions_contract_number");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_contract_versions_organization_status");
    }
}

/// <summary>
/// Mapping for <see cref="ContractTerm"/>.
/// </summary>
/// <remarks>
/// The same column shape as <c>offer_terms</c> and deliberately so: reconciliation
/// compares the two, and a second money representation would make that comparison
/// a conversion. Money is <c>numeric</c>, never floating point (CLAUDE.md section 5).
/// </remarks>
public sealed class ContractTermConfiguration : IEntityTypeConfiguration<ContractTerm>
{
    public void Configure(EntityTypeBuilder<ContractTerm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_terms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ContractTermId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractVersionId)
            .HasColumnName("contract_version_id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .IsRequired();

        builder.Property(x => x.Code).HasColumnName("code").HasConversion<int>().IsRequired();
        builder.Property(x => x.ValueKind).HasColumnName("value_kind").HasConversion<int>().IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)");

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(x => x.NumericValue)
            .HasColumnName("numeric_value")
            .HasColumnType("numeric(19,6)");

        builder.Property(x => x.IntegerValue).HasColumnName("integer_value");
        builder.Property(x => x.TextValue).HasColumnName("text_value").HasMaxLength(2000);
        builder.Property(x => x.BooleanValue).HasColumnName("boolean_value");
        builder.Property(x => x.DateValue).HasColumnName("date_value");
        builder.Property(x => x.Unit).HasColumnName("unit").HasConversion<int?>();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.ClauseReference).HasColumnName("clause_reference").HasMaxLength(100);
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.Privilege).HasColumnName("privilege").HasConversion<int>().IsRequired();

        builder.Ignore(x => x.AsMoney);
        builder.Ignore(x => x.IsEconomic);

        // One row per term code per version, for the reason offer terms have it:
        // reconciliation joins on the code, and two rows sharing one would make the
        // comparison ambiguous.
        builder.HasIndex(x => new { x.ContractVersionId, x.Code })
            .IsUnique()
            .HasDatabaseName("ux_contract_terms_version_code");
    }
}

/// <summary>
/// Mapping for <see cref="RightsGrant"/>.
/// </summary>
/// <remarks>
/// A record of what a contract says was granted. Not a title chain, not a
/// verification that the grantor held what they purported to grant, and not a
/// clearance: a row here means an instrument used those words (ADR-0022).
/// </remarks>
public sealed class RightsGrantConfiguration : IEntityTypeConfiguration<RightsGrant>
{
    public void Configure(EntityTypeBuilder<RightsGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rights_grants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new RightsGrantId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ContractVersionId)
            .HasColumnName("contract_version_id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .IsRequired();

        builder.Property(x => x.ClauseReference).HasColumnName("clause_reference").HasMaxLength(100);
        builder.Property(x => x.GrantorPartyId).HasColumnName("grantor_party_id").IsRequired();
        builder.Property(x => x.GranteePartyId).HasColumnName("grantee_party_id").IsRequired();
        builder.Property(x => x.RightType).HasColumnName("right_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Medium).HasColumnName("medium").HasConversion<int>().IsRequired();
        builder.Property(x => x.Territory).HasColumnName("territory").HasConversion<int>().IsRequired();

        // Free text on purpose. A structured territory model would be a
        // geopolitical ontology, which M8 does not build; the four coarse values
        // cover what the agency actually filters on, and anything else is the
        // clause's own words (ADR-0022).
        builder.Property(x => x.TerritoryDetail).HasColumnName("territory_detail").HasMaxLength(1000);

        builder.Property(x => x.Exclusivity)
            .HasColumnName("exclusivity")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.PeriodKind).HasColumnName("period_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on");
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");

        builder.Property(x => x.SourcePropertyId)
            .HasColumnName("source_property_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SourcePropertyId(value.Value) : null);

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ProjectId(value.Value) : null);

        builder.Property(x => x.Reservations).HasColumnName("reservations").HasMaxLength(4000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(x => x.SupersededByGrantId)
            .HasColumnName("superseded_by_grant_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new RightsGrantId(value.Value) : null);

        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Ignore(x => x.ExcludesOthers);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContractVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractVersionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractParty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.GrantorPartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_rights_grants_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_rights_grants_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.ProjectId })
            .HasDatabaseName("ix_rights_grants_organization_project");

        builder.HasIndex(x => new { x.OrganizationId, x.RightType, x.Medium })
            .HasDatabaseName("ix_rights_grants_organization_right_medium");
    }
}

/// <summary>
/// Mapping for <see cref="ContractOption"/> and the events it owns.
/// </summary>
/// <remarks>
/// The deadline is stored as its rule, plus the date that rule currently resolves
/// to. The resolved date is a cache of a pure function and is recomputed whenever
/// the anchor becomes known; the rule is what the contract actually says. Storing
/// only the date would lose why, and storing only the rule would make every work
/// queue scan the table (ADR-0022).
/// </remarks>
public sealed class ContractOptionConfiguration : IEntityTypeConfiguration<ContractOption>
{
    public void Configure(EntityTypeBuilder<ContractOption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_options");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ContractOptionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ContractVersionId)
            .HasColumnName("contract_version_id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .IsRequired();

        builder.Property(x => x.ClauseReference).HasColumnName("clause_reference").HasMaxLength(100);
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.HolderPartyId).HasColumnName("holder_party_id").IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ProjectId(value.Value) : null);

        builder.Property(x => x.SourcePropertyId)
            .HasColumnName("source_property_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SourcePropertyId(value.Value) : null);

        builder.Property(x => x.WindowOpensOn).HasColumnName("window_opens_on");
        builder.Property(x => x.ResolvedDeadlineOn).HasColumnName("resolved_deadline_on");
        builder.Property(x => x.ExerciseMethod).HasColumnName("exercise_method").HasMaxLength(500);
        builder.Property(x => x.NoticeRequirementId).HasColumnName("notice_requirement_id");

        builder.Property(x => x.EconomicsTermId)
            .HasColumnName("economics_term_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractTermId(value.Value) : null);

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.ResolvedOn).HasColumnName("resolved_on");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        M8Deadlines.Map(builder.OwnsOne(x => x.Deadline), "deadline");

        builder.Navigation(x => x.Deadline).IsRequired();

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ContractOptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ContractOption.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsTerminal);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContractVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractVersionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractParty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.HolderPartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_contract_options_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_contract_options_organization_status");

        // The work queue reads this: options still available, ordered by the date
        // their deadline resolved to.
        builder.HasIndex(x => new { x.OrganizationId, x.ResolvedDeadlineOn })
            .HasDatabaseName("ix_contract_options_organization_deadline");
    }
}

/// <summary>Mapping for <see cref="OptionEvent"/>.</summary>
public sealed class OptionEventConfiguration : IEntityTypeConfiguration<OptionEvent>
{
    public void Configure(EntityTypeBuilder<OptionEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("option_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ContractOptionId)
            .HasColumnName("contract_option_id")
            .HasConversion(id => id.Value, value => new ContractOptionId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Transition).HasColumnName("transition").HasConversion<int?>();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ContractOptionId, x.RecordedAt })
            .HasDatabaseName("ix_option_events_option_recorded");
    }
}

/// <summary>Mapping for <see cref="Obligation"/> and the events it owns.</summary>
public sealed class ObligationConfiguration : IEntityTypeConfiguration<Obligation>
{
    public void Configure(EntityTypeBuilder<Obligation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("obligations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ObligationId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ContractVersionId)
            .HasColumnName("contract_version_id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .IsRequired();

        builder.Property(x => x.ClauseReference).HasColumnName("clause_reference").HasMaxLength(100);
        builder.Property(x => x.ObligorPartyId).HasColumnName("obligor_party_id").IsRequired();
        builder.Property(x => x.ObligeePartyId).HasColumnName("obligee_party_id").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ResolvedDueOn).HasColumnName("resolved_due_on");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.ResolvedOn).HasColumnName("resolved_on");

        builder.Property(x => x.RelatedOptionId)
            .HasColumnName("related_option_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractOptionId(value.Value) : null);

        builder.Property(x => x.RelatedRightsGrantId)
            .HasColumnName("related_rights_grant_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new RightsGrantId(value.Value) : null);

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.Privilege).HasColumnName("privilege").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        M8Deadlines.Map(builder.OwnsOne(x => x.Due), "due");

        builder.Navigation(x => x.Due).IsRequired();

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ObligationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Obligation.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsOutstanding);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContractVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractVersionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractParty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ObligorPartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractOption>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.RelatedOptionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RightsGrant>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.RelatedRightsGrantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_obligations_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_obligations_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.ResolvedDueOn })
            .HasDatabaseName("ix_obligations_organization_due");
    }
}

/// <summary>Mapping for <see cref="ObligationEvent"/>.</summary>
public sealed class ObligationEventConfiguration : IEntityTypeConfiguration<ObligationEvent>
{
    public void Configure(EntityTypeBuilder<ObligationEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("obligation_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ObligationId)
            .HasColumnName("obligation_id")
            .HasConversion(id => id.Value, value => new ObligationId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Transition).HasColumnName("transition").HasConversion<int?>();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ObligationId, x.RecordedAt })
            .HasDatabaseName("ix_obligation_events_obligation_recorded");
    }
}

/// <summary>Mapping for <see cref="NoticeRequirement"/>.</summary>
public sealed class NoticeRequirementConfiguration : IEntityTypeConfiguration<NoticeRequirement>
{
    public void Configure(EntityTypeBuilder<NoticeRequirement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notice_requirements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new NoticeRequirementId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ContractVersionId)
            .HasColumnName("contract_version_id")
            .HasConversion(id => id.Value, value => new ContractVersionId(value))
            .IsRequired();

        builder.Property(x => x.ClauseReference).HasColumnName("clause_reference").HasMaxLength(100);
        builder.Property(x => x.ObligorPartyId).HasColumnName("obligor_party_id").IsRequired();
        builder.Property(x => x.RecipientPartyId).HasColumnName("recipient_party_id").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ResolvedDueOn).HasColumnName("resolved_due_on");
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();

        // A reference to where the notice address is recorded, not the address.
        // Copying it here would create a second copy that quietly goes stale.
        builder.Property(x => x.AddressReference).HasColumnName("address_reference").HasMaxLength(500);

        builder.Property(x => x.RelatedOptionId)
            .HasColumnName("related_option_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractOptionId(value.Value) : null);

        builder.Property(x => x.RelatedObligationId)
            .HasColumnName("related_obligation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ObligationId(value.Value) : null);

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        M8Deadlines.Map(builder.OwnsOne(x => x.Due), "due");

        builder.Navigation(x => x.Due).IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContractVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractVersionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractParty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ObligorPartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_notice_requirements_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.ResolvedDueOn })
            .HasDatabaseName("ix_notice_requirements_organization_due");
    }
}

/// <summary>
/// Mapping for <see cref="NoticeRecord"/>.
/// </summary>
/// <remarks>
/// An assertion that a notice passed between the parties. AgencyOS did not send
/// it and cannot confirm it arrived: there is no delivery status column, because
/// the system has no way to know one (ADR-0022).
/// </remarks>
public sealed class NoticeRecordConfiguration : IEntityTypeConfiguration<NoticeRecord>
{
    public void Configure(EntityTypeBuilder<NoticeRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notice_records");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.NoticeRequirementId)
            .HasColumnName("notice_requirement_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new NoticeRequirementId(value.Value) : null);

        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.SenderPartyId).HasColumnName("sender_party_id").IsRequired();
        builder.Property(x => x.RecipientPartyId).HasColumnName("recipient_party_id").IsRequired();
        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(500);
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NoticeRequirement>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.NoticeRequirementId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ContractId, x.OccurredOn })
            .HasDatabaseName("ix_notice_records_contract_occurred");
    }
}

/// <summary>
/// Mapping for <see cref="ContractRelationship"/>.
/// </summary>
/// <remarks>
/// An amendment is a separate executed instrument, not version five of the paper
/// it changes. This table is how the two stay connected without one pretending to
/// be a draft of the other (ADR-0022).
/// </remarks>
public sealed class ContractRelationshipConfiguration : IEntityTypeConfiguration<ContractRelationship>
{
    public void Configure(EntityTypeBuilder<ContractRelationship> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_relationships");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.RelatedContractId)
            .HasColumnName("related_contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.RelatedContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ContractId, x.RelatedContractId, x.Kind })
            .IsUnique()
            .HasDatabaseName("ux_contract_relationships_pair_kind");

        builder.HasIndex(x => x.RelatedContractId)
            .HasDatabaseName("ix_contract_relationships_related");
    }
}

/// <summary>Mapping for <see cref="ContractTaskLink"/>.</summary>
public sealed class ContractTaskLinkConfiguration : IEntityTypeConfiguration<ContractTaskLink>
{
    public void Configure(EntityTypeBuilder<ContractTaskLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("contract_task_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.TaskItemId)
            .HasColumnName("task_item_id")
            .HasConversion(id => id.Value, value => new TaskItemId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ObligationId)
            .HasColumnName("obligation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ObligationId(value.Value) : null);

        builder.Property(x => x.ContractOptionId)
            .HasColumnName("contract_option_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractOptionId(value.Value) : null);

        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Obligation>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ObligationId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContractOption>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractOptionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A task belongs to at most one piece of contract work, on the M7 precedent.
        builder.HasIndex(x => x.TaskItemId)
            .IsUnique()
            .HasDatabaseName("ux_contract_task_links_task");

        builder.HasIndex(x => x.ContractId)
            .HasDatabaseName("ix_contract_task_links_contract");
    }
}

/// <summary>
/// Maps a <see cref="DeadlineRule"/> into the owner's own row.
/// </summary>
/// <remarks>
/// The rule is a value, not an entity: it has no identity of its own and never
/// outlives the option or obligation carrying it, so it lives in the same table as
/// a prefixed group of columns. Options, obligations and notice requirements all
/// use the identical shape, so one function maps all three and they cannot drift
/// apart.
/// </remarks>
internal static class M8Deadlines
{
    internal static void Map<TOwner>(OwnedNavigationBuilder<TOwner, DeadlineRule> builder, string prefix)
        where TOwner : class
    {
        builder.Property(x => x.Kind).HasColumnName($"{prefix}_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.On).HasColumnName($"{prefix}_on");
        builder.Property(x => x.Anchor).HasColumnName($"{prefix}_anchor").HasConversion<int?>();
        builder.Property(x => x.Offset).HasColumnName($"{prefix}_offset");
        builder.Property(x => x.Unit).HasColumnName($"{prefix}_unit").HasConversion<int?>();
        builder.Property(x => x.Before).HasColumnName($"{prefix}_before").IsRequired();
        builder.Property(x => x.Basis).HasColumnName($"{prefix}_basis").HasConversion<int>().IsRequired();

        // The clause's own wording, kept whether or not the rule could be
        // structured. It is what a person reads when the machine cannot resolve it.
        builder.Property(x => x.Description).HasColumnName($"{prefix}_description").HasMaxLength(1000);

        builder.Ignore(x => x.NeedsAnchorDate);
    }
}
