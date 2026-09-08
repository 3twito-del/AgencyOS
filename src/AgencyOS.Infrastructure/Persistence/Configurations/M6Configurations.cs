using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Opportunity"/> and the subjects it owns.
/// </summary>
/// <remarks>
/// Child keys are explicitly <c>ValueGeneratedNever</c>, and computed properties
/// are explicitly ignored. M4 was bitten by the first and M5 by the second: EF
/// treats a key-bearing child as an existing row, and takes a LINQ iterator over a
/// collection for a navigation it can add to.
/// </remarks>
public sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunities");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<int>().IsRequired();
        builder.Property(x => x.OpenedOn).HasColumnName("opened_on").IsRequired();
        builder.Property(x => x.ClosedOn).HasColumnName("closed_on");
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<int?>();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.StrategyNotes).HasColumnName("strategy_notes").HasMaxLength(8000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

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

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Subjects)
            .WithOne()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Opportunity.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Opportunity.Subjects))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Derived from the columns and children above, not stored.
        builder.Ignore(x => x.IsTerminal);
        builder.Ignore(x => x.AcceptsMarketActivity);
        builder.Ignore(x => x.PrimarySubject);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_opportunities_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId })
            .HasDatabaseName("ix_opportunities_organization_owner");

        builder.HasIndex(x => new { x.OrganizationId, x.UpdatedAt })
            .HasDatabaseName("ix_opportunities_organization_updated");
    }
}

/// <summary>Mapping for <see cref="OpportunityEvent"/>.</summary>
public sealed class OpportunityEventConfiguration : IEntityTypeConfiguration<OpportunityEvent>
{
    public void Configure(EntityTypeBuilder<OpportunityEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.OpportunityId, x.RecordedAt })
            .HasDatabaseName("ix_opportunity_events_opportunity_recorded");
    }
}

/// <summary>
/// Mapping for <see cref="OpportunitySubject"/>.
/// </summary>
/// <remarks>
/// Four nullable typed foreign keys in an exclusive arc, all tenant-qualified.
/// M5's package elements used a raw identifier and a validator; here the domain can
/// express the choice and the database can enforce it, so it does (ADR-0020).
/// </remarks>
public sealed class OpportunitySubjectConfiguration : IEntityTypeConfiguration<OpportunitySubject>
{
    public void Configure(EntityTypeBuilder<OpportunitySubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_subjects");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.TalentProfileId)
            .HasColumnName("talent_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new TalentProfileId(value.Value) : null);

        builder.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ProjectId(value.Value) : null);

        builder.Property(x => x.PackageId)
            .HasColumnName("package_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PackageId(value.Value) : null);

        builder.Property(x => x.ProjectRoleId)
            .HasColumnName("project_role_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ProjectRoleId(value.Value) : null);

        builder.HasOne<TalentProfile>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TalentProfileId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Package>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PackageId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProjectRole>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProjectRoleId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.TargetId);
        builder.Ignore(x => x.Reference);
    }
}

/// <summary>Mapping for <see cref="OpportunityTarget"/>.</summary>
public sealed class OpportunityTargetConfiguration : IEntityTypeConfiguration<OpportunityTarget>
{
    public void Configure(EntityTypeBuilder<OpportunityTarget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_targets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OpportunityTargetId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        // Added in M7. A deal anchors to (organization, opportunity, target) as one
        // composite foreign key, so "this target belongs to that pursuit" is a
        // database fact rather than a check somebody has to remember to write.
        // Purely additive: it constrains nothing M6 did not already guarantee,
        // because a target's opportunity never changes (ADR-0021).
        builder.HasAlternateKey(x => new { x.OrganizationId, x.OpportunityId, x.Id });

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.ContactPersonId)
            .HasColumnName("contact_person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.Stage).HasColumnName("stage").HasConversion<int>().IsRequired();
        builder.Property(x => x.NextActionOn).HasColumnName("next_action_on");
        builder.Property(x => x.ClosedOn).HasColumnName("closed_on");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CompanyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContactPersonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.OpportunityTargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(OpportunityTarget.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.IsCompanyTarget);

        builder.HasIndex(x => new { x.OpportunityId, x.Stage })
            .HasDatabaseName("ix_opportunity_targets_opportunity_stage");

        builder.HasIndex(x => new { x.OrganizationId, x.NextActionOn })
            .HasDatabaseName("ix_opportunity_targets_organization_next_action");
    }
}

/// <summary>Mapping for <see cref="OpportunityTargetEvent"/>.</summary>
public sealed class OpportunityTargetEventConfiguration
    : IEntityTypeConfiguration<OpportunityTargetEvent>
{
    public void Configure(EntityTypeBuilder<OpportunityTargetEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_target_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityTargetId)
            .HasColumnName("opportunity_target_id")
            .HasConversion(id => id.Value, value => new OpportunityTargetId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.FromStage).HasColumnName("from_stage").HasConversion<int?>();
        builder.Property(x => x.ToStage).HasColumnName("to_stage").HasConversion<int?>();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);

