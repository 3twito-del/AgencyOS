using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="TalentProfile"/>.
/// </summary>
/// <remarks>
/// The composite foreign key on <c>(organization_id, person_id)</c> is what makes
/// a profile describing another tenant's person impossible in the database rather
/// than merely unlikely (ADR-0011).
/// </remarks>
public sealed class TalentProfileConfiguration : IEntityTypeConfiguration<TalentProfile>
{
    public void Configure(EntityTypeBuilder<TalentProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("talent_profiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TalentProfileId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.CareerStage).HasColumnName("career_stage").HasConversion<int>().IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000);
        builder.Property(x => x.PositioningNotes).HasColumnName("positioning_notes").HasMaxLength(4000);
        builder.Property(x => x.BaseMarket).HasColumnName("base_market").HasMaxLength(128);
        builder.Property(x => x.Languages).HasColumnName("languages").HasMaxLength(256);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // One profile per person per tenant. A second would split the agency's view
        // of somebody, and whichever a query found would look complete.
        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .IsUnique()
            .HasDatabaseName("ux_talent_profiles_organization_person");

        builder.Metadata
            .FindNavigation(nameof(TalentProfile.Disciplines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Disciplines)
            .WithOne()
            .HasForeignKey(x => x.TalentProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapping for <see cref="TalentDiscipline"/>, an effective-dated child row.</summary>
public sealed class TalentDisciplineConfiguration : IEntityTypeConfiguration<TalentDiscipline>
{
    public void Configure(EntityTypeBuilder<TalentDiscipline> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "talent_disciplines",
            table => table.HasCheckConstraint(
                "ck_talent_disciplines_period",
                "ends_on IS NULL OR ends_on >= starts_on"));

        builder.HasKey(x => x.Id);

        // Application-supplied, not store-generated. EF decides Added vs Modified
        // for an entity discovered through a navigation by whether its key is set;
        // with the default OnAdd generation a new child with a key already assigned
        // is taken for an existing row and issued as an UPDATE that matches
        // nothing.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.TalentProfileId)
            .HasColumnName("talent_profile_id")
            .HasConversion(id => id.Value, value => new TalentProfileId(value))
            .IsRequired();

        builder.Property(x => x.Discipline).HasColumnName("discipline").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => x.TalentProfileId).HasDatabaseName("ix_talent_disciplines_profile");
    }
}

/// <summary>
/// Mapping for <see cref="Prospect"/>.
/// </summary>
/// <remarks>
/// A partial unique index allows at most one open pursuit of a person per tenant.
/// Two would mean two agents courting the same person without knowing about each
/// other - the exact situation this system exists to prevent - so it is refused by
/// the database as well as by the handler.
/// </remarks>
public sealed class ProspectConfiguration : IEntityTypeConfiguration<Prospect>
{
    public void Configure(EntityTypeBuilder<Prospect> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("prospects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ProspectId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Stage).HasColumnName("stage").HasConversion<int>().IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(256);
        builder.Property(x => x.StrategyNotes).HasColumnName("strategy_notes").HasMaxLength(4000);
        builder.Property(x => x.IdentifiedOn).HasColumnName("identified_on").IsRequired();
        builder.Property(x => x.NextFollowUpOn).HasColumnName("next_follow_up_on");

        builder.Property(x => x.ConvertedToRepresentationId)
            .HasColumnName("converted_to_representation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new RepresentationId(value.Value) : null);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Ignore(x => x.IsOpen);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.Stage })
            .HasDatabaseName("ix_prospects_organization_stage");

        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId, x.NextFollowUpOn })
            .HasDatabaseName("ix_prospects_owner_follow_up");

        builder.Metadata
            .FindNavigation(nameof(Prospect.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ProspectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapping for <see cref="ProspectEvent"/>, append-only pursuit history.</summary>
public sealed class ProspectEventConfiguration : IEntityTypeConfiguration<ProspectEvent>
{
    public void Configure(EntityTypeBuilder<ProspectEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("prospect_events");

        builder.HasKey(x => x.Id);

        // Application-supplied, not store-generated. EF decides Added vs Modified
        // for an entity discovered through a navigation by whether its key is set;
        // with the default OnAdd generation a new child with a key already assigned
        // is taken for an existing row and issued as an UPDATE that matches
        // nothing.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProspectId)
            .HasColumnName("prospect_id")
            .HasConversion(id => id.Value, value => new ProspectId(value))
            .IsRequired();

        builder.Property(x => x.FromStage).HasColumnName("from_stage").HasConversion<int?>();
        builder.Property(x => x.ToStage).HasColumnName("to_stage").HasConversion<int>().IsRequired();
        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.HasIndex(x => new { x.ProspectId, x.OccurredOn })
            .HasDatabaseName("ix_prospect_events_prospect_occurred");
    }
}

/// <summary>
/// Mapping for <see cref="Representation"/>.
/// </summary>
/// <remarks>
/// The partial unique index on non-terminal statuses is the last line of defence
/// against a duplicate active representation. The handler checks first and the
/// command is idempotent, but this holds even when both are bypassed - and a
/// duplicate here would mean the agency believed it represented somebody twice.
/// </remarks>
public sealed class RepresentationConfiguration : IEntityTypeConfiguration<Representation>
{
    public void Configure(EntityTypeBuilder<Representation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "representations",
            table => table.HasCheckConstraint(
                "ck_representations_period",
                "ends_on IS NULL OR ends_on >= starts_on"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new RepresentationId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");
        builder.Property(x => x.IsExclusive).HasColumnName("is_exclusive");
        builder.Property(x => x.Territory).HasColumnName("territory").HasMaxLength(256);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Ignore(x => x.IsNonTerminal);
        builder.Ignore(x => x.MakesClient);
        builder.Ignore(x => x.CurrentScopes);
        builder.Ignore(x => x.CurrentTeam);
        builder.Ignore(x => x.Lead);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_representations_organization_status");

        foreach (string navigation in new[]
        {
            nameof(Representation.Events),
            nameof(Representation.Scopes),
            nameof(Representation.Team),
        })
        {
            builder.Metadata.FindNavigation(navigation)!.SetPropertyAccessMode(PropertyAccessMode.Field);
        }

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.RepresentationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Scopes)
            .WithOne()
            .HasForeignKey(x => x.RepresentationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Team)
            .WithOne()
            .HasForeignKey(x => x.RepresentationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapping for <see cref="RepresentationEvent"/>, append-only status history.</summary>
public sealed class RepresentationEventConfiguration : IEntityTypeConfiguration<RepresentationEvent>
{
    public void Configure(EntityTypeBuilder<RepresentationEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("representation_events");

        builder.HasKey(x => x.Id);

        // Application-supplied, not store-generated. EF decides Added vs Modified
        // for an entity discovered through a navigation by whether its key is set;
        // with the default OnAdd generation a new child with a key already assigned
        // is taken for an existing row and issued as an UPDATE that matches
        // nothing.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.RepresentationId)
            .HasColumnName("representation_id")
            .HasConversion(id => id.Value, value => new RepresentationId(value))
            .IsRequired();

        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.HasIndex(x => new { x.RepresentationId, x.OccurredOn })
            .HasDatabaseName("ix_representation_events_representation_occurred");
    }
}

/// <summary>Mapping for <see cref="RepresentationScope"/>, effective-dated.</summary>
public sealed class RepresentationScopeConfiguration : IEntityTypeConfiguration<RepresentationScope>
{
    public void Configure(EntityTypeBuilder<RepresentationScope> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "representation_scopes",
            table => table.HasCheckConstraint(
                "ck_representation_scopes_period",
                "ends_on IS NULL OR ends_on >= starts_on"));

        builder.HasKey(x => x.Id);

        // Application-supplied, not store-generated. EF decides Added vs Modified
        // for an entity discovered through a navigation by whether its key is set;
        // with the default OnAdd generation a new child with a key already assigned
        // is taken for an existing row and issued as an UPDATE that matches
        // nothing.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.RepresentationId)
            .HasColumnName("representation_id")
            .HasConversion(id => id.Value, value => new RepresentationId(value))
            .IsRequired();

        builder.Property(x => x.Area).HasColumnName("area").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => x.RepresentationId).HasDatabaseName("ix_representation_scopes_representation");
    }
}

/// <summary>
/// Mapping for <see cref="RepresentationTeamMember"/>.
/// </summary>
/// <remarks>
/// The user is a real foreign key; that they are a <em>member</em> of the tenant is
/// checked in the application. Making membership a foreign key would forbid keeping
/// the assignment after somebody's membership was revoked, which would delete the
/// history this table exists to hold.
/// </remarks>
public sealed class RepresentationTeamMemberConfiguration : IEntityTypeConfiguration<RepresentationTeamMember>
{
    public void Configure(EntityTypeBuilder<RepresentationTeamMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "representation_team_members",
            table => table.HasCheckConstraint(
                "ck_representation_team_members_period",
                "ends_on IS NULL OR ends_on >= starts_on"));

        builder.HasKey(x => x.Id);

        // Application-supplied, not store-generated. EF decides Added vs Modified
        // for an entity discovered through a navigation by whether its key is set;
        // with the default OnAdd generation a new child with a key already assigned
        // is taken for an existing row and issued as an UPDATE that matches
        // nothing.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.RepresentationId)
            .HasColumnName("representation_id")
            .HasConversion(id => id.Value, value => new RepresentationId(value))
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");

        builder.Ignore(x => x.IsOpen);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.RepresentationId).HasDatabaseName("ix_representation_team_representation");

        builder.HasIndex(x => new { x.OrganizationId, x.UserId })
            .HasDatabaseName("ix_representation_team_organization_user");
    }
}

/// <summary>Mapping for <see cref="Credit"/>.</summary>
public sealed class CreditConfiguration : IEntityTypeConfiguration<Credit>
{
    public void Configure(EntityTypeBuilder<Credit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("credits");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CreditId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(256);
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Year).HasColumnName("year");

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(512);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        // The seam M4 opened, closed by M5. Still nullable, because most
        // historical credits describe work the agency had nothing to do with and
        // will never have a project record; the foreign key is tenant-qualified,
        // so a credit cannot point at another tenant's project.
        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new Domain.Projects.ProjectId(value.Value) : null);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

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

        builder.HasOne<Domain.Projects.Project>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ix_credits_organization_person");

        builder.HasIndex(x => new { x.OrganizationId, x.ProjectId })
            .HasDatabaseName("ix_credits_organization_project");
    }
}

/// <summary>Mapping for <see cref="Material"/>.</summary>
public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("materials");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MaterialId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(128);

        // Constrained to absolute http/https by the domain. A device path is refused
        // rather than stored, because it resolves on exactly one machine.
        builder.Property(x => x.ExternalUri).HasColumnName("external_uri").HasMaxLength(2048);

        builder.Property(x => x.ReceivedOn).HasColumnName("received_on");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(512);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ix_materials_organization_person");
    }
}
