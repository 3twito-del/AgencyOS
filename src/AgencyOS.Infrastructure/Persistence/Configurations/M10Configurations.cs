using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The exclusive-arc columns a link table carries.
/// </summary>
/// <remarks>
/// <para>
/// One nullable column per target, each with a real composite foreign key into
/// <c>(organization_id, id)</c> on the target's own table, plus a check constraint
/// that exactly one is set and matches the discriminator.
/// </para>
/// <para>
/// It is fourteen columns, and that is the cost of the design. The alternatives
/// were worse: an untyped <c>(entity_type, guid)</c> pair has no referential
/// integrity at all and cheerfully points at deleted or foreign-tenant rows, and
/// fourteen separate link tables would be fourteen tables saying the same thing
/// with fourteen sets of indexes. Adding a fifteenth target is deliberately not
/// free (ADR-0025).
/// </para>
/// </remarks>
internal static class M10Links
{
    /// <summary>Every target, with the table and column it maps to.</summary>
    internal static readonly (DocumentLinkTarget Target, string Column, string Table)[] Targets =
    [
        (DocumentLinkTarget.Person, "person_id", "people"),
        (DocumentLinkTarget.Company, "company_id", "companies"),
        (DocumentLinkTarget.TalentProfile, "talent_profile_id", "talent_profiles"),
        (DocumentLinkTarget.Material, "material_id", "materials"),
        (DocumentLinkTarget.Project, "project_id", "projects"),
        (DocumentLinkTarget.Package, "package_id", "packages"),
        (DocumentLinkTarget.Opportunity, "opportunity_id", "opportunities"),
        (DocumentLinkTarget.Submission, "submission_id", "submissions"),
        (DocumentLinkTarget.Deal, "deal_id", "deals"),
        (DocumentLinkTarget.Offer, "offer_id", "offers"),
        (DocumentLinkTarget.Contract, "contract_id", "contracts"),
        (DocumentLinkTarget.ContractVersion, "contract_version_id", "contract_versions"),
        (DocumentLinkTarget.Invoice, "invoice_id", "invoices"),
        (DocumentLinkTarget.Payment, "payment_id", "payments"),
    ];

    /// <summary>
    /// Maps the discriminator and the typed columns.
    /// </summary>
    /// <remarks>
    /// The domain exposes one <c>TargetId</c>; the database holds fourteen columns.
    /// The shadow properties are written by the save interceptor below, so the
    /// aggregate stays readable and the schema stays honest.
    /// </remarks>
    internal static void MapArc<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        foreach ((_, string column, _) in Targets)
        {
            builder.Property<Guid?>(column).HasColumnName(column);
        }
    }
}

/// <summary>Mapping for <see cref="BlobObject"/>.</summary>
/// <remarks>
/// The digest is unique per organization, which is what makes deduplication
/// tenant-scoped. A global unique index would let one tenant discover another's
/// files by uploading a candidate and watching the insert fail (ADR-0024).
/// </remarks>
public sealed class BlobObjectConfiguration : IEntityTypeConfiguration<BlobObject>
{
    public void Configure(EntityTypeBuilder<BlobObject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("blob_objects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BlobObjectId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ByteLength).HasColumnName("byte_length").IsRequired();
        builder.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(150);

        builder.Property(x => x.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(400)
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.ScanState)
            .HasColumnName("scan_state")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ScannedAt).HasColumnName("scanned_at");
        builder.Property(x => x.ScannerName).HasColumnName("scanner_name").HasMaxLength(100);
        builder.Property(x => x.ScanDetail).HasColumnName("scan_detail").HasMaxLength(500);

        builder.HasIndex(x => new { x.OrganizationId, x.ContentHash })
            .HasDatabaseName("ux_blob_objects_organization_hash")
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="BlobIngestion"/>, the staging ledger.</summary>
public sealed class BlobIngestionConfiguration : IEntityTypeConfiguration<BlobIngestion>
{
    public void Configure(EntityTypeBuilder<BlobIngestion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("blob_ingestions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BlobIngestionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(400);

        builder.Property(x => x.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(x => x.ByteLength).HasColumnName("byte_length");

        builder.Property(x => x.DisplayFileName)
            .HasColumnName("display_file_name")
            .HasMaxLength(300);

        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.StartedBy)
            .HasColumnName("started_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        // The sweeper's index. Only unfinished attempts are of interest, so a
        // partial index keeps it the size of the backlog rather than the size of
        // every upload ever made (ADR-0024).
        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("ix_blob_ingestions_unfinished")
            .HasFilter("state IN (1, 2)");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="Document"/>.</summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("documents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(x => x.Sensitivity)
            .HasColumnName("sensitivity")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.ArchiveReason).HasColumnName("archive_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasMany(x => x.Versions)
            .WithOne()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Links)
            .WithOne()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Links).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_documents_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.Kind })
            .HasDatabaseName("ix_documents_organization_kind");

        // Sensitivity is indexed because every list narrows by the caller's
        // readable set before it counts anything (ADR-0025).
        builder.HasIndex(x => new { x.OrganizationId, x.Sensitivity })
            .HasDatabaseName("ix_documents_organization_sensitivity");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="DocumentVersion"/>.</summary>
/// <remarks>
/// Immutable once written, enforced by a trigger in the migration as well as by the
/// aggregate. The sequence is unique per document, so two concurrent uploads cannot
/// both claim to be version three (ADR-0024).
/// </remarks>
public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("document_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DocumentVersionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();

        builder.Property(x => x.BlobObjectId)
            .HasColumnName("blob_object_id")
            .HasConversion(id => id.Value, value => new BlobObjectId(value))
            .IsRequired();

        builder.Property(x => x.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ByteLength).HasColumnName("byte_length").IsRequired();

        builder.Property(x => x.DisplayFileName)
            .HasColumnName("display_file_name")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.MediaType)
            .HasColumnName("media_type")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();

        builder.Property(x => x.SourceExternalReference)
            .HasColumnName("source_external_reference")
            .HasMaxLength(500);

        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Property(x => x.ExtractionState)
            .HasColumnName("extraction_state")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ExtractedText).HasColumnName("extracted_text");

        builder.Property(x => x.ExtractionDetail)
            .HasColumnName("extraction_detail")
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.DocumentId, x.Sequence })
            .HasDatabaseName("ux_document_versions_document_sequence")
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.ContentHash })
            .HasDatabaseName("ix_document_versions_organization_hash");
    }
}