        builder.Property(x => x.SubmissionId)
            .HasColumnName("submission_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SubmissionId(value.Value) : null);

        builder.Property(x => x.PitchId)
            .HasColumnName("pitch_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OpportunityPitchId(value.Value) : null);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.OpportunityTargetId, x.OccurredAt })
            .HasDatabaseName("ix_opportunity_target_events_target_occurred");
    }
}

/// <summary>Mapping for <see cref="Submission"/>.</summary>
public sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("submissions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SubmissionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityTargetId)
            .HasColumnName("opportunity_target_id")
            .HasConversion(id => id.Value, value => new OpportunityTargetId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.SentAt).HasColumnName("sent_at").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(512);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.ResponseExpectedBy).HasColumnName("response_expected_by");
        builder.Property(x => x.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(512);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.SentByUserId)
            .HasColumnName("sent_by_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<OpportunityTarget>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityTargetId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Materials)
            .WithOne()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Submission.Materials))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OpportunityTargetId, x.SentAt })
            .HasDatabaseName("ix_submissions_target_sent");

        builder.HasIndex(x => new { x.OrganizationId, x.ResponseExpectedBy })
            .HasDatabaseName("ix_submissions_organization_response_expected");
    }
}

/// <summary>Mapping for <see cref="SubmissionMaterial"/>.</summary>
public sealed class SubmissionMaterialConfiguration : IEntityTypeConfiguration<SubmissionMaterial>
{
    public void Configure(EntityTypeBuilder<SubmissionMaterial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("submission_materials");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.SubmissionId)
            .HasColumnName("submission_id")
            .HasConversion(id => id.Value, value => new SubmissionId(value))
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id")
            .HasConversion(id => id.Value, value => new MaterialId(value))
            .IsRequired();

        // The snapshot. Immutable once written: it says what the agent believed
        // they were sending, which stops being knowable the moment somebody
        // retitles the material.
        builder.Property(x => x.TitleAtSubmission)
            .HasColumnName("title_at_submission")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.TypeAtSubmission)
            .HasColumnName("type_at_submission")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.VersionLabelAtSubmission)
            .HasColumnName("version_label_at_submission")
            .HasMaxLength(128);

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(x => x.Position).HasColumnName("position").IsRequired();

        builder.HasOne<Material>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.MaterialId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubmissionId, x.MaterialId })
            .IsUnique()
            .HasDatabaseName("ux_submission_materials_pair");
    }
}

/// <summary>Mapping for <see cref="OpportunityPitch"/>.</summary>
public sealed class OpportunityPitchConfiguration : IEntityTypeConfiguration<OpportunityPitch>
{
    public void Configure(EntityTypeBuilder<OpportunityPitch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_pitches");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OpportunityPitchId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityTargetId)
            .HasColumnName("opportunity_target_id")
            .HasConversion(id => id.Value, value => new OpportunityTargetId(value))
            .IsRequired();

        builder.Property(x => x.InteractionId)
            .HasColumnName("interaction_id")
            .HasConversion(id => id.Value, value => new InteractionId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<int>().IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(512);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<OpportunityTarget>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityTargetId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Interaction>()
            .WithMany()
            .HasForeignKey(x => x.InteractionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Materials)
            .WithOne()
            .HasForeignKey(x => x.PitchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(OpportunityPitch.Materials))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // One pitch per interaction. Two would mean the meeting happened twice.
        builder.HasIndex(x => x.InteractionId)
            .IsUnique()
            .HasDatabaseName("ux_opportunity_pitches_interaction");

        builder.HasIndex(x => new { x.OpportunityTargetId, x.OccurredAt })
            .HasDatabaseName("ix_opportunity_pitches_target_occurred");
    }
}

/// <summary>Mapping for <see cref="PitchMaterial"/>.</summary>
public sealed class PitchMaterialConfiguration : IEntityTypeConfiguration<PitchMaterial>
{
    public void Configure(EntityTypeBuilder<PitchMaterial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pitch_materials");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PitchId)
            .HasColumnName("pitch_id")
            .HasConversion(id => id.Value, value => new OpportunityPitchId(value))
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id")
            .HasConversion(id => id.Value, value => new MaterialId(value))
            .IsRequired();

        builder.Property(x => x.TitleAtPitch)
            .HasColumnName("title_at_pitch")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.TypeAtPitch)
            .HasColumnName("type_at_pitch")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.VersionLabelAtPitch)
            .HasColumnName("version_label_at_pitch")
            .HasMaxLength(128);

        builder.Property(x => x.Position).HasColumnName("position").IsRequired();

        builder.HasOne<Material>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.MaterialId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PitchId, x.MaterialId })
            .IsUnique()
            .HasDatabaseName("ux_pitch_materials_pair");
    }
}

/// <summary>Mapping for <see cref="OpportunityTaskLink"/>.</summary>
public sealed class OpportunityTaskLinkConfiguration : IEntityTypeConfiguration<OpportunityTaskLink>
{
    public void Configure(EntityTypeBuilder<OpportunityTaskLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opportunity_task_links");

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

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityTargetId)
            .HasColumnName("opportunity_target_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OpportunityTargetId(value.Value) : null);

        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<OpportunityTarget>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityTargetId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A task belongs to at most one pursuit.
        builder.HasIndex(x => x.TaskItemId)
            .IsUnique()
            .HasDatabaseName("ux_opportunity_task_links_task");

        builder.HasIndex(x => x.OpportunityId)
            .HasDatabaseName("ix_opportunity_task_links_opportunity");
    }
}
