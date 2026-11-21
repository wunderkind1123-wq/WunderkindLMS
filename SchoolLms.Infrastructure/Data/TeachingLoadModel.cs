using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Yuklama, ustoz bandligi va dars xonasi (2026-11-21, migratsiya <c>TeachingLoads</c>).
/// Mavjud ma'lumot o'zgarmaydi: <c>schedule_lesson.room_id</c> nullable va DEFAULT'siz.
/// </summary>
internal static class TeachingLoadModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<TeachingLoad>(e =>
        {
            e.ToTable("teaching_loads", t =>
            {
                t.HasCheckConstraint("ck_teaching_loads_hours", "hours_per_week between 1 and 20");
                t.HasCheckConstraint("ck_teaching_loads_sub_group", "sub_group between 0 and 2");
                t.HasCheckConstraint("ck_teaching_loads_owner_kind", "owner_kind in ('class','group')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerKind).HasMaxLength(10);
            e.Property(x => x.OwnerId).HasMaxLength(64);
            e.Property(x => x.SubjectId).HasMaxLength(64);
            e.Property(x => x.TeacherId).HasMaxLength(64);
            e.HasIndex(x => new { x.OwnerKind, x.OwnerId, x.SubjectId, x.SubGroup }).IsUnique()
                .HasDatabaseName("ux_teaching_loads_owner_subject");
            e.HasIndex(x => x.TeacherId).HasDatabaseName("ix_teaching_loads_teacher");
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TeacherUnavailability>(e =>
        {
            e.ToTable("teacher_unavailability", t =>
            {
                t.HasCheckConstraint("ck_teacher_unavailability_day", "day between 0 and 5");
                t.HasCheckConstraint("ck_teacher_unavailability_period", "period between 0 and 12");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.TeacherId).HasMaxLength(64);
            e.HasIndex(x => new { x.TeacherId, x.Day, x.Period }).IsUnique()
                .HasDatabaseName("ux_teacher_unavailability");
        });

        b.Entity<OwnerStudyDays>(e =>
        {
            e.ToTable("owner_study_days", t => t.HasCheckConstraint("ck_owner_study_days_days", "days between 5 and 6"));
            e.HasKey(x => x.OwnerId);
            e.Property(x => x.OwnerId).HasMaxLength(64);
            e.Property(x => x.OwnerKind).HasMaxLength(10);
        });

        b.Entity<ScheduleLesson>(e =>
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull));
    }
}
