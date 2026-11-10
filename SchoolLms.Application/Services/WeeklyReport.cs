using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Haftalik o'quvchi hisoboti — the weekly report every parent gets as a PDF on Sunday morning, from
//  the school's study office, through the bot (client, 2026-11-08; the sample was
//  "Madinaxon_Abdunumonova_haftalik_hisobot_17-23_oktabr.pdf"). The client asked for it simpler than
//  the sample: what a parent understands at a glance, but showing the week's learning exactly.
//
//  WHAT IT SHOWS (one pupil, Monday–Saturday of one week)
//    · every mark of the week next to the class average of THAT lesson (same owner, subject, day,
//      period and half) — so a "2" on a lesson the whole class failed reads differently from a "2"
//      the pupil got alone;
//    · attendance per day from the held lessons (HeldLessons.ListAsync — the rule every attendance
//      figure uses) and the pupil's journal reasons; a late reason is "kech qoldi", not an absence;
//    · per subject: the week's marks, their average, the class average on the same lessons and the
//      quarter average the parent sees in the cabinet (StudentReportBuilder);
//    · the behaviour score and what moved it this week (DisciplineIncidents.HistoryAsync);
//    · last week's average, and a short summary — strong sides and what needs attention — written
//      from these numbers by fixed rules (no free text, nothing a teacher has to type).
//
//  WHOSE ROWS: ClassAttainment decides, as for every other report — the pupil's class, the classes they
//  left and their study groups, each only inside the membership window.
//
//  READ-ONLY: nothing here writes. The PDF itself is WeeklyReportDocument.
// ===========================================================================

/// <summary>One mark of the week and the class average on the same lesson.</summary>
public sealed record WeeklyMark(DateOnly Date, int Period, string Subject, int Grade, double? ClassAverage);

/// <summary>One school day of the week: held lessons, how many the pupil missed or came late to, and the marks.</summary>
public sealed record WeeklyDay(DateOnly Date, int Lessons, int Absent, int Late, List<WeeklyMark> Marks);

/// <summary>One subject's week.</summary>
/// <param name="QuarterAverage">The quarter average the parent sees (official quarter mark when set).</param>
public sealed record WeeklySubject(
    string Name, List<int> Grades, double? Average, double? ClassAverage, double? QuarterAverage);

/// <summary>A behaviour entry of the week.</summary>
public sealed record WeeklyBehaviour(DateOnly Date, string Reason, int Points);

/// <summary>One growth recommendation: what to work on, why (the week's numbers), what to do at home, the goal.</summary>
public sealed record WeeklyRecommendation(string Title, string Why, List<string> Steps, string Goal);

/// <summary>A goal for the next week ("≥ 4,5" — "haftalik o'rtacha baho").</summary>
public sealed record WeeklyGoal(string Value, string Label);

/// <summary>Everything the weekly PDF prints — nothing else.</summary>
public sealed record WeeklyReportModel(
    string SchoolName,
    string StudentName,
    string FirstName,
    string ClassName,
    string Homeroom,
    string ParentName,
    DateOnly From,
    DateOnly To,
    int Quarter,
    List<WeeklyDay> Days,
    List<WeeklySubject> Subjects,
    double? PreviousAverage,
    int PreviousCount,
    double? QuarterAverage,
    int DisciplineScore,
    List<WeeklyBehaviour> Behaviour,
    List<string> Strengths,
    List<string> Attention)
{
    public List<WeeklyMark> Marks => [.. Days.SelectMany(d => d.Marks)];

    public double? Average => Marks.Count == 0 ? null : Math.Round(Marks.Average(m => m.Grade), 2);

    /// <summary>The class average on the same lessons the pupil was marked in.</summary>
    public double? ClassAverage
    {
        get
        {
            var withClass = Marks.Where(m => m.ClassAverage is not null).ToList();
            return withClass.Count == 0 ? null : Math.Round(withClass.Average(m => m.ClassAverage!.Value), 2);
        }
    }

    public int Lessons => Days.Sum(d => d.Lessons);
    public int Absent => Days.Sum(d => d.Absent);
    public int Late => Days.Sum(d => d.Late);

    /// <summary>Attended share of held lessons, 0–100; null — no lesson was held.</summary>
    public int? AttendancePercent => Lessons == 0 ? null : (int)Math.Round(100.0 * (Lessons - Absent) / Lessons);

    public int BehaviourDelta => Behaviour.Sum(b => b.Points);

    /// <summary>The week in one paragraph, with a verdict ("Yaxshi hafta").</summary>
    public string Overview { get; init; } = "";

    /// <summary>Growth recommendations, most important first (2026-11-08, client: "o'sish uchun tavsiyalar").</summary>
    public List<WeeklyRecommendation> Recommendations { get; init; } = [];

    /// <summary>Next week's goals.</summary>
    public List<WeeklyGoal> Goals { get; init; } = [];
}

