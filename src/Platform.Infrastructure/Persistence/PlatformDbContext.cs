using Microsoft.EntityFrameworkCore;

namespace Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string HistorySchema = "platform";
    public const string HistoryTable = "__EFMigrationsHistory";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(HistorySchema);
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
