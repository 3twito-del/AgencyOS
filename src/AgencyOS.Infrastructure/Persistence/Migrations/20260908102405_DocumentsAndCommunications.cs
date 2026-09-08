using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsAndCommunications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "submission_materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "contract_versions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "contract_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "blob_ingestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    byte_length = table.Column<long>(type: "bigint", nullable: true),
                    display_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blob_ingestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_blob_ingestions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "blob_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    media_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scan_state = table.Column<int>(type: "integer", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scanner_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blob_objects", x => x.id);
                    table.UniqueConstraint("AK_blob_objects_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_blob_objects_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "communication_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    mailbox_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_account_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    granted_scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    delta_cursor = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    protected_refresh_token = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    credential_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sync_lease_owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sync_lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_accounts", x => x.id);
                    table.UniqueConstraint("AK_communication_accounts_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_communication_accounts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "communication_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "communication_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_message_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    internet_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    body_text = table.Column<string>(type: "text", nullable: true),
                    sanitized_html = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    synchronized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    folder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted_at_provider = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_messages", x => x.id);
                    table.UniqueConstraint("AK_communication_messages_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "communication_threads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_thread_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    first_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_threads", x => x.id);
                    table.UniqueConstraint("AK_communication_threads_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "document_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                    table.UniqueConstraint("AK_documents_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_documents_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbound_dispatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    in_reply_to_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_draft_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    internet_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_verdict = table.Column<int>(type: "integer", nullable: true),
                    last_reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_dispatches", x => x.id);
                    table.UniqueConstraint("AK_outbound_dispatches_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "communication_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_attachment_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    media_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    is_inline = table.Column<bool>(type: "boolean", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ingested_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_attachments", x => x.id);
                    table.UniqueConstraint("AK_communication_attachments_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_communication_attachments_communication_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "communication_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "communication_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_communication_links_communication_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "communication_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "communication_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_communication_participants_communication_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "communication_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_links_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    blob_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    display_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    media_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    extraction_state = table.Column<int>(type: "integer", nullable: false),
                    extracted_text = table.Column<string>(type: "text", nullable: true),
                    extraction_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.id);
                    table.UniqueConstraint("AK_document_versions_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_document_versions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbound_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    media_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbound_attachments_outbound_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "outbound_dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbound_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_recipients", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbound_recipients_outbound_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "outbound_dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blob_ingestions_organization_id",
                table: "blob_ingestions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_blob_ingestions_unfinished",
                table: "blob_ingestions",
                column: "updated_at",
                filter: "state IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_blob_objects_organization_hash",
                table: "blob_objects",
                columns: new[] { "organization_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communication_accounts_sync_queue",
                table: "communication_accounts",
                columns: new[] { "state", "sync_lease_expires_at" },
                filter: "state = 1");

            migrationBuilder.CreateIndex(
                name: "ux_communication_accounts_external",
                table: "communication_accounts",
                columns: new[] { "organization_id", "provider", "external_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_communication_attachments_external",
                table: "communication_attachments",
                columns: new[] { "message_id", "external_attachment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communication_events_dispatch",
                table: "communication_events",
                column: "dispatch_id");

            migrationBuilder.CreateIndex(
                name: "ix_communication_events_organization",
                table: "communication_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_communication_links_target",
                table: "communication_links",
                columns: new[] { "organization_id", "target", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ux_communication_links_message_target",
                table: "communication_links",
                columns: new[] { "message_id", "target", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communication_messages_account_sent",
                table: "communication_messages",
                columns: new[] { "account_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_communication_messages_synchronized",
                table: "communication_messages",
                columns: new[] { "organization_id", "synchronized_at" });

            migrationBuilder.CreateIndex(
                name: "ux_communication_messages_external",
                table: "communication_messages",
                columns: new[] { "account_id", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communication_participants_address",
                table: "communication_participants",
                columns: new[] { "organization_id", "address" });

            migrationBuilder.CreateIndex(
                name: "ix_communication_participants_message",
                table: "communication_participants",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ux_communication_threads_external",
                table: "communication_threads",
                columns: new[] { "account_id", "external_thread_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_events_document",
                table: "document_events",
                columns: new[] { "document_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_links_target",
                table: "document_links",
                columns: new[] { "organization_id", "target", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ux_document_links_document_target",
                table: "document_links",
                columns: new[] { "document_id", "target", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_organization_hash",
                table: "document_versions",
                columns: new[] { "organization_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ux_document_versions_document_sequence",
                table: "document_versions",
                columns: new[] { "document_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_organization_kind",
                table: "documents",
                columns: new[] { "organization_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_organization_sensitivity",
                table: "documents",
                columns: new[] { "organization_id", "sensitivity" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_organization_status",
                table: "documents",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_outbound_attachments_version",
                table: "outbound_attachments",
                columns: new[] { "dispatch_id", "document_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_dispatches_attention",
                table: "outbound_dispatches",
                columns: new[] { "organization_id", "state" },
                filter: "state IN (7, 8)");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_dispatches_queue",
                table: "outbound_dispatches",
                columns: new[] { "next_attempt_at", "lease_expires_at" },
                filter: "state IN (2, 3, 4, 6)");

            migrationBuilder.CreateIndex(
                name: "ux_outbound_dispatches_reference",
                table: "outbound_dispatches",
                columns: new[] { "organization_id", "client_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_recipients_dispatch",
                table: "outbound_recipients",
                column: "dispatch_id");

            // ---- Tenant-safe links, immutability and search (M10) ----
            //
            // Everything below is expressed as SQL because it is not expressible in
            // the EF model: composite foreign keys through alternate keys, an
            // exclusive-arc check, and triggers that refuse to rewrite history.

            // Composite, so a link can never point at another tenant's people.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_person_id
                    FOREIGN KEY (organization_id, person_id)
                    REFERENCES people (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's companies.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_company_id
                    FOREIGN KEY (organization_id, company_id)
                    REFERENCES companies (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's talent_profiles.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_talent_profile_id
                    FOREIGN KEY (organization_id, talent_profile_id)
                    REFERENCES talent_profiles (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's materials.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_material_id
                    FOREIGN KEY (organization_id, material_id)
                    REFERENCES materials (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's projects.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_project_id
                    FOREIGN KEY (organization_id, project_id)
                    REFERENCES projects (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's packages.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_package_id
                    FOREIGN KEY (organization_id, package_id)
                    REFERENCES packages (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's opportunities.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_opportunity_id
                    FOREIGN KEY (organization_id, opportunity_id)
                    REFERENCES opportunities (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's submissions.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_submission_id
                    FOREIGN KEY (organization_id, submission_id)
                    REFERENCES submissions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's deals.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_deal_id
                    FOREIGN KEY (organization_id, deal_id)
                    REFERENCES deals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's offers.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_offer_id
                    FOREIGN KEY (organization_id, offer_id)
                    REFERENCES offers (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's contracts.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_contract_id
                    FOREIGN KEY (organization_id, contract_id)
                    REFERENCES contracts (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's contract_versions.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_contract_version_id
                    FOREIGN KEY (organization_id, contract_version_id)
                    REFERENCES contract_versions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's invoices.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_invoice_id
                    FOREIGN KEY (organization_id, invoice_id)
                    REFERENCES invoices (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's payments.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT fk_document_links_payment_id
                    FOREIGN KEY (organization_id, payment_id)
                    REFERENCES payments (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Exactly one typed column, and it must be the one the discriminator
            // names. Without this a link could carry a stale identifier in a
            // second column and point at two records at once (ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE document_links
                ADD CONSTRAINT ck_document_links_exclusive_arc CHECK (
                    ((CASE WHEN person_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN company_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN talent_profile_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN material_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN project_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN package_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN opportunity_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN submission_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN deal_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN offer_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN contract_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN contract_version_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN invoice_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN payment_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE target
                        WHEN 1 THEN (person_id IS NOT NULL AND person_id = target_id)
                        WHEN 2 THEN (company_id IS NOT NULL AND company_id = target_id)
                        WHEN 3 THEN (talent_profile_id IS NOT NULL AND talent_profile_id = target_id)
                        WHEN 4 THEN (material_id IS NOT NULL AND material_id = target_id)
                        WHEN 5 THEN (project_id IS NOT NULL AND project_id = target_id)
                        WHEN 6 THEN (package_id IS NOT NULL AND package_id = target_id)
                        WHEN 7 THEN (opportunity_id IS NOT NULL AND opportunity_id = target_id)
                        WHEN 8 THEN (submission_id IS NOT NULL AND submission_id = target_id)
                        WHEN 9 THEN (deal_id IS NOT NULL AND deal_id = target_id)
                        WHEN 10 THEN (offer_id IS NOT NULL AND offer_id = target_id)
                        WHEN 11 THEN (contract_id IS NOT NULL AND contract_id = target_id)
                        WHEN 12 THEN (contract_version_id IS NOT NULL AND contract_version_id = target_id)
                        WHEN 13 THEN (invoice_id IS NOT NULL AND invoice_id = target_id)
                        WHEN 14 THEN (payment_id IS NOT NULL AND payment_id = target_id)
                        ELSE false
                    END
                );
                """);
            // Composite, so a link can never point at another tenant's people.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_person_id
                    FOREIGN KEY (organization_id, person_id)
                    REFERENCES people (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's companies.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_company_id
                    FOREIGN KEY (organization_id, company_id)
                    REFERENCES companies (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's talent_profiles.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_talent_profile_id
                    FOREIGN KEY (organization_id, talent_profile_id)
                    REFERENCES talent_profiles (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's materials.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_material_id
                    FOREIGN KEY (organization_id, material_id)
                    REFERENCES materials (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's projects.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_project_id
                    FOREIGN KEY (organization_id, project_id)
                    REFERENCES projects (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's packages.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_package_id
                    FOREIGN KEY (organization_id, package_id)
                    REFERENCES packages (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's opportunities.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_opportunity_id
                    FOREIGN KEY (organization_id, opportunity_id)
                    REFERENCES opportunities (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's submissions.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_submission_id
                    FOREIGN KEY (organization_id, submission_id)
                    REFERENCES submissions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's deals.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_deal_id
                    FOREIGN KEY (organization_id, deal_id)
                    REFERENCES deals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's offers.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_offer_id
                    FOREIGN KEY (organization_id, offer_id)
                    REFERENCES offers (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's contracts.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_contract_id
                    FOREIGN KEY (organization_id, contract_id)
                    REFERENCES contracts (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's contract_versions.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_contract_version_id
                    FOREIGN KEY (organization_id, contract_version_id)
                    REFERENCES contract_versions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's invoices.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_invoice_id
                    FOREIGN KEY (organization_id, invoice_id)
                    REFERENCES invoices (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Composite, so a link can never point at another tenant's payments.
            // The tenant column is part of the key rather than a value somebody
            // remembered to filter on (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT fk_communication_links_payment_id
                    FOREIGN KEY (organization_id, payment_id)
                    REFERENCES payments (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Exactly one typed column, and it must be the one the discriminator
            // names. Without this a link could carry a stale identifier in a
            // second column and point at two records at once (ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE communication_links
                ADD CONSTRAINT ck_communication_links_exclusive_arc CHECK (
                    ((CASE WHEN person_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN company_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN talent_profile_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN material_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN project_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN package_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN opportunity_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN submission_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN deal_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN offer_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN contract_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN contract_version_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN invoice_id IS NULL THEN 0 ELSE 1 END) +
                        (CASE WHEN payment_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE target
                        WHEN 1 THEN (person_id IS NOT NULL AND person_id = target_id)
                        WHEN 2 THEN (company_id IS NOT NULL AND company_id = target_id)
                        WHEN 3 THEN (talent_profile_id IS NOT NULL AND talent_profile_id = target_id)
                        WHEN 4 THEN (material_id IS NOT NULL AND material_id = target_id)
                        WHEN 5 THEN (project_id IS NOT NULL AND project_id = target_id)
                        WHEN 6 THEN (package_id IS NOT NULL AND package_id = target_id)
                        WHEN 7 THEN (opportunity_id IS NOT NULL AND opportunity_id = target_id)
                        WHEN 8 THEN (submission_id IS NOT NULL AND submission_id = target_id)
                        WHEN 9 THEN (deal_id IS NOT NULL AND deal_id = target_id)
                        WHEN 10 THEN (offer_id IS NOT NULL AND offer_id = target_id)
                        WHEN 11 THEN (contract_id IS NOT NULL AND contract_id = target_id)
                        WHEN 12 THEN (contract_version_id IS NOT NULL AND contract_version_id = target_id)
                        WHEN 13 THEN (invoice_id IS NOT NULL AND invoice_id = target_id)
                        WHEN 14 THEN (payment_id IS NOT NULL AND payment_id = target_id)
                        ELSE false
                    END
                );
                """);

            // ---- Seam foreign keys (M4, M6, M8, M9) ----
            //
            // A material, a submission, a drafting version and an invoice may each
            // name the canonical stored version of the file they describe. Nullable
            // and additive: every row written before M10 keeps saying nothing, which
            // remains the truth about it (ADR-0024).
            migrationBuilder.Sql("""
                ALTER TABLE materials
                ADD CONSTRAINT fk_materials_document_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE SET NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE submission_materials
                ADD CONSTRAINT fk_submission_materials_document_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE SET NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_versions
                ADD CONSTRAINT fk_contract_versions_document_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE SET NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invoices
                ADD CONSTRAINT fk_invoices_document_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE SET NULL;
                """);

            // ---- Containment ----

            migrationBuilder.Sql("""
                ALTER TABLE document_versions
                ADD CONSTRAINT fk_document_versions_blob
                    FOREIGN KEY (organization_id, blob_object_id)
                    REFERENCES blob_objects (organization_id, id)
                    ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE communication_messages
                ADD CONSTRAINT fk_communication_messages_account
                    FOREIGN KEY (organization_id, account_id)
                    REFERENCES communication_accounts (organization_id, id)
                    ON DELETE CASCADE;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE outbound_dispatches
                ADD CONSTRAINT fk_outbound_dispatches_account
                    FOREIGN KEY (organization_id, account_id)
                    REFERENCES communication_accounts (organization_id, id)
                    ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE outbound_attachments
                ADD CONSTRAINT fk_outbound_attachments_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE communication_attachments
                ADD CONSTRAINT fk_communication_attachments_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE SET NULL;
                """);

            // ---- Shape checks ----

            migrationBuilder.Sql("""
                ALTER TABLE blob_objects
                ADD CONSTRAINT ck_blob_objects_shape CHECK (
                    byte_length >= 0
                    AND content_hash ~ '^[0-9a-f]{64}$'
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE document_versions
                ADD CONSTRAINT ck_document_versions_shape CHECK (
                    sequence > 0
                    AND byte_length >= 0
                    AND content_hash ~ '^[0-9a-f]{64}$'
                );
                """);

            // Extracted text exists only where extraction succeeded. A row claiming
            // text while reporting failure would be two facts that disagree
            // (ADR-0024).
            migrationBuilder.Sql("""
                ALTER TABLE document_versions
                ADD CONSTRAINT ck_document_versions_extraction CHECK (
                    extraction_state = 2 OR extracted_text IS NULL
                );
                """);

            // A disconnected mailbox holds no usable credential. Keeping one "in
            // case" would leave AgencyOS able to read a mailbox somebody withdrew
            // (ADR-0027).
            migrationBuilder.Sql("""
                ALTER TABLE communication_accounts
                ADD CONSTRAINT ck_communication_accounts_credential CHECK (
                    state <> 2 OR protected_refresh_token IS NULL
                );
                """);

            // A confirmed send has a time; an unsent one does not. The state and the
            // evidence cannot disagree.
            migrationBuilder.Sql("""
                ALTER TABLE outbound_dispatches
                ADD CONSTRAINT ck_outbound_dispatches_sent CHECK (
                    (state = 5 AND sent_at IS NOT NULL)
                    OR (state <> 5 AND sent_at IS NULL)
                );
                """);

            // A send cannot have been requested without a provider draft to send.
            migrationBuilder.Sql("""
                ALTER TABLE outbound_dispatches
                ADD CONSTRAINT ck_outbound_dispatches_draft CHECK (
                    state NOT IN (4, 5, 8) OR provider_draft_id IS NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE outbound_dispatches
                ADD CONSTRAINT ck_outbound_dispatches_attempts CHECK (attempt_count >= 0);
                """);

            // ---- Immutability ----
            //
            // The aggregates refuse these already. The triggers exist so a future
            // code path that forgets fails loudly instead of quietly rewriting what
            // a version, a message or an audited send actually said. This is the
            // fifth kind of immutability in AgencyOS and is deliberately its own
            // thing (ADR-0024).

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_blob_objects_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        RAISE EXCEPTION
                            'blob_objects rows are immutable: stored bytes are never deleted '
                            'through an application path.';
                    END IF;

                    IF (NEW.content_hash IS DISTINCT FROM OLD.content_hash
                        OR NEW.storage_key IS DISTINCT FROM OLD.storage_key
                        OR NEW.byte_length IS DISTINCT FROM OLD.byte_length
                        OR NEW.organization_id IS DISTINCT FROM OLD.organization_id) THEN
                        RAISE EXCEPTION
                            'A blob object identifies specific bytes. Its digest, key, length '
                            'and tenant cannot change.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_blob_objects_immutable
                BEFORE UPDATE OR DELETE ON blob_objects
                FOR EACH ROW EXECUTE FUNCTION agencyos_blob_objects_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_document_versions_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        RAISE EXCEPTION
                            'A document version is a historical fact about what the agency held. '
                            'Archive the document instead.';
                    END IF;

                    IF (NEW.content_hash IS DISTINCT FROM OLD.content_hash
                        OR NEW.blob_object_id IS DISTINCT FROM OLD.blob_object_id
                        OR NEW.byte_length IS DISTINCT FROM OLD.byte_length
                        OR NEW.sequence IS DISTINCT FROM OLD.sequence
                        OR NEW.document_id IS DISTINCT FROM OLD.document_id
                        OR NEW.recorded_at IS DISTINCT FROM OLD.recorded_at) THEN
                        RAISE EXCEPTION
                            'A document version records specific bytes at a specific moment. '
                            'Add version N+1 instead of rewriting version N.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_versions_immutable
                BEFORE UPDATE OR DELETE ON document_versions
                FOR EACH ROW EXECUTE FUNCTION agencyos_document_versions_immutable();
                """);

            // A message's provider identity and its observed times are what the mail
            // system reported. Folder, deletion and subject may be updated by a
            // later delta; nothing else may (ADR-0026).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_communication_messages_frozen()
                RETURNS trigger AS $$
                BEGIN
                    IF (NEW.external_message_id IS DISTINCT FROM OLD.external_message_id
                        OR NEW.account_id IS DISTINCT FROM OLD.account_id
                        OR NEW.direction IS DISTINCT FROM OLD.direction
                        OR NEW.sent_at IS DISTINCT FROM OLD.sent_at
                        OR NEW.received_at IS DISTINCT FROM OLD.received_at) THEN
                        RAISE EXCEPTION
                            'A message records what a mail system reported. Its identity, '
                            'mailbox, direction and timestamps cannot be rewritten.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_communication_messages_frozen
                BEFORE UPDATE ON communication_messages
                FOR EACH ROW EXECUTE FUNCTION agencyos_communication_messages_frozen();
                """);

            // The one that matters most. A dispatch that reached Sent must never
            // leave it: a confirmed external send cannot be un-confirmed by any
            // later code path, including a buggy one (ADR-0028).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_outbound_dispatches_monotonic()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF (OLD.state = 5) THEN
                            RAISE EXCEPTION
                                'A sent message cannot be deleted. AgencyOS cannot unsend '
                                'anything, and the record says what happened.';
                        END IF;

                        RETURN OLD;
                    END IF;

                    IF (OLD.state = 5 AND NEW.state <> 5) THEN
                        RAISE EXCEPTION
                            'This message was confirmed sent. That cannot be reversed: the '
                            'recipient has it.';
                    END IF;

                    IF (NEW.client_reference IS DISTINCT FROM OLD.client_reference) THEN
                        RAISE EXCEPTION
                            'The correlation reference is how a sent message is later matched '
                            'back to this intent. It cannot change.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_outbound_dispatches_monotonic
                BEFORE UPDATE OR DELETE ON outbound_dispatches
                FOR EACH ROW EXECUTE FUNCTION agencyos_outbound_dispatches_monotonic();
                """);

            // ---- Search ----
            //
            // Metadata only. Extracted document text and message bodies are
            // deliberately not indexed: a snippet from a privileged contract, or
            // from somebody else's mailbox, is exactly the leak the classification
            // and the mailbox permissions exist to prevent. Full-content search is
            // explicitly deferred (ADR-0025, ADR-0026).

            migrationBuilder.Sql("""
                ALTER TABLE documents
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('simple', coalesce(reference, '')), 'B')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_documents_search ON documents USING GIN (search_vector);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_documents_title_trgm
                ON documents USING GIN (title gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE communication_messages
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('simple', coalesce(subject, ''))
                ) STORED;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_communication_messages_search
                ON communication_messages USING GIN (search_vector);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers, functions and generated columns are dropped before the
            // tables, so the reverse path is provably clean.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_outbound_dispatches_monotonic ON outbound_dispatches;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_outbound_dispatches_monotonic();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_communication_messages_frozen ON communication_messages;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_communication_messages_frozen();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_document_versions_immutable ON document_versions;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_document_versions_immutable();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_blob_objects_immutable ON blob_objects;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_blob_objects_immutable();");

            migrationBuilder.Sql(
                "ALTER TABLE invoices DROP CONSTRAINT IF EXISTS fk_invoices_document_version;");
            migrationBuilder.Sql(
                "ALTER TABLE contract_versions "
                    + "DROP CONSTRAINT IF EXISTS fk_contract_versions_document_version;");
            migrationBuilder.Sql(
                "ALTER TABLE submission_materials "
                    + "DROP CONSTRAINT IF EXISTS fk_submission_materials_document_version;");
            migrationBuilder.Sql(
                "ALTER TABLE materials DROP CONSTRAINT IF EXISTS fk_materials_document_version;");

            // The containment keys declared in SQL are dropped here too. EF orders
            // its own DropTable calls around the relationships it knows about, and
            // it does not know about these, so a table would otherwise be dropped
            // while something still referenced it.
            migrationBuilder.Sql(
                "ALTER TABLE document_versions "
                    + "DROP CONSTRAINT IF EXISTS fk_document_versions_blob;");
            migrationBuilder.Sql(
                "ALTER TABLE communication_messages "
                    + "DROP CONSTRAINT IF EXISTS fk_communication_messages_account;");
            migrationBuilder.Sql(
                "ALTER TABLE outbound_dispatches "
                    + "DROP CONSTRAINT IF EXISTS fk_outbound_dispatches_account;");
            migrationBuilder.Sql(
                "ALTER TABLE outbound_attachments "
                    + "DROP CONSTRAINT IF EXISTS fk_outbound_attachments_version;");
            migrationBuilder.Sql(
                "ALTER TABLE communication_attachments "
                    + "DROP CONSTRAINT IF EXISTS fk_communication_attachments_version;");

            migrationBuilder.DropTable(
                name: "blob_ingestions");

            migrationBuilder.DropTable(
                name: "blob_objects");

            migrationBuilder.DropTable(
                name: "communication_accounts");

            migrationBuilder.DropTable(
                name: "communication_attachments");

            migrationBuilder.DropTable(
                name: "communication_events");

            migrationBuilder.DropTable(
                name: "communication_links");

            migrationBuilder.DropTable(
                name: "communication_participants");

            migrationBuilder.DropTable(
                name: "communication_threads");

            migrationBuilder.DropTable(
                name: "document_events");

            migrationBuilder.DropTable(
                name: "document_links");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "outbound_attachments");

            migrationBuilder.DropTable(
                name: "outbound_recipients");

            migrationBuilder.DropTable(
                name: "communication_messages");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "outbound_dispatches");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "submission_materials");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "contract_versions");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "contract_versions");
        }
    }
}
