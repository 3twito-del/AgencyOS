using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The typed subject columns an agent run can point at.
/// </summary>
/// <remarks>
/// Five, matching <see cref="AgentSubjectKind"/>, each with a composite key into
/// its own table. Narrower than M11's ten because a run is about the one thing it
/// was started from (ADR-0011, ADR-0031).
/// </remarks>
internal static class M12RunSubjects
{
    internal static readonly (AgentSubjectKind Kind, string Column, string Table)[] Kinds =
    [
        (AgentSubjectKind.ResearchCase, "research_case_id", "research_cases"),
        (AgentSubjectKind.Person, "person_id", "people"),
        (AgentSubjectKind.Company, "company_id", "companies"),
        (AgentSubjectKind.Deal, "deal_id", "deals"),
        (AgentSubjectKind.Contract, "contract_id", "contracts"),
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

/// <summary>Mapping for <see cref="AgentRun"/>.</summary>
public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_runs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AgentRunId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Kind).HasColumnName("agent_kind").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        builder.Property(x => x.Task).HasColumnName("task").HasMaxLength(2000).IsRequired();

        builder.Property(x => x.SubjectKind).HasColumnName("subject_kind").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");

        M12RunSubjects.MapArc(builder);

        builder.Property(x => x.ProviderKey)
            .HasColumnName("provider_key").HasMaxLength(100).IsRequired();

        builder.Property(x => x.ModelKey)
            .HasColumnName("model_key").HasMaxLength(200).IsRequired();

        builder.Property(x => x.PromptTemplateId)
            .HasColumnName("prompt_template_id").HasMaxLength(100).IsRequired();

        builder.Property(x => x.PromptTemplateVersion)
            .HasColumnName("prompt_template_version").IsRequired();

        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");

        builder.Property(x => x.Failure).HasColumnName("failure").IsRequired();
        builder.Property(x => x.FailureDetail).HasColumnName("failure_detail").HasMaxLength(2000);

        // The answer is kept; the prompt is not. What the person was shown is what
        // they may need to revisit, and the material it was drawn from already
        // lives under its own classification in M10 and M11 (§19, ADR-0031).
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(60_000);

        builder.Property(x => x.ModelInvocationCount)
            .HasColumnName("model_invocation_count").IsRequired();

        builder.Property(x => x.ToolCallCount).HasColumnName("tool_call_count").IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.AgentRunId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);

        // What the desk opens: my runs, newest first.
        builder.HasIndex(x => new { x.OrganizationId, x.UserId, x.StartedAt })
            .HasDatabaseName("ix_ai_runs_user")
            .IsDescending(false, false, true);

        // The work queue: runs that have stopped and are waiting for somebody.
        builder.HasIndex(x => new { x.OrganizationId, x.Status })
            .HasDatabaseName("ix_ai_runs_waiting")
            .HasFilter("status = 3");

        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.StartedAt })
            .HasDatabaseName("ix_ai_runs_status")
            .IsDescending(false, false, true);
    }
}

/// <summary>Mapping for <see cref="AgentRunStep"/>.</summary>
/// <remarks>
/// Completed steps are immutable, enforced by a trigger in the migration. A run
/// history the model or a later turn could rewrite would be worthless as an
/// explanation of what the model did (§17).
/// </remarks>
public sealed class AgentRunStepConfiguration : IEntityTypeConfiguration<AgentRunStep>
{
    public void Configure(EntityTypeBuilder<AgentRunStep> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_run_steps");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.AgentRunId)
            .HasColumnName("ai_run_id")
            .HasConversion(id => id.Value, value => new AgentRunId(value))
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("step_kind").IsRequired();

        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();

        // Bounded, and never a prompt or a model response body. A tool's arguments
        // reach here only where they are already what an approval dialog shows
        // (§19, §55).
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(4000);

        builder.Property(x => x.ReferenceId).HasColumnName("reference_id");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.HasIndex(x => new { x.AgentRunId, x.Sequence })
            .HasDatabaseName("ux_ai_run_steps_sequence")
            .IsUnique();
    }
}

