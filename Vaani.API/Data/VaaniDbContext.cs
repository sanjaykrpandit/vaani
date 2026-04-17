using Microsoft.EntityFrameworkCore;
using Vaani.API.Models.Entities;

namespace Vaani.API.Data;

/// <summary>
/// Database context for Vaani API
/// </summary>
public class VaaniDbContext : DbContext
{
    public VaaniDbContext(DbContextOptions<VaaniDbContext> options) : base(options)
    {
    }

    public DbSet<Meeting> Meetings { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<SessionLog> SessionLogs { get; set; }
    public DbSet<AdminUser> AdminUsers { get; set; }
    public DbSet<AzureSubscription> AzureSubscriptions { get; set; }
    public DbSet<Language> Languages { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AdminUser configuration
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("admin_users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(50);
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired().HasMaxLength(255);
            entity.Property(e => e.FullName).HasColumnName("full_name").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasColumnName("email").IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        });

        // AzureSubscription configuration
        modelBuilder.Entity<AzureSubscription>(entity =>
        {
            entity.ToTable("azure_subscriptions");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SubscriptionKey).HasColumnName("subscription_key").IsRequired().HasMaxLength(255);
            entity.Property(e => e.Region).HasColumnName("region").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasMany(e => e.Meetings)
                .WithOne(e => e.AzureSubscription)
                .HasForeignKey(e => e.AzureSubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Meeting configuration
        modelBuilder.Entity<Meeting>(entity =>
        {
            entity.ToTable("meetings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MeetingId).IsUnique();
            entity.HasIndex(e => e.AzureSubscriptionId);            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id").IsRequired().HasMaxLength(50);
            entity.Property(e => e.MeetingName).HasColumnName("meeting_name").IsRequired().HasMaxLength(255);
            entity.Property(e => e.AzureSubscriptionId).HasColumnName("azure_subscription_id").IsRequired();
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidUntil).HasColumnName("valid_until");
            entity.Property(e => e.MeetingLanguage).HasColumnName("meeting_language").IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired().HasMaxLength(255);
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by").IsRequired().HasMaxLength(255);
            entity.Property(e => e.PublicToken).HasColumnName("public_token").IsRequired().HasMaxLength(255);

            entity.HasMany(e => e.Sessions)
                .WithOne(e => e.Meeting)
                .HasForeignKey(e => e.MeetingId)
                .HasPrincipalKey(e => e.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Session configuration
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MeetingId, e.DeviceId });
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id").IsRequired().HasMaxLength(50);
            entity.Property(e => e.DeviceId).HasColumnName("device_id").IsRequired().HasMaxLength(50);
            entity.Property(e => e.DeviceName).HasColumnName("device_name").IsRequired().HasMaxLength(255);
            entity.Property(e => e.AppVersion).HasColumnName("app_version").HasMaxLength(20);
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("Active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SessionLog).HasColumnName("session_log");
            entity.Property(e => e.SessionTrascript).HasColumnName("session_transcript");
            entity.Property(e => e.UserName).HasColumnName("user_name");

            entity.HasMany(e => e.SessionLogs)
                .WithOne(e => e.Session)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SessionLog configuration
        modelBuilder.Entity<SessionLog>(entity =>
        {
            entity.ToTable("session_logs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId);
            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Details).HasColumnName("details").HasColumnType("text");
        });

        // Language configuration
        modelBuilder.Entity<Language>(entity =>
        {
            entity.ToTable("languages");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LanguageCode).IsUnique();            
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code").IsRequired().HasMaxLength(10);
            entity.Property(e => e.LanguageName).HasColumnName("language_name").IsRequired().HasMaxLength(100);
            entity.Property(e => e.LanguageMaleNeural).HasColumnName("language_male_neural").IsRequired().HasMaxLength(100);
            entity.Property(e => e.LanguageFemaleNeural).HasColumnName("language_female_neural").IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by").IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
