using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Ish reja — Excel import, "o'tildi" belgilari va hisobotlar (docs/modules/curriculum-monitoring.md §1).
///
/// <para>
/// <b>Ega (owner)</b> — rejani o'tadigan sinf yoki guruh. Daraja rejasining egalari — shu darajadagi
/// sinflar (va shu darajaning shu fandan ODDIY guruhlari), qaysiki dars jadvalida shu fan bor;
/// jadvalda hech biri bo'lmasa — darajaning barcha sinflari. Yo'nalish rejasining egasi — yo'nalishning
/// o'zi. "O'tildi" belgisi har ega uchun alohida.
/// </para>
/// </summary>
public static class CurriculumService
{
    /// <summary>Excel ustunlari — namuna fayl va import bir xil tartibda.</summary>
    public static readonly string[] Columns =
        ["№", "Chorak", "Hafta", "Bo'lim (modul)", "Mavzu", "Soat", "Maqsad / kutilgan natija", "Uy vazifasi"];

    // ------------------------------------------------------------------ Excel

    public sealed record ParsedTopic(int Order, int? Quarter, int? Week, string? Section, string Title, int Hours, string? Goal, string? Homework);

    /// <summary>
    /// Excel qatorlari → mavzular. Sarlavha qatori (Mavzu ustunida "Mavzu") va bo'sh qatorlar
    /// o'tkazib yuboriladi. Xatolar Excel qator raqami bilan qaytadi; bitta xato bo'lsa ham hech
    /// narsa saqlanmaydi.
    /// </summary>
    public static (List<ParsedTopic> Topics, List<string> Errors) Parse(Stream stream)
    {
        List<string[]> rows;
        try { rows = ExcelImport.ReadRows(stream, Columns.Length); }
        catch (Exception) { return ([], ["Fayl o'qilmadi — .xlsx (Excel) formatida yuklang."]); }

        var topics = new List<ParsedTopic>();
        var errors = new List<string>();
        var seen = new HashSet<int>();
        var next = 1;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i].Select(c => (c ?? "").Trim()).ToArray();
            var line = i + 1;
            if (r.All(string.IsNullOrEmpty)) continue;
            if (string.Equals(r[4], "Mavzu", StringComparison.OrdinalIgnoreCase) || r[0] == "№") continue;
            if (r[4].Length == 0) { errors.Add($"{line}-qator: «Mavzu» bo'sh"); continue; }
            if (r[4].Length > 500) { errors.Add($"{line}-qator: mavzu 500 belgidan uzun"); continue; }

            int order;
            if (r[0].Length == 0) order = next;
            else if (!TryInt(r[0], out order) || order < 1) { errors.Add($"{line}-qator: «№» butun son bo'lsin"); continue; }
            if (!seen.Add(order)) { errors.Add($"{line}-qator: {order}-raqam takrorlangan"); continue; }
            next = Math.Max(next, order + 1);

            int? quarter = null;
            if (r[1].Length > 0)
            {
                if (!TryInt(r[1], out var q) || q is < 1 or > 4) { errors.Add($"{line}-qator: «Chorak» 1–4 bo'lsin"); continue; }
                quarter = q;
            }
            int? week = null;
            if (r[2].Length > 0)
            {
                if (!TryInt(r[2], out var w) || w is < 1 or > 20) { errors.Add($"{line}-qator: «Hafta» 1–20 bo'lsin (chorak ichidagi hafta)"); continue; }
                week = w;
            }
            var hours = 1;
            if (r[5].Length > 0 && (!TryInt(r[5], out hours) || hours is < 1 or > 50))
            { errors.Add($"{line}-qator: «Soat» 1–50 bo'lsin"); continue; }

