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
    public DbSet<PostalCodeEntry> PostalCodes => Set<PostalCodeEntry>();
    public DbSet<RepairRequest> RepairRequests => Set<RepairRequest>();
    public DbSet<RepairRequestImage> RepairRequestImages => Set<RepairRequestImage>();
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<SparePartImage> SparePartImages => Set<SparePartImage>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

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

        modelBuilder.Entity<PostalCodeEntry>(entity =>
        {
            entity.ToTable("postal_codes");
            entity.HasKey(pc => pc.Id);
            entity.Property(pc => pc.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(pc => pc.PostalCode).HasColumnName("postal_code").HasMaxLength(5).IsRequired();
            entity.Property(pc => pc.Neighborhood).HasColumnName("neighborhood").HasMaxLength(150).IsRequired();
            entity.Property(pc => pc.SettlementType).HasColumnName("settlement_type").HasMaxLength(80).IsRequired();
            entity.Property(pc => pc.Municipality).HasColumnName("municipality").HasMaxLength(120).IsRequired();
            entity.Property(pc => pc.State).HasColumnName("state").HasMaxLength(80).IsRequired();
            entity.Property(pc => pc.City).HasColumnName("city").HasMaxLength(120);
            entity.Property(pc => pc.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(pc => pc.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(pc => pc.PostalCode);
        });

        modelBuilder.Entity<RepairRequest>(entity =>
        {
            entity.ToTable("repair_requests");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnName("id");
            entity.Property(r => r.ClientUserId).HasColumnName("client_user_id").IsRequired();
            entity.Property(r => r.AssignedTechnicianId).HasColumnName("assigned_technician_id");
            entity.Property(r => r.ApplianceName).HasColumnName("appliance_name").HasMaxLength(120).IsRequired();
            entity.Property(r => r.BrandModel).HasColumnName("brand_model").HasMaxLength(120);
            entity.Property(r => r.IssueDescription).HasColumnName("issue_description").HasMaxLength(1000).IsRequired();
            entity.Property(r => r.PreferredDate).HasColumnName("preferred_date").IsRequired();
            entity.Property(r => r.PreferredTimeSlot).HasColumnName("preferred_time_slot").HasMaxLength(80).IsRequired();
            entity.Property(r => r.PostalCode).HasColumnName("postal_code").HasMaxLength(5).IsRequired();
            entity.Property(r => r.State).HasColumnName("state").HasMaxLength(80).IsRequired();
            entity.Property(r => r.Municipality).HasColumnName("municipality").HasMaxLength(100).IsRequired();
            entity.Property(r => r.City).HasColumnName("city").HasMaxLength(100);
            entity.Property(r => r.Neighborhood).HasColumnName("neighborhood").HasMaxLength(120).IsRequired();
            entity.Property(r => r.Street).HasColumnName("street").HasMaxLength(150).IsRequired();
            entity.Property(r => r.ExteriorNumber).HasColumnName("exterior_number").HasMaxLength(30);
            entity.Property(r => r.InteriorNumber).HasColumnName("interior_number").HasMaxLength(30);
            entity.Property(r => r.AddressReference).HasColumnName("address_reference").HasMaxLength(250);
            entity.Property(r => r.Status).HasColumnName("status").HasConversion<int>().IsRequired();
            entity.Property(r => r.AssignedAt).HasColumnName("assigned_at");
            entity.Property(r => r.ScheduledStartAt).HasColumnName("scheduled_start_at");
            entity.Property(r => r.StartedAt).HasColumnName("started_at");
            entity.Property(r => r.ClosureRequestedAt).HasColumnName("closure_requested_at");
            entity.Property(r => r.ClosureNotes).HasColumnName("closure_notes").HasMaxLength(500);
            entity.Property(r => r.CompletedAt).HasColumnName("completed_at");
            entity.Property(r => r.CancelledAt).HasColumnName("cancelled_at");
            entity.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(r => r.DeletedAt).HasColumnName("deleted_at");

            entity.HasIndex(r => new { r.Status, r.CreatedAt });
            entity.HasIndex(r => new { r.ClientUserId, r.CreatedAt });
            entity.HasIndex(r => new { r.AssignedTechnicianId, r.ScheduledStartAt });

            entity.HasOne(r => r.ClientUser)
                .WithMany()
                .HasForeignKey(r => r.ClientUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.AssignedTechnician)
                .WithMany()
                .HasForeignKey(r => r.AssignedTechnicianId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RepairRequestImage>(entity =>
        {
            entity.ToTable("repair_request_images");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.RepairRequestId).HasColumnName("repair_request_id").IsRequired();
            entity.Property(i => i.ImageUrl).HasColumnName("image_url").HasMaxLength(500).IsRequired();
            entity.Property(i => i.DisplayOrder).HasColumnName("display_order").IsRequired();
            entity.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(i => new { i.RepairRequestId, i.DisplayOrder });

            entity.HasOne(i => i.RepairRequest)
                .WithMany(r => r.Images)
                .HasForeignKey(i => i.RepairRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SparePart>(entity =>
        {
            entity.ToTable("spare_parts");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.TechnicianUserId).HasColumnName("technician_user_id").IsRequired();
            entity.Property(p => p.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
            entity.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
            entity.Property(p => p.ApplianceCategory).HasColumnName("appliance_category").HasMaxLength(80).IsRequired();
            entity.Property(p => p.Compatibility).HasColumnName("compatibility").HasMaxLength(200);
            entity.Property(p => p.Condition).HasColumnName("condition").HasConversion<int>().IsRequired();
            entity.Property(p => p.Modality).HasColumnName("modality").HasConversion<int>().IsRequired();
            entity.Property(p => p.PriceMxn).HasColumnName("price_mxn").HasPrecision(12, 2).IsRequired();
            entity.Property(p => p.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20);
            entity.Property(p => p.ContactEmail).HasColumnName("contact_email").HasMaxLength(254);
            entity.Property(p => p.ContactNotes).HasColumnName("contact_notes").HasMaxLength(200);
            entity.Property(p => p.IsAvailable).HasColumnName("is_available").IsRequired();
            entity.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(p => p.DeletedAt).HasColumnName("deleted_at");

            entity.HasIndex(p => new { p.IsAvailable, p.CreatedAt });
            entity.HasIndex(p => new { p.TechnicianUserId, p.CreatedAt });

            entity.HasOne(p => p.TechnicianUser)
                .WithMany()
                .HasForeignKey(p => p.TechnicianUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SparePartImage>(entity =>
        {
            entity.ToTable("spare_part_images");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.SparePartId).HasColumnName("spare_part_id").IsRequired();
            entity.Property(i => i.ImageUrl).HasColumnName("image_url").HasMaxLength(500).IsRequired();
            entity.Property(i => i.DisplayOrder).HasColumnName("display_order").IsRequired();
            entity.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(i => new { i.SparePartId, i.DisplayOrder });

            entity.HasOne(i => i.SparePart)
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.SparePartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.ToTable("chat_conversations");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasColumnName("id");
            entity.Property(c => c.ContextType).HasColumnName("context_type").HasConversion<int>().IsRequired();
            entity.Property(c => c.RepairRequestId).HasColumnName("repair_request_id");
            entity.Property(c => c.SparePartId).HasColumnName("spare_part_id");
            entity.Property(c => c.ParticipantAUserId).HasColumnName("participant_a_user_id").IsRequired();
            entity.Property(c => c.ParticipantBUserId).HasColumnName("participant_b_user_id").IsRequired();
            entity.Property(c => c.LastMessageAt).HasColumnName("last_message_at");
            entity.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(c => c.RepairRequestId)
                .IsUnique()
                .HasFilter("repair_request_id IS NOT NULL");

            entity.HasIndex(c => new { c.SparePartId, c.ParticipantBUserId })
                .IsUnique()
                .HasFilter("spare_part_id IS NOT NULL");

            entity.HasIndex(c => new { c.ParticipantAUserId, c.LastMessageAt });
            entity.HasIndex(c => new { c.ParticipantBUserId, c.LastMessageAt });

            entity.HasOne(c => c.RepairRequest)
                .WithMany()
                .HasForeignKey(c => c.RepairRequestId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.SparePart)
                .WithMany()
                .HasForeignKey(c => c.SparePartId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.ParticipantAUser)
                .WithMany()
                .HasForeignKey(c => c.ParticipantAUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.ParticipantBUser)
                .WithMany()
                .HasForeignKey(c => c.ParticipantBUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasColumnName("id");
            entity.Property(m => m.ConversationId).HasColumnName("conversation_id").IsRequired();
            entity.Property(m => m.SenderUserId).HasColumnName("sender_user_id").IsRequired();
            entity.Property(m => m.Body).HasColumnName("body").HasMaxLength(ChatMessage.MaxBodyLength).IsRequired();
            entity.Property(m => m.SentAt).HasColumnName("sent_at").IsRequired();
            entity.Property(m => m.ReadAt).HasColumnName("read_at");

            entity.HasIndex(m => new { m.ConversationId, m.SentAt });
            entity.HasIndex(m => new { m.ConversationId, m.SenderUserId, m.ReadAt });

            entity.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.SenderUser)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
