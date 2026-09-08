using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="Thesis"/>, its revisions and its evidence.</summary>
public sealed class ThesisConfiguration :
    IEntityTypeConfiguration<Thesis>,
    IEntityTypeConfiguration<ThesisRevision>,
    IEntityTypeConfiguration<ThesisEvidence>
{
    public void Configure(EntityTypeBuilder<Thesis> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("theses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ThesisId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();

        // The current position, for reading. The revisions are the record.
        builder.Property(x => x.Proposition)
            .HasColumnName("proposition")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(8000);

        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").IsRequired();
        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.SupersededByThesisId)
            .HasColumnName("superseded_by_thesis_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ThesisId(value.Value) : null);

        builder.Property(x => x.ClosedReason).HasColumnName("closed_reason").HasMaxLength(2000);
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(x => x.IsHeld);

        builder.HasMany(x => x.Revisions)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ThesisId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Evidence)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ThesisId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Subjects)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ThesisId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Evidence).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Subjects).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_theses_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.UpdatedAt })
            .HasDatabaseName("ix_theses_organization_updated")
            .IsDescending(false, true);
    }

    /// <summary>
    /// Mapping for one stated position of a thesis.
    /// </summary>
    /// <remarks>
    /// Immutable once written, enforced in the migration by a trigger. This is what
    /// makes "what did we believe in March" answerable, and an editable revision
    /// would quietly rewrite the agency's own memory (ADR-0030).
    /// </remarks>
    public void Configure(EntityTypeBuilder<ThesisRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("thesis_revisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ThesisId)
            .HasColumnName("thesis_id")
            .HasConversion(id => id.Value, value => new ThesisId(value))
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();

        builder.Property(x => x.Proposition)
            .HasColumnName("proposition")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(8000);
        builder.Property(x => x.Confidence).HasColumnName("confidence").IsRequired();
        builder.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(2000);

        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ThesisId, x.Sequence })
            .HasDatabaseName("ux_thesis_revisions_thesis_sequence")
            .IsUnique();
    }

    public void Configure(EntityTypeBuilder<ThesisEvidence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("thesis_evidence");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ThesisId)
            .HasColumnName("thesis_id")
            .HasConversion(id => id.Value, value => new ThesisId(value))
            .IsRequired();

        builder.Property(x => x.SignalId)
            .HasColumnName("signal_id")
            .HasConversion(id => id.Value, value => new SignalId(value))
            .IsRequired();

        // A stance, not a weight. Nothing sums these (ADR-0030).
        builder.Property(x => x.Stance).HasColumnName("stance").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);

        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.ThesisId, x.SignalId })
            .HasDatabaseName("ux_thesis_evidence_thesis_signal")
            .IsUnique();

        builder.HasIndex(x => x.SignalId).HasDatabaseName("ix_thesis_evidence_signal");
    }
}