            topics.Add(new ParsedTopic(order, quarter, week, Cut(r[3], 300), r[4], hours, Cut(r[6], 1000), Cut(r[7], 500)));
        }
        if (topics.Count == 0 && errors.Count == 0) errors.Add("Faylda birorta ham mavzu topilmadi");
        return ([.. topics.OrderBy(t => t.Order)], errors);
    }

    private static string? Cut(string s, int max) => s.Length == 0 ? null : s.Length > max ? s[..max] : s;

    private static bool TryInt(string s, out int value)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        // Excel may store "3" as 3.0
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d))
        { value = (int)d; return true; }
        return false;
    }

    /// <summary>Namuna fayl — "Ish reja" varag'i (to'ldiriladi) va "Ko'rsatma" varag'i.</summary>
    public static byte[] Template()
    {
        var sample = new List<IReadOnlyList<string>>
        {
            new[] { "1", "1", "1", "Natural sonlar", "Natural sonlar va ularni yozish", "1", "O'quvchi ko'p xonali sonni o'qiy va yoza oladi", "§1, 3-5-misollar" },
            new[] { "2", "1", "1", "Natural sonlar", "Natural sonlarni taqqoslash", "1", "Sonlarni <, >, = belgilari bilan taqqoslaydi", "§2, 7-misol" },
            new[] { "3", "1", "2", "Natural sonlar", "Qo'shish va uning xossalari", "2", "Qo'shishning o'rin almashtirish va guruhlash xossalarini qo'llaydi", "§3, 12-15-misollar" },
            new[] { "4", "1", "2", "Natural sonlar", "Nazorat ishi №1", "1", "Bo'lim bo'yicha bilimni tekshirish", "" },
        };
        var guide = new List<IReadOnlyList<string>>
        {
            new[] { "№", "Mavzuning tartib raqami (1, 2, 3 …). Qayta yuklaganda shu raqam bo'yicha «o'tildi» belgilari saqlanadi." },
            new[] { "Chorak", "1–4. Hisobotdagi «chorak bo'yicha reja» va «rejadan orqada/oldinda» shundan hisoblanadi." },
            new[] { "Hafta", "Chorak ichidagi hafta raqami (1 dan). Bo'sh qolsa — mavzu chorak bo'ylab teng taqsimlanadi." },
            new[] { "Bo'lim (modul)", "Ixtiyoriy. Bir nechta mavzuni birlashtiruvchi bo'lim nomi." },
            new[] { "Mavzu", "MAJBURIY. Darsda o'tiladigan mavzu — o'qituvchi darsda ishlatadigan to'liq nomi bilan." },
            new[] { "Soat", "Shu mavzuga ajratilgan dars soni (sukut — 1). Tugash muddati prognozi soat bo'yicha hisoblanadi." },
            new[] { "Maqsad / kutilgan natija", "Mavzu oxirida o'quvchi nimani bila olishi kerak. Monitoring testi savollari shu mavzuga bog'lanadi." },
            new[] { "Uy vazifasi", "Ixtiyoriy." },
            new[] { "", "" },
            new[] { "Qoidalar", "Birinchi qator — sarlavha. Har mavzu alohida qatorda. Bitta fan va bitta sinf darajasi (yoki yo'nalish) uchun bitta fayl." },
            new[] { "", "Reja butun o'quv yili uchun to'liq bo'lsin: tizim o'tilgan va qolgan mavzularni, tugash sanasini shundan hisoblaydi." },
        };
        return ExcelExport.Build(
        [
            new ExcelExport.SheetSpec("Ish reja", Columns, sample),
            new ExcelExport.SheetSpec("Ko'rsatma", ["Ustun", "Izoh"], guide),
        ]);
    }

    /// <summary>
    /// Rejaning mavzularini yangilaydi: shu tartib raqamli mavzu bor bo'lsa — matni almashadi
    /// (belgisi qoladi), yangisi qo'shiladi, faylda yo'g'i o'chadi (belgisi bilan).
    /// </summary>
    public static async Task ReplaceTopicsAsync(IAppDbContext db, CurriculumPlan plan, IReadOnlyList<ParsedTopic> parsed, CancellationToken ct)
    {
        var existing = await db.CurriculumTopics.Where(t => t.PlanId == plan.Id).ToListAsync(ct);
        var byOrder = existing.ToDictionary(t => t.Order);
        foreach (var p in parsed)
        {
            if (!byOrder.Remove(p.Order, out var t))
            {
                t = new CurriculumTopic { PlanId = plan.Id, Order = p.Order };
                db.CurriculumTopics.Add(t);
            }
            t.Quarter = p.Quarter;
            t.Week = p.Week;
            t.Section = p.Section;
            t.Title = p.Title;
            t.Hours = p.Hours;
            t.Goal = p.Goal;
            t.Homework = p.Homework;
        }
        foreach (var gone in byOrder.Values) db.CurriculumTopics.Remove(gone);
    }

    // ------------------------------------------------------------------ owners

    /// <summary>Darsi bor (ega, fan) juftliklari — arxivlanmagan jadvallardan.</summary>
    public static async Task<HashSet<(string OwnerKind, string OwnerId, string SubjectId)>> TaughtPairsAsync(
        IAppDbContext db, CancellationToken ct)
    {
        var scope = await LessonRoster.GroupScopeAsync(db, ct);
        var rows = await (from t in db.ScheduleTemplates.AsNoTracking()
                          where !t.IsArchived
                          from l in t.Lessons
                          select new { t.OwnerKind, t.ClassId, l.SubjectId }).Distinct().ToListAsync(ct);
        return [.. rows.Where(r => scope.Allows(r.OwnerKind, r.ClassId)).Select(r => (r.OwnerKind, r.ClassId, r.SubjectId))];
    }

    public sealed record Owner(string Kind, string Id, string Name, int Grade);

    private sealed record World(
        List<SchoolClass> Classes,
        List<StudyGroup> Groups,
        Dictionary<Guid, int> GroupGrade,
        HashSet<(string OwnerKind, string OwnerId, string SubjectId)> Taught);

    private static async Task<World> WorldAsync(IAppDbContext db, CancellationToken ct)
    {
        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var groups = await db.StudyGroups.AsNoTracking().Where(g => !g.IsArchived).OrderBy(g => g.Name).ToListAsync(ct);
        var gradeOf = classes.ToDictionary(c => c.Id, c => c.Grade);
        var groupGrade = (await db.StudyGroupClasses.AsNoTracking().ToListAsync(ct))
            .Where(x => gradeOf.ContainsKey(x.ClassId))
            .GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.Min(x => gradeOf[x.ClassId]));
        return new World(classes, groups, groupGrade, await TaughtPairsAsync(db, ct));
    }

    /// <summary>Rejani o'tadigan sinf/guruhlar (sinfiy tartibda).</summary>
    private static List<Owner> OwnersOf(CurriculumPlan plan, World w)
    {
        if (plan.GroupId is { } gid)
        {
            var g = w.Groups.FirstOrDefault(x => x.Id == gid);
            return g is null ? [] : [new Owner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, w.GroupGrade.GetValueOrDefault(g.Id))];
        }
        var grade = plan.Grade ?? -1;
        var gradeClasses = w.Classes.Where(c => c.Grade == grade).ToList();
        var taughtClasses = gradeClasses.Where(c => w.Taught.Contains((LessonOwnerKind.Class, c.Id, plan.SubjectId))).ToList();
        var groups = w.Groups
            .Where(g => !g.IsTrack && g.SubjectId == plan.SubjectId && w.GroupGrade.GetValueOrDefault(g.Id, -2) == grade
                        && w.Taught.Contains((LessonOwnerKind.Group, g.Id.ToString(), plan.SubjectId)))
            .ToList();
        var owners = (taughtClasses.Count + groups.Count > 0 ? taughtClasses : gradeClasses)
            .Select(c => new Owner(LessonOwnerKind.Class, c.Id, c.Name, c.Grade)).ToList();
        owners.AddRange(groups.Select(g => new Owner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, grade)));
        return owners;
    }

    /// <summary>Ega va fan qaysi rejaga tushadi: (fan, daraja) yoki (fan, yo'nalish).</summary>
    private static (string SubjectId, int? Grade, Guid? GroupId)? KeyOf(string ownerKind, string ownerId, string subjectId, World w)
    {
        if (ownerKind == LessonOwnerKind.Class)
        {
            var c = w.Classes.FirstOrDefault(x => x.Id == ownerId);
            return c is null ? null : (subjectId, c.Grade, null);
        }
        if (!Guid.TryParse(ownerId, out var gid)) return null;
        var g = w.Groups.FirstOrDefault(x => x.Id == gid);
        if (g is null) return null;
        if (g.IsTrack) return (subjectId, null, g.Id);
        return w.GroupGrade.TryGetValue(g.Id, out var grade) ? (subjectId, grade, null) : null;
    }

    // ------------------------------------------------------------------ calendar

    private sealed record Calendar(List<(int Quarter, DateOnly Start, DateOnly End)> Quarters)
    {
        public DateOnly? YearStart => Quarters.Count == 0 ? null : Quarters.Min(q => q.Start);
        public DateOnly? YearEnd => Quarters.Count == 0 ? null : Quarters.Max(q => q.End);
    }

    private static async Task<Calendar> CalendarAsync(IAppDbContext db, CancellationToken ct)
    {
        var list = new List<(int, DateOnly, DateOnly)>();
        foreach (var q in await db.Quarters.AsNoTracking().OrderBy(q => q.Quarter).ToListAsync(ct))
            if (DateOnly.TryParse(q.StartDate, CultureInfo.InvariantCulture, out var s)
                && DateOnly.TryParse(q.EndDate, CultureInfo.InvariantCulture, out var e) && e >= s)
                list.Add((q.Quarter, s, e));
        return new Calendar(list);
    }

    /// <summary>
    /// Mavzu rejaga ko'ra qachon o'tilishi kerak: chorak + hafta berilsa — o'sha haftaning boshi;
    /// faqat chorak — mavzular chorak bo'ylab teng taqsimlanadi; hech biri — butun yil bo'ylab.
    /// Choraklar kiritilmagan bo'lsa — null (rejadan orqada/oldinda hisoblanmaydi).
    /// </summary>
    private static Dictionary<Guid, DateOnly> PlannedDates(IReadOnlyList<CurriculumTopic> topics, Calendar cal)
    {
        var result = new Dictionary<Guid, DateOnly>();
        if (cal.YearStart is not { } yStart || cal.YearEnd is not { } yEnd) return result;

        static DateOnly Spread(DateOnly start, DateOnly end, int index, int count) =>
            start.AddDays((int)Math.Floor((end.DayNumber - start.DayNumber) * (index + 0.5) / Math.Max(count, 1)));

        foreach (var group in topics.Where(t => t.Quarter is not null).GroupBy(t => t.Quarter!.Value))
        {
            var q = cal.Quarters.FirstOrDefault(x => x.Quarter == group.Key);
            if (q == default) continue;
            var weeks = ScheduleMath.GetQuarterWeeks(q.Start.ToString("yyyy-MM-dd"), q.End.ToString("yyyy-MM-dd"));
            var noWeek = group.Where(t => t.Week is null).OrderBy(t => t.Order).ToList();
            foreach (var t in group.Where(t => t.Week is not null))
            {
                var w = weeks.FirstOrDefault(x => x.Week == t.Week) ?? weeks.LastOrDefault();
                result[t.Id] = w is null ? q.Start : DateOnly.ParseExact(w.StartISO, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            for (var i = 0; i < noWeek.Count; i++) result[noWeek[i].Id] = Spread(q.Start, q.End, i, noWeek.Count);
        }
        var free = topics.Where(t => t.Quarter is null).OrderBy(t => t.Order).ToList();
        for (var i = 0; i < free.Count; i++) result[free[i].Id] = Spread(yStart, yEnd, i, free.Count);
        return result;
    }

    // ------------------------------------------------------------------ read models

    public static async Task<List<CurriculumPlanListItemDto>> ListAsync(IAppDbContext db, CancellationToken ct)
    {
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var w = await WorldAsync(db, ct);
        var topicIds = plans.SelectMany(p => p.Topics).Select(t => t.Id).ToList();
        var progress = await db.CurriculumProgress.AsNoTracking().Where(p => topicIds.Contains(p.TopicId))
            .Select(p => new { p.TopicId, p.OwnerKind, p.OwnerId }).ToListAsync(ct);
        var passedBy = progress.GroupBy(p => p.TopicId).ToDictionary(g => g.Key, g => g.Select(x => (x.OwnerKind, x.OwnerId)).ToHashSet());

        return [.. plans
            .Select(p =>
            {
                var owners = OwnersOf(p, w);
                var total = p.Topics.Count;
                var avg = owners.Count == 0 || total == 0 ? 0m
                    : Math.Round(owners.Average(o => (decimal)p.Topics.Count(t => passedBy.GetValueOrDefault(t.Id)?.Contains((o.Kind, o.Id)) == true)) * 100m / total, 1);
                return new CurriculumPlanListItemDto(
                    p.Id, p.Title, p.SubjectId, subjects.GetValueOrDefault(p.SubjectId) ?? "—", p.Grade, p.GroupId,
                    p.GroupId is { } g ? w.Groups.FirstOrDefault(x => x.Id == g)?.Name : null,
                    total, p.Topics.Sum(t => t.Hours), p.FileUrl, p.FileName, p.UpdatedAt,
                    [.. owners.Select(o => o.Name)], avg);
            })
            .OrderBy(p => p.GroupId is null ? 0 : 1).ThenBy(p => p.Grade).ThenBy(p => p.GroupName).ThenBy(p => p.SubjectName)];
    }

    /// <summary>
    /// Bitta reja: mavzular va har ega bo'yicha belgilar. <paramref name="onlyOwners"/> berilsa
    /// (o'qituvchi) — faqat o'sha egalar.
    /// </summary>
    public static async Task<CurriculumPlanDetailDto?> DetailAsync(
        IAppDbContext db, Guid id, IReadOnlySet<(string Kind, string Id)>? onlyOwners, CancellationToken ct)
    {
        var plan = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return null;
        var w = await WorldAsync(db, ct);
        var owners = OwnersOf(plan, w).Where(o => onlyOwners is null || onlyOwners.Contains((o.Kind, o.Id))).ToList();
        var topics = plan.Topics.OrderBy(t => t.Order).ToList();
        var topicIds = topics.Select(t => t.Id).ToList();
        var marks = await db.CurriculumProgress.AsNoTracking().Where(p => topicIds.Contains(p.TopicId)).ToListAsync(ct);
        var cal = await CalendarAsync(db, ct);
        var planned = PlannedDates(topics, cal);
        var subjectName = await db.Subjects.AsNoTracking().Where(s => s.Id == plan.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";

        return new CurriculumPlanDetailDto(
            plan.Id, plan.Title, plan.SubjectId, subjectName, plan.Grade, plan.GroupId, plan.FileUrl, plan.FileName, plan.UpdatedAt,
            [.. owners.Select(o => new CurriculumOwnerDto(o.Kind, o.Id, o.Name))],
            [.. topics.Select(t => new CurriculumTopicDto(
                t.Id, t.Order, t.Quarter, t.Week, t.Section, t.Title, t.Hours, t.Goal, t.Homework,
                planned.TryGetValue(t.Id, out var d) ? d.ToString("yyyy-MM-dd") : null,
                [.. marks.Where(m => m.TopicId == t.Id && owners.Any(o => o.Kind == m.OwnerKind && o.Id == m.OwnerId))
                    .Select(m => new CurriculumMarkDto(m.OwnerKind, m.OwnerId, m.PassedOn.ToString("yyyy-MM-dd")))]))]);
    }

    /// <summary>Belgi qo'yish / olib tashlash. Ega rejaning egalaridan biri bo'lishi shart.</summary>
    public static async Task<string?> MarkAsync(
        IAppDbContext db, CurriculumMarkRequest req, string userId, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TopicId, ct);
        if (topic is null) return "Mavzu topilmadi";
        var plan = await db.CurriculumPlans.AsNoTracking().FirstAsync(p => p.Id == topic.PlanId, ct);
        var w = await WorldAsync(db, ct);
        if (!OwnersOf(plan, w).Any(o => o.Kind == req.OwnerKind && o.Id == req.OwnerId))
            return "Bu sinf yoki guruh shu ish rejani o'tmaydi";

        var row = await db.CurriculumProgress.FirstOrDefaultAsync(
            p => p.TopicId == req.TopicId && p.OwnerKind == req.OwnerKind && p.OwnerId == req.OwnerId, ct);
        if (!req.Passed)
        {
            if (row is not null) db.CurriculumProgress.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }
        var day = AppClock.Today;
        if (!string.IsNullOrWhiteSpace(req.PassedOn))
        {
            if (!DateOnly.TryParseExact(req.PassedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                return "Sana noto'g'ri";
            if (day > AppClock.Today) return "Kelajakdagi sanani belgilab bo'lmaydi";
        }
        if (row is null)
        {
            row = new CurriculumProgress { TopicId = req.TopicId, OwnerKind = req.OwnerKind, OwnerId = req.OwnerId };
            db.CurriculumProgress.Add(row);
        }
        row.PassedOn = day;
        row.MarkedBy = userId;
        row.MarkedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>O'qituvchining rejalari: u dars beradigan (ega, fan) juftliklariga tushganlari.</summary>
    public static async Task<List<TeacherCurriculumDto>> ForTeacherAsync(IAppDbContext db, string teacherId, CancellationToken ct)
    {
        var pairs = await TeacherOwnerAccess.PairsAsync(db, teacherId, ct);
        var w = await WorldAsync(db, ct);
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var result = new List<TeacherCurriculumDto>();
        foreach (var plan in plans)
        {
            var mine = OwnersOf(plan, w).Where(o => pairs.Contains((o.Id, o.Kind, plan.SubjectId))).ToList();
            if (mine.Count == 0) continue;
            var ids = plan.Topics.Select(t => t.Id).ToList();
            var marks = await db.CurriculumProgress.AsNoTracking().Where(p => ids.Contains(p.TopicId)).ToListAsync(ct);
            foreach (var o in mine)
            {
                var passed = marks.Where(m => m.OwnerKind == o.Kind && m.OwnerId == o.Id).Select(m => m.TopicId).ToHashSet();
                var next = plan.Topics.OrderBy(t => t.Order).FirstOrDefault(t => !passed.Contains(t.Id));
                result.Add(new TeacherCurriculumDto(plan.Id, plan.Title, subjects.GetValueOrDefault(plan.SubjectId) ?? "—",
                    o.Kind, o.Id, o.Name, plan.Topics.Count, passed.Count, next?.Id, next?.Title));
            }
        }
        return [.. result.OrderBy(r => r.OwnerName).ThenBy(r => r.SubjectName)];
    }

    /// <summary>
    /// Hisobot: har reja × ega — o'tilgan/qolgan, rejadan orqada/oldinda, tugash prognozi, chorak
    /// bo'yicha; va jadvalda darsi bor, lekin ish rejasi yo'q (fan, daraja/yo'nalish) juftliklari.
    /// </summary>
    public static async Task<CurriculumReportDto> ReportAsync(IAppDbContext db, CancellationToken ct)
    {
        var today = AppClock.Today;
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var w = await WorldAsync(db, ct);
        var cal = await CalendarAsync(db, ct);
        var allIds = plans.SelectMany(p => p.Topics).Select(t => t.Id).ToList();
        var marks = await db.CurriculumProgress.AsNoTracking().Where(p => allIds.Contains(p.TopicId)).ToListAsync(ct);
        var marksByOwner = marks.ToLookup(m => (m.OwnerKind, m.OwnerId));

        var rows = new List<CurriculumReportRowDto>();
        foreach (var plan in plans)
        {
            var topics = plan.Topics.OrderBy(t => t.Order).ToList();
            var planned = PlannedDates(topics, cal);
            var totalHours = topics.Sum(t => t.Hours);
            var topicSet = topics.ToDictionary(t => t.Id);
            foreach (var o in OwnersOf(plan, w))
            {
                var mine = marksByOwner[(o.Kind, o.Id)].Where(m => topicSet.ContainsKey(m.TopicId)).ToList();
                var passedIds = mine.Select(m => m.TopicId).ToHashSet();
                var passedHours = topics.Where(t => passedIds.Contains(t.Id)).Sum(t => t.Hours);
                int? expected = planned.Count == 0 ? null : topics.Count(t => planned.TryGetValue(t.Id, out var d) && d <= today);

                // pace: hours per week since the year (or the first mark) began
                DateOnly? finish = null;
                var since = cal.YearStart is { } ys && ys <= today ? ys : mine.Count > 0 ? mine.Min(m => m.PassedOn) : (DateOnly?)null;
                if (since is { } s && passedHours > 0)
                {
                    var weeks = Math.Max(1m, (today.DayNumber - s.DayNumber) / 7m);
                    var pace = passedHours / weeks;
                    var left = totalHours - passedHours;
                    finish = left <= 0 ? mine.Max(m => m.PassedOn) : today.AddDays((int)Math.Ceiling(left / pace * 7m));
                }

                var quarters = cal.Quarters.Select(q => new CurriculumQuarterDto(
                    q.Quarter,
                    topics.Count(t => planned.TryGetValue(t.Id, out var d) ? d >= q.Start && d <= q.End : t.Quarter == q.Quarter),
                    mine.Count(m => m.PassedOn >= q.Start && m.PassedOn <= q.End))).ToList();

                rows.Add(new CurriculumReportRowDto(
                    plan.Id, plan.Title, subjects.GetValueOrDefault(plan.SubjectId) ?? "—", o.Kind, o.Id, o.Name,
                    topics.Count, passedIds.Count, totalHours, passedHours,
                    topics.Count == 0 ? 0 : Math.Round(passedIds.Count * 100m / topics.Count, 1),
                    expected, expected is { } e ? passedIds.Count - e : null,
                    finish?.ToString("yyyy-MM-dd"),
                    finish is { } f && cal.YearEnd is { } end && f > end,
                    quarters));
            }
        }

        // taught (subject, grade/track) without a plan
        var planKeys = plans.Select(p => (p.SubjectId, p.Grade, p.GroupId)).ToHashSet();
        var missing = new Dictionary<(string, int?, Guid?), List<string>>();
        foreach (var (kind, ownerId, subjectId) in w.Taught)
        {
            if (KeyOf(kind, ownerId, subjectId, w) is not { } key || planKeys.Contains(key)) continue;
            var name = kind == LessonOwnerKind.Class
                ? w.Classes.FirstOrDefault(c => c.Id == ownerId)?.Name
                : w.Groups.FirstOrDefault(g => g.Id.ToString() == ownerId)?.Name;
            if (!missing.TryGetValue(key, out var list)) missing[key] = list = [];
            if (name is not null && !list.Contains(name)) list.Add(name);
        }
        var missingDtos = missing
            .Select(kv => new CurriculumMissingDto(
                kv.Key.Item1, subjects.GetValueOrDefault(kv.Key.Item1) ?? "—", kv.Key.Item2, kv.Key.Item3,
                kv.Key.Item3 is { } g ? w.Groups.FirstOrDefault(x => x.Id == g)?.Name : null,
                [.. kv.Value.Order()]))
            .OrderBy(m => m.GroupId is null ? 0 : 1).ThenBy(m => m.Grade).ThenBy(m => m.GroupName).ThenBy(m => m.SubjectName)
            .ToList();

        return new CurriculumReportDto(
            today.ToString("yyyy-MM-dd"), cal.YearEnd?.ToString("yyyy-MM-dd"), cal.Quarters.Count > 0,
            [.. rows.OrderBy(r => r.OwnerName).ThenBy(r => r.SubjectName)], missingDtos);
    }
}
