using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Project"/> and everything it owns.
/// </summary>
/// <remarks>
/// <para>
/// The composite alternate key on <c>(organization_id, id)</c> is what lets
/// everything downstream - roles, attachments, packages, credits - point at a
/// project through a tenant-qualified foreign key, so a cross-tenant reference is
/// impossible in the database rather than merely checked (ADR-0011).
/// </para>
/// <para>
/// Child keys are explicitly <c>ValueGeneratedNever</c>. EF decides whether an
/// entity discovered through a navigation is new by whether its key is set; with
/// the default generation a new child arriving with an identifier already assigned
/// is taken for an existing row and issued as an UPDATE that matches nothing. M4
/// found that the hard way.
/// </para>
/// </remarks>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("projects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(400).IsRequired();
        builder.Property(x => x.WorkingTitle).HasColumnName("working_title").HasMaxLength(400);
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Stage).HasColumnName("stage").HasConversion<int>().IsRequired();
        builder.Property(x => x.Logline).HasColumnName("logline").HasMaxLength(1000);
        builder.Property(x => x.Synopsis).HasColumnName("synopsis").HasMaxLength(8000);
        builder.Property(x => x.Year).HasColumnName("year");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);

        builder.Property(x => x.PrimaryCompanyId)
            .HasColumnName("primary_company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.LeadUserId)
            .HasColumnName("lead_user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PrimaryCompanyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.LeadUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Roles)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.CompanyParticipations)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.SourceProperties)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Materials)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Project.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Project.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Project.Attachments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Project.CompanyParticipations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Project.SourceProperties))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Project.Materials))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Derived views over the collections above, not stored state. EF would
        // otherwise take them for navigations and fail on the first save, because
        // a LINQ iterator is not a collection it can add to.
        builder.Ignore(x => x.OpenRoles);
        builder.Ignore(x => x.CurrentAttachments);
        builder.Ignore(x => x.CurrentParticipations);
        builder.Ignore(x => x.StageIsFrozen);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_projects_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.UpdatedAt })
            .HasDatabaseName("ix_projects_organization_updated");
    }
}

/// <summary>Mapping for <see cref="ProjectEvent"/>.</summary>
public sealed class ProjectEventConfiguration : IEntityTypeConfiguration<ProjectEvent>
{
    public void Configure(EntityTypeBuilder<ProjectEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int?>();
        builder.Property(x => x.FromStage).HasColumnName("from_stage").HasConversion<int?>();
        builder.Property(x => x.ToStage).HasColumnName("to_stage").HasConversion<int?>();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ProjectId, x.RecordedAt })
            .HasDatabaseName("ix_project_events_project_recorded");
    }
}

/// <summary>Mapping for <see cref="ProjectRole"/>.</summary>
public sealed class ProjectRoleConfiguration : IEntityTypeConfiguration<ProjectRole>
{
    public void Configure(EntityTypeBuilder<ProjectRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_roles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ProjectRoleId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.IsExclusive).HasColumnName("is_exclusive").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => new { x.ProjectId, x.Status })
            .HasDatabaseName("ix_project_roles_project_status");
    }
}

/// <summary>Mapping for <see cref="Attachment"/>.</summary>
/// <remarks>
/// The person and company foreign keys are both tenant-qualified, so an attachment
/// naming another tenant's person cannot be written. A check constraint enforces
/// that exactly one of them is set, which is the invariant
/// <see cref="AttachmentParty"/> expresses in the domain.
/// </remarks>
public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AttachmentId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.Property(x => x.ProjectRoleId)
            .HasColumnName("project_role_id")
            .HasConversion(id => id.Value, value => new ProjectRoleId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

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

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RoleIsExclusive).HasColumnName("role_is_exclusive").IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(400);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<ProjectRole>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectRoleId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

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

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Attachment.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Derived from whichever party column is set, not a stored value.
        builder.Ignore(x => x.Party);
        builder.Ignore(x => x.IsCurrent);
        builder.Ignore(x => x.HoldsTheRole);

        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ix_attachments_organization_person");

        builder.HasIndex(x => new { x.ProjectId, x.Status })
            .HasDatabaseName("ix_attachments_project_status");
    }
}

/// <summary>Mapping for <see cref="AttachmentEvent"/>.</summary>
public sealed class AttachmentEventConfiguration : IEntityTypeConfiguration<AttachmentEvent>
{
    public void Configure(EntityTypeBuilder<AttachmentEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attachment_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.AttachmentId)
            .HasColumnName("attachment_id")
            .HasConversion(id => id.Value, value => new AttachmentId(value))
            .IsRequired();

        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.AttachmentId, x.RecordedAt })
            .HasDatabaseName("ix_attachment_events_attachment_recorded");
    }
}