/// <summary>Mapping for <see cref="Prediction"/> and its forecast history.</summary>
public sealed class PredictionConfiguration :
    IEntityTypeConfiguration<Prediction>,
    IEntityTypeConfiguration<PredictionRevision>,
    IEntityTypeConfiguration<PredictionEvidence>
{
    public void Configure(EntityTypeBuilder<Prediction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("predictions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PredictionId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Statement)
            .HasColumnName("statement")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.ResolutionCriteria)
            .HasColumnName("resolution_criteria")
            .HasMaxLength(4000);

        builder.Property(x => x.ResolvesBy).HasColumnName("resolves_by").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();

        builder.Property(x => x.Outcome).HasColumnName("outcome");
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(x => x.ResolvedBy)
            .HasColumnName("resolved_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.ResolutionNote)
            .HasColumnName("resolution_note")
            .HasMaxLength(2000);

        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");

        builder.Property(x => x.CancelledReason)
            .HasColumnName("cancelled_reason")
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        // Every one of these is computed from the revisions or the clock. A stored
        // copy would be a second truth that drifts, and the probability especially
        // would erase the forecasting history (ADR-0023, ADR-0030).
        builder.Ignore(x => x.CurrentProbability);
        builder.Ignore(x => x.LatestRevision);
        builder.Ignore(x => x.IsResolved);
        builder.Ignore(x => x.IsCancelled);
        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.BrierScore);

        builder.HasMany(x => x.Revisions)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.PredictionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Evidence)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.PredictionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Subjects)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.PredictionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Evidence).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Subjects).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The work queue for the desk: what is past its deadline and unresolved.
        builder.HasIndex(x => new { x.OrganizationId, x.ResolvesBy })
            .HasDatabaseName("ix_predictions_organization_resolves")
            .HasFilter("outcome IS NULL AND cancelled_at IS NULL");

        builder.HasIndex(x => new { x.OrganizationId, x.Outcome })
            .HasDatabaseName("ix_predictions_organization_outcome");

        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId })
            .HasDatabaseName("ix_predictions_organization_owner");
    }

    /// <summary>
    /// Mapping for one forecast.
    /// </summary>
    /// <remarks>
    /// Immutable once written, enforced by a trigger in the migration. The
    /// calibration record is exactly this list, and an editable revision would let
    /// a forecaster improve their own history (ADR-0030).
    /// </remarks>
    public void Configure(EntityTypeBuilder<PredictionRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("prediction_revisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PredictionId)
            .HasColumnName("prediction_id")
            .HasConversion(id => id.Value, value => new PredictionId(value))
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();

        // numeric(5,4): four decimal places, one basis point. Never a float — two
        // equal forecasts must stay equal (ADR-0023, ADR-0030).
        builder.Property(x => x.Probability)
            .HasColumnName("probability")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(4000);

        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.PredictionId, x.Sequence })
            .HasDatabaseName("ux_prediction_revisions_prediction_sequence")
            .IsUnique();
    }

    public void Configure(EntityTypeBuilder<PredictionEvidence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("prediction_evidence");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.PredictionId)
            .HasColumnName("prediction_id")
            .HasConversion(id => id.Value, value => new PredictionId(value))
            .IsRequired();

        builder.Property(x => x.SourceId)
            .HasColumnName("source_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new IntelligenceSourceId(value.Value) : null);

        builder.Property(x => x.SignalId)
            .HasColumnName("signal_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SignalId(value.Value) : null);

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);

        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => x.PredictionId).HasDatabaseName("ix_prediction_evidence_prediction");
    }
}

/// <summary>Mapping for <see cref="Watchlist"/>.</summary>
public sealed class WatchlistConfiguration : IEntityTypeConfiguration<Watchlist>
{
    public void Configure(EntityTypeBuilder<Watchlist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("watchlists");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new WatchlistId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(2000);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();

        // The anchor for "what is new since I last looked".
        builder.Property(x => x.LastReviewedAt).HasColumnName("last_reviewed_at");

        builder.Property(x => x.LastReviewedBy)
            .HasColumnName("last_reviewed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasMany(x => x.Entries)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.WatchlistId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_watchlists_organization_status");
    }
}

/// <summary>Mapping for <see cref="TalentRadarEntry"/>.</summary>
public sealed class TalentRadarEntryConfiguration : IEntityTypeConfiguration<TalentRadarEntry>
{
    public void Configure(EntityTypeBuilder<TalentRadarEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("talent_radar_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TalentRadarEntryId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.PersonId)
            .HasColumnName("person_id")
            .HasConversion(id => id.Value, value => new PersonId(value))
            .IsRequired();

        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Rationale)
            .HasColumnName("rationale")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.IntendedDisciplines)
            .HasColumnName("intended_disciplines")
            .HasMaxLength(500);

        // Assigned by hand or not at all. Nothing computes a radar priority.
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired();
        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();

        builder.Property(x => x.FirstObservedAt).HasColumnName("first_observed_at").IsRequired();
        builder.Property(x => x.LastReviewedAt).HasColumnName("last_reviewed_at");

