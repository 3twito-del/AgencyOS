using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="CommunicationAccount"/>.</summary>
/// <remarks>
/// The protected credential is an ordinary text column holding ciphertext. It is
/// never projected into a read model, never mapped to an API contract and never
/// serialized into an audit delta; the shape of every record downstream is part of
/// that guarantee (ADR-0027).
/// </remarks>
public sealed class CommunicationAccountConfiguration
    : IEntityTypeConfiguration<CommunicationAccount>
{
    public void Configure(EntityTypeBuilder<CommunicationAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommunicationAccountId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();

        builder.Property(x => x.MailboxAddress)
            .HasColumnName("mailbox_address")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);

        builder.Property(x => x.ExternalAccountId)
            .HasColumnName("external_account_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.GrantedScopes)
            .HasColumnName("granted_scopes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();

        builder.Property(x => x.Visibility)
            .HasColumnName("visibility")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.DeltaCursor).HasColumnName("delta_cursor").HasMaxLength(4000);
        builder.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
        builder.Property(x => x.LastSyncError).HasColumnName("last_sync_error").HasMaxLength(1000);

        builder.Property(x => x.ProtectedRefreshToken)
            .HasColumnName("protected_refresh_token")
            .HasMaxLength(8000);

        builder.Property(x => x.CredentialExpiresAt).HasColumnName("credential_expires_at");
        builder.Property(x => x.SyncLeaseOwner).HasColumnName("sync_lease_owner").HasMaxLength(100);
        builder.Property(x => x.SyncLeaseExpiresAt).HasColumnName("sync_lease_expires_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        // One connection per mailbox per provider per tenant. Two rows for the same
        // mailbox would synchronize the same messages twice under two cursors.
        builder.HasIndex(x => new { x.OrganizationId, x.Provider, x.ExternalAccountId })
            .HasDatabaseName("ux_communication_accounts_external")
            .IsUnique();

        // The worker's claim index: connected mailboxes whose lease has lapsed.
        builder.HasIndex(x => new { x.State, x.SyncLeaseExpiresAt })
            .HasDatabaseName("ix_communication_accounts_sync_queue")
            .HasFilter("state = 1");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="CommunicationThread"/>.</summary>
public sealed class CommunicationThreadConfiguration : IEntityTypeConfiguration<CommunicationThread>
{
    public void Configure(EntityTypeBuilder<CommunicationThread> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_threads");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommunicationThreadId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => new CommunicationAccountId(value))
            .IsRequired();

        builder.Property(x => x.ExternalThreadId)
            .HasColumnName("external_thread_id")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500);
        builder.Property(x => x.FirstMessageAt).HasColumnName("first_message_at").IsRequired();
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at").IsRequired();
        builder.Property(x => x.MessageCount).HasColumnName("message_count").IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.ExternalThreadId })
            .HasDatabaseName("ux_communication_threads_external")
            .IsUnique();
    }
}

/// <summary>Mapping for <see cref="CommunicationMessage"/>.</summary>
/// <remarks>
/// The provider identifier is unique <em>within the account</em>, which is what
/// makes delta synchronization idempotent: a provider that hands back the same
/// message after a cursor reset updates the row instead of adding one. The same
/// message seen from two mailboxes is legitimately two rows (ADR-0026).
/// </remarks>
public sealed class CommunicationMessageConfiguration
    : IEntityTypeConfiguration<CommunicationMessage>
{
    public void Configure(EntityTypeBuilder<CommunicationMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommunicationMessageId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => new CommunicationAccountId(value))
            .IsRequired();

        builder.Property(x => x.ThreadId)
            .HasColumnName("thread_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationThreadId(value.Value) : null);

        builder.Property(x => x.ExternalMessageId)
            .HasColumnName("external_message_id")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.InternetMessageId)
            .HasColumnName("internet_message_id")
            .HasMaxLength(500);

        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500);
        builder.Property(x => x.BodyText).HasColumnName("body_text");
        builder.Property(x => x.SanitizedHtml).HasColumnName("sanitized_html");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at");
        builder.Property(x => x.SynchronizedAt).HasColumnName("synchronized_at").IsRequired();
        builder.Property(x => x.Folder).HasColumnName("folder").HasMaxLength(200);
        builder.Property(x => x.HasAttachments).HasColumnName("has_attachments").IsRequired();

        builder.Property(x => x.IsDeletedAtProvider)
            .HasColumnName("is_deleted_at_provider")
            .IsRequired();

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasMany(x => x.Participants)
            .WithOne()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Links)
            .WithOne()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Links).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.AccountId, x.ExternalMessageId })
            .HasDatabaseName("ux_communication_messages_external")
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.SynchronizedAt })
            .HasDatabaseName("ix_communication_messages_synchronized");

        builder.HasIndex(x => new { x.AccountId, x.SentAt })
            .HasDatabaseName("ix_communication_messages_account_sent");
    }
}

