using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'quvchilar leaderboardi (client, 2026-11-08): "o'quvchining gamifikatsiya ballari, sinfdagi
//  baholari, davomati, oylik test natijalari hisobga olinadi ... har bir sinf va yo'nalish bo'yicha
//  alohida ham umumiy ko'rinishda ... butun o'quv yili uchun amal qiladi."
//
//  THE SCORE (0–100, then the hand-added points on top)
//    Baholar        40%  journal marks of the year, average ÷ 5. A TRACK pupil (yo'nalish) gets no journal
//                        marks: their part is the average of their interim tests instead, in percent
//                        (oraliq test, InterimTestService — client, 2026-11-10)
//    Davomat        20%  attended share of the held lessons (HeldLessons — the rule every attendance
//                        figure uses; a late reason is not an absence)
//    Oylik test     25%  monthly marks of Mavsumiy baholash (0–100) and the block tests of Imtihonlar (the
//                        track groups' monthly test, percent), average
//    Gamifikatsiya  15%  the behaviour score (DisciplineIncidents: 100 + rewards − penalties), 150 = full
//  A part a pupil has no data for (no monthly test above grade 8, no lesson held yet) is left out and
//  the others share its weight — nobody loses points for what the school never measured.
//  + Qo'shimcha ballar: LeaderboardAward rows of the year, added as they are.
//
//  THE YEAR: from the first quarter's start to today (never past the last quarter's end).
//  WHOSE ROWS: ClassAttainment, as every report — a mark counts for the pupil inside the membership
//  window of the class or group it was given in. READ-ONLY.
// ===========================================================================

/// <summary>One pupil's leaderboard line (before ranking).</summary>
/// <param name="Grades">Average journal mark (2–5); null — none this year.</param>
/// <param name="IsTrack">In a track group: the marks part is <paramref name="Interim"/>, not the journal.</param>
/// <param name="Interim">Average interim test result, 0–100; null — none.</param>
/// <param name="Attendance">Attended share of held lessons, 0–100; null — no lesson held.</param>
/// <param name="MonthlyTest">Average monthly test mark, 0–100; null — none.</param>
/// <param name="Base">The weighted score, 0–100.</param>
/// <param name="Total">Base + hand-added points.</param>
public sealed record LeaderboardRow(
    string StudentId, string FullName, string ClassId, string ClassName, int Grade,
    List<string> TrackIds,
    double? Grades, int GradeCount,
    bool IsTrack, double? Interim, int InterimCount,
    double? Attendance, int Lessons, int Absent,
    double? MonthlyTest, int MonthlyCount,
    int Behaviour,
    int ManualPoints,
    double Base, double Total);

/// <summary>A class or a track group the leaderboard can be narrowed to.</summary>
public sealed record LeaderboardScope(string Id, string Name);

/// <summary>The whole school's leaderboard for one academic year.</summary>
public sealed record LeaderboardResult(
    DateOnly From, DateOnly To, List<LeaderboardRow> Rows, List<LeaderboardScope> Classes, List<LeaderboardScope> Tracks);

/// <summary>Builds the leaderboard. See the file header.</summary>
public static class LeaderboardService
{
    public const double GradesWeight = 40, AttendanceWeight = 20, MonthlyWeight = 25, BehaviourWeight = 15;

    /// <summary>The behaviour score that counts as full marks (100 is where every pupil starts).</summary>
    public const int BehaviourFull = 150;

    /// <summary>The academic year around <paramref name="today"/>: first quarter's start → today (≤ last quarter's end).</summary>
    public static async Task<(DateOnly From, DateOnly To)> YearAsync(IAppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var quarters = await db.Quarters.AsNoTracking().ToListAsync(ct);
        var starts = quarters.Select(q => ParseDay(q.StartDate)).Where(d => d is not null).Select(d => d!.Value).ToList();
        var ends = quarters.Select(q => ParseDay(q.EndDate)).Where(d => d is not null).Select(d => d!.Value).ToList();
        var from = starts.Count > 0 ? starts.Min() : new DateOnly(today.Month >= 8 ? today.Year : today.Year - 1, 9, 1);
        var to = ends.Count > 0 && ends.Max() < today ? ends.Max() : today;
        return (from, to < from ? from : to);
    }

