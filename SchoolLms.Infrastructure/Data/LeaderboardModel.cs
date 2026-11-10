using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// <c>leaderboard_reasons</c> and <c>leaderboard_awards</c> — the points staff add by hand to the pupils'
/// leaderboard (<c>LeaderboardAwards</c> migration, 2026-11-08). Everything else the leaderboard shows is
/// read from where it already lives.
/// </summary>
internal static class LeaderboardModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<LeaderboardReason>(e =>
        {
            e.ToTable("leaderboard_reasons", t =>
                t.HasCheckConstraint("ck_leaderboard_reasons_points", "points between -1000 and 1000"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
        });

        b.Entity<LeaderboardAward>(e =>
        {
            e.ToTable("leaderboard_awards", t =>
                t.HasCheckConstraint("ck_leaderboard_awards_points", "points between -1000 and 1000 and points <> 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.ReasonName).HasMaxLength(120);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            // A deleted reason leaves its awards (they keep the copied name and points).
            e.HasOne<LeaderboardReason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.StudentId, x.Date }).HasDatabaseName("ix_leaderboard_awards_student_date");
        });
    }
}
