using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="MonetaryObligation"/>.
/// </summary>
/// <remarks>
/// Every money column is <c>numeric</c>, never <c>double precision</c>, with the
/// same scale M7 and M8 use so a figure that crossed from a contract term into a
/// finance obligation is bit-for-bit the same figure. The due date reuses M8's
/// deadline column group, so a payment date measured from delivery resolves
/// honestly or not at all (ADR-0022, ADR-0023).
/// </remarks>
public sealed class MonetaryObligationConfiguration : IEntityTypeConfiguration<MonetaryObligation>
{
    public void Configure(EntityTypeBuilder<MonetaryObligation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("monetary_obligations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MonetaryObligationId(value))
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

        builder.Property(x => x.SourceObligationId)
            .HasColumnName("source_obligation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ObligationId(value.Value) : null);

        builder.Property(x => x.SourceTermCode)
            .HasColumnName("source_term_code")
            .HasConversion<int?>();

        builder.Property(x => x.PayerPartyId).HasColumnName("payer_party_id").IsRequired();
        builder.Property(x => x.PayeePartyId).HasColumnName("payee_party_id").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<int>().IsRequired();

        builder.Property(x => x.AmountKind)
            .HasColumnName("amount_kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)");

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(x => x.Quantity).HasColumnName("quantity");

        builder.Property(x => x.UnitAmountValue)
            .HasColumnName("unit_amount_value")
            .HasColumnType("numeric(19,4)");

        builder.Property(x => x.Unit).HasColumnName("unit").HasConversion<int?>();
        builder.Property(x => x.Condition).HasColumnName("condition").HasMaxLength(1000);
        builder.Property(x => x.ResolvedDueOn).HasColumnName("resolved_due_on");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        M8Deadlines.Map(builder.OwnsOne(x => x.Due), "due");

        builder.Navigation(x => x.Due).IsRequired();

        // Derived from the columns above. A stored "is quantified" flag beside the
        // amount would be a second fact that can disagree with the first.
        builder.Ignore(x => x.IsQuantified);
        builder.Ignore(x => x.Amount);
        builder.Ignore(x => x.AcceptsReceivable);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContractVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractVersionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId).HasDatabaseName("ix_monetary_obligations_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_monetary_obligations_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.ResolvedDueOn })
            .HasDatabaseName("ix_monetary_obligations_organization_due");
    }
}

/// <summary>
/// Mapping for <see cref="Receivable"/>.
/// </summary>
/// <remarks>
/// There is no balance column and no status column. Both are derived from the
/// allocations and adjustments every time they are asked for, because a mutable
/// balance drifts the moment two code paths update it differently and the drift is
/// invisible - the number still looks like a number (ADR-0023).
/// </remarks>
public sealed class ReceivableConfiguration : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("receivables");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ReceivableId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.MonetaryObligationId)
            .HasColumnName("monetary_obligation_id")
            .HasConversion(id => id.Value, value => new MonetaryObligationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.PayerPartyId).HasColumnName("payer_party_id").IsRequired();

