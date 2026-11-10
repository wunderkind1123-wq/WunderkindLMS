using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  ORALIQ TEST (client, 2026-11-10)
// ===========================================================================
//
//  Track groups get no journal marks. Each week the teacher gives an interim test in their subject;
//  three in a month, and the fourth is the monthly block test (Imtihonlar). The teacher creates the
//  test with its question count and types every pupil's correct answers; the result is a percentage
//  (correct / questions × 100), so a 10-question and a 30-question test weigh the same.
//
//  WHO
//  ---
//  A teacher works on the (track group, subject) pairs they teach in the timetable — the same rule as
//  the block test entry (TeacherOwnerAccess.PairsAsync). Any teacher of the pair may edit its tests.
//  Staff with the "exams" permission see and edit all of them (scope = null).
//
//  ROSTER
//  ------
//  The group's members on the test day (joined on or before it, not yet left), plus anybody who
//  already has a result — a pupil who moved to another track later keeps the result they earned.
// ===========================================================================

public sealed record InterimSubjectDto(string Id, string Name);

public sealed record InterimOptionDto(Guid GroupId, string GroupName, List<InterimSubjectDto> Subjects);

public sealed record InterimTestRowDto(
    Guid Id, Guid GroupId, string GroupName, string SubjectId, string SubjectName, string Date,
    int Number, int QuestionCount, string? Topic, int PupilCount, int DoneCount, double? AveragePercent,
    string CreatedBy);

public sealed record InterimEntryRowDto(
    string StudentId, string FullName, string ClassName, int? Correct, bool Absent, double? Percent, bool Member);

public sealed record InterimEntryDto(InterimTestRowDto Test, List<InterimEntryRowDto> Rows);

public sealed record InterimTestRequest(Guid? GroupId, string? SubjectId, string? Date, int? QuestionCount, string? Topic);

public sealed record InterimResultCell(string? StudentId, int? Correct, bool Absent);

public sealed record InterimResultsRequest(List<InterimResultCell>? Rows);

public sealed record InterimError(int Status, string Message);

/// <summary>A value or the error to answer with.</summary>
public sealed record InterimOutcome<T>(T? Value, InterimError? Error)
{
    public static implicit operator InterimOutcome<T>(T value) => new(value, null);
    public static implicit operator InterimOutcome<T>(InterimError error) => new(default, error);
}

public static class InterimTestService
{
    public const int MaxQuestions = 300;
    public const string NotYoursMessage = "Bu yo'nalish yoki fan sizga biriktirilmagan";
    public const string NotFoundMessage = "Oraliq test topilmadi";

    /// <summary>The (group, subject) pairs a teacher may work on: their timetable lessons in active track groups.</summary>
    public static async Task<HashSet<(Guid GroupId, string SubjectId)>> TeacherScopeAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var pairs = await TeacherOwnerAccess.PairsAsync(db, teacherId, ct);
        var ids = pairs.Where(p => p.OwnerKind == LessonOwnerKind.Group)
            .Select(p => Guid.TryParse(p.OwnerId, out var g) ? g : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var tracks = (await db.StudyGroups.AsNoTracking()
                .Where(g => ids.Contains(g.Id) && g.IsTrack && !g.IsArchived)
                .Select(g => g.Id)
                .ToListAsync(ct))
            .ToHashSet();
        return pairs.Where(p => p.OwnerKind == LessonOwnerKind.Group)
            .Select(p => Guid.TryParse(p.OwnerId, out var g) ? (g, p.SubjectId) : (Guid.Empty, p.SubjectId))
            .Where(p => tracks.Contains(p.Item1))
            .ToHashSet();
    }

