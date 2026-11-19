using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Kunlik hisobot — the academic director's daily PDF (client, 2026-11-16): "har kuni qo'yilayotgan
//  baholar va gamifikatsiya ballari, darslar o'tilgan yoki o'tilmaganligi haqida kunlik hisobot …
//  soat 17:00 da telegram botga pdf file holatda". The point is control over the teachers, so every
//  number is per teacher.
//
//  WHAT ONE DAY SHOWS
//    · every timetable lesson of the day (quarter → week → the template assigned to it → that weekday),
//      with its teacher — the substitute when the lesson was covered — and its state:
//        conducted  the teacher marked "dars o'tildi" (lesson_notes.conducted — the rule the teacher
//                   report and payroll use, TeacherActivityReport);
//        marked     nobody marked it conducted, but attendance was taken on it (daily_attendance_marks);
//        missed     neither — "o'tilmagan";
//      a lesson conducted outside the timetable is listed too, as conducted;
//    · the marks of the day (journal_entries.date = the day, grade set), per lesson, per teacher and per
//      class, with the 5/4/3/2 split;
//    · the gamification entries made that day (discipline_points.created_at on the day): who gave them,
//      to which class, for what, plus and minus. Attendance-reason points are automatic — left out.
//
//  WHOSE GROUPS: classes, track groups always, other study groups only when group lessons are on —
//  LessonRoster.GroupScopeAsync, the same scope as the teacher report.
//
//  READ-ONLY: nothing here writes. The PDF is DailyDirectorReportDocument, the sending
//  DailyDirectorReportService.
// ===========================================================================

/// <summary>One timetable lesson of the day and what happened to it.</summary>
/// <param name="State"><see cref="DailyLessonState"/>.</param>
/// <param name="Grades">Marks given on this lesson.</param>
public sealed record DailyLesson(
    int Period, string OwnerId, string OwnerName, string Subject, int SubGroup,
    string? TeacherId, string TeacherName, string State, bool HasTopic, bool HasHomework, List<int> Grades)
{
    public double? Average => Grades.Count == 0 ? null : Math.Round(Grades.Average(), 2);
}

/// <summary>A lesson's state at report time.</summary>
public static class DailyLessonState
{
    public const string Conducted = "conducted";
    public const string Marked = "marked";
    public const string Missed = "missed";
}

/// <summary>One teacher's day.</summary>
public sealed record DailyTeacherRow(
    string? TeacherId, string Name, int Lessons, int Conducted, int Marked, int Missed,
    int WithTopic, int Grades, double? Average, int GamePlus, int GameMinus, int GameEntries)
{
    /// <summary>Had lessons but conducted none, gave no mark and no point.</summary>
    public bool Idle => Lessons > 0 && Conducted == 0 && Grades == 0 && GameEntries == 0;
}

/// <summary>One class's (or track group's) marks of the day.</summary>
public sealed record DailyClassRow(string Name, int Five, int Four, int Three, int Two, int GamePlus, int GameMinus)
{
    public int Grades => Five + Four + Three + Two;
    public double? Average => Grades == 0 ? null : Math.Round((5.0 * Five + 4.0 * Four + 3.0 * Three + 2.0 * Two) / Grades, 2);
}

/// <summary>
/// Blok guruhi (docs/modules/blocks.md, TZ 6.5): ustoz bo'yicha har guruh alohida qator — darslar, qo'yilgan
/// baholar soni va gamifikatsiya ballari.
/// </summary>
public sealed record DailyBlockGroupRow(
    string Teacher, string Block, string Group, int Lessons, int Grades, double? Average, int GamePlus, int GameMinus);

/// <summary>One gamification entry of the day.</summary>
public sealed record DailyGameEntry(string Time, string Author, string Student, string ClassName, string Reason, int Points);