        // The field that keeps client money and agency money apart.
        builder.Property(x => x.Beneficiary)
            .HasColumnName("beneficiary")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ClientPersonId).HasColumnName("client_person_id");
        builder.Property(x => x.RepresentationId).HasColumnName("representation_id");

        builder.Property(x => x.OriginalAmountValue)
            .HasColumnName("original_amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.AllocatedAmountValue)
            .HasColumnName("allocated_amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.AdjustedAmountValue)
            .HasColumnName("adjusted_amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.DueOn).HasColumnName("due_on");
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);
        builder.Property(x => x.IsClosedByAct).HasColumnName("is_closed_by_act").IsRequired();
        builder.Property(x => x.IsWriteOff).HasColumnName("is_write_off").IsRequired();
        builder.Property(x => x.ClosureReason).HasColumnName("closure_reason").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // Every one of these is arithmetic over rows the database already holds.
        builder.Ignore(x => x.OriginalAmount);
        builder.Ignore(x => x.Outstanding);
        builder.Ignore(x => x.Status);
        builder.Ignore(x => x.AcceptsAllocation);

        builder.HasOne<MonetaryObligation>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.MonetaryObligationId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // The client link is a composite foreign key the migration adds in raw SQL,
        // for the reason M8 used the same shape: the column is a plain Guid at the
        // domain boundary, and EF will not build a key from mismatched types. The
        // constraint is no weaker for being written by hand (ADR-0011).
        builder.HasIndex(x => x.ContractId).HasDatabaseName("ix_receivables_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.DueOn })
            .HasDatabaseName("ix_receivables_organization_due");

        builder.HasIndex(x => new { x.OrganizationId, x.ClientPersonId })
            .HasDatabaseName("ix_receivables_organization_client");

        builder.HasIndex(x => x.MonetaryObligationId)
            .HasDatabaseName("ix_receivables_obligation");
    }
}

/// <summary>Mapping for <see cref="Invoice"/> and its lines.</summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invoices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        // Supplied by the operator, never generated. Uniqueness per tenant is
        // enforced by a partial index the migration adds, because a null reference
        // on an unnumbered draft is ordinary (ADR-0023).
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.DebtorPartyId).HasColumnName("debtor_party_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.IssuedOn).HasColumnName("issued_on");
        builder.Property(x => x.DueOn).HasColumnName("due_on");

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(500);

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Invoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.IsEditable);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ContractId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ContractId).HasDatabaseName("ix_invoices_contract");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_invoices_organization_status");

        builder.HasIndex(x => new { x.OrganizationId, x.DueOn })
            .HasDatabaseName("ix_invoices_organization_due");
    }
}

/// <summary>Mapping for <see cref="InvoiceLine"/>.</summary>
public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invoice_lines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.InvoiceId)
            .HasColumnName("invoice_id")
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .IsRequired();

        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(id => id.Value, value => new ReceivableId(value))
            .IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();

        builder.Ignore(x => x.Amount);

        builder.HasOne<Receivable>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ReceivableId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // One line per receivable per invoice, so the invoice total stays
        // reconcilable against what is owed.
        builder.HasIndex(x => new { x.InvoiceId, x.ReceivableId })
            .IsUnique()
            .HasDatabaseName("ux_invoice_lines_invoice_receivable");
    }
}

/// <summary>
/// Mapping for <see cref="Payment"/> and the allocations it owns.
/// </summary>
/// <remarks>
/// The amount, currency and received date are frozen once recorded by a trigger the
/// migration adds. A payment is what somebody observed, and observations are
/// corrected by recording another one rather than by editing the first (ADR-0023).
/// </remarks>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.PayerPartyId).HasColumnName("payer_party_id");
        builder.Property(x => x.PayerName).HasColumnName("payer_name").HasMaxLength(300);
        builder.Property(x => x.PayeePartyId).HasColumnName("payee_party_id");
        builder.Property(x => x.PayeeName).HasColumnName("payee_name").HasMaxLength(300);

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        // The economic date and the system date, never merged. A wire that landed
        // on Friday and was entered on Monday happened on Friday.
        builder.Property(x => x.ReceivedOn).HasColumnName("received_on").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();

        builder.Property(x => x.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(200);

        builder.Property(x => x.SourceSystem).HasColumnName("source_system").HasMaxLength(100);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(x => x.ReversedByPaymentId)
            .HasColumnName("reversed_by_payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.ReversalOfPaymentId)
            .HasColumnName("reversal_of_payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(1000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasMany(x => x.Allocations)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Payment.Allocations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.Amount);
        builder.Ignore(x => x.Allocated);
        builder.Ignore(x => x.Unapplied);
        builder.Ignore(x => x.HasUnappliedCash);
        builder.Ignore(x => x.AcceptsAllocation);
        builder.Ignore(x => x.IsReversal);

        builder.HasIndex(x => new { x.OrganizationId, x.ReceivedOn })
            .HasDatabaseName("ix_payments_organization_received");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_payments_organization_status");

        // For duplicate detection, not for uniqueness. Two genuinely different
        // payments can carry the same remittance text (ADR-0023).
        builder.HasIndex(x => new { x.OrganizationId, x.ExternalReference })
            .HasDatabaseName("ix_payments_organization_reference");
    }
}

/// <summary>Mapping for <see cref="PaymentAllocation"/>.</summary>
public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_allocations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PaymentAllocationId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .IsRequired();

        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(id => id.Value, value => new ReceivableId(value))
            .IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.IsApplied).HasColumnName("is_applied").IsRequired();
        builder.Property(x => x.AppliedAt).HasColumnName("applied_at").IsRequired();
        builder.Property(x => x.ReversedAt).HasColumnName("reversed_at");
        builder.Property(x => x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);

        builder.Property(x => x.AppliedBy)
            .HasColumnName("applied_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.ReversedBy)
            .HasColumnName("reversed_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Ignore(x => x.Amount);

        builder.HasOne<Receivable>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ReceivableId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReceivableId)
            .HasDatabaseName("ix_payment_allocations_receivable");

        builder.HasIndex(x => x.PaymentId)
            .HasDatabaseName("ix_payment_allocations_payment");
    }
}