    /// <summary>Track groups and their subjects to create a test in. Staff: every subject of the group's timetable.</summary>
    public static async Task<List<InterimOptionDto>> OptionsAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, CancellationToken ct = default)
    {
        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => g.IsTrack && !g.IsArchived)
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(ct);

        Dictionary<Guid, HashSet<string>> subjectsOf;
        if (scope is not null)
        {
            subjectsOf = scope.GroupBy(p => p.GroupId).ToDictionary(g => g.Key, g => g.Select(p => p.SubjectId).ToHashSet());
        }
        else
        {
            var ids = groups.Select(g => g.Id.ToString()).ToList();
            var lessons = await db.ScheduleTemplates.AsNoTracking()
                .Where(t => t.OwnerKind == LessonOwnerKind.Group && !t.IsArchived && ids.Contains(t.ClassId))
                .SelectMany(t => t.Lessons.Select(l => new { t.ClassId, l.SubjectId }))
                .Distinct()
                .ToListAsync(ct);
            subjectsOf = lessons.GroupBy(l => Guid.Parse(l.ClassId))
                .ToDictionary(g => g.Key, g => g.Select(l => l.SubjectId).ToHashSet());
        }

        var names = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return groups
            .Where(g => subjectsOf.ContainsKey(g.Id))
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .Select(g => new InterimOptionDto(g.Id, g.Name, subjectsOf[g.Id]
                .Where(names.ContainsKey)
                .Select(id => new InterimSubjectDto(id, names[id]))
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ToList()))
            .ToList();
    }

    /// <summary>Tests, newest first. <paramref name="month"/> is "yyyy-MM".</summary>
    public static async Task<List<InterimTestRowDto>> ListAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope,
        Guid? groupId, string? subjectId, string? month, CancellationToken ct = default)
    {
        var q = db.InterimTests.AsNoTracking();
        if (groupId is { } g) q = q.Where(t => t.GroupId == g);
        if (!string.IsNullOrWhiteSpace(subjectId)) q = q.Where(t => t.SubjectId == subjectId);
        if (TryMonth(month, out var from, out var to)) q = q.Where(t => t.Date >= from && t.Date <= to);
        if (scope is not null)
        {
            var groupIds = scope.Select(p => p.GroupId).Distinct().ToList();
            q = q.Where(t => groupIds.Contains(t.GroupId));
        }
        var tests = (await q.OrderByDescending(t => t.Date).ThenBy(t => t.CreatedAt).Take(500).ToListAsync(ct))
            .Where(t => scope is null || scope.Contains((t.GroupId, t.SubjectId)))
            .ToList();
        return await RowsAsync(db, tests, ct);
    }

    public static async Task<InterimOutcome<InterimTestRowDto>> CreateAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, InterimTestRequest req,
        string? userId, string userName, CancellationToken ct = default)
    {
        if (req.GroupId is not { } groupId) return Bad("Yo'nalishni tanlang");
        var subjectId = req.SubjectId?.Trim();
        if (string.IsNullOrEmpty(subjectId)) return Bad("Fanni tanlang");
        if (scope is not null && !scope.Contains((groupId, subjectId))) return new InterimError(403, NotYoursMessage);
        if (!await db.StudyGroups.AnyAsync(g => g.Id == groupId && g.IsTrack && !g.IsArchived, ct))
            return Bad("Yo'nalish topilmadi");
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) return Bad("Fan topilmadi");
        if (CheckFields(req) is { } error) return error;
        var date = DateOnly.ParseExact(req.Date!, "yyyy-MM-dd");
        if (await db.InterimTests.AnyAsync(t => t.GroupId == groupId && t.SubjectId == subjectId && t.Date == date, ct))
            return new InterimError(409, "Bu kunda shu fandan oraliq test allaqachon bor");

        var test = new InterimTest
        {
            GroupId = groupId, SubjectId = subjectId, Date = date, QuestionCount = req.QuestionCount!.Value,
            Topic = Topic(req.Topic), CreatedByUserId = userId, CreatedBy = userName, CreatedAt = AppClock.NowInstant,
        };
        db.InterimTests.Add(test);
        await db.SaveChangesAsync(ct);
        return (await RowsAsync(db, [test], ct))[0];
    }

    /// <summary>Changes the date, question count or topic. The count may not drop below a correct count already typed.</summary>
    public static async Task<InterimOutcome<InterimTestRowDto>> UpdateAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, Guid id, InterimTestRequest req,
        CancellationToken ct = default)
    {
        var test = await db.InterimTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null || (scope is not null && !scope.Contains((test.GroupId, test.SubjectId))))
            return new InterimError(404, NotFoundMessage);
        if (CheckFields(req) is { } error) return error;
        var date = DateOnly.ParseExact(req.Date!, "yyyy-MM-dd");
        if (date != test.Date && await db.InterimTests.AnyAsync(
                t => t.Id != id && t.GroupId == test.GroupId && t.SubjectId == test.SubjectId && t.Date == date, ct))
            return new InterimError(409, "Bu kunda shu fandan oraliq test allaqachon bor");
        var best = await db.InterimTestResults.Where(r => r.TestId == id && r.Correct != null).MaxAsync(r => r.Correct, ct);
        if (best is { } top && top > req.QuestionCount)
            return Bad($"Bir o'quvchida {top} ta to'g'ri javob kiritilgan — savollar soni undan kam bo'lmasin");

        test.Date = date;
        test.QuestionCount = req.QuestionCount!.Value;
        test.Topic = Topic(req.Topic);
        await db.SaveChangesAsync(ct);
        return (await RowsAsync(db, [test], ct))[0];
    }

    public static async Task<InterimOutcome<InterimTest>> DeleteAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, Guid id, CancellationToken ct = default)
    {
        var test = await db.InterimTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null || (scope is not null && !scope.Contains((test.GroupId, test.SubjectId))))
            return new InterimError(404, NotFoundMessage);
        db.InterimTests.Remove(test);
        await db.SaveChangesAsync(ct);
        return test;
    }

    /// <summary>The test and its roster with the results typed so far.</summary>
    public static async Task<InterimOutcome<InterimEntryDto>> EntryAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, Guid id, CancellationToken ct = default)
    {
        var test = await db.InterimTests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null || (scope is not null && !scope.Contains((test.GroupId, test.SubjectId))))
            return new InterimError(404, NotFoundMessage);

        var members = await MembersOnAsync(db, test.GroupId, test.Date, ct);
        var results = await db.InterimTestResults.AsNoTracking().Where(r => r.TestId == id).ToListAsync(ct);
        var ids = members.Concat(results.Select(r => r.StudentId)).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToListAsync(ct);
        var byStudent = results.ToDictionary(r => r.StudentId);
        var memberSet = members.ToHashSet();

        var rows = students
            .OrderBy(s => s.FullName, StringComparer.Ordinal)
            .Select(s =>
            {
                var r = byStudent.GetValueOrDefault(s.Id);
                return new InterimEntryRowDto(s.Id, s.FullName, s.ClassName, r?.Correct, r?.Absent ?? false,
                    r?.Correct is { } c ? Percent(c, test.QuestionCount) : null, memberSet.Contains(s.Id));
            })
            .ToList();
        return new InterimEntryDto((await RowsAsync(db, [test], ct))[0], rows);
    }

    /// <summary>
    /// Saves the rows sent: a correct count, "absent", or neither (clears the result). Nothing is written
    /// if any row is out of range or not on the roster.
    /// </summary>
    public static async Task<InterimOutcome<int>> SaveResultsAsync(
        IAppDbContext db, HashSet<(Guid GroupId, string SubjectId)>? scope, Guid id, InterimResultsRequest req,
        string userName, CancellationToken ct = default)
    {
        var test = await db.InterimTests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null || (scope is not null && !scope.Contains((test.GroupId, test.SubjectId))))
            return new InterimError(404, NotFoundMessage);

        var existing = await db.InterimTestResults.Where(r => r.TestId == id).ToListAsync(ct);
        var roster = (await MembersOnAsync(db, test.GroupId, test.Date, ct))
            .Concat(existing.Select(r => r.StudentId))
            .ToHashSet();
        var byStudent = existing.ToDictionary(r => r.StudentId);
        var rows = req.Rows ?? [];

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.StudentId) || !roster.Contains(row.StudentId))
                return new InterimError(403, "Ro'yxatda bu yo'nalishda bo'lmagan o'quvchi bor — sahifani yangilang");
            if (row.Correct is { } c && (c < 0 || c > test.QuestionCount))
                return Bad($"To'g'ri javoblar 0 dan {test.QuestionCount} gacha bo'ladi");
        }

        var saved = 0;
        foreach (var row in rows)
        {
            var current = byStudent.GetValueOrDefault(row.StudentId!);
            if (!row.Absent && row.Correct is null)
            {
                if (current is null) continue;
                db.InterimTestResults.Remove(current);
                saved++;
                continue;
            }
            if (current is null)
            {
                current = new InterimTestResult { TestId = id, StudentId = row.StudentId! };
                db.InterimTestResults.Add(current);
            }
            else if (current.Absent == row.Absent && current.Correct == (row.Absent ? null : row.Correct))
            {
                continue;
            }
            current.Absent = row.Absent;
            current.Correct = row.Absent ? null : row.Correct;
            current.UpdatedBy = userName;
            current.UpdatedAt = AppClock.NowInstant;
            saved++;
        }
        await db.SaveChangesAsync(ct);
        return saved;
    }

    /// <summary>
    /// Every pupil's interim-test percentages between two days, for readers such as the leaderboard.
    /// Absent pupils have no value.
    /// </summary>
    public static async Task<Dictionary<string, List<double>>> PercentsAsync(
        IAppDbContext db, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var rows = await (from r in db.InterimTestResults.AsNoTracking()
                          join t in db.InterimTests.AsNoTracking() on r.TestId equals t.Id
                          where t.Date >= start && t.Date <= end && !r.Absent && r.Correct != null
                          select new { r.StudentId, Correct = r.Correct!.Value, t.QuestionCount })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(r => Percent(r.Correct, r.QuestionCount)).ToList());
    }

    public static double Percent(int correct, int questions) =>
        questions <= 0 ? 0 : Math.Round(correct * 100.0 / questions, 1);

    // =====================================================================

    private static InterimError Bad(string message) => new(400, message);

    private static InterimError? CheckFields(InterimTestRequest req)
    {
        if (!DateOnly.TryParseExact(req.Date, "yyyy-MM-dd", out var date)) return Bad("Sanani tanlang");
        if (date > AppClock.Today) return Bad("Kelgusi kun uchun test kiritib bo'lmaydi");
        if (req.QuestionCount is not { } n || n < 1 || n > MaxQuestions)
            return Bad($"Savollar soni 1 dan {MaxQuestions} gacha bo'ladi");
        if (req.Topic?.Trim().Length > 200) return Bad("Mavzu 200 belgidan oshmasin");
        return null;
    }

    private static string? Topic(string? topic) => string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();

    private static bool TryMonth(string? month, out DateOnly from, out DateOnly to)
    {
        from = to = default;
        if (string.IsNullOrWhiteSpace(month) || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out from)) return false;
        to = from.AddMonths(1).AddDays(-1);
        return true;
    }

    /// <summary>Pupils in the group on the day: joined on or before it and not yet left.</summary>
    private static async Task<List<string>> MembersOnAsync(IAppDbContext db, Guid groupId, DateOnly day, CancellationToken ct) =>
        await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.JoinedOn <= day && (m.LeftOn == null || m.LeftOn > day))
            .Join(db.Students.Where(s => !s.IsArchived), m => m.StudentId, s => s.Id, (m, s) => s.Id)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// Row view of tests: names, the test's number in its month for its group and subject ("2-oraliq test"),
    /// how many pupils were entered and the average percentage.
    /// </summary>
    private static async Task<List<InterimTestRowDto>> RowsAsync(IAppDbContext db, List<InterimTest> tests, CancellationToken ct)
    {
        if (tests.Count == 0) return [];
        var groupIds = tests.Select(t => t.GroupId).Distinct().ToList();
        var subjectIds = tests.Select(t => t.SubjectId).Distinct().ToList();
        var testIds = tests.Select(t => t.Id).ToList();
        var minDay = tests.Min(t => t.Date);
        var monthStart = new DateOnly(minDay.Year, minDay.Month, 1);
        var maxDay = tests.Max(t => t.Date);

        var groups = await db.StudyGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var siblings = await db.InterimTests.AsNoTracking()
            .Where(t => groupIds.Contains(t.GroupId) && subjectIds.Contains(t.SubjectId) && t.Date >= monthStart && t.Date <= maxDay)
            .Select(t => new { t.Id, t.GroupId, t.SubjectId, t.Date })
            .ToListAsync(ct);
        var results = (await db.InterimTestResults.AsNoTracking()
                .Where(r => testIds.Contains(r.TestId))
                .Select(r => new { r.TestId, r.Correct, r.Absent })
                .ToListAsync(ct))
            .ToLookup(r => r.TestId);
        var members = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId))
            .Join(db.Students.Where(s => !s.IsArchived), m => m.StudentId, s => s.Id, (m, s) => new { m.GroupId, m.JoinedOn, m.LeftOn })
            .ToListAsync(ct);

        return tests.Select(t =>
        {
            var number = siblings.Count(s => s.GroupId == t.GroupId && s.SubjectId == t.SubjectId
                && s.Date.Year == t.Date.Year && s.Date.Month == t.Date.Month && s.Date <= t.Date);
            var pupils = members.Count(m => m.GroupId == t.GroupId && m.JoinedOn <= t.Date && (m.LeftOn == null || m.LeftOn > t.Date));
            var typed = results[t.Id].ToList();
            var scores = typed.Where(r => r.Correct != null).Select(r => Percent(r.Correct!.Value, t.QuestionCount)).ToList();
            return new InterimTestRowDto(
                t.Id, t.GroupId, groups.GetValueOrDefault(t.GroupId, ""), t.SubjectId, subjects.GetValueOrDefault(t.SubjectId, t.SubjectId),
                t.Date.ToString("yyyy-MM-dd"), Math.Max(number, 1), t.QuestionCount, t.Topic,
                Math.Max(pupils, typed.Count), typed.Count, scores.Count > 0 ? Math.Round(scores.Average(), 1) : null, t.CreatedBy);
        }).ToList();
    }
}
