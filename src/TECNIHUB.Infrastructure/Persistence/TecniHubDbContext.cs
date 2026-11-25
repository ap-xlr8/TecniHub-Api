using Microsoft.EntityFrameworkCore;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class TecniHubDbContext : DbContext
{
    public TecniHubDbContext(DbContextOptions<TecniHubDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TechnicianProfile> TechnicianProfiles => Set<TechnicianProfile>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var seedTimestamp = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(r => r.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
            entity.Property(r => r.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            entity.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(r => r.Code).IsUnique();

            entity.HasData(
                new Role { Id = 1, Code = "Client", Name = "Cliente", CreatedAt = seedTimestamp, UpdatedAt = seedTimestamp },
                new Role { Id = 2, Code = "Technician", Name = "Técnico", CreatedAt = seedTimestamp, UpdatedAt = seedTimestamp });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.RoleId).HasColumnName("role_id").IsRequired();
            entity.Property(u => u.FirstName).HasColumnName("first_name").HasMaxLength(80).IsRequired();
            entity.Property(u => u.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
            entity.Property(u => u.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            entity.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
            entity.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(20);
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
            entity.Property(u => u.IsEmailVerified).HasColumnName("is_email_verified").IsRequired();
            entity.Property(u => u.Status).HasColumnName("status").HasConversion<int>().IsRequired();
            entity.Property(u => u.DeletedAt).HasColumnName("deleted_at");
            entity.Property(u => u.ScheduledPurgeAt).HasColumnName("scheduled_purge_at");
            entity.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
            entity.HasIndex(u => new { u.Status, u.ScheduledPurgeAt });

            entity.HasOne(u => u.Role)
                .WithMany()
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TechnicianProfile>(entity =>
        {
            entity.ToTable("technician_profiles");
            entity.HasKey(tp => tp.UserId);
            entity.Property(tp => tp.UserId).HasColumnName("user_id");
            entity.Property(tp => tp.Specialties).HasColumnName("specialties").HasMaxLength(300).IsRequired();
            entity.Property(tp => tp.ExperienceYears).HasColumnName("experience_years").IsRequired();
            entity.Property(tp => tp.ServiceZone).HasColumnName("service_zone").HasMaxLength(150).IsRequired();
            entity.Property(tp => tp.BusinessName).HasColumnName("business_name").HasMaxLength(120);
            entity.Property(tp => tp.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(tp => tp.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasOne(tp => tp.User)
                .WithOne(u => u.TechnicianProfile)
                .HasForeignKey<TechnicianProfile>(tp => tp.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.ToTable("verification_codes");
            entity.HasKey(vc => vc.Id);
            entity.Property(vc => vc.Id).HasColumnName("id");
            entity.Property(vc => vc.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(vc => vc.Purpose).HasColumnName("purpose").HasConversion<int>().IsRequired();
            entity.Property(vc => vc.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
            entity.Property(vc => vc.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(vc => vc.AttemptCount).HasColumnName("attempt_count").IsRequired();
            entity.Property(vc => vc.LockedUntil).HasColumnName("locked_until");
            entity.Property(vc => vc.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(vc => vc.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(vc => vc.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(vc => new { vc.UserId, vc.Purpose, vc.CreatedAt });

            entity.HasOne(vc => vc.User)
                .WithMany(u => u.VerificationCodes)
                .HasForeignKey(vc => vc.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(rt => rt.Id);
            entity.Property(rt => rt.Id).HasColumnName("id");
            entity.Property(rt => rt.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(rt => rt.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
            entity.Property(rt => rt.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(rt => rt.RevokedAt).HasColumnName("revoked_at");
            entity.Property(rt => rt.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
            entity.Property(rt => rt.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            entity.Property(rt => rt.UserAgent).HasColumnName("user_agent").HasMaxLength(300);
            entity.Property(rt => rt.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(rt => rt.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasIndex(rt => new { rt.UserId, rt.RevokedAt });

            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(al => al.Id);
            entity.Property(al => al.Id).HasColumnName("id");
            entity.Property(al => al.OccurredAt).HasColumnName("occurred_at").IsRequired();
            entity.Property(al => al.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(al => al.ActorRole).HasColumnName("actor_role").HasMaxLength(32);
            entity.Property(al => al.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            entity.Property(al => al.EntityName).HasColumnName("entity_name").HasMaxLength(80).IsRequired();
            entity.Property(al => al.EntityId).HasColumnName("entity_id").HasMaxLength(100);
            entity.Property(al => al.Outcome).HasColumnName("outcome").HasConversion<int>().IsRequired();
            entity.Property(al => al.MetadataJson).HasColumnName("metadata_json").HasMaxLength(2000);
            entity.Property(al => al.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            entity.Property(al => al.UserAgent).HasColumnName("user_agent").HasMaxLength(300);

            entity.HasIndex(al => al.OccurredAt);
            entity.HasIndex(al => al.ActorUserId);
            entity.HasIndex(al => al.Action);
        });
    }
}
