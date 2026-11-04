using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "Sinfim" — marks and behaviour points of the homeroom teacher's pupils (client, 2026-11-04:
//  parents ask the homeroom teacher why a mark or a point was given, so every mark and point must be
//  visible to them, whoever gave it).
//
//  WHO: "my pupils" is the red list's and the debts card's circle (HomeroomDirectory.PupilsAsync);
//  anyone else's pupil reads as missing. READ-ONLY: nothing here writes.
//
//  THE SAME NUMBERS AS THE PARENT: quarter results come from StudentReportBuilder and the score and its
//  history from DisciplineIncidents.HistoryAsync. The parent-only `show_learning_progress` flag does not
//  apply: it closes the parents' cabinet, not the school's own staff.
//
//  WHO GAVE A MARK: journal_entries keeps no author, so the name is read from the lesson — an active
//  substitution first, then the timetable cell of that weekday and period, then any lesson of that
//  subject for that class. Seasonal marks and behaviour points do keep their author.
// ===========================================================================

/// <summary>What a homeroom teacher reads about their own pupils. See the file header.</summary>
public static class HomeroomPupilProgress
{
    /// <summary>How many seasonal marks the page shows (newest first).</summary>
    private const int RecentSeasonalMarks = 30;

    /// <summary>The behaviour score of every pupil the teacher leads.</summary>
    public static async Task<List<HomeroomPupilScoreDto>> ScoresAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var (_, groupOf) = await HomeroomDirectory.PupilsAsync(db, teacherId, ct);
        var ids = groupOf.Keys.ToList();
        var totals = await DisciplineIncidents.TotalsAsync(db, ids, ct);
        return [.. ids.Select(id =>
        {
            var (plus, minus) = totals.GetValueOrDefault(id);
            return new HomeroomPupilScoreDto(id, DisciplineIncidents.BaseScore + plus - minus);
        })];
    }

    /// <summary>
    /// One pupil's page: quarter results, the quarter's journal marks, seasonal marks and the behaviour
    /// history. <c>null</c> when the pupil is not one of the teacher's. <paramref name="quarter"/> null
    /// — the current quarter.
    /// </summary>
    public static async Task<HomeroomPupilProgressDto?> ForTeacherAsync(
        IAppDbContext db, string teacherId, string studentId, int? quarter, CancellationToken ct = default)
    {
        var (_, groupOf) = await HomeroomDirectory.PupilsAsync(db, teacherId, ct);
        if (!groupOf.TryGetValue(studentId, out var group)) return null;
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return null;

        var q = quarter ?? (await PortalSchedule.CurrentQuarterWeekAsync(db)).Quarter;
        var quarters = await db.Quarters.AsNoTracking().OrderBy(x => x.Quarter).Select(x => x.Quarter).ToListAsync(ct);
        var report = await StudentReportBuilder.BuildAsync(db, student);
        var subjects = report.Subjects
            .Select(s => new HomeroomSubjectAverageDto(s.Id, s.Name,
                report.Grades.TryGetValue(s.Id, out var byQuarter) && byQuarter.TryGetValue(q, out var average)
                    ? average : null))
            .ToList();

        return new HomeroomPupilProgressDto(
            student.Id, student.FullName, group, q, quarters, subjects,
            await GradesAsync(db, student, q, ct),
            await SeasonalMarksAsync(db, student.Id, ct),
            await DisciplineIncidents.HistoryAsync(db, student.Id, ct));
    }

    /// <summary>The quarter's journal marks, newest first — the same entries the report averages.</summary>
    private static async Task<List<HomeroomGradeDto>> GradesAsync(
        IAppDbContext db, Student student, int quarter, CancellationToken ct)
    {
        var classId = await db.Classes.AsNoTracking()
            .Where(c => c.Name == student.ClassName).Select(c => c.Id).FirstOrDefaultAsync(ct);
        var attainment = await ClassAttainment.ForStudentAsync(db, student, ct);
        var ownerIds = attainment.OwnerIdsForStudent(student.Id, classId);
        var unscoped = ownerIds.Count == 0;

        var entries = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.StudentId == student.Id && e.Quarter == quarter && e.Grade != null
                    && (unscoped || ownerIds.Contains(e.ClassId)))
                .ToListAsync(ct))
            .Where(e => attainment.CountsFor(student.Id, e.ClassId, e.OwnerKind, e.Date))
            .OrderByDescending(e => e.Date, StringComparer.Ordinal).ThenByDescending(e => e.Period)
            .ToList();
        if (entries.Count == 0) return [];

        var owners = entries.Select(e => e.ClassId).Distinct().ToList();
        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var notes = (await db.LessonNotes.AsNoTracking()
                .Where(n => owners.Contains(n.ClassId) && n.Quarter == quarter)
                .ToListAsync(ct))
            .GroupBy(n => (n.ClassId, n.Date, n.Period, n.SubjectId, n.SubGroup))
            .ToDictionary(g => g.Key, g => g.First().Topic);
        var substitutes = (await db.LessonSubstitutions.AsNoTracking()
                .Where(s => s.CancelledAt == null && owners.Contains(s.OwnerId))
                .ToListAsync(ct))
            .ToLookup(s => (s.OwnerId, Date: s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), s.Period, s.SubjectId));
        var lessons = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => owners.Contains(t.ClassId))
                .ToListAsync(ct))
            .SelectMany(t => t.Lessons.Select(l => (OwnerId: t.ClassId, Lesson: l)))
            .ToList();

        string TeacherOf(JournalEntry e)
        {
            var substitute = substitutes[(e.ClassId, e.Date, e.Period, e.SubjectId)]
                .FirstOrDefault(s => e.SubGroup == 0 || s.SubGroup == 0 || s.SubGroup == e.SubGroup);
            if (substitute is not null) return teacherNames.GetValueOrDefault(substitute.SubstituteTeacherId, "");
            var ofSubject = lessons
                .Where(x => x.OwnerId == e.ClassId && x.Lesson.SubjectId == e.SubjectId
                    && (e.SubGroup == 0 || x.Lesson.SubGroup == 0 || x.Lesson.SubGroup == e.SubGroup))
                .Select(x => x.Lesson)
                .ToList();
            var day = WeekdayIndex(e.Date);
            var exact = ofSubject.Where(l => l.Day == day && l.Period == e.Period).ToList();
            var source = exact.Count > 0 ? exact : ofSubject;
            return string.Join(", ", source
                .Select(l => teacherNames.GetValueOrDefault(l.TeacherId, ""))
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        }

        return [.. entries.Select(e => new HomeroomGradeDto(
            e.Date, e.Period, subjectNames.GetValueOrDefault(e.SubjectId, ""), e.Grade!.Value,
            notes.GetValueOrDefault((e.ClassId, e.Date, e.Period, e.SubjectId, e.SubGroup), ""),
            TeacherOf(e)))];
    }

    /// <summary>The pupil's latest seasonal marks with their authors.</summary>
    private static async Task<List<HomeroomSeasonalMarkDto>> SeasonalMarksAsync(
        IAppDbContext db, string studentId, CancellationToken ct)
    {
        var marks = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .OrderByDescending(m => m.UpdatedAt).Take(RecentSeasonalMarks)
            .ToListAsync(ct);
        if (marks.Count == 0) return [];

        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var authorIds = marks.Where(m => m.CreatedByUserId != null).Select(m => m.CreatedByUserId!).Distinct().ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return [.. marks.Select(m => new HomeroomSeasonalMarkDto(
            subjectNames.GetValueOrDefault(m.SubjectId, ""), m.PeriodKind, m.Year, m.Month, m.Quarter,
            m.Score, m.Comment, authors.GetValueOrDefault(m.CreatedByUserId ?? "", "")))];
    }

    /// <summary>ISO date → ScheduleLesson.Day (0 = Monday … 6 = Sunday); -1 when unreadable.</summary>
    private static int WeekdayIndex(string isoDate) =>
        DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", out var date) ? ((int)date.DayOfWeek + 6) % 7 : -1;
}
