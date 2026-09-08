using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The typed columns behind an intelligence subject.
/// </summary>
/// <remarks>
/// <para>
/// Ten kinds, each with its own column and its own composite foreign key into
/// <c>(organization_id, id)</c> on the subject's own table. That is what gives a
/// subject real referential integrity and real tenant integrity, rather than an
/// untyped pair that cheerfully points at a deleted or foreign-tenant row
/// (ADR-0025, ADR-0030).
/// </para>
/// <para>
/// Adding a kind is a deliberate act: a column, a foreign key and a check
/// constraint. That cost keeps the set small and keeps every member honest.
/// </para>
/// </remarks>
internal static class M11Subjects
{
    internal static readonly (IntelligenceSubjectKind Kind, string Column, string Table)[] Kinds =
    [
        (IntelligenceSubjectKind.Person, "person_id", "people"),
        (IntelligenceSubjectKind.Company, "company_id", "companies"),
        (IntelligenceSubjectKind.TalentProfile, "talent_profile_id", "talent_profiles"),
        (IntelligenceSubjectKind.Project, "project_id", "projects"),
        (IntelligenceSubjectKind.SourceProperty, "source_property_id", "source_properties"),
        (IntelligenceSubjectKind.Package, "package_id", "packages"),
        (IntelligenceSubjectKind.ProjectRole, "project_role_id", "project_roles"),
        (IntelligenceSubjectKind.Opportunity, "opportunity_id", "opportunities"),
        (IntelligenceSubjectKind.Deal, "deal_id", "deals"),
        (IntelligenceSubjectKind.Contract, "contract_id", "contracts"),
    ];

    /// <summary>The five intelligence objects that can name subjects.</summary>
    internal static readonly (IntelligenceOwnerKind Owner, string Column, string Table)[] Owners =
    [
        (IntelligenceOwnerKind.Signal, "signal_id", "signals"),
        (IntelligenceOwnerKind.Thesis, "thesis_id", "theses"),
        (IntelligenceOwnerKind.Prediction, "prediction_id", "predictions"),
        (IntelligenceOwnerKind.Watchlist, "watchlist_id", "watchlists"),
        (IntelligenceOwnerKind.ResearchCase, "research_case_id", "research_cases"),
    ];

    /// <summary>Maps the shared subject columns onto whichever subtype this is.</summary>
    internal static void MapArc(EntityTypeBuilder<IntelligenceSubject> builder)
    {
        foreach ((_, string column, _) in Kinds)
        {
            builder.Property<Guid?>(column).HasColumnName(column);
        }
    }
}

/// <summary>What a research case attaches to, and where each lives.</summary>
internal static class M11ResearchLinks
{
    internal static readonly (ResearchLinkKind Kind, string Column, string Table)[] Kinds =
    [
        (ResearchLinkKind.Source, "source_id", "intelligence_sources"),
        (ResearchLinkKind.Signal, "signal_id", "signals"),
        (ResearchLinkKind.Thesis, "thesis_id", "theses"),
        (ResearchLinkKind.Prediction, "prediction_id", "predictions"),
        (ResearchLinkKind.Task, "task_id", "tasks"),
    ];

    internal static void MapArc<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        foreach ((_, string column, _) in Kinds)
        {
            builder.Property<Guid?>(column).HasColumnName(column);
        }
    }
}

/// <summary>Which intelligence object a curated event happened to.</summary>
internal static class M11EventOwners
{
    internal static readonly (IntelligenceOwnerKind Owner, string Column, string Table)[] Owners =
    [
        (IntelligenceOwnerKind.Source, "source_id", "intelligence_sources"),
        (IntelligenceOwnerKind.Signal, "signal_id", "signals"),
        (IntelligenceOwnerKind.Thesis, "thesis_id", "theses"),
        (IntelligenceOwnerKind.Prediction, "prediction_id", "predictions"),
        (IntelligenceOwnerKind.Watchlist, "watchlist_id", "watchlists"),
        (IntelligenceOwnerKind.TalentRadarEntry, "radar_entry_id", "talent_radar_entries"),
        (IntelligenceOwnerKind.ResearchCase, "research_case_id", "research_cases"),
    ];

