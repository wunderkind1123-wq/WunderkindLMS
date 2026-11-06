using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Ish reja va ichki monitoring (<c>CurriculumMonitoring</c> migration,
/// docs/modules/curriculum-monitoring.md). Also the two block-subject columns of a track group
/// (§3 — certificate points).
/// </summary>
internal static class CurriculumMonitoringModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<StudyGroup>(e =>
        {
            e.Property(x => x.BlockSubject1Id).HasMaxLength(64);
            e.Property(x => x.BlockSubject2Id).HasMaxLength(64);
        });

        // ---------------------------------------------------------------- ish reja

        b.Entity<CurriculumPlan>(e =>
        {
            e.ToTable("curriculum_plans", t =>
            {
                // exactly one owner: a grade or a track group
                t.HasCheckConstraint("ck_curriculum_plans_owner", "(grade is null) <> (group_id is null)");
                t.HasCheckConstraint("ck_curriculum_plans_grade", "grade is null or grade between 0 and 11");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.FileUrl).HasMaxLength(500);
            e.Property(x => x.FileName).HasMaxLength(255);
            // one plan per subject + grade, one per subject + track group
            e.HasIndex(x => new { x.SubjectId, x.Grade }).IsUnique().HasFilter("grade is not null")
                .HasDatabaseName("ux_curriculum_plans_subject_grade");
            e.HasIndex(x => new { x.SubjectId, x.GroupId }).IsUnique().HasFilter("group_id is not null")
                .HasDatabaseName("ux_curriculum_plans_subject_group");
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Topics).WithOne().HasForeignKey(t => t.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CurriculumTopic>(e =>
        {
            e.ToTable("curriculum_topics", t =>
            {
                t.HasCheckConstraint("ck_curriculum_topics_quarter", "quarter is null or quarter between 1 and 4");
                t.HasCheckConstraint("ck_curriculum_topics_hours", "hours >= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Section).HasMaxLength(300);
            e.Property(x => x.Goal).HasMaxLength(1000);
            e.Property(x => x.Homework).HasMaxLength(500);
            e.HasIndex(x => new { x.PlanId, x.Order }).IsUnique().HasDatabaseName("ux_curriculum_topics_order");
        });

        b.Entity<CurriculumProgress>(e =>
        {
            e.ToTable("curriculum_progress", t =>
                t.HasCheckConstraint("ck_curriculum_progress_owner_kind", "owner_kind in ('class','group')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerKind).HasMaxLength(8);
            e.Property(x => x.OwnerId).HasMaxLength(64);
            e.HasIndex(x => new { x.TopicId, x.OwnerKind, x.OwnerId }).IsUnique()
                .HasDatabaseName("ux_curriculum_progress_once");
            e.HasIndex(x => new { x.OwnerKind, x.OwnerId });
            e.HasOne<CurriculumTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------- monitoring

        b.Entity<MonitoringBank>(e =>
        {
            e.ToTable("monitoring_banks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.BankId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MonitoringQuestion>(e =>
        {
            e.ToTable("monitoring_questions", t =>
            {
                t.HasCheckConstraint("ck_monitoring_questions_correct", "correct_index >= 0 and correct_index < 6");
                t.HasCheckConstraint("ck_monitoring_questions_weight", "weight > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.Weight).HasPrecision(6, 2);
            e.HasIndex(x => new { x.BankId, x.Order });
            // a topic deleted by a plan re-upload just unlinks the question
            e.HasOne<CurriculumTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MonitoringTest>(e =>
        {
            e.ToTable("monitoring_tests", t =>
            {
                t.HasCheckConstraint("ck_monitoring_tests_delivery", "delivery in ('paper','online')");
                t.HasCheckConstraint("ck_monitoring_tests_scoring", "scoring in ('simple','national')");
                t.HasCheckConstraint("ck_monitoring_tests_method",
                    "(scoring = 'simple' and national_method is null) or (scoring = 'national' and national_method in ('rasch','weights'))");
                t.HasCheckConstraint("ck_monitoring_tests_status", "status in ('draft','published','closed')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Delivery).HasMaxLength(8);
            e.Property(x => x.Scoring).HasMaxLength(10);
            e.Property(x => x.NationalMethod).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(10);
            e.HasIndex(x => x.TestDate);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.TestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MonitoringItem>(e =>
        {
            e.ToTable("monitoring_items", t =>
            {
                t.HasCheckConstraint("ck_monitoring_items_options", "option_count between 2 and 6");
                t.HasCheckConstraint("ck_monitoring_items_correct", "correct_index >= 0 and correct_index < option_count");
                t.HasCheckConstraint("ck_monitoring_items_weight", "weight > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.Weight).HasPrecision(6, 2);
            e.HasIndex(x => new { x.TestId, x.Order }).IsUnique().HasDatabaseName("ux_monitoring_items_order");
            e.HasOne<MonitoringQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<CurriculumTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MonitoringParticipant>(e =>
        {
            e.ToTable("monitoring_participants", t =>
            {
                t.HasCheckConstraint("ck_monitoring_participants_status", "status in ('pending','in_progress','done','absent')");
                t.HasCheckConstraint("ck_monitoring_participants_owner_kind", "owner_kind in ('class','group')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerKind).HasMaxLength(8);
            e.Property(x => x.OwnerId).HasMaxLength(64);
            e.Property(x => x.Answers).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.AccessCode).HasMaxLength(12);
            e.Property(x => x.DeviceToken).HasMaxLength(64);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.Property(x => x.Points).HasPrecision(5, 1);
            e.Property(x => x.Level).HasMaxLength(4);
            e.HasIndex(x => new { x.TestId, x.StudentId }).IsUnique().HasDatabaseName("ux_monitoring_participants_once");
            e.HasIndex(x => x.AccessCode).IsUnique().HasFilter("access_code is not null")
                .HasDatabaseName("ux_monitoring_participants_code");
            e.HasOne<MonitoringTest>().WithMany().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