/// <summary>Mapping for <see cref="CommunicationParticipant"/>.</summary>
public sealed class CommunicationParticipantConfiguration
    : IEntityTypeConfiguration<CommunicationParticipant>
{
    public void Configure(EntityTypeBuilder<CommunicationParticipant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_participants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasConversion(id => id.Value, value => new CommunicationMessageId(value))
            .IsRequired();

        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();

        // The raw address, exactly as observed. Canonical, and never replaced by an
        // AgencyOS identification (ADR-0026).
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(320).IsRequired();

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        builder.Property(x => x.PersonId).HasColumnName("person_id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");

        builder.Property(x => x.ResolvedBy)
            .HasColumnName("resolved_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(x => new { x.OrganizationId, x.Address })
            .HasDatabaseName("ix_communication_participants_address");

        builder.HasIndex(x => x.MessageId)
            .HasDatabaseName("ix_communication_participants_message");
    }
}

/// <summary>Mapping for <see cref="CommunicationAttachment"/>.</summary>
public sealed class CommunicationAttachmentConfiguration
    : IEntityTypeConfiguration<CommunicationAttachment>
{
    public void Configure(EntityTypeBuilder<CommunicationAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_attachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CommunicationAttachmentId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasConversion(id => id.Value, value => new CommunicationMessageId(value))
            .IsRequired();

        builder.Property(x => x.ExternalAttachmentId)
            .HasColumnName("external_attachment_id")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(300).IsRequired();

        builder.Property(x => x.MediaType)
            .HasColumnName("media_type")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.ByteLength).HasColumnName("byte_length").IsRequired();
        builder.Property(x => x.IsInline).HasColumnName("is_inline").IsRequired();

        // Null until the bytes are actually ingested. The difference between
        // knowing a file exists and holding it (ADR-0024).
        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DocumentVersionId(value.Value) : null);

        builder.Property(x => x.IngestedAt).HasColumnName("ingested_at");

        builder.Property(x => x.IngestedBy)
            .HasColumnName("ingested_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.HasIndex(x => new { x.MessageId, x.ExternalAttachmentId })
            .HasDatabaseName("ux_communication_attachments_external")
            .IsUnique();
    }
}

/// <summary>Mapping for <see cref="CommunicationLink"/>.</summary>
public sealed class CommunicationLinkConfiguration : IEntityTypeConfiguration<CommunicationLink>
{
    public void Configure(EntityTypeBuilder<CommunicationLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_links");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasConversion(id => id.Value, value => new CommunicationMessageId(value))
            .IsRequired();

        builder.Property(x => x.Target).HasColumnName("target").HasConversion<int>().IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();

        M10Links.MapArc(builder);

        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.LinkedAt).HasColumnName("linked_at").IsRequired();

        builder.Property(x => x.LinkedBy)
            .HasColumnName("linked_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => new { x.MessageId, x.Target, x.TargetId })
            .HasDatabaseName("ux_communication_links_message_target")
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.Target, x.TargetId })
            .HasDatabaseName("ix_communication_links_target");
    }
}