    internal static void MapArc<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        foreach ((_, string column, _) in Owners)
        {
            builder.Property<Guid?>(column).HasColumnName(column);
        }
    }
}

/// <summary>Mapping for <see cref="IntelligenceSource"/>.</summary>
public sealed class IntelligenceSourceConfiguration
    : IEntityTypeConfiguration<IntelligenceSource>
{
    public void Configure(EntityTypeBuilder<IntelligenceSource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("intelligence_sources");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new IntelligenceSourceId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();

        // The canonical M10 identities. Nothing is copied from them: the title and
        // the notes are what a person recorded about the evidence, and the evidence
        // itself stays where M10 keeps it (ADR-0030).
        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationMessageId(value.Value) : null);

        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2000);
        builder.Property(x => x.Publisher).HasColumnName("publisher").HasMaxLength(200);
        builder.Property(x => x.Author).HasColumnName("author").HasMaxLength(200);

        builder.Property(x => x.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(200);

        // Three times, three columns. Merging any two would invent a history
        // nobody observed (ADR-0030).
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.Reliability).HasColumnName("reliability").IsRequired();

        builder.Property(x => x.ReliabilityRationale)
            .HasColumnName("reliability_rationale")
            .HasMaxLength(1000);

        builder.Property(x => x.ReliabilityAssessedBy)
            .HasColumnName("reliability_assessed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.ReliabilityAssessedAt).HasColumnName("reliability_assessed_at");

        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(x => x.IsHeldByAgencyOS);

        builder.HasIndex(x => new { x.OrganizationId, x.ObservedAt })
            .HasDatabaseName("ix_intelligence_sources_organization_observed")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.OrganizationId, x.Kind })
            .HasDatabaseName("ix_intelligence_sources_organization_kind");
    }
}

/// <summary>Mapping for <see cref="Signal"/> and its evidence.</summary>
public sealed class SignalConfiguration
    : IEntityTypeConfiguration<Signal>, IEntityTypeConfiguration<SignalEvidence>
{
    public void Configure(EntityTypeBuilder<Signal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("signals");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SignalId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Claim).HasColumnName("claim").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

        builder.Property(x => x.Verification).HasColumnName("verification").IsRequired();

        builder.Property(x => x.VerificationNote)
            .HasColumnName("verification_note")
            .HasMaxLength(2000);

        builder.Property(x => x.VerificationChangedBy)
            .HasColumnName("verification_changed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.VerificationChangedAt).HasColumnName("verification_changed_at");

        builder.Property(x => x.Confidence).HasColumnName("confidence").IsRequired();
        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);

        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(x => x.IsStandingClaim);

        builder.HasMany(x => x.Evidence)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.SignalId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Subjects)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.SignalId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Evidence).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Subjects).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.ObservedAt })
            .HasDatabaseName("ix_signals_organization_observed")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.OrganizationId, x.Verification })
            .HasDatabaseName("ix_signals_organization_verification");

        builder.HasIndex(x => new { x.OrganizationId, x.Sensitivity })
            .HasDatabaseName("ix_signals_organization_sensitivity");
    }

    public void Configure(EntityTypeBuilder<SignalEvidence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("signal_evidence");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.SignalId)
            .HasColumnName("signal_id")
            .HasConversion(id => id.Value, value => new SignalId(value))
            .IsRequired();

        builder.Property(x => x.SourceId)
            .HasColumnName("source_id")
            .HasConversion(id => id.Value, value => new IntelligenceSourceId(value))
            .IsRequired();

        builder.Property(x => x.Role).HasColumnName("role").IsRequired();

        // An analyst's selection, never the whole source. Duplicating a document
        // or a message body here would create a second copy with weaker guards
        // (ADR-0025, ADR-0030).
        builder.Property(x => x.Excerpt).HasColumnName("excerpt").HasMaxLength(2000);
        builder.Property(x => x.Locator).HasColumnName("locator").HasMaxLength(200);

        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.SignalId, x.SourceId })
            .HasDatabaseName("ux_signal_evidence_signal_source")
            .IsUnique();

        builder.HasIndex(x => x.SourceId).HasDatabaseName("ix_signal_evidence_source");
    }
}

