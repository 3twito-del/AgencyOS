using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Person"/>.
/// </summary>
/// <remarks>
/// The alternate key on <c>(organization_id, id)</c> exists so that everything
/// referencing a person can use a composite foreign key including the tenant.
/// That is what makes a cross-tenant reference impossible in the database rather
/// than merely unlikely (ADR-0011). It doubles as the tenant-scoped lookup index.
/// </remarks>
public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("people");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PersonId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.MiddleName).HasColumnName("middle_name").HasMaxLength(128);
        builder.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(128);
        builder.Property(x => x.PreferredName).HasColumnName("preferred_name").HasMaxLength(128);

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(x => x.PrimaryCompanyId)
            .HasColumnName("primary_company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256);
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(64);
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

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_people_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.DisplayName })
            .HasDatabaseName("ix_people_organization_display_name");
    }
}

/// <summary>Mapping for <see cref="Company"/>.</summary>
public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("companies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CompanyId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(256);
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Website).HasColumnName("website").HasMaxLength(512);
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

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_companies_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.Name })
            .HasDatabaseName("ix_companies_organization_name");
    }
}

/// <summary>
/// Mapping for <see cref="ProfessionalRelationship"/>.
/// </summary>
/// <remarks>
/// The four endpoint columns are mapped as plain columns rather than as EF
/// relationships. EF cannot express an optional foreign key whose parts are a
/// required tenant column and a nullable party column, so the composite foreign
/// keys and the exclusive-arc check constraints are installed as explicit SQL in
/// the M2 migration. The database ends up with stronger guarantees than EF can
/// describe.
/// </remarks>
public sealed class ProfessionalRelationshipConfiguration : IEntityTypeConfiguration<ProfessionalRelationship>
{
    public void Configure(EntityTypeBuilder<ProfessionalRelationship> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("professional_relationships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new RelationshipId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.FromPersonId)
            .HasColumnName("from_person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.FromCompanyId)
            .HasColumnName("from_company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.ToPersonId)
            .HasColumnName("to_person_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PersonId(value.Value) : null);

        builder.Property(x => x.ToCompanyId)
            .HasColumnName("to_company_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CompanyId(value.Value) : null);

        builder.Property(x => x.Type).HasColumnName("relationship_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Strength).HasColumnName("strength");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
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

        builder.Property(x => x.EndedBy)
            .HasColumnName("ended_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.From);
        builder.Ignore(x => x.To);

        // "Which relationships touch this party" is the query the timeline and the
        // detail views both run, and the arc makes it two lookups per side.
        builder.HasIndex(x => new { x.OrganizationId, x.FromPersonId })
            .HasDatabaseName("ix_relationships_organization_from_person");
        builder.HasIndex(x => new { x.OrganizationId, x.ToPersonId })
            .HasDatabaseName("ix_relationships_organization_to_person");
        builder.HasIndex(x => new { x.OrganizationId, x.FromCompanyId })
            .HasDatabaseName("ix_relationships_organization_from_company");
        builder.HasIndex(x => new { x.OrganizationId, x.ToCompanyId })
            .HasDatabaseName("ix_relationships_organization_to_company");
        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_relationships_organization_status");
    }
}