/// <summary>Mapping for <see cref="AiToolRequest"/>.</summary>
public sealed class AiToolRequestConfiguration : IEntityTypeConfiguration<AiToolRequest>
{
    public void Configure(EntityTypeBuilder<AiToolRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_tool_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AiToolRequestId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.AgentRunId)
            .HasColumnName("ai_run_id")
            .HasConversion(id => id.Value, value => new AgentRunId(value))
            .IsRequired();

        builder.Property(x => x.ToolName)
            .HasColumnName("tool_name").HasMaxLength(100).IsRequired();

        builder.Property(x => x.ToolVersion).HasColumnName("tool_version").IsRequired();
        builder.Property(x => x.Effect).HasColumnName("effect").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        // The validated arguments, as AgencyOS canonicalized them. Typed as text
        // rather than jsonb: nothing queries inside them, and a jsonb column would
        // invite somebody to start (§55).
        builder.Property(x => x.Arguments)
            .HasColumnName("arguments").HasMaxLength(20_000).IsRequired();

        builder.Property(x => x.Fingerprint)
            .HasColumnName("fingerprint")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasColumnName("summary").HasMaxLength(1000).IsRequired();

        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(20_000);
        builder.Property(x => x.Refusal).HasColumnName("refusal").HasMaxLength(1000);

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.AgentRunId })
            .HasDatabaseName("ix_ai_tool_requests_run");

        // One request per exact action per run. A model that asks for the same
        // thing twice in one run gets one row, and therefore one approval and one
        // effect (§48).
        builder.HasIndex(x => new { x.AgentRunId, x.Fingerprint })
            .HasDatabaseName("ux_ai_tool_requests_fingerprint")
            .IsUnique();
    }
}

/// <summary>Mapping for <see cref="AiApproval"/>.</summary>
public sealed class AiApprovalConfiguration : IEntityTypeConfiguration<AiApproval>
{
    public void Configure(EntityTypeBuilder<AiApproval> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_approvals");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AiApprovalId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.AgentRunId)
            .HasColumnName("ai_run_id")
            .HasConversion(id => id.Value, value => new AgentRunId(value))
            .IsRequired();

        builder.Property(x => x.ToolRequestId)
            .HasColumnName("ai_tool_request_id")
            .HasConversion(id => id.Value, value => new AiToolRequestId(value))
            .IsRequired();

        builder.Property(x => x.Fingerprint)
            .HasColumnName("fingerprint")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.RequestedOf)
            .HasColumnName("requested_of")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.Property(x => x.Decision).HasColumnName("decision").IsRequired();

        builder.Property(x => x.DecidedBy)
            .HasColumnName("decided_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        // One approval per tool request. Two would make "was this approved" a
        // question with more than one answer, which is the shape of a race (§14).
        builder.HasIndex(x => x.ToolRequestId)
            .HasDatabaseName("ux_ai_approvals_request")
            .IsUnique();

        // What is waiting for me: the query the command centre runs.
        builder.HasIndex(x => new { x.OrganizationId, x.RequestedOf, x.Decision })
            .HasDatabaseName("ix_ai_approvals_pending")
            .HasFilter("decision = 1");
    }
}

/// <summary>Mapping for <see cref="AiProviderPolicy"/>.</summary>
/// <remarks>
/// Holds what an organization permits and no credential of any kind. A provider
/// API key is server infrastructure held in configuration; a tenant-editable copy
/// in the database would turn a database read into a credential theft (§3).
/// </remarks>
public sealed class AiProviderPolicyConfiguration : IEntityTypeConfiguration<AiProviderPolicy>
{
    public void Configure(EntityTypeBuilder<AiProviderPolicy> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_provider_policies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AiProviderPolicyId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.ProviderKey)
            .HasColumnName("provider_key").HasMaxLength(100).IsRequired();

        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();

        builder.Property(x => x.MaximumSensitivity)
            .HasColumnName("maximum_sensitivity").IsRequired();

        builder.Property(x => x.AllowsCanonicalWriteProposals)
            .HasColumnName("allows_write_proposals").IsRequired();

        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.UpdatedBy)
            .HasColumnName("updated_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        // One policy per provider per organization. Two rows would make "may we
        // transmit this" ambiguous, and ambiguity here resolves the wrong way.
        builder.HasIndex(x => new { x.OrganizationId, x.ProviderKey })
            .HasDatabaseName("ux_ai_provider_policies")
            .IsUnique();
    }
}