/// <summary>Mapping for <see cref="ProjectCompanyParticipation"/>.</summary>
public sealed class ProjectCompanyParticipationConfiguration
    : IEntityTypeConfiguration<ProjectCompanyParticipation>
{
    public void Configure(EntityTypeBuilder<ProjectCompanyParticipation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_company_participations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .HasConversion(id => id.Value, value => new CompanyId(value))
            .IsRequired();

        builder.Property(x => x.Capacity).HasColumnName("capacity").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(x => x.EndsOn).HasColumnName("ends_on");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CompanyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => new { x.OrganizationId, x.CompanyId })
            .HasDatabaseName("ix_project_companies_organization_company");
    }
}

/// <summary>Mapping for <see cref="SourceProperty"/>.</summary>
public sealed class SourcePropertyConfiguration : IEntityTypeConfiguration<SourceProperty>
{
    public void Configure(EntityTypeBuilder<SourceProperty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_properties");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SourcePropertyId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(400).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.AttributedCreator).HasColumnName("attributed_creator").HasMaxLength(400);
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(1000);
        builder.Property(x => x.Provenance).HasColumnName("provenance").HasMaxLength(400);
        builder.Property(x => x.Year).HasColumnName("year");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatorPersonId)
            .HasColumnName("creator_person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CreatorPersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="ProjectSourceProperty"/>.</summary>
public sealed class ProjectSourcePropertyConfiguration : IEntityTypeConfiguration<ProjectSourceProperty>
{
    public void Configure(EntityTypeBuilder<ProjectSourceProperty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_source_properties");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.Property(x => x.SourcePropertyId)
            .HasColumnName("source_property_id")
            .HasConversion(id => id.Value, value => new SourcePropertyId(value))
            .IsRequired();

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.Property(x => x.LinkedBy)
            .HasColumnName("linked_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<SourceProperty>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.SourcePropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ProjectId, x.SourcePropertyId })
            .IsUnique()
            .HasDatabaseName("ux_project_source_properties_pair");
    }
}

/// <summary>Mapping for <see cref="ProjectMaterialLink"/>.</summary>
public sealed class ProjectMaterialLinkConfiguration : IEntityTypeConfiguration<ProjectMaterialLink>
{
    public void Configure(EntityTypeBuilder<ProjectMaterialLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_material_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id")
            .HasConversion(id => id.Value, value => new Domain.Talent.MaterialId(value))
            .IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.Property(x => x.LinkedBy)
            .HasColumnName("linked_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Domain.Talent.Material>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.MaterialId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ProjectId, x.MaterialId })
            .IsUnique()
            .HasDatabaseName("ux_project_material_links_pair");
    }
}

/// <summary>Mapping for <see cref="Package"/>.</summary>
public sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("packages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PackageId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Thesis).HasColumnName("thesis").HasMaxLength(2000);
        builder.Property(x => x.StrategyNotes).HasColumnName("strategy_notes").HasMaxLength(8000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.LeadUserId)
            .HasColumnName("lead_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.LeadUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Elements)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Package.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Package.Elements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_packages_organization_status");
    }
}

/// <summary>Mapping for <see cref="PackageEvent"/>.</summary>
public sealed class PackageEventConfiguration : IEntityTypeConfiguration<PackageEvent>
{
    public void Configure(EntityTypeBuilder<PackageEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("package_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PackageId)
            .HasColumnName("package_id")
            .HasConversion(id => id.Value, value => new PackageId(value))
            .IsRequired();

        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.PackageId, x.RecordedAt })
            .HasDatabaseName("ix_package_events_package_recorded");
    }
}

/// <summary>Mapping for <see cref="PackageElement"/>.</summary>
public sealed class PackageElementConfiguration : IEntityTypeConfiguration<PackageElement>
{
    public void Configure(EntityTypeBuilder<PackageElement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("package_elements");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PackageId)
            .HasColumnName("package_id")
            .HasConversion(id => id.Value, value => new PackageId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
        builder.Property(x => x.Position).HasColumnName("position").IsRequired();
        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // The same thing cannot be in a package twice. Adding it again is what a
        // retry looks like, so the domain returns the existing element; the index
        // catches two requests that race past that check.
        builder.HasIndex(x => new { x.PackageId, x.Kind, x.TargetId })
            .IsUnique()
            .HasDatabaseName("ux_package_elements_target");
    }
}