/// <summary>Everything the daily PDF prints.</summary>
public sealed record DailyDirectorReportModel(
    string SchoolName,
    DateOnly Date,
    DateTime GeneratedAt,
    List<DailyLesson> Lessons,
    List<DailyTeacherRow> Teachers,
    List<DailyClassRow> Classes,
    List<DailyGameEntry> Game,
    List<DailyBlockGroupRow>? BlockGroups = null)
{
    /// <summary>Blok guruhlari qatorlari (blok bo'lmasa — bo'sh).</summary>
    public List<DailyBlockGroupRow> BlockRows => BlockGroups ?? [];

    public int Planned => Lessons.Count;
    public int Conducted => Lessons.Count(l => l.State == DailyLessonState.Conducted);
    public int Marked => Lessons.Count(l => l.State == DailyLessonState.Marked);
    public int Missed => Lessons.Count(l => l.State == DailyLessonState.Missed);

    /// <summary>Conducted share of the day's lessons, 0–100; null — no lesson.</summary>
    public int? ConductedPercent => Planned == 0 ? null : (int)Math.Round(100.0 * Conducted / Planned);

    public int GradeCount => Classes.Sum(c => c.Grades);

    public double? GradeAverage => GradeCount == 0
        ? null
        : Math.Round(Classes.Sum(c => 5.0 * c.Five + 4.0 * c.Four + 3.0 * c.Three + 2.0 * c.Two) / GradeCount, 2);

    public int GamePlus => Game.Where(g => g.Points > 0).Sum(g => g.Points);
    public int GameMinus => -Game.Where(g => g.Points < 0).Sum(g => g.Points);
}