/// <summary>
/// Mapping for every intelligence subject, in one table.
/// </summary>
/// <remarks>
/// <para>
/// Table-per-hierarchy over the five owners. Five separate tables would each carry
/// the same ten subject columns and the same shared fields, and the question people
/// actually ask — "what does the agency know about this person" — wants one scan
/// rather than five (ADR-0030).
/// </para>
/// <para>
/// The owner column and the subject column are both exclusive arcs with real
/// composite foreign keys, checked by the database.
/// </para>
/// </remarks>
public sealed class IntelligenceSubjectConfiguration
    : IEntityTypeConfiguration<IntelligenceSubject>
{
    public void Configure(EntityTypeBuilder<IntelligenceSubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("intelligence_subjects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("subject_kind").IsRequired();

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // The domain exposes one SubjectId; the database holds it *and* ten typed
        // columns, on M10's precedent. The single column is what every query reads
        // and what the composite index covers; the typed columns are what carry the
        // foreign keys, and a check constraint ties the two together so they cannot
        // disagree. The typed columns are filled centrally on save, so no call site
        // can set the discriminator and forget the column (ADR-0025, ADR-0030).
        builder.Property(x => x.SubjectId).HasColumnName("subject_id").IsRequired();

        M11Subjects.MapArc(builder);

        builder.HasDiscriminator<int>("owner_kind")
            .HasValue<SignalSubject>((int)IntelligenceOwnerKind.Signal)
            .HasValue<ThesisSubject>((int)IntelligenceOwnerKind.Thesis)
            .HasValue<PredictionSubject>((int)IntelligenceOwnerKind.Prediction)
            .HasValue<WatchlistEntry>((int)IntelligenceOwnerKind.Watchlist)
            .HasValue<ResearchCaseSubject>((int)IntelligenceOwnerKind.ResearchCase);

        // Named by property, not by column. "subject_kind" is what the column is
        // called; asking for it here would silently create a shadow property of that
        // name instead of indexing the one that exists.
        builder.HasIndex("owner_kind", nameof(IntelligenceSubject.Kind))
            .HasDatabaseName("ix_intelligence_subjects_owner_subject");

        // The index that answers "what does the agency know about this record".
        // One scan across signals, theses, predictions, watchlists and research
        // cases at once, which is the whole reason these five live in one table.
        builder.HasIndex(x => new { x.OrganizationId, x.Kind, x.SubjectId })
            .HasDatabaseName("ix_intelligence_subjects_subject");
    }
}

/// <summary>The owner keys of each subject subtype.</summary>
public sealed class IntelligenceSubjectSubtypeConfiguration :
    IEntityTypeConfiguration<SignalSubject>,
    IEntityTypeConfiguration<ThesisSubject>,
    IEntityTypeConfiguration<PredictionSubject>,
    IEntityTypeConfiguration<WatchlistEntry>,
    IEntityTypeConfiguration<ResearchCaseSubject>
{
    public void Configure(EntityTypeBuilder<SignalSubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.SignalId)
            .HasColumnName("signal_id")
            .HasConversion(id => id.Value, value => new SignalId(value));
    }

    public void Configure(EntityTypeBuilder<ThesisSubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.ThesisId)
            .HasColumnName("thesis_id")
            .HasConversion(id => id.Value, value => new ThesisId(value));
    }

    public void Configure(EntityTypeBuilder<PredictionSubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.PredictionId)
            .HasColumnName("prediction_id")
            .HasConversion(id => id.Value, value => new PredictionId(value));
    }

    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.WatchlistId)
            .HasColumnName("watchlist_id")
            .HasConversion(id => id.Value, value => new WatchlistId(value));
    }

    public void Configure(EntityTypeBuilder<ResearchCaseSubject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.ResearchCaseId)
            .HasColumnName("research_case_id")
            .HasConversion(id => id.Value, value => new ResearchCaseId(value));
    }
}