/// <summary>Mapping for <see cref="DocumentLink"/>.</summary>
public sealed class DocumentLinkConfiguration : IEntityTypeConfiguration<DocumentLink>
{
    public void Configure(EntityTypeBuilder<DocumentLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("document_links");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        builder.Property(x => x.Target).HasColumnName("target").HasConversion<int>().IsRequired();

        // The domain's single readable view of whichever typed column was written.
        // The columns themselves carry the foreign keys (ADR-0025).
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();

        M10Links.MapArc(builder);

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.Property(x => x.LinkedBy)
            .HasColumnName("linked_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.DocumentId, x.Target, x.TargetId })
            .HasDatabaseName("ux_document_links_document_target")
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.Target, x.TargetId })
            .HasDatabaseName("ix_document_links_target");
    }
}

/// <summary>Mapping for <see cref="DocumentEvent"/>.</summary>
public sealed class DocumentEventConfiguration : IEntityTypeConfiguration<DocumentEvent>
{
    public void Configure(EntityTypeBuilder<DocumentEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("document_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(2000);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.DocumentId, x.OccurredAt })
            .HasDatabaseName("ix_document_events_document");
    }
}

/// <summary>
/// The M4, M6, M8 and M9 seams M10 closes.
/// </summary>
/// <remarks>
/// <para>
/// Additive and nullable, every one. A material, a submission, a drafting version
/// and an invoice all recorded facts about files AgencyOS had never seen; each may
/// now name the canonical version that holds the bytes, and each keeps saying
/// nothing where nobody has uploaded anything (ADR-0024).
/// </para>
/// <para>
/// Deliberately not a foreign key to <c>document_versions</c> declared through EF's
/// navigation model: these are references from four different aggregates, and
/// modelling them as navigations would drag the document graph into four unrelated
/// loads. The composite tenant foreign keys are declared in the migration instead.
/// </para>
/// </remarks>
public sealed class M10SeamConfiguration :
    IEntityTypeConfiguration<Domain.Talent.Material>,
    IEntityTypeConfiguration<Domain.Legal.ContractVersion>,
    IEntityTypeConfiguration<Domain.Finance.Invoice>,
    IEntityTypeConfiguration<Domain.Opportunities.SubmissionMaterial>
{
    public void Configure(EntityTypeBuilder<Domain.Talent.Material> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);
    }

    public void Configure(EntityTypeBuilder<Domain.Legal.ContractVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);

        // Null until bytes exist. A digest AgencyOS did not compute would be a
        // claim about identity it cannot support, which is why M8 carried none.
        builder.Property(x => x.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength();
    }

    public void Configure(EntityTypeBuilder<Domain.Finance.Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);
    }

    public void Configure(EntityTypeBuilder<Domain.Opportunities.SubmissionMaterial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);
    }
}