/// <summary>Builds <see cref="DailyDirectorReportModel"/>. See the file header.</summary>
public static class DailyDirectorReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"16 noyabr 2026, dushanba".</summary>
    public static string Label(DateOnly d) =>
        $"{d.Day:00} {WeeklyReport.MonthsGenitive[d.Month - 1]} {d.Year}, {WeeklyReport.WeekDay(d).ToLowerInvariant()}";

    /// <summary>A school day: not Sunday, not a holiday.</summary>
    public static async Task<bool> IsSchoolDayAsync(IAppDbContext db, DateOnly day, CancellationToken ct = default)
    {
        if (day.DayOfWeek == DayOfWeek.Sunday) return false;
        var iso = Iso(day);
        return !await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == iso, ct);
    }

    public static async Task<DailyDirectorReportModel> BuildAsync(IAppDbContext db, DateOnly day, CancellationToken ct = default)
    {
        var iso = Iso(day);
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        var scope = await LessonRoster.GroupScopeAsync(db, ct);
        bool InScope(string kind, string id) => scope.Allows(kind, id);
        string OwnerName(string id) => owners.TryGetValue(id, out var o) ? o.Name : "—";
        string KindOf(string id, string fallback) => owners.TryGetValue(id, out var o) ? o.Kind : fallback;

        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string SubjectName(string id) => subjects.GetValueOrDefault(id, "Fan");
        var teachers = await db.Teachers.AsNoTracking()
            .Select(t => new { t.Id, t.FullName, t.UserId }).ToListAsync(ct);
        var teacherName = teachers.ToDictionary(t => t.Id, t => t.FullName, StringComparer.Ordinal);
        var teacherOfUser = teachers.Where(t => t.UserId != null)
            .GroupBy(t => t.UserId!).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        // ---- The day's timetable (HeldLessons.ScheduledAsync's walk, keeping the teacher) ----
        var slots = new List<(string OwnerId, string Kind, ScheduleLesson L)>();
        var quarter = (await db.Quarters.AsNoTracking().ToListAsync(ct)).FirstOrDefault(q =>
            string.CompareOrdinal(iso, q.StartDate) >= 0 && string.CompareOrdinal(iso, q.EndDate) <= 0);
        var week = quarter is null
            ? null
            : ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate).FirstOrDefault(w =>
                string.CompareOrdinal(iso, w.StartISO) >= 0 && string.CompareOrdinal(iso, w.EndISO) <= 0);
        if (quarter is not null && week is not null && await IsSchoolDayAsync(db, day, ct))
        {
            var assignments = await db.WeekAssignments.AsNoTracking()
                .Where(a => a.TemplateId != null && a.Quarter == quarter.Quarter && a.Week == week.Week)
                .Select(a => new { a.ClassId, a.OwnerKind, a.TemplateId }).ToListAsync(ct);
            var templateIds = assignments.Select(a => a.TemplateId!).Distinct().ToList();
            var lessonsByTemplate = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                    .Where(t => templateIds.Contains(t.Id)).ToListAsync(ct))
                .ToDictionary(t => t.Id, t => t.Lessons, StringComparer.Ordinal);
            var lessonDay = (int)day.DayOfWeek - 1; // dushanba = 0
            foreach (var a in assignments)
            {
                if (!owners.ContainsKey(a.ClassId) || !InScope(KindOf(a.ClassId, a.OwnerKind), a.ClassId)) continue;
                if (!lessonsByTemplate.TryGetValue(a.TemplateId!, out var lessons)) continue;
                foreach (var l in lessons.Where(l => l.Day == lessonDay))
                    slots.Add((a.ClassId, KindOf(a.ClassId, a.OwnerKind), l));
            }
        }
        slots = slots.DistinctBy(s => (s.OwnerId, s.L.SubjectId, s.L.Period, s.L.SubGroup)).ToList();

        // Covered lessons belong to the substitute; an external substitute is named, not a teacher here.
        var subs = await db.LessonSubstitutions.AsNoTracking()
            .Where(s => s.CancelledAt == null && s.Date == day).ToListAsync(ct);
        var externalIds = subs.Where(s => s.ExternalSubstituteId != null).Select(s => s.ExternalSubstituteId!.Value).ToList();
        var externals = externalIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ExternalSubstitutes.AsNoTracking().Where(e => externalIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        LessonSubstitution? CoverOf(string ownerId, string subjectId, int period, int sub) =>
            subs.FirstOrDefault(s => s.OwnerId == ownerId && s.SubjectId == subjectId && s.Period == period && s.SubGroup == sub)
            ?? subs.FirstOrDefault(s => s.OwnerId == ownerId && s.SubjectId == subjectId && s.Period == period && s.SubGroup == 0);

        (string? Id, string Name) TeacherOf(string ownerId, string subjectId, int period, int sub, string? planned)
        {
            var cover = CoverOf(ownerId, subjectId, period, sub);
            if (cover?.SubstituteTeacherId is { } st) return (st, teacherName.GetValueOrDefault(st, "—") + " (o'rindosh)");
            if (cover?.ExternalSubstituteId is { } ex)
                return (null, externals.GetValueOrDefault(ex, "Tashqi o'rindosh") + " (tashqi o'rindosh)");
            return string.IsNullOrEmpty(planned) ? (null, "Biriktirilmagan") : (planned, teacherName.GetValueOrDefault(planned, "—"));
        }

        // ---- What happened: notes, attendance marks, marks ----
        var notes = (await db.LessonNotes.AsNoTracking().Where(n => n.Date == iso).ToListAsync(ct))
            .Where(n => InScope(KindOf(n.ClassId, n.OwnerKind), n.ClassId)).ToList();
        var marked = (await HeldLessons.MarkedAsync(db, null, iso, iso, ct))
            .Select(m => (m.ClassId, m.SubjectId, m.Period)).ToHashSet();
        var grades = (await db.JournalEntries.AsNoTracking().Where(e => e.Date == iso && e.Grade != null)
                .Select(e => new { e.ClassId, e.OwnerKind, e.SubjectId, e.Period, e.SubGroup, e.StudentId, Grade = e.Grade!.Value, e.SourceClassId })
                .ToListAsync(ct))
            .Where(e => InScope(KindOf(e.ClassId, e.OwnerKind), e.ClassId)).ToList();

        static bool SameHalf(int a, int b) => a == 0 || b == 0 || a == b;

        var lessonsOut = new List<DailyLesson>();
        var usedNotes = new HashSet<LessonNote>();
        var usedGrades = new HashSet<object>();
        foreach (var s in slots)
        {
            var l = s.L;
            var note = notes.FirstOrDefault(n => n.ClassId == s.OwnerId && n.SubjectId == l.SubjectId
                && n.Period == l.Period && SameHalf(n.SubGroup, l.SubGroup));
            if (note is not null) usedNotes.Add(note);
            var lessonGrades = grades.Where(g => g.ClassId == s.OwnerId && g.SubjectId == l.SubjectId
                && g.Period == l.Period && SameHalf(g.SubGroup, l.SubGroup)).ToList();
            foreach (var g in lessonGrades) usedGrades.Add(g);
            var state = note?.Conducted == true ? DailyLessonState.Conducted
                : marked.Contains((s.OwnerId, l.SubjectId, l.Period)) ? DailyLessonState.Marked
                : DailyLessonState.Missed;
            var (tid, tname) = TeacherOf(s.OwnerId, l.SubjectId, l.Period, l.SubGroup, l.TeacherId);
            lessonsOut.Add(new DailyLesson(
                l.Period, s.OwnerId, OwnerName(s.OwnerId), SubjectName(l.SubjectId), l.SubGroup, tid, tname, state,
                !string.IsNullOrWhiteSpace(note?.Topic), !string.IsNullOrWhiteSpace(note?.Homework),
                [.. lessonGrades.Select(g => g.Grade)]));
        }

        // A lesson conducted outside the timetable (an extra or moved lesson) — its teacher from any
        // template of that owner and subject, when there is exactly one.
        var templateTeachers = await TemplateTeachersAsync(db, ct);
        foreach (var n in notes.Where(n => n.Conducted && !usedNotes.Contains(n)))
        {
            var lessonGrades = grades.Where(g => g.ClassId == n.ClassId && g.SubjectId == n.SubjectId
                && g.Period == n.Period && SameHalf(g.SubGroup, n.SubGroup)).ToList();
            foreach (var g in lessonGrades) usedGrades.Add(g);
            var planned = templateTeachers.GetValueOrDefault((n.ClassId, n.SubjectId, n.SubGroup))
                ?? templateTeachers.GetValueOrDefault((n.ClassId, n.SubjectId, 0));
            var (tid, tname) = TeacherOf(n.ClassId, n.SubjectId, n.Period, n.SubGroup, planned);
            lessonsOut.Add(new DailyLesson(
                n.Period, n.ClassId, OwnerName(n.ClassId), SubjectName(n.SubjectId), n.SubGroup, tid, tname,
                DailyLessonState.Conducted, !string.IsNullOrWhiteSpace(n.Topic), !string.IsNullOrWhiteSpace(n.Homework),
                [.. lessonGrades.Select(g => g.Grade)]));
        }

        // Marks with no lesson row (a mark put on a lesson nobody planned or conducted) still count —
        // per teacher through the template, per class always.
        var looseByTeacher = grades.Where(g => !usedGrades.Contains(g))
            .Select(g =>
            {
                var planned = templateTeachers.GetValueOrDefault((g.ClassId, g.SubjectId, g.SubGroup))
                    ?? templateTeachers.GetValueOrDefault((g.ClassId, g.SubjectId, 0));
                return (Teacher: TeacherOf(g.ClassId, g.SubjectId, g.Period, g.SubGroup, planned), g.Grade);
            }).ToList();

        // ---- Gamification entries of the day ----
        var points = await db.DisciplinePoints.AsNoTracking()
            .Where(p => p.CreatedAt.StartsWith(iso) && p.Points != 0).ToListAsync(ct);
        var pupilIds = points.Select(p => p.StudentId).Distinct().ToList();
        var pupils = await db.Students.AsNoTracking().Where(s => pupilIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, ct);
        var tracks = await TrackMembership.ByStudentAsync(db, pupilIds, day, ct);
        string PupilGroup(string id) => tracks.TryGetValue(id, out var t) ? t.Name
            : pupils.TryGetValue(id, out var p) ? p.ClassName : "—";

        var game = points
            .OrderBy(p => p.CreatedAt, StringComparer.Ordinal)
            .Select(p => new DailyGameEntry(
                p.CreatedAt.Length >= 16 ? p.CreatedAt.Substring(11, 5) : "",
                string.IsNullOrWhiteSpace(p.CreatedBy) ? "—" : p.CreatedBy,
                pupils.TryGetValue(p.StudentId, out var s) ? s.FullName : "—",
                PupilGroup(p.StudentId), p.ReasonName, p.Points))
            .ToList();

        // ---- Per teacher ----
        var rows = new Dictionary<string, TeacherAcc>(StringComparer.Ordinal);
        TeacherAcc Acc(string? id, string name)
        {
            var key = id ?? "name:" + name;
            if (!rows.TryGetValue(key, out var a)) rows[key] = a = new TeacherAcc(id, name);
            return a;
        }
        foreach (var l in lessonsOut)
        {
            var a = Acc(l.TeacherId, l.TeacherId is null ? l.TeacherName : teacherName.GetValueOrDefault(l.TeacherId, l.TeacherName));
            a.Lessons++;
            if (l.State == DailyLessonState.Conducted) a.Conducted++;
            else if (l.State == DailyLessonState.Marked) a.Marked++;
            else a.Missed++;
            if (l.HasTopic) a.WithTopic++;
            a.Grades.AddRange(l.Grades);
        }
        foreach (var (t, grade) in looseByTeacher)
            Acc(t.Id, t.Id is null ? t.Name : teacherName.GetValueOrDefault(t.Id, t.Name)).Grades.Add(grade);
        foreach (var p in points)
        {
            var tid = p.CreatedByUserId is { } uid ? teacherOfUser.GetValueOrDefault(uid) : null;
            var a = Acc(tid, tid is null ? (string.IsNullOrWhiteSpace(p.CreatedBy) ? "—" : p.CreatedBy) : teacherName[tid]);
            a.GameEntries++;
            if (p.Points > 0) a.GamePlus += p.Points; else a.GameMinus -= p.Points;
        }
        var teacherRows = rows.Values
            .Select(a => new DailyTeacherRow(
                a.Id, a.Name, a.Lessons, a.Conducted, a.Marked, a.Missed, a.WithTopic, a.Grades.Count,
                a.Grades.Count == 0 ? null : Math.Round(a.Grades.Average(), 2), a.GamePlus, a.GameMinus, a.GameEntries))
            // Weakest first: missed lessons, then nothing done, then by name.
            .OrderByDescending(r => r.Missed).ThenByDescending(r => r.Idle).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        // ---- Per class / track group ----
        var classAcc = new Dictionary<string, int[]>(StringComparer.Ordinal); // 5,4,3,2,plus,minus
        int[] C(string name) => classAcc.TryGetValue(name, out var v) ? v : classAcc[name] = new int[6];
        // Blok bahosi o'quvchining MANBA sinfiga yoziladi (TZ 6.5: baholar sinf bo'yicha jamlanadi).
        foreach (var g in grades)
        {
            var v = C(OwnerName(g.SourceClassId ?? g.ClassId));
            switch (g.Grade) { case >= 5: v[0]++; break; case 4: v[1]++; break; case 3: v[2]++; break; default: v[3]++; break; }
        }
        foreach (var e in game)
        {
            var v = C(e.ClassName);
            if (e.Points > 0) v[4] += e.Points; else v[5] -= e.Points;
        }
        var classRows = classAcc
            .Select(kv => new DailyClassRow(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]))
            .OrderBy(r => ClassOrder(r.Name)).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        // ---- Blok guruhlari: ustoz × guruh ----
        var blockGroups = await db.BlockGroups.AsNoTracking()
            .Join(db.Blocks, g => g.BlockId, b => b.Id, (g, b) => new { g.Id, g.StudyGroupId, Group = g.Name, Block = b.Name })
            .ToListAsync(ct);
        var blockRows = new List<DailyBlockGroupRow>();
        if (blockGroups.Count > 0)
        {
            var bySg = blockGroups.ToDictionary(g => g.StudyGroupId.ToString(), StringComparer.Ordinal);
            var byId = blockGroups.ToDictionary(g => g.Id);
            var acc = new Dictionary<(string Teacher, Guid Group), BlockAcc>();
            BlockAcc Get((string, Guid) key) => acc.TryGetValue(key, out var v) ? v : acc[key] = new BlockAcc();
            foreach (var l in lessonsOut.Where(l => bySg.ContainsKey(l.OwnerId)))
            {
                var key = (l.TeacherId is null ? l.TeacherName : teacherName.GetValueOrDefault(l.TeacherId, l.TeacherName), bySg[l.OwnerId].Id);
                var a = Get(key);
                a.Grades.AddRange(l.Grades);
                a.Lessons++;
            }
            foreach (var p in points.Where(p => p.BlockGroupId is { } bg && byId.ContainsKey(bg)))
            {
                var tid = p.CreatedByUserId is { } uid ? teacherOfUser.GetValueOrDefault(uid) : null;
                var key = (tid is null ? (string.IsNullOrWhiteSpace(p.CreatedBy) ? "—" : p.CreatedBy) : teacherName[tid], p.BlockGroupId!.Value);
                var a = Get(key);
                if (p.Points > 0) a.Plus += p.Points; else a.Minus -= p.Points;
            }
            blockRows = [.. acc.Select(kv => new DailyBlockGroupRow(
                    kv.Key.Teacher, byId[kv.Key.Group].Block, byId[kv.Key.Group].Group, kv.Value.Lessons, kv.Value.Grades.Count,
                    kv.Value.Grades.Count == 0 ? null : Math.Round(kv.Value.Grades.Average(), 2), kv.Value.Plus, kv.Value.Minus))
                .OrderBy(r => r.Teacher, StringComparer.Ordinal).ThenBy(r => r.Block, StringComparer.Ordinal).ThenBy(r => r.Group, StringComparer.Ordinal)];
        }

        var school = await db.SchoolMeta.AsNoTracking().Select(m => m.Name).FirstOrDefaultAsync(ct);
        return new DailyDirectorReportModel(
            string.IsNullOrWhiteSpace(school) ? "Maktab" : school.Trim(), day, AppClock.Now,
            [.. lessonsOut.OrderBy(l => l.Period).ThenBy(l => ClassOrder(l.OwnerName)).ThenBy(l => l.OwnerName, StringComparer.Ordinal)],
            teacherRows, classRows, game, blockRows);
    }

    /// <summary>Blok guruhi qatorining yig'indisi (ustoz × guruh).</summary>
    private sealed class BlockAcc
    {
        public int Lessons;
        public List<int> Grades = [];
        public int Plus;
        public int Minus;
    }

    /// <summary>(owner, subject, half) → the teacher, from every template; ambiguous keys are left out.</summary>
    private static async Task<Dictionary<(string, string, int), string>> TemplateTeachersAsync(IAppDbContext db, CancellationToken ct)
    {
        var rows = await db.ScheduleTemplates.AsNoTracking()
            .SelectMany(t => t.Lessons.Where(l => l.TeacherId != "").Select(l => new { t.ClassId, l.SubjectId, l.SubGroup, l.TeacherId }))
            .Distinct().ToListAsync(ct);
        return rows.GroupBy(r => (r.ClassId, r.SubjectId, r.SubGroup))
            .Where(g => g.Select(r => r.TeacherId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().TeacherId);
    }

    /// <summary>"5-A" before "10-B": the leading grade number, then the name; a track group after classes.</summary>
    private static int ClassOrder(string name)
    {
        var digits = new string(name.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 100;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private sealed class TeacherAcc(string? id, string name)
    {
        public string? Id { get; } = id;
        public string Name { get; } = name;
        public int Lessons, Conducted, Marked, Missed, WithTopic, GamePlus, GameMinus, GameEntries;
        public List<int> Grades { get; } = [];
    }
}
