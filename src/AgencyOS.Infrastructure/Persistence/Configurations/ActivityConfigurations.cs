using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="Interaction"/> and its participants.</summary>
public sealed class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("interactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InteractionId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Type).HasColumnName("interaction_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(512).IsRequired();
        builder.Property(x => x.DetailedNotes).HasColumnName("detailed_notes");
        builder.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Participants)
            .WithOne()
            .HasForeignKey(x => x.InteractionId)
            .OnDelete(DeleteBehavior.Cascade);

        // The aggregate owns its participants through a backing field; EF must go
        // through the field rather than the read-only property.
        IMutableNavigation participants = builder.Metadata.FindNavigation(nameof(Interaction.Participants))!;
        participants.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt })
            .HasDatabaseName("ix_interactions_organization_occurred_at");
    }
}

/// <summary>Mapping for <see cref="InteractionParticipant"/>.</summary>
/// <remarks>
/// Person and company references are an exclusive arc. As with relationships, the
/// composite tenant foreign keys and the arc check constraint are installed by
/// explicit SQL in the M2 migration (ADR-0011).
/// </remarks>
public sealed class InteractionParticipantConfiguration : IEntityTypeConfiguration<InteractionParticipant>
{
    public void Configure(EntityTypeBuilder<InteractionParticipant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("interaction_participants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InteractionParticipantId(value));

        builder.Property(x => x.InteractionId)
            .HasColumnName("interaction_id")
            .HasConversion(id => id.Value, value => new InteractionId(value))
            .IsRequired();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

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

        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(128);

        builder.Ignore(x => x.Party);

        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ix_interaction_participants_organization_person");
        builder.HasIndex(x => new { x.OrganizationId, x.CompanyId })
            .HasDatabaseName("ix_interaction_participants_organization_company");

        // A party takes part in an interaction at most once.
        builder.HasIndex(x => new { x.InteractionId, x.PersonId, x.CompanyId })
            .IsUnique()
            .HasDatabaseName("ux_interaction_participants_party");
    }
}

/// <summary>Mapping for <see cref="TaskItem"/>.</summary>
public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tasks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TaskItemId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<int>().IsRequired();
        builder.Property(x => x.DueAt).HasColumnName("due_at");

        builder.Property(x => x.RelatedPersonId)
            .HasColumnName("related_person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.RelatedCompanyId)
            .HasColumnName("related_company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.SourceInteractionId)
            .HasColumnName("source_interaction_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new InteractionId(value.Value) : null);

        builder.Property(x => x.AssignedTo)
            .HasColumnName("assigned_to")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // The optimistic concurrency token is an explicit column the client sees
        // and sends back, not a hidden xmin: a client that never learns the token
        // cannot be asked to prove what it saw (ADR-0014).
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");

        builder.Property(x => x.CompletedBy)
            .HasColumnName("completed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Ignore(x => x.Subject);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Command Center's three lists are all "open tasks in this tenant,
        // ordered by due date", so that is the index.
        builder.HasIndex(x => new { x.OrganizationId, x.State, x.DueAt })
            .HasDatabaseName("ix_tasks_organization_state_due_at");

        builder.HasIndex(x => new { x.OrganizationId, x.RelatedPersonId })
            .HasDatabaseName("ix_tasks_organization_related_person");
        builder.HasIndex(x => new { x.OrganizationId, x.RelatedCompanyId })
            .HasDatabaseName("ix_tasks_organization_related_company");
    }
}
