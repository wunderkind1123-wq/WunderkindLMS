using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Olimpiada bo'limi jadvallari (Olympiad.cs, docs/modules/olympiad.md). Hammasi `olympiad_` prefiksli.</summary>
internal static class OlympiadModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<OlympiadSettings>(e =>
        {
            e.ToTable("olympiad_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.BotToken).HasMaxLength(200);
            e.Property(x => x.BotUsername).HasMaxLength(100);
            e.Property(x => x.BotName).HasMaxLength(200);
            e.Property(x => x.DefaultOnFull).HasMaxLength(16);
            e.Property(x => x.RulesFileUrl).HasMaxLength(500);
            e.Property(x => x.RulesFileName).HasMaxLength(200);
            e.Property(x => x.RulesDisplay).HasMaxLength(8);
            e.Property(x => x.InviteText).HasMaxLength(100);
            e.Property(x => x.InviteUrl).HasMaxLength(500);
            e.Property(x => x.CertificateFormat).HasMaxLength(8);
            e.Property(x => x.AdminLink).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        b.Entity<OlympiadChannel>(e =>
        {
            e.ToTable("olympiad_channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.ChatRef).HasMaxLength(100);
            e.Property(x => x.InviteLink).HasMaxLength(300);
            e.Property(x => x.LastError).HasMaxLength(300);
        });

        b.Entity<OlympiadBotUser>(e =>
        {
            e.ToTable("olympiad_bot_users");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.DisplayName);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.FirstName).HasMaxLength(200);
            e.Property(x => x.LastName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Source).HasMaxLength(64);
            e.HasIndex(x => x.TelegramUserId).IsUnique();
        });

        b.Entity<OlympiadParticipant>(e =>
        {
            e.ToTable("olympiad_participants");
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.School).HasMaxLength(100);
            e.Property(x => x.LeadId).HasMaxLength(64);
            e.HasIndex(x => x.BotUserId);
            e.HasIndex(x => x.Phone);
            e.HasOne<OlympiadBotUser>().WithMany().HasForeignKey(x => x.BotUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<OlympiadEvent>(e =>
        {
            e.ToTable("olympiad_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Location).HasMaxLength(300);
            e.Property(x => x.LocationMapUrl).HasMaxLength(500);
            e.Property(x => x.OnFull).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.MaxScore).HasPrecision(10, 2);
            e.Property(x => x.RulesFileUrl).HasMaxLength(500);
            e.Property(x => x.RulesFileName).HasMaxLength(200);
            e.Property(x => x.RulesDisplay).HasMaxLength(8);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.Status, x.StartsAt });
        });

        b.Entity<OlympiadRegistration>(e =>
        {
            e.ToTable("olympiad_registrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.QrToken).HasMaxLength(64);
            e.Property(x => x.CheckedInBy).HasMaxLength(200);
            e.Property(x => x.CheckinMethod).HasMaxLength(16);
            e.Property(x => x.Score).HasPrecision(10, 2);
            e.Property(x => x.ResultSentScore).HasPrecision(10, 2);
            e.Property(x => x.ScoreSource).HasMaxLength(16);
            e.Property(x => x.ScoreUpdatedBy).HasMaxLength(200);
            e.Property(x => x.CreatedVia).HasMaxLength(8);
            e.HasIndex(x => new { x.EventId, x.ParticipantId }).IsUnique();
            e.HasIndex(x => x.QrToken).IsUnique();
            e.HasIndex(x => x.ParticipantId);
            e.HasOne<OlympiadEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<OlympiadParticipant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OlympiadReminder>(e =>
        {
            e.ToTable("olympiad_reminders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Target).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.EventId });
            e.HasOne<OlympiadEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OlympiadBroadcast>(e =>
        {
            e.ToTable("olympiad_broadcasts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => x.Status);
        });

        b.Entity<OlympiadDelivery>(e =>
        {
            e.ToTable("olympiad_deliveries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Tag).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(300);
            e.HasIndex(x => new { x.Kind, x.RefId, x.BotUserId, x.RegistrationId, x.Tag }).IsUnique();
            e.HasIndex(x => new { x.Status, x.Id });
        });

        b.Entity<OlympiadTemplate>(e =>
        {
            e.ToTable("olympiad_templates");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });

        b.Entity<OlympiadMediaCache>(e =>
        {
            e.ToTable("olympiad_media_cache");
            e.HasKey(x => x.Path);
            e.Property(x => x.Path).HasMaxLength(500);
            e.Property(x => x.FileId).HasMaxLength(300);
        });
    }
}