    /// <summary>
    /// Weighted 0–100 score of the parts present (see the file header). <paramref name="marks"/> is already
    /// in percent: the journal average ÷ 5, or a track pupil's interim test average.
    /// </summary>
    public static double BaseScore(double? marks, double? attendance, double? monthly, int behaviour)
    {
        var parts = new List<(double Value, double Weight)>();
        if (marks is { } g) parts.Add((Math.Clamp(g, 0, 100), GradesWeight));
        if (attendance is { } a) parts.Add((Math.Clamp(a, 0, 100), AttendanceWeight));
        if (monthly is { } m) parts.Add((Math.Clamp(m, 0, 100), MonthlyWeight));
        parts.Add((Math.Clamp(behaviour * 100.0 / BehaviourFull, 0, 100), BehaviourWeight));
        var weight = parts.Sum(p => p.Weight);
        return Math.Round(parts.Sum(p => p.Value * p.Weight) / weight, 1);
    }

    public static async Task<LeaderboardResult> BuildAsync(IAppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var (from, to) = await YearAsync(db, today, ct);
        var (f, t) = (Iso(from), Iso(to));

        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var classByName = classes.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.SubGroup }).ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();

        var tracks = await db.StudyGroups.AsNoTracking().Where(g => g.IsTrack && !g.IsArchived)
            .OrderBy(g => g.Name).Select(g => new { g.Id, g.Name }).ToListAsync(ct);
        var trackIds = tracks.Select(x => x.Id).ToList();
        var trackOf = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => trackIds.Contains(m.GroupId) && m.LeftOn == null)
                .Select(m => new { m.StudentId, m.GroupId }).ToListAsync(ct))
            .GroupBy(m => m.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.GroupId.ToString()).Distinct().ToList(), StringComparer.Ordinal);

        var attainment = await ClassAttainment.BuildAsync(db, ct);

        // Journal rows of the year: marks and absences.
        var entries = (await db.JournalEntries.AsNoTracking()
                .Where(e => string.Compare(e.Date, f) >= 0 && string.Compare(e.Date, t) <= 0
                    && (e.Grade != null || e.ReasonId != null))
                .Select(e => new { e.StudentId, e.ClassId, e.OwnerKind, e.Date, e.Grade, e.ReasonId })
                .ToListAsync(ct))
            .Where(e => attainment.CountsFor(e.StudentId, e.ClassId, e.OwnerKind, e.Date))
            .GroupBy(e => e.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var lateIds = (await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // Held lessons of the year, by owner (class or group).
        var heldByOwner = (await HeldLessons.ListAsync(db, null, f, t, ct))
            .DistinctBy(h => (h.ClassId, h.SubjectId, h.Date, h.Period, h.SubGroup))
            .GroupBy(h => h.ClassId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Monthly tests (Mavsumiy baholash) whose month falls in the year.
        // + the block tests of Imtihonlar (the track groups' monthly test), in percent.
        var (fromKey, toKey) = (from.Year * 100 + from.Month, to.Year * 100 + to.Month);
        var seasonal = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.PeriodKind == SeasonalPeriodKind.Monthly && m.Score != null && m.Month != null
                && m.Year * 100 + m.Month >= fromKey && m.Year * 100 + m.Month <= toKey)
            .Select(m => new { m.StudentId, Score = (double)m.Score!.Value })
            .ToListAsync(ct);
        var blockTests = await (from p in db.ExamParticipants.AsNoTracking()
                                join e in db.Exams.AsNoTracking() on p.ExamId equals e.Id
                                where e.Kind == ExamKind.Block && (e.Status == ExamStatus.Published || e.Status == ExamStatus.Closed)
                                    && p.StudentId != null && p.Status == ExamParticipantStatus.Finished && p.Percent != null
                                    && e.ExamDate != null && string.Compare(e.ExamDate, f) >= 0 && string.Compare(e.ExamDate, t) <= 0
                                select new { StudentId = p.StudentId!, Score = (double)p.Percent!.Value })
            .ToListAsync(ct);
        var monthly = seasonal.Concat(blockTests)
            .GroupBy(m => m.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Avg: g.Average(m => m.Score), Count: g.Count()), StringComparer.Ordinal);
        var interim = await InterimTestService.PercentsAsync(db, from, to, ct);

        var behaviour = await DisciplineIncidents.TotalsAsync(db, ids, ct);
        var manual = (await db.LeaderboardAwards.AsNoTracking()
                .Where(a => a.Date >= from && a.Date <= to)
                .GroupBy(a => a.StudentId)
                .Select(g => new { StudentId = g.Key, Points = g.Sum(a => a.Points) })
                .ToListAsync(ct))
            .ToDictionary(a => a.StudentId, a => a.Points, StringComparer.Ordinal);

        var rows = new List<LeaderboardRow>(students.Count);
        foreach (var s in students)
        {
            var cls = classByName.GetValueOrDefault(s.ClassName ?? "");
            var own = entries.GetValueOrDefault(s.Id) ?? [];
            var marks = own.Where(e => e.Grade != null).Select(e => e.Grade!.Value).ToList();
            var absent = own.Count(e => e.ReasonId != null && !lateIds.Contains(e.ReasonId));

            // The lessons the pupil sat in: their class (their half of a split lesson) and their groups.
            var owners = new List<(string Id, bool Group)>();
            if (cls is not null) owners.Add((cls.Id, false));
            owners.AddRange(attainment.OwnerIdsForStudent(s.Id, cls?.Id)
                .Where(id => cls is null || id != cls.Id)
                .Select(id => (id, attainment.GroupsOf(s.Id).Any(g => g.Id == id))));
            var lessons = 0;
            foreach (var (ownerId, isGroup) in owners)
                if (heldByOwner.TryGetValue(ownerId, out var held))
                    lessons += held.Count(h => attainment.CountsFor(s.Id, ownerId,
                            isGroup ? LessonOwnerKind.Group : LessonOwnerKind.Class, h.Date)
                        && (isGroup || h.SubGroup == 0 || s.SubGroup == 0 || h.SubGroup == s.SubGroup));
            lessons = Math.Max(lessons, absent);

            double? grades = marks.Count == 0 ? null : Math.Round(marks.Average(), 2);
            var isTrack = trackOf.ContainsKey(s.Id);
            var tests = interim.GetValueOrDefault(s.Id) ?? [];
            double? interimAvg = tests.Count == 0 ? null : Math.Round(tests.Average(), 1);
            // A track pupil's marks part is the interim tests; their journal marks do not count.
            var marksPart = isTrack ? interimAvg : grades / 5.0 * 100;
            double? attendance = lessons == 0 ? null : Math.Round(100.0 * (lessons - absent) / lessons, 1);
            var test = monthly.TryGetValue(s.Id, out var mt) ? mt : default;
            double? monthlyAvg = test.Count == 0 ? null : Math.Round(test.Avg, 1);
            var (plus, minus) = behaviour.GetValueOrDefault(s.Id);
            var score = DisciplineIncidents.BaseScore + plus - minus;
            var extra = manual.GetValueOrDefault(s.Id);
            var baseScore = BaseScore(marksPart, attendance, monthlyAvg, score);

            rows.Add(new LeaderboardRow(
                s.Id, s.FullName, cls?.Id ?? "", s.ClassName ?? "", cls?.Grade ?? 0,
                trackOf.GetValueOrDefault(s.Id) ?? [],
                grades, marks.Count, isTrack, interimAvg, tests.Count, attendance, lessons, absent, monthlyAvg, test.Count, score, extra,
                baseScore, Math.Round(baseScore + extra, 1)));
        }

        return new LeaderboardResult(from, to,
            [.. rows.OrderByDescending(r => r.Total).ThenBy(r => r.FullName, StringComparer.Ordinal)],
            [.. classes.Select(c => new LeaderboardScope(c.Id, c.Name))],
            [.. tracks.Select(x => new LeaderboardScope(x.Id.ToString(), x.Name))]);
    }

    /// <summary>Competition ranking of rows already in order: equal totals share a place (1, 2, 2, 4).</summary>
    public static List<(int Rank, LeaderboardRow Row)> Rank(IEnumerable<LeaderboardRow> rows)
    {
        var ranked = new List<(int, LeaderboardRow)>();
        int place = 0, index = 0;
        double? previous = null;
        foreach (var r in rows)
        {
            index++;
            if (previous != r.Total) place = index;
            previous = r.Total;
            ranked.Add((place, r));
        }
        return ranked;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