/// <summary>Mapping for <see cref="PaymentAdjustment"/>.</summary>
public sealed class PaymentAdjustmentConfiguration : IEntityTypeConfiguration<PaymentAdjustment>
{
    public void Configure(EntityTypeBuilder<PaymentAdjustment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_adjustments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PaymentAdjustmentId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(id => id.Value, value => new ReceivableId(value))
            .IsRequired();

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();

        builder.Property(x => x.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(200);

        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.IsApplied).HasColumnName("is_applied").IsRequired();
        builder.Property(x => x.ReversedAt).HasColumnName("reversed_at");
        builder.Property(x => x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Ignore(x => x.Amount);

        builder.HasOne<Receivable>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ReceivableId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReceivableId)
            .HasDatabaseName("ix_payment_adjustments_receivable");
    }
}

/// <summary>Mapping for <see cref="CommissionRule"/>.</summary>
/// <remarks>
/// Effective-dated, because a commission rate is a term of a relationship and
/// relationships are renegotiated. Editing a rate in place would restate every
/// commission ever calculated under it (ADR-0023).
/// </remarks>
public sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_rules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommissionRuleId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.RepresentationId).HasColumnName("representation_id").IsRequired();
        builder.Property(x => x.ClientPersonId).HasColumnName("client_person_id").IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractId(value.Value) : null);

        builder.Property(x => x.Basis).HasColumnName("basis").HasConversion<int>().IsRequired();

        // Rates carry six places, because a rate is not money and 12.5 per cent is
        // not the same figure as 12.50 dollars.
        builder.Property(x => x.RatePercent)
            .HasColumnName("rate_percent")
            .HasColumnType("numeric(9,6)");

        builder.Property(x => x.FixedAmountValue)
            .HasColumnName("fixed_amount_value")
            .HasColumnType("numeric(19,4)");

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(x => x.TermCode).HasColumnName("term_code").HasConversion<int?>();
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.Provenance).HasColumnName("provenance").HasMaxLength(500);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // Same shape as the receivable's client link: a composite foreign key the
        // migration adds in raw SQL.
        builder.HasIndex(x => new { x.OrganizationId, x.ClientPersonId })
            .HasDatabaseName("ix_commission_rules_organization_client");

        builder.HasIndex(x => new { x.OrganizationId, x.EffectiveFrom })
            .HasDatabaseName("ix_commission_rules_organization_effective");
    }
}