/// <summary>Mapping for <see cref="OutboundDispatch"/>.</summary>
/// <remarks>
/// The row is the canonical state of an external operation, which is why the lease
/// and the attempt schedule live on it rather than in a worker's memory: a restart
/// must lose nothing about a send that may already have happened (ADR-0028).
/// </remarks>
public sealed class OutboundDispatchConfiguration : IEntityTypeConfiguration<OutboundDispatch>
{
    public void Configure(EntityTypeBuilder<OutboundDispatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbound_dispatches");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OutboundDispatchId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => new CommunicationAccountId(value))
            .IsRequired();

        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
        builder.Property(x => x.BodyText).HasColumnName("body_text").IsRequired();

        builder.Property(x => x.InReplyToMessageId)
            .HasColumnName("in_reply_to_message_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationMessageId(value.Value) : null);

        // The correlation value the provider carries, generated once and reused on
        // every attempt. Unique per tenant so a reconciliation search can never
        // match another intent's message (ADR-0028).
        builder.Property(x => x.ClientReference)
            .HasColumnName("client_reference")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ProviderDraftId)
            .HasColumnName("provider_draft_id")
            .HasMaxLength(300);

        builder.Property(x => x.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasMaxLength(300);

        builder.Property(x => x.InternetMessageId)
            .HasColumnName("internet_message_id")
            .HasMaxLength(500);

        builder.Property(x => x.SentMessageId)
            .HasColumnName("sent_message_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationMessageId(value.Value) : null);

        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);

        builder.Property(x => x.LastVerdict)
            .HasColumnName("last_verdict")
            .HasConversion<int?>();

        builder.Property(x => x.LastReconciledAt).HasColumnName("last_reconciled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.SentAt).HasColumnName("sent_at");

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(100);
        builder.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasMany(x => x.Recipients)
            .WithOne()
            .HasForeignKey(x => x.DispatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(x => x.DispatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OrganizationId, x.ClientReference })
            .HasDatabaseName("ux_outbound_dispatches_reference")
            .IsUnique();

        // The worker's claim index. Only workable states appear, so it stays the
        // size of the queue rather than the size of every message ever sent
        // (ADR-0029).
        builder.HasIndex(x => new { x.NextAttemptAt, x.LeaseExpiresAt })
            .HasDatabaseName("ix_outbound_dispatches_queue")
            .HasFilter("state IN (2, 3, 4, 6)");

        // What a person has to look at: unknown outcomes and permanent failures.
        builder.HasIndex(x => new { x.OrganizationId, x.State })
            .HasDatabaseName("ix_outbound_dispatches_attention")
            .HasFilter("state IN (7, 8)");
    }
}

/// <summary>Mapping for <see cref="OutboundRecipient"/>.</summary>
public sealed class OutboundRecipientConfiguration : IEntityTypeConfiguration<OutboundRecipient>
{
    public void Configure(EntityTypeBuilder<OutboundRecipient> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbound_recipients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.DispatchId)
            .HasColumnName("dispatch_id")
            .HasConversion(id => id.Value, value => new OutboundDispatchId(value))
            .IsRequired();

        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(320).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);

        builder.HasIndex(x => x.DispatchId).HasDatabaseName("ix_outbound_recipients_dispatch");
    }
}

/// <summary>Mapping for <see cref="OutboundAttachment"/>.</summary>
public sealed class OutboundAttachmentConfiguration : IEntityTypeConfiguration<OutboundAttachment>
{
    public void Configure(EntityTypeBuilder<OutboundAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbound_attachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.DispatchId)
            .HasColumnName("dispatch_id")
            .HasConversion(id => id.Value, value => new OutboundDispatchId(value))
            .IsRequired();

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        builder.Property(x => x.DocumentVersionId)
            .HasColumnName("document_version_id")
            .HasConversion(id => id.Value, value => new DocumentVersionId(value))
            .IsRequired();

        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(300).IsRequired();

        builder.Property(x => x.MediaType)
            .HasColumnName("media_type")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.ByteLength).HasColumnName("byte_length").IsRequired();

        builder.HasIndex(x => new { x.DispatchId, x.DocumentVersionId })
            .HasDatabaseName("ux_outbound_attachments_version")
            .IsUnique();
    }
}

/// <summary>Mapping for <see cref="CommunicationEvent"/>.</summary>
public sealed class CommunicationEventConfiguration : IEntityTypeConfiguration<CommunicationEvent>
{
    public void Configure(EntityTypeBuilder<CommunicationEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("communication_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationAccountId(value.Value) : null);

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunicationMessageId(value.Value) : null);

        builder.Property(x => x.DispatchId)
            .HasColumnName("dispatch_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OutboundDispatchId(value.Value) : null);

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(2000);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt })
            .HasDatabaseName("ix_communication_events_organization");

        builder.HasIndex(x => x.DispatchId).HasDatabaseName("ix_communication_events_dispatch");
    }
}