/// <summary>Builds <see cref="WeeklyReportModel"/>. See the file header.</summary>
public static class WeeklyReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] MonthsGenitive =
    [
        "yanvar", "fevral", "mart", "aprel", "may", "iyun",
        "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr",
    ];

    public static readonly string[] WeekDayNames =
        ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba", "Yakshanba"];

    /// <summary>
    /// The school week a report for <paramref name="day"/> covers: on Sunday — the Monday–Saturday just
    /// ended; on any other day — this week's Monday up to that day (a sample made mid-week).
    /// </summary>
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly day)
    {
        if (day.DayOfWeek == DayOfWeek.Sunday) return (day.AddDays(-6), day.AddDays(-1));
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return (monday, day);
    }

    /// <summary>"02–07 noyabr 2026", or "28 oktabr – 02 noyabr 2026" across a month.</summary>
    public static string Label(DateOnly from, DateOnly to)
    {
        if (from == to) return $"{to.Day:00} {MonthsGenitive[to.Month - 1]} {to.Year}";
        return from.Month == to.Month && from.Year == to.Year
            ? $"{from.Day:00}–{to.Day:00} {MonthsGenitive[to.Month - 1]} {to.Year}"
            : $"{from.Day:00} {MonthsGenitive[from.Month - 1]} – {to.Day:00} {MonthsGenitive[to.Month - 1]} {to.Year}";
    }

    public static string WeekDay(DateOnly d) => WeekDayNames[((int)d.DayOfWeek + 6) % 7];

    /// <summary>"4,33" — the comma the school's papers use.</summary>
    public static string Num(double value, int decimals = 2) =>
        Math.Round(value, decimals).ToString(decimals == 1 ? "0.0" : "0.00", Inv).Replace('.', ',');

    /// <summary>
    /// The pupils a week's report goes to: not archived, and with something in that week — a held lesson
    /// of their class or study group, or a journal entry of their own. A holiday week reaches nobody.
    /// </summary>
    public static async Task<List<string>> PupilsAsync(IAppDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (f, t) = (Iso(from), Iso(to));
        var held = await HeldLessons.ListAsync(db, null, f, t, ct);
        var owners = held.Select(h => h.ClassId).ToHashSet(StringComparer.Ordinal);
        var classNames = await db.Classes.AsNoTracking()
            .Where(c => !c.IsArchived && owners.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct);
        var groupIds = owners.Select(o => Guid.TryParse(o, out var g) ? g : (Guid?)null)
            .Where(g => g is not null).Select(g => g!.Value).ToList();
        var groupMembers = groupIds.Count == 0
            ? []
            : await db.StudyGroupMembers.AsNoTracking()
                .Where(m => groupIds.Contains(m.GroupId) && m.LeftOn == null).Select(m => m.StudentId).ToListAsync(ct);
        var withEntries = await db.JournalEntries.AsNoTracking()
            .Where(e => string.Compare(e.Date, f) >= 0 && string.Compare(e.Date, t) <= 0
                && (e.Grade != null || e.ReasonId != null))
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);

        var wanted = new HashSet<string>(groupMembers.Concat(withEntries), StringComparer.Ordinal);
        return await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && (classNames.Contains(s.ClassName) || wanted.Contains(s.Id)))
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName)
            .Select(s => s.Id).ToListAsync(ct);
    }

    /// <summary>One pupil's week; null — no such pupil.</summary>
    public static async Task<WeeklyReportModel?> BuildAsync(
        IAppDbContext db, string studentId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return null;

        var (f, t) = (Iso(from), Iso(to));
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Name == student.ClassName, ct);
        var attainment = await ClassAttainment.ForStudentAsync(db, student, ct);
        var ownerIds = attainment.OwnerIdsForStudent(student.Id, cls?.Id);
        var unscoped = ownerIds.Count == 0;
        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string SubjectName(string id) => subjectNames.GetValueOrDefault(id, "Fan");

        // The pupil's own rows of the week and of the week before (last week's average).
        var prevFrom = Iso(from.AddDays(-7));
        var own = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.StudentId == student.Id && (unscoped || ownerIds.Contains(e.ClassId))
                    && string.Compare(e.Date, prevFrom) >= 0 && string.Compare(e.Date, t) <= 0)
                .ToListAsync(ct))
            .Where(e => attainment.CountsFor(student.Id, e.ClassId, e.OwnerKind, e.Date))
            .ToList();
        var week = own.Where(e => string.CompareOrdinal(e.Date, f) >= 0).ToList();
        var previous = own.Where(e => string.CompareOrdinal(e.Date, f) < 0 && e.Grade != null).ToList();

        // Class averages on the very lessons the pupil was marked in.
        var markedOwners = week.Where(e => e.Grade != null).Select(e => e.ClassId).Distinct().ToList();
        var lessonAverages = markedOwners.Count == 0
            ? new Dictionary<(string, string, string, int, int), double>()
            : (await db.JournalEntries.AsNoTracking()
                    .Where(e => markedOwners.Contains(e.ClassId) && e.Grade != null
                        && string.Compare(e.Date, f) >= 0 && string.Compare(e.Date, t) <= 0)
                    .Select(e => new { e.ClassId, e.SubjectId, e.Date, e.Period, e.SubGroup, e.Grade })
                    .ToListAsync(ct))
                .GroupBy(e => (e.ClassId, e.SubjectId, e.Date, e.Period, e.SubGroup))
                .ToDictionary(g => g.Key, g => g.Average(e => (double)e.Grade!.Value));

        // Held lessons the pupil sat in (their half of a split class lesson; a group lesson in its window).
        var held = unscoped ? [] : (await HeldLessons.ListAsync(db, ownerIds, f, t, ct))
            .Where(h => attainment.CountsFor(student.Id, h.ClassId, h.OwnerKind, h.Date)
                && (h.OwnerKind == LessonOwnerKind.Group || h.SubGroup == 0 || student.SubGroup == 0 || h.SubGroup == student.SubGroup))
            .DistinctBy(h => (h.ClassId, h.SubjectId, h.Date, h.Period))
            .ToList();
        var late = await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct);
        var lateIds = late.ToHashSet(StringComparer.Ordinal);

        var marks = week.Where(e => e.Grade != null)
            .OrderBy(e => e.Date, StringComparer.Ordinal).ThenBy(e => e.Period)
            .Select(e => new WeeklyMark(
                DateOnly.ParseExact(e.Date, "yyyy-MM-dd", Inv), e.Period, SubjectName(e.SubjectId), e.Grade!.Value,
                lessonAverages.TryGetValue((e.ClassId, e.SubjectId, e.Date, e.Period, e.SubGroup), out var avg)
                    ? Math.Round(avg, 2) : null))
            .ToList();

        var days = new List<WeeklyDay>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var iso = Iso(d);
            var lessons = held.Count(h => h.Date == iso);
            var reasons = week.Where(e => e.Date == iso && e.ReasonId != null).ToList();
            var absent = reasons.Count(e => !lateIds.Contains(e.ReasonId!));
            var lateCount = reasons.Count(e => lateIds.Contains(e.ReasonId!));
            var dayMarks = marks.Where(m => m.Date == d).ToList();
            // A day with no lesson and no entry (Sunday, a holiday) is not a school day.
            if (lessons == 0 && reasons.Count == 0 && dayMarks.Count == 0) continue;
            days.Add(new WeeklyDay(d, Math.Max(lessons, absent + lateCount), absent, lateCount, dayMarks));
        }

        // Quarter averages as the parent's cabinet shows them.
        var quarter = await QuarterOfAsync(db, to, week, ct);
        var report = await StudentReportBuilder.BuildAsync(db, student);
        var quarterBySubject = report.Subjects
            .Where(s => report.Grades.TryGetValue(s.Id, out var q) && q.ContainsKey(quarter))
            .ToDictionary(s => s.Name, s => report.Grades[s.Id][quarter], StringComparer.Ordinal);
        double? quarterAverage = quarterBySubject.Count == 0 ? null : Math.Round(quarterBySubject.Values.Average(), 2);

        var subjects = marks.GroupBy(m => m.Subject, StringComparer.Ordinal)
            .Select(g =>
            {
                var withClass = g.Where(m => m.ClassAverage is not null).ToList();
                return new WeeklySubject(
                    g.Key, [.. g.Select(m => m.Grade)], Math.Round(g.Average(m => m.Grade), 2),
                    withClass.Count == 0 ? null : Math.Round(withClass.Average(m => m.ClassAverage!.Value), 2),
                    quarterBySubject.TryGetValue(g.Key, out var qa) ? qa : null);
            })
            .OrderByDescending(s => s.Average).ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        var discipline = await DisciplineIncidents.HistoryAsync(db, student.Id, ct);
        var behaviour = discipline.Items
            .Select(i => (Day: DayOfIso(i.CreatedAt), i.ReasonName, i.Points))
            .Where(i => i.Day is { } day && day >= from && day <= to && i.Points != 0)
            .OrderBy(i => i.Day)
            .Select(i => new WeeklyBehaviour(i.Day!.Value, i.ReasonName, i.Points))
            .ToList();

        var school = await db.SchoolMeta.AsNoTracking().Select(m => m.Name).FirstOrDefaultAsync(ct);
        var homeroom = cls is null ? "" : await HomeroomDirectory.NamesOfClassNameAsync(db, cls.Name, ct);
        var firstName = AutoMessageService.FirstNameOf(student.FullName, student.FirstName);

        var model = new WeeklyReportModel(
            string.IsNullOrWhiteSpace(school) ? "Maktab" : school.Trim(),
            student.FullName, firstName, student.ClassName, homeroom, student.ParentFullName,
            from, to, quarter, days, subjects,
            previous.Count == 0 ? null : Math.Round(previous.Average(e => (double)e.Grade!.Value), 2), previous.Count,
            quarterAverage, discipline.Remaining, behaviour, [], []);
        model = model with { Strengths = Strengths(model), Attention = Attention(model) };
        return model with { Overview = Overview(model), Recommendations = Recommendations(model), Goals = Goals(model) };
    }

    /// <summary>The quarter the week belongs to: the marks' own quarter, else the quarter period around it.</summary>
    private static async Task<int> QuarterOfAsync(IAppDbContext db, DateOnly to, List<JournalEntry> week, CancellationToken ct)
    {
        var fromMarks = week.Where(e => e.Quarter > 0).GroupBy(e => e.Quarter)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (fromMarks > 0) return fromMarks;
        var iso = Iso(to);
        var periods = await db.Quarters.AsNoTracking().ToListAsync(ct);
        var hit = periods.FirstOrDefault(p => string.CompareOrdinal(p.StartDate, iso) <= 0 && string.CompareOrdinal(p.EndDate, iso) >= 0);
        return hit?.Quarter ?? (await PortalSchedule.CurrentQuarterWeekAsync(db)).Quarter;
    }

    // ---------------------------------------------------------------------
    //  The summary — fixed rules over the numbers above
    // ---------------------------------------------------------------------

    private static List<string> Strengths(WeeklyReportModel m)
    {
        var result = new List<string>();
        var allFives = m.Subjects.Where(s => s.Grades.Count > 0 && s.Grades.All(g => g == 5)).Select(s => s.Name).ToList();
        if (allFives.Count > 0)
            result.Add($"{Join(allFives)} — haftadagi barcha baholar «5».");

        // A good mark on a lesson the class found hard.
        var hard = m.Marks.Where(x => x.Grade >= 4 && x.ClassAverage is { } c && c <= x.Grade - 1.0)
            .OrderBy(x => x.ClassAverage).FirstOrDefault();
        if (hard is not null)
            result.Add($"{hard.Date:dd.MM} dagi {hard.Subject} darsida sinf o'rtachasi {Num(hard.ClassAverage!.Value, 1)} bo'lganda «{hard.Grade}» oldi.");

        if (m.Average is { } avg && m.ClassAverage is { } cls && avg >= cls + 0.2)
            result.Add($"Haftalik o'rtacha baho ({Num(avg)}) sinf o'rtachasidan ({Num(cls)}) yuqori.");
        if (m.PreviousAverage is { } prev && m.Average is { } now && now >= prev + 0.2)
            result.Add($"O'rtacha baho o'tgan haftadagi {Num(prev)} dan {Num(now)} ga ko'tarildi.");
        if (m.Lessons > 0 && m.Absent == 0 && m.Late == 0)
            result.Add("Hafta davomida birorta ham dars qoldirmadi va kechikmadi.");
        else if (m.Lessons > 0 && m.Absent == 0)
            result.Add("Hafta davomida birorta ham dars qoldirmadi.");
        foreach (var b in m.Behaviour.Where(b => b.Points > 0).Take(2))
            result.Add($"Rag'bat: «{b.Reason}» (+{b.Points} ball).");

        if (result.Count == 0) result.Add("Bu hafta alohida ajralib turadigan natija qayd etilmadi.");
        return result;
    }

    private static List<string> Attention(WeeklyReportModel m)
    {
        var result = new List<string>();
        foreach (var g in m.Marks.Where(x => x.Grade <= 3).GroupBy(x => x.Subject, StringComparer.Ordinal))
        {
            var list = string.Join(", ", g.Select(x => $"{x.Date:dd.MM} da «{x.Grade}»"));
            var allHard = g.All(x => x.ClassAverage is { } c && c < 3.0);
            result.Add(allHard
                ? $"{g.Key}: {list} — bu darslarda sinf o'rtachasi ham past bo'lgan, mavzu umuman qiyin kelgan."
                : $"{g.Key}: {list}.");
        }

        var below = m.Subjects.Where(s => s.Average is { } a && s.ClassAverage is { } c && a <= c - 0.5
                && s.Grades.All(g => g > 3)).Select(s => s.Name).ToList();
        if (below.Count > 0)
            result.Add($"{Join(below)} — baholar sinf o'rtachasidan past.");

        if (m.PreviousAverage is { } prev && m.Average is { } now && now <= prev - 0.3)
            result.Add($"O'rtacha baho o'tgan haftadagi {Num(prev)} dan {Num(now)} ga tushdi.");
        if (m.Absent > 0)
        {
            var dates = m.Days.Where(d => d.Absent > 0)
                .Select(d => d.Absent >= d.Lessons ? $"{d.Date:dd.MM} (butun kun)" : $"{d.Date:dd.MM} ({d.Absent} dars)");
            result.Add($"{m.Absent} ta dars qoldirildi: {string.Join(", ", dates)}.");
        }
        if (m.Late > 0)
            result.Add($"{m.Late} marta darsga kechikdi.");
        foreach (var b in m.Behaviour.Where(b => b.Points < 0).Take(3))
            result.Add($"Intizom: {b.Date:dd.MM} — «{b.Reason}» ({b.Points} ball).");
        if (m.Lessons > 0 && m.Marks.Count == 0)
            result.Add("Bu hafta birorta ham baho qo'yilmagan.");

        if (result.Count == 0) result.Add("Bu hafta jiddiy e'tibor talab qiladigan holat yo'q.");
        return result;
    }

    // ---------------------------------------------------------------------
    //  Overview, growth recommendations and next week's goals (client, 2026-11-08: "haftalik umumiy
    //  xulosalar bilan o'sish uchun tavsiyalar") — fixed rules again; every "why" quotes the week.
    // ---------------------------------------------------------------------

    private static string Overview(WeeklyReportModel m)
    {
        string[] verdicts = ["E'tibor talab qiladigan hafta", "O'rtacha hafta", "Yaxshi hafta", "Juda yaxshi hafta"];
        var level = m.Average switch
        {
            null => -1,
            >= 4.5 when m.Absent == 0 => 3,
            >= 4.0 => 2,
            >= 3.5 => 1,
            _ => 0,
        };
        // Clearly below the class, or clearly down on last week — one step lower.
        if (level > 0 && ((m.Average <= m.ClassAverage - 0.2) || (m.Average <= m.PreviousAverage - 0.2))) level--;
        var verdict = level < 0 ? "Bu hafta baholar yo'q" : verdicts[level];
        var parts = new List<string>();
        if (m.Average is { } avg)
        {
            var vsClass = m.ClassAverage is { } cls
                ? avg >= cls + 0.1 ? $", bu sinf o'rtachasidan ({Num(cls)}) yuqori"
                : avg <= cls - 0.1 ? $", bu sinf o'rtachasidan ({Num(cls)}) past"
                : $", sinf o'rtachasi bilan deyarli teng ({Num(cls)})"
                : "";
            parts.Add($"{m.FirstName} bu hafta {m.Marks.Count} ta baho oldi, o'rtachasi {Num(avg)}{vsClass}.");
        }
        else
            parts.Add($"{m.FirstName}ga bu hafta baho qo'yilmagan.");

        if (m.PreviousAverage is { } prev && m.Average is { } now)
            parts.Add(now >= prev + 0.1 ? $"O'tgan haftaga nisbatan o'sish bor ({Num(prev)} → {Num(now)})."
                : now <= prev - 0.1 ? $"O'tgan haftaga nisbatan pasayish bor ({Num(prev)} → {Num(now)})."
                : "Natija o'tgan hafta darajasida saqlandi.");

        parts.Add(m.Lessons == 0 ? "Davomat qayd etilmagan."
            : m.Absent == 0 ? m.Late == 0 ? "Davomat a'lo — birorta dars qoldirilmadi." : $"Dars qoldirilmadi, lekin {m.Late} marta kechikdi."
            : $"{m.Lessons} darsdan {m.Absent} tasi qoldirildi (davomat {m.AttendancePercent}%).");

        var best = m.Subjects.Where(s => s.Average >= 4.5).Select(s => s.Name).Take(3).ToList();
        var weak = WeakSubjects(m).Select(s => s.Name).Take(3).ToList();
        if (best.Count > 0) parts.Add($"Eng yaxshi natijalar: {Join(best)}.");
        if (weak.Count > 0) parts.Add($"Ko'proq e'tibor kerak: {Join(weak)}.");
        return $"{verdict}. " + string.Join(" ", parts);
    }

    /// <summary>Subjects with a mark of 3 or lower, or an average well below the class — weakest first.</summary>
    private static List<WeeklySubject> WeakSubjects(WeeklyReportModel m) =>
    [
        .. m.Subjects
            .Where(s => s.Grades.Any(g => g <= 3) || (s.Average is { } a && s.ClassAverage is { } c && a <= c - 0.5))
            .OrderBy(s => s.Average).ThenBy(s => s.Name, StringComparer.Ordinal),
    ];

    private static readonly string[] LanguageWords = ["tili", "til ", "ingliz", "rus", "nemis", "arab", "adabiyot", "mutolaa", "english"];
    private static readonly string[] ExactWords = ["matem", "algebra", "geometr", "fizika", "kimyo", "informatika"];

    private static List<string> StudySteps(string subject)
    {
        var s = subject.ToLowerInvariant() + " ";
        if (LanguageWords.Any(s.Contains))
            return
            [
                "Har kuni 15 daqiqa so'z yodlash: 8–10 ta yangi so'z kartochkada, ertasi kuni avval kechagilari takrorlanadi.",
                "Har kuni 10 daqiqa ovoz chiqarib o'qish va o'qiganini 3–4 gapda aytib berish.",
                "«Xatolar daftari»: past bahodan keyin qaysi qoida yoki so'zda xato qilgani yozib boriladi va hafta oxirida qayta ishlanadi.",
            ];
        if (ExactWords.Any(s.Contains))
            return
            [
                "Uy vazifasidan tashqari kuniga 3 ta qo'shimcha masala; xato qilingan masala ertasi kuni qayta yechiladi.",
                "Har bir mavzuning asosiy formulasi yoki qoidasi alohida daftarga yozib boriladi va hafta oxirida takrorlanadi.",
                "Tushunilmagan masala belgilab qo'yiladi va keyingi darsda o'qituvchidan so'raladi.",
            ];
        return
        [
            "«Uyda o'qituvchi» usuli: o'sha kuni o'tilgan mavzuni 3 daqiqada ota-onaga yoki uka-singlisiga tushuntirib beradi.",
            "Mavzuni 5–6 so'zli qisqa sxema, jadval yoki rasm ko'rinishida chizish.",
            "Darslikdagi mavzu oxiridagi savollarga og'zaki javob berish.",
        ];
    }

    private static List<WeeklyRecommendation> Recommendations(WeeklyReportModel m)
    {
        var result = new List<WeeklyRecommendation>();
        var weak = WeakSubjects(m);

        foreach (var s in weak.Take(2))
        {
            var marks = string.Join(", ", m.Marks.Where(x => x.Subject == s.Name).Select(x => $"{x.Date:dd.MM} da «{x.Grade}»"));
            var why = s.ClassAverage is { } c && s.Average is { } a
                ? $"Bu hafta: {marks}; fan bo'yicha o'rtacha {Num(a)}, sinfniki — {Num(c)}."
                : $"Bu hafta: {marks}.";
            if (s.QuarterAverage is { } q) why += $" Chorak o'rtachasi — {Num(q)}.";
            var steps = StudySteps(s.Name);
            steps.Add($"{s.Name} o'qituvchisi bilan qisqa (10 daqiqalik) suhbat: qaysi mavzuda qiynalayotganini va nimani takrorlash kerakligini aniqlash.");
            steps.Add("Ota-ona har kuni 5 daqiqa uy vazifasi bajarilganini ko'zdan kechiradi — tekshirish emas, qiziqish bildirish.");
            var targetAvg = Num(Math.Min(5, Math.Max(4.0, (s.Average ?? 3) + 0.5)), 1);
            result.Add(new WeeklyRecommendation(
                $"{s.Name} — asosiy o'sish nuqtasi", why, steps,
                s.Grades.Any(g => g <= 3)
                    ? $"keyingi haftada {s.Name}dan «3» va undan past baho bo'lmasligi; o'rtachani {targetAvg} ga yetkazish."
                    : $"keyingi haftada {s.Name}dan o'rtachani {targetAvg} ga yetkazib, sinf darajasiga chiqish."));
        }

        var weakNames = weak.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var almost = m.Subjects.Where(s => !weakNames.Contains(s.Name) && s.Average is >= 4.0 and < 5.0)
            .Select(s => s.Name).Take(4).ToList();
        if (almost.Count > 0)
            result.Add(new WeeklyRecommendation(
                $"«4» lardan «5» ga: {Join(almost)}",
                $"{(almost.Count == 1 ? "Bu fanda" : "Bu fanlarda")} bilim bor — baholar yaxshi, lekin «5» gacha bir qadam yetmayapti.",
                [
                    "«Uyda o'qituvchi» usuli: har kuni kechqurun o'tilgan bitta mavzuni 3 daqiqada boshqaga tushuntirib berish — bilimni mustahkamlashning eng samarali yo'llaridan biri.",
                    "Har bir fan bo'yicha haftasiga 1–2 marta qo'shimcha topshiriq (masala, savol yoki qisqa matn).",
                    "Darsda faol qatnashish — qo'l ko'tarib javob berishga undash.",
                ],
                $"keyingi haftada {(almost.Count == 1 ? almost[0] : "shu fanlarning har biri")}dan kamida bitta «5»."));

        if (m.Absent > 0 || m.Late > 0)
        {
            var missed = m.Days.Where(d => d.Absent > 0).Select(d => $"{d.Date:dd.MM}").ToList();
            var steps = new List<string>();
            if (m.Absent > 0)
            {
                steps.Add($"Qoldirilgan kun(lar)da ({string.Join(", ", missed)}) o'tilgan mavzularni sinfdoshining daftari yoki darslik orqali o'qib chiqish.");
                steps.Add("Shu kunlarda baho olinmay qolgan fanlar bo'yicha o'qituvchilar bilan kelishib, javob berish.");
                steps.Add("Dars qoldirishga to'g'ri kelsa — sinf rahbariga oldindan xabar berish; sababli bo'lsa, rasmiylashtirish.");
            }
            if (m.Late > 0)
                steps.Add("Kechikmaslik uchun: kechqurun sumka va kiyim tayyorlab qo'yiladi, ertalab 10–15 daqiqa erta turiladi.");
            result.Add(new WeeklyRecommendation(
                "Davomatni tiklash",
                m.Absent > 0
                    ? $"Bu hafta {m.Absent} ta dars qoldirildi (davomat {m.AttendancePercent}%). Qoldirilgan dars — o'tilmay qolgan mavzu."
                    : $"Bu hafta {m.Late} marta kechikdi.",
                steps, "keyingi haftada 100% davomat va kechikishsiz."));
        }

        var minus = m.Behaviour.Where(b => b.Points < 0).ToList();
        if (minus.Count > 0)
            result.Add(new WeeklyRecommendation(
                "Intizom",
                $"Bu hafta {minus.Count} ta salbiy qayd: {string.Join(", ", minus.Take(3).Select(b => $"«{b.Reason}»"))}.",
                [
                    "Farzand bilan xotirjam suhbat: nima bo'lganini uning o'zidan eshitish, tanbeh o'rniga «Keyingi safar nima qilamiz?» deb so'rash.",
                    "Savollar bo'lsa — sinf rahbari bilan bog'lanib, holatni birga muhokama qilish.",
                    "Darsda faol qatnashish va topshiriqni vaqtida bajarish rag'bat bali bilan qayd etiladi — buni farzandga eslatish.",
                ],
                "keyingi haftada birorta ham salbiy qayd bo'lmasligi."));

        if (m.Marks.Count == 0 && m.Lessons > 0)
            result.Add(new WeeklyRecommendation(
                "Baholar yo'q",
                "Bu hafta darslar bo'lgan, lekin birorta ham baho qo'yilmagan.",
                [
                    "Farzanddan darslarda qanday mavzular o'tilgani va o'zini qanday his qilgani haqida so'rash.",
                    "Sinf rahbari orqali o'qituvchilardan farzandning darsdagi faolligi haqida bilish.",
                ],
                "keyingi haftada darslarda faol qatnashib, baholar olish."));

        // Always last: keep what works.
        var strong = m.Subjects.Where(s => s.Average >= 4.5).Select(s => s.Name).Take(4).ToList();
        result.Add(new WeeklyRecommendation(
            "Kuchli tomonlarni saqlash va to'g'ri rag'batlantirish",
            strong.Count > 0 ? $"{Join(strong)} — bu hafta eng yaxshi natijalar."
                : "Har bir yaxshi natija — rag'batlantirish uchun imkoniyat.",
            [
                "Maqtovni bahoga emas, harakatga qarating: «Sen yaxshi tayyorlanding» — «Sen aqllisan»dan ko'ra samaraliroq.",
                "Past baho olgan kuni tanbeh o'rniga savol bering: «Qaysi joyi qiyin bo'ldi? Ertaga nima qilamiz?» — bola xatosini yashirmaydi.",
                "Uy vazifasidan oldin telefonni boshqa xonada qoldirish va yetarli uyqu (9–10 soat) diqqatni sezilarli yaxshilaydi.",
            ],
            m.Average is { } avg ? $"haftalik o'rtachani {Num(avg)} dan past tushirmaslik." : "keyingi haftada barqaror natija."));

        return result;
    }

    private static List<WeeklyGoal> Goals(WeeklyReportModel m)
    {
        var goals = new List<WeeklyGoal>();
        var target = m.Average is { } avg ? Math.Min(5.0, Math.Max(4.0, Math.Ceiling((avg + 0.15) * 10) / 10)) : 4.0;
        goals.Add(new WeeklyGoal($"≥ {Num(target, 1)}", "haftalik o'rtacha baho"));
        var weak = WeakSubjects(m).FirstOrDefault();
        goals.Add(weak is not null
            ? new WeeklyGoal("0 ta «2»/«3»", $"{weak.Name} fanidan")
            : new WeeklyGoal("0 ta «2»", "barcha fanlardan"));
        goals.Add(new WeeklyGoal("100%", "davomat"));
        goals.Add(m.Behaviour.Any(b => b.Points < 0)
            ? new WeeklyGoal("0 ta", "salbiy intizom qaydi")
            : new WeeklyGoal("+1", "rag'bat qaydi"));
        return goals;
    }

    private static string Join(List<string> names) =>
        names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join(", ", names[..^1]) + " va " + names[^1];

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    /// <summary>The day of an ISO date or date-time ("2026-11-03", "2026-11-03T10:15:00").</summary>
    private static DateOnly? DayOfIso(string? text) =>
        text is { Length: >= 10 } && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d)
            ? d : null;
}