/// <summary>Mapping for <see cref="CommissionEntitlement"/> and its adjustments.</summary>
public sealed class CommissionEntitlementConfiguration
    : IEntityTypeConfiguration<CommissionEntitlement>
{
    public void Configure(EntityTypeBuilder<CommissionEntitlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_entitlements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommissionEntitlementId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.MonetaryObligationId)
            .HasColumnName("monetary_obligation_id")
            .HasConversion(id => id.Value, value => new MonetaryObligationId(value))
            .IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(id => id.Value, value => new ContractId(value))
            .IsRequired();

        builder.Property(x => x.ClientPersonId).HasColumnName("client_person_id").IsRequired();
        builder.Property(x => x.RepresentationId).HasColumnName("representation_id").IsRequired();

        builder.Property(x => x.CommissionRuleId)
            .HasColumnName("commission_rule_id")
            .HasConversion(id => id.Value, value => new CommissionRuleId(value))
            .IsRequired();

        builder.Property(x => x.ClientReceivableId)
            .HasColumnName("client_receivable_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ReceivableId(value.Value) : null);

        builder.Property(x => x.Basis).HasColumnName("basis").HasConversion<int>().IsRequired();

        // The rate as it stood at calculation, never re-read from the rule. A later
        // correction must not silently restate what was already acted on.
        builder.Property(x => x.RatePercentSnapshot)
            .HasColumnName("rate_percent_snapshot")
            .HasColumnType("numeric(9,6)");

        builder.Property(x => x.BasisAmountValue)
            .HasColumnName("basis_amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.EntitledAmountValue)
            .HasColumnName("entitled_amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.GoverningOn).HasColumnName("governing_on").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.CalculatedAt).HasColumnName("calculated_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CalculatedBy)
            .HasColumnName("calculated_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasMany(x => x.Adjustments)
            .WithOne()
            .HasForeignKey(x => x.CommissionEntitlementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(CommissionEntitlement.Adjustments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.EntitledAmount);
        builder.Ignore(x => x.BasisAmount);
        builder.Ignore(x => x.AdjustedAmount);

        builder.HasOne<MonetaryObligation>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.MonetaryObligationId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CommissionRule>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CommissionRuleId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.MonetaryObligationId)
            .HasDatabaseName("ix_commission_entitlements_obligation");

        builder.HasIndex(x => new { x.OrganizationId, x.ClientPersonId })
            .HasDatabaseName("ix_commission_entitlements_organization_client");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_commission_entitlements_organization_status");
    }
}

/// <summary>Mapping for <see cref="CommissionAdjustment"/>.</summary>
public sealed class CommissionAdjustmentConfiguration : IEntityTypeConfiguration<CommissionAdjustment>
{
    public void Configure(EntityTypeBuilder<CommissionAdjustment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_adjustments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.CommissionEntitlementId)
            .HasColumnName("commission_entitlement_id")
            .HasConversion(id => id.Value, value => new CommissionEntitlementId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.IsApplied).HasColumnName("is_applied").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Ignore(x => x.Amount);

        builder.HasIndex(x => x.CommissionEntitlementId)
            .HasDatabaseName("ix_commission_adjustments_entitlement");
    }
}

/// <summary>Mapping for a ledger <see cref="Account"/>.</summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AccountId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<int>().IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Ignore(x => x.IncreasesOnDebit);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One account of each kind per organization. The chart is fixed, and two
        // cash accounts in one tenant would make every balance ambiguous.
        builder.HasIndex(x => new { x.OrganizationId, x.Kind })
            .IsUnique()
            .HasDatabaseName("ux_accounts_organization_kind");
    }
}

/// <summary>
/// Mapping for <see cref="JournalEntry"/> and its lines.
/// </summary>
/// <remarks>
/// Balance is checked three times over: the aggregate refuses to post unbalanced
/// lines, the F# kernel computes the totals, and a deferred constraint trigger the
/// migration adds checks the same arithmetic at commit. Posted entries and lines
/// are immutable by trigger (ADR-0023).
/// </remarks>
public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journal_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new JournalEntryId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        builder.Property(x => x.Memo).HasColumnName("memo").HasMaxLength(500).IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        // Three dates, never merged. Posting date is indexed so a period close can
        // be added later without rewriting journal history.
        builder.Property(x => x.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(x => x.PostingDate).HasColumnName("posting_date").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.PostedAt).HasColumnName("posted_at");

        builder.Property(x => x.PostedBy)
            .HasColumnName("posted_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        // Typed provenance, one per source. A join rather than a convention.
        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ReceivableId(value.Value) : null);

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.PaymentAllocationId)
            .HasColumnName("payment_allocation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentAllocationId(value.Value) : null);

        builder.Property(x => x.PaymentAdjustmentId)
            .HasColumnName("payment_adjustment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentAdjustmentId(value.Value) : null);

        builder.Property(x => x.CommissionEntitlementId)
            .HasColumnName("commission_entitlement_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommissionEntitlementId(value.Value) : null);

        builder.Property(x => x.ReversalOfEntryId)
            .HasColumnName("reversal_of_entry_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new JournalEntryId(value.Value) : null);

        builder.Property(x => x.ReversedByEntryId)
            .HasColumnName("reversed_by_entry_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new JournalEntryId(value.Value) : null);

        builder.Property(x => x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(1000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(JournalEntry.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.Debits);
        builder.Ignore(x => x.Credits);
        builder.Ignore(x => x.IsBalanced);
        builder.Ignore(x => x.IsEditable);
        builder.Ignore(x => x.AffectsBalance);

        builder.HasOne<Receivable>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ReceivableId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PaymentId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaymentAllocation>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PaymentAllocationId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CommissionEntitlement>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.CommissionEntitlementId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.PostingDate })
            .HasDatabaseName("ix_journal_entries_organization_posting");

        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_journal_entries_organization_status");

        builder.HasIndex(x => x.PaymentAllocationId)
            .HasDatabaseName("ix_journal_entries_allocation");

        builder.HasIndex(x => new { x.ReceivableId, x.Source })
            .HasDatabaseName("ix_journal_entries_receivable_source");
    }
}

/// <summary>Mapping for <see cref="JournalLine"/>.</summary>
public sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journal_lines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.JournalEntryId)
            .HasColumnName("journal_entry_id")
            .HasConversion(id => id.Value, value => new JournalEntryId(value))
            .IsRequired();

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => new AccountId(value))
            .IsRequired();

        // An explicit side and a positive amount, never a signed number whose
        // meaning depends on the account's normal balance.
        builder.Property(x => x.Side).HasColumnName("side").HasConversion<int>().IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Memo).HasColumnName("memo").HasMaxLength(500);

        builder.Ignore(x => x.Amount);

        // Tenant-contained by the whole pair, so a line cannot post to another
        // organization's account.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.AccountId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.JournalEntryId).HasDatabaseName("ix_journal_lines_entry");

        builder.HasIndex(x => new { x.AccountId, x.CurrencyCodeValue })
            .HasDatabaseName("ix_journal_lines_account_currency");
    }
}

