using Microsoft.EntityFrameworkCore;

namespace Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string HistorySchema = "platform";
    public const string HistoryTable = "__EFMigrationsHistory";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(HistorySchema);
        modelBuilder.Entity<AuditEntryRow>(entity =>
        {
            entity.ToTable("entries", "audit", table =>
            {
                table.HasCheckConstraint("entries_actor_issuer_check", "char_length(actor_issuer) BETWEEN 1 AND 2048");
                table.HasCheckConstraint("entries_actor_subject_check", "char_length(actor_subject) BETWEEN 1 AND 2048");
                table.HasCheckConstraint("entries_action_check", "action IN ('tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted')");
                table.HasCheckConstraint("entries_summary_check", "char_length(summary) <= 512");
                table.HasCheckConstraint("entries_correlation_id_check", "char_length(correlation_id) BETWEEN 1 AND 128");
            });
            entity.HasKey(x => x.Id).HasName("entries_pkey");
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id }).HasDatabaseName("entries_tenant_created");
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.ActorIssuer).HasColumnName("actor_issuer");
            entity.Property(x => x.ActorSubject).HasColumnName("actor_subject");
            entity.Property(x => x.Action).HasColumnName("action");
            entity.Property(x => x.TargetId).HasColumnName("target_id");
            entity.Property(x => x.Summary).HasColumnName("summary");
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("entries_tenant_id_fkey");
        });
        modelBuilder.Entity<InviteRow>(entity =>
        {
            entity.ToTable("invites", table =>
            {
                table.HasCheckConstraint("invites_token_hash_check", "octet_length(token_hash)=32");
                table.HasCheckConstraint("invites_email_check", "char_length(email) BETWEEN 1 AND 320");
                table.HasCheckConstraint("invites_normalized_email_check", "char_length(normalized_email) BETWEEN 1 AND 320");
                table.HasCheckConstraint("invites_role_check", "role IN ('admin','agent','operator')");
                table.HasCheckConstraint("invites_expires_at_check", "expires_at=created_at + interval '24 hours'");
                table.HasCheckConstraint("invites_check", "accepted_at IS NULL OR revoked_at IS NULL");
            });
            entity.HasKey(x => new { x.TenantId, x.Id }).HasName("invites_pkey");
            entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("invites_token_hash_key");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash");
            entity.Property(x => x.Email).HasColumnName("email");
            entity.Property(x => x.NormalizedEmail).HasColumnName("normalized_email");
            entity.Property(x => x.Role).HasColumnName("role");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("invites_tenant_id_fkey");
        });
        modelBuilder.Entity<TenantRow>(entity =>
        {
            entity.ToTable("tenants", table =>
            {
                table.HasCheckConstraint("tenants_name_check", "char_length(name) BETWEEN 1 AND 200");
                table.HasCheckConstraint("tenants_status_check", "status IN ('active','suspended')");
            });
            entity.HasKey(x => x.Id).HasName("tenants_pkey");
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
        modelBuilder.Entity<MembershipRow>(entity =>
        {
            entity.ToTable("memberships", table =>
            {
                table.HasCheckConstraint("memberships_issuer_check", "length(btrim(issuer)) > 0");
                table.HasCheckConstraint("memberships_subject_check", "length(btrim(subject)) > 0");
                table.HasCheckConstraint("memberships_role_check", "role IN ('owner','admin','agent','operator')");
                table.HasCheckConstraint("memberships_status_check", "status IN ('active','inactive')");
                table.HasCheckConstraint("memberships_version_check", "version > 0");
            });
            entity.HasKey(x => new { x.TenantId, x.Id }).HasName("memberships_pkey");
            entity.HasIndex(x => new { x.TenantId, x.Issuer, x.Subject }).IsUnique().HasDatabaseName("memberships_tenant_id_issuer_subject_key");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Issuer).HasColumnName("issuer");
            entity.Property(x => x.Subject).HasColumnName("subject");
            entity.Property(x => x.Role).HasColumnName("role");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("memberships_tenant_id_fkey");
        });
    }
}

internal sealed class TenantRow
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal sealed class MembershipRow
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public long Version { get; set; }
}

internal sealed class AuditEntryRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string ActorIssuer { get; set; }
    public required string ActorSubject { get; set; }
    public required string Action { get; set; }
    public Guid TargetId { get; set; }
    public required string Summary { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal sealed class InviteRow
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
    public required byte[] TokenHash { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