        builder.Property(x => x.LastReviewedBy)
            .HasColumnName("last_reviewed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        // The M4 records a conversion produced. Null until one happens, and the
        // entry is only marked converted once both actually exist (ADR-0030).
        builder.Property(x => x.ProspectId)
            .HasColumnName("prospect_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ProspectId(value.Value) : null);

        builder.Property(x => x.TalentProfileId)
            .HasColumnName("talent_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new TalentProfileId(value.Value) : null);

        builder.Property(x => x.ConvertedAt).HasColumnName("converted_at");

        builder.Property(x => x.ConvertedBy)
            .HasColumnName("converted_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.DismissedReason)
            .HasColumnName("dismissed_reason")
            .HasMaxLength(2000);

        builder.Property(x => x.DismissedAt).HasColumnName("dismissed_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(x => x.IsOpen);

        // One person is watched once at a time. A partial unique index makes a
        // second open entry impossible rather than merely unlikely, on the M4
        // precedent for open prospects (ADR-0030).
        builder.HasIndex(x => new { x.OrganizationId, x.PersonId })
            .HasDatabaseName("ux_talent_radar_open_person")
            .IsUnique()
            .HasFilter("status IN (1, 2, 3)");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_talent_radar_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.LastReviewedAt })
            .HasDatabaseName("ix_talent_radar_organization_reviewed");
    }
}

/// <summary>Mapping for <see cref="ResearchCase"/> and its links.</summary>
public sealed class ResearchCaseConfiguration :
    IEntityTypeConfiguration<ResearchCase>,
    IEntityTypeConfiguration<ResearchCaseLink>
{
    public void Configure(EntityTypeBuilder<ResearchCase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("research_cases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ResearchCaseId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Question).HasColumnName("question").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Context).HasColumnName("context").HasMaxLength(8000);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Sensitivity).HasColumnName("sensitivity").IsRequired();

        // A summary for a reader. Anything reusable is a linked signal or thesis.
        builder.Property(x => x.Conclusion).HasColumnName("conclusion").HasMaxLength(8000);

        builder.Property(x => x.OpenedAt).HasColumnName("opened_at").IsRequired();
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(x => x.IsOpen);

        builder.HasMany(x => x.Links)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ResearchCaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Subjects)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ResearchCaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Links).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Subjects).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_research_cases_organization_status");
    }

    public void Configure(EntityTypeBuilder<ResearchCaseLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("research_case_links");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ResearchCaseId)
            .HasColumnName("research_case_id")
            .HasConversion(id => id.Value, value => new ResearchCaseId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.AddedAt).HasColumnName("added_at").IsRequired();

        builder.Property(x => x.AddedBy)
            .HasColumnName("added_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // Held as a column as well as in the typed arc, so a case's links can be
        // read in one scan and a check constraint can prove the two agree.
        builder.Property(x => x.LinkedId).HasColumnName("linked_id").IsRequired();

        M11ResearchLinks.MapArc(builder);

        // One case links one thing once. A second link to the same signal is a
        // duplicate row in a reading list, not a second piece of research.
        builder.HasIndex(x => new { x.ResearchCaseId, x.Kind, x.LinkedId })
            .HasDatabaseName("ux_research_case_links_case_target")
            .IsUnique();

        // The reverse question: which cases are working on this thing.
        builder.HasIndex(x => new { x.OrganizationId, x.Kind, x.LinkedId })
            .HasDatabaseName("ix_research_case_links_target");
    }
}

/// <summary>
/// Mapping for the curated intelligence history.
/// </summary>
/// <remarks>
/// One table with an owner arc rather than seven event tables, because each would
/// carry the same five columns and a timeline wants them interleaved anyway. It is
/// not the audit trail: that answers who is accountable for a change, and this
/// answers what became of a belief (ADR-0012, ADR-0030).
/// </remarks>
public sealed class IntelligenceEventConfiguration : IEntityTypeConfiguration<IntelligenceEvent>
{
    public void Configure(EntityTypeBuilder<IntelligenceEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("intelligence_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OwnerKind).HasColumnName("owner_kind").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();

        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();

        // Never the claim, the proposition or the rationale. Those live on their
        // own rows under their own sensitivity (ADR-0030).
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(2000);

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // The history of one object is read by owner, so the owner is a column
        // rather than seven columns somebody has to coalesce.
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();

        M11EventOwners.MapArc(builder);

        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt })
            .HasDatabaseName("ix_intelligence_events_organization_occurred")
            .IsDescending(false, true);

        // The timeline of one object, newest first. This is the index every detail
        // read uses; without it the history panel scans the whole table.
        builder.HasIndex(x => new { x.OrganizationId, x.OwnerKind, x.OwnerId, x.OccurredAt })
            .HasDatabaseName("ix_intelligence_events_owner")
            .IsDescending(false, false, false, true);
    }
}
