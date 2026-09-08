using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Deal"/> and the events it owns.
/// </summary>
/// <remarks>
/// The lineage to M6 is a composite foreign key over the triple
/// (organization, opportunity, target), so a deal cannot anchor to a target from
/// another pursuit or another tenant. That needs a unique constraint on the same
/// triple in <c>opportunity_targets</c>, which the migration adds; it is additive
/// and changes nothing about how M6 behaves (ADR-0011, ADR-0021).
/// </remarks>
public sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deals");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DealId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.OpportunityId)
            .HasColumnName("opportunity_id")
            .HasConversion(id => id.Value, value => new OpportunityId(value))
            .IsRequired();

        builder.Property(x => x.OpportunityTargetId)
            .HasColumnName("opportunity_target_id")
            .HasConversion(id => id.Value, value => new OpportunityTargetId(value))
            .IsRequired();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.OpenedOn).HasColumnName("opened_on").IsRequired();
        builder.Property(x => x.ClosedOn).HasColumnName("closed_on");
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000);
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

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // The whole triple, so "this target belongs to that opportunity" is a
        // database fact rather than a check the application performs.
        builder.HasOne<OpportunityTarget>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OpportunityId, x.OpportunityTargetId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.OpportunityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.DealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Deal.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Derived from the columns above, never stored. EF would otherwise take
        // the first two for columns and refuse to translate them.
        builder.Ignore(x => x.IsTerminal);
        builder.Ignore(x => x.IsLive);
        builder.Ignore(x => x.AcceptsOfferActivity);
        builder.Ignore(x => x.HasAgreedTerms);

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_deals_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId })
            .HasDatabaseName("ix_deals_organization_owner");

        builder.HasIndex(x => new { x.OrganizationId, x.UpdatedAt })
            .HasDatabaseName("ix_deals_organization_updated");

        builder.HasIndex(x => x.OpportunityTargetId)
            .HasDatabaseName("ix_deals_opportunity_target");
    }
}

/// <summary>Mapping for <see cref="DealEvent"/>.</summary>
public sealed class DealEventConfiguration : IEntityTypeConfiguration<DealEvent>
{
    public void Configure(EntityTypeBuilder<DealEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deal_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.DealId)
            .HasColumnName("deal_id")
            .HasConversion(id => id.Value, value => new DealId(value))
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

        builder.HasIndex(x => new { x.DealId, x.RecordedAt })
            .HasDatabaseName("ix_deal_events_deal_recorded");
    }
}

/// <summary>
/// Mapping for <see cref="Offer"/> and the terms and events it owns.
/// </summary>
/// <remarks>
/// The two partial unique indexes on this table carry the milestone's central
/// invariants: at most one standing offer and at most one accepted offer per
/// negotiation. They are added in raw SQL by the migration, because EF cannot
/// express a filtered index over a converted enum column portably.
/// </remarks>
public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("offers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OfferId(value))
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

        builder.Property(x => x.RespondsToOfferId)
            .HasColumnName("responds_to_offer_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OfferId(value.Value) : null);

        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.CommunicatedAt).HasColumnName("communicated_at");
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(8000);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.RecordedByUserId)
            .HasColumnName("recorded_by_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasOne<Deal>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.DealId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // The response link is deliberately not an EF relationship. It has to be a
        // foreign key over the whole triple (organization, deal, answered offer),
        // so that "an offer answers one in the same negotiation" is structural
        // rather than checked in code; the migration adds that and the unique
        // constraint it references. A weaker tenant-level relationship here would
        // only be a second, redundant constraint over the same columns.

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Terms)
            .WithOne()
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Offer.Terms))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Offer.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.IsEditable);
        builder.Ignore(x => x.IsStanding);
        builder.Ignore(x => x.IsTerminal);

        builder.HasIndex(x => new { x.DealId, x.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_offers_deal_sequence");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_offers_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.ExpiresAt })
            .HasDatabaseName("ix_offers_organization_expires");
    }
}

/// <summary>
/// Mapping for <see cref="OfferTerm"/>.
/// </summary>
/// <remarks>
/// Every numeric column is <c>numeric</c>, never <c>double precision</c>. Money is
/// scale 4 rather than 2 so a three-decimal currency such as KWD is representable
/// without the column deciding what a currency's minor units are; the domain
/// applies each currency's own precision (CLAUDE.md section 5).
/// </remarks>
public sealed class OfferTermConfiguration : IEntityTypeConfiguration<OfferTerm>
{
    public void Configure(EntityTypeBuilder<OfferTerm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("offer_terms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OfferTermId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(id => id.Value, value => new OfferId(value))
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
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.Ignore(x => x.AsMoney);
        builder.Ignore(x => x.IsEconomic);

        // One row per term code per offer. Comparison joins on the code, and two
        // rows sharing one would make the diff ambiguous.
        builder.HasIndex(x => new { x.OfferId, x.Code })
            .IsUnique()
            .HasDatabaseName("ux_offer_terms_offer_code");
    }
}

/// <summary>Mapping for <see cref="OfferEvent"/>.</summary>
public sealed class OfferEventConfiguration : IEntityTypeConfiguration<OfferEvent>
{
    public void Configure(EntityTypeBuilder<OfferEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("offer_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(id => id.Value, value => new OfferId(value))
            .IsRequired();

        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion<int?>();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Transition).HasColumnName("transition").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.OfferId, x.RecordedAt })
            .HasDatabaseName("ix_offer_events_offer_recorded");
    }
}

/// <summary>Mapping for <see cref="DealTaskLink"/>.</summary>
public sealed class DealTaskLinkConfiguration : IEntityTypeConfiguration<DealTaskLink>
{
    public void Configure(EntityTypeBuilder<DealTaskLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deal_task_links");

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

        builder.Property(x => x.DealId)
            .HasColumnName("deal_id")
            .HasConversion(id => id.Value, value => new DealId(value))
            .IsRequired();

        builder.Property(x => x.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OfferId(value.Value) : null);

        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Deal>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.DealId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Offer>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OfferId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A task belongs to at most one negotiation.
        builder.HasIndex(x => x.TaskItemId)
            .IsUnique()
            .HasDatabaseName("ux_deal_task_links_task");

        builder.HasIndex(x => x.DealId)
            .HasDatabaseName("ix_deal_task_links_deal");
    }
}
