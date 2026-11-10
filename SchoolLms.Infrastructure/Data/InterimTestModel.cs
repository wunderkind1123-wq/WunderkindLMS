using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// <c>interim_tests</c> and <c>interim_test_results</c> — the track groups' weekly interim tests
/// (<c>InterimTests</c> migration, 2026-11-10).
/// </summary>
internal static class InterimTestModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<InterimTest>(e =>
        {
            e.ToTable("interim_tests", t =>
                t.HasCheckConstraint("ck_interim_tests_question_count", "question_count between 1 and 300"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GroupId, x.SubjectId, x.Date }).IsUnique()
                .HasDatabaseName("ux_interim_tests_group_subject_date");
        });

        b.Entity<InterimTestResult>(e =>
        {
            e.ToTable("interim_test_results", t =>
                t.HasCheckConstraint("ck_interim_test_results_value",
                    "(absent and correct is null) or (not absent and correct is not null and correct >= 0)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasOne<InterimTest>().WithMany().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TestId, x.StudentId }).IsUnique()
                .HasDatabaseName("ux_interim_test_results_test_student");
            e.HasIndex(x => x.StudentId).HasDatabaseName("ix_interim_test_results_student_id");
        });
    }
}