/// <summary>Mapping for <see cref="FinanceEvent"/>.</summary>
public sealed class FinanceEventConfiguration : IEntityTypeConfiguration<FinanceEvent>
{
    public void Configure(EntityTypeBuilder<FinanceEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();

        builder.Property(x => x.ContractId)
            .HasColumnName("contract_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ContractId(value.Value) : null);

        builder.Property(x => x.MonetaryObligationId)
            .HasColumnName("monetary_obligation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new MonetaryObligationId(value.Value) : null);

        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ReceivableId(value.Value) : null);

        builder.Property(x => x.InvoiceId)
            .HasColumnName("invoice_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new InvoiceId(value.Value) : null);

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.CommissionEntitlementId)
            .HasColumnName("commission_entitlement_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommissionEntitlementId(value.Value) : null);

        builder.Property(x => x.JournalEntryId)
            .HasColumnName("journal_entry_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new JournalEntryId(value.Value) : null);

        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();

        builder.Property(x => x.AmountValue)
            .HasColumnName("amount_value")
            .HasColumnType("numeric(19,4)");

        builder.Property(x => x.CurrencyCodeValue)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1000);

        builder.Property(x => x.RecordedBy)
            .HasColumnName("recorded_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt })
            .HasDatabaseName("ix_finance_events_organization_occurred");

        builder.HasIndex(x => x.ContractId).HasDatabaseName("ix_finance_events_contract");
        builder.HasIndex(x => x.ReceivableId).HasDatabaseName("ix_finance_events_receivable");
    }
}

/// <summary>Mapping for <see cref="FinanceTaskLink"/>.</summary>
public sealed class FinanceTaskLinkConfiguration : IEntityTypeConfiguration<FinanceTaskLink>
{
    public void Configure(EntityTypeBuilder<FinanceTaskLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_task_links");

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

        builder.Property(x => x.ReceivableId)
            .HasColumnName("receivable_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ReceivableId(value.Value) : null);

        builder.Property(x => x.InvoiceId)
            .HasColumnName("invoice_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new InvoiceId(value.Value) : null);

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PaymentId(value.Value) : null);

        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Receivable>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ReceivableId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A task belongs to at most one piece of finance work, on the M6, M7 and
        // M8 precedent.
        builder.HasIndex(x => x.TaskItemId)
            .IsUnique()
            .HasDatabaseName("ux_finance_task_links_task");

        builder.HasIndex(x => x.ReceivableId).HasDatabaseName("ix_finance_task_links_receivable");
    }
}
