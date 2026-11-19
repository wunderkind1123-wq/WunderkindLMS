using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Ustoz ish rejasini o'zi yuklaydi va tahrirlaydi (mijoz, 2026-11-18):
//  "ish rejani ustozning o'zi yuklaydigan holatga keltirish … ish rejada asosan chorak / hafta /
//  mavzu / sana bo'lishi kerak. Ustozlarga o'z ish rejalarini edit qilish — dars holatidan kelib
//  chiqib o'zgartirish imkoni bo'lsin."
//
//  * To'liqlik (ustoz yuklagan fayl): har mavzuda Chorak, Hafta, Mavzu va Sana bo'lishi SHART, hafta
//    sanaga mos kelishi shart — aks holda fayl qabul qilinmaydi (qator raqami bilan xato).
//  * Tahrir: mavzu qo'shish / o'zgartirish / o'chirish. Sana majburiy; chorak va hafta sanadan.
//  * "Dars bo'lmadi" (ShiftAsync): shu mavzudan boshlab har mavzu keyingisining sanasini oladi,
//    oxirgisi — jadval bo'yicha keyingi dars kuniga. Belgilar ("o'tildi") o'zgarmaydi.
// ===========================================================================

public static partial class CurriculumService
{
    /// <summary>
    /// Ustoz faylining to'liqligi: <paramref name="parsed"/> — fayldagi xom qiymatlar,
    /// <paramref name="resolved"/> — sanadan hisoblangan chorak/hafta (<see cref="ResolveDates"/>).
    /// </summary>
    public static List<string> CompletenessErrors(IReadOnlyList<ParsedTopic> parsed, IReadOnlyList<ParsedTopic> resolved)
    {
        var errors = new List<string>();
        var byLine = resolved.GroupBy(t => t.Line).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in parsed.OrderBy(t => t.Line))
        {
            var missing = new List<string>();
            if (p.Quarter is null) missing.Add("«Chorak»");
            if (p.Week is null) missing.Add("«Hafta»");
            if (p.Date is null) missing.Add("«Sana»");
            if (missing.Count > 0)
            {
                errors.Add($"{p.Line}-qator ({Clip(p.Title)}): {string.Join(", ", missing)} to'ldirilmagan");
                continue;
            }
            if (byLine.TryGetValue(p.Line, out var r) && r.PlannedOn is { } d && r.Week is { } week && week != p.Week)
                errors.Add($"{p.Line}-qator: {Short(d)} sanasi {r.Quarter}-chorakning {week}-haftasiga tushadi, «Hafta» ustunida esa {p.Week}");
        }
        return errors;

        static string Clip(string s) => s.Length > 40 ? s[..40] + "…" : s;
    }

    /// <summary>
    /// Mavzuni saqlaydi: <paramref name="topicId"/> null — yangi mavzu. Xato matni yoki null.
    /// </summary>
    public static async Task<string?> SaveTopicAsync(
        IAppDbContext db, Guid planId, Guid? topicId, CurriculumTopicSaveRequest req, CancellationToken ct)
    {
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) return "Mavzu nomini yozing";
        if (title.Length > 500) return "Mavzu 500 belgidan uzun";
        if (req.Hours is < 1 or > 50) return "Soat 1–50 bo'lsin";
        if (string.IsNullOrWhiteSpace(req.PlannedOn)) return "Dars sanasini tanlang";
        if (!DateOnly.TryParseExact(req.PlannedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return "Sana noto'g'ri";

        var cal = await CalendarAsync(db, ct);
        int quarter, week;
        if (cal.Quarters.Count > 0)
        {
            var q = cal.Quarters.FirstOrDefault(x => date >= x.Start && date <= x.End);
            if (q == default)
                return $"{Short(date)} hech bir chorakka tushmaydi ("
                    + string.Join(", ", cal.Quarters.Select(x => $"{x.Quarter}-chorak {Short(x.Start)}–{Short(x.End)}")) + ")";
            quarter = q.Quarter;
            week = WeekOf(q.Start, q.End, date);
        }
        else
        {
            if (req.Quarter is not (>= 1 and <= 4)) return "Chorakni tanlang (1–4)";
            if (req.Week is not (>= 1 and <= 20)) return "Haftani kiriting (1–20)";
            (quarter, week) = (req.Quarter.Value, req.Week.Value);
        }
        if ((await HolidaysAsync(db, ct)).TryGetValue(date, out var holiday))
            return $"{Short(date)} — dam olish kuni{(string.IsNullOrWhiteSpace(holiday) ? "" : $" ({holiday})")}. Boshqa sana tanlang";
        if (date.DayOfWeek == DayOfWeek.Sunday) return $"{Short(date)} — yakshanba. Boshqa sana tanlang";

        var plan = await db.CurriculumPlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return "Ish reja topilmadi";
        var topics = await db.CurriculumTopics.Where(t => t.PlanId == planId).OrderBy(t => t.Order).ToListAsync(ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        CurriculumTopic topic;
        if (topicId is { } id)
        {
            topic = topics.FirstOrDefault(t => t.Id == id)!;
            if (topic is null) return "Mavzu topilmadi";
        }
        else
        {
            // a new topic goes after AfterOrder: the later ones move one place down
            var at = req.AfterOrder is { } after ? Math.Clamp(after, 0, topics.Count == 0 ? 0 : topics.Max(t => t.Order)) + 1
                : (topics.Count == 0 ? 0 : topics.Max(t => t.Order)) + 1;
            await RenumberAsync(db, topics.Where(t => t.Order >= at).ToDictionary(t => t, t => t.Order + 1), ct);
            topic = new CurriculumTopic { PlanId = planId, Order = at };
            db.CurriculumTopics.Add(topic);
        }
        topic.Title = title;
        topic.Section = Trimmed(req.Section, 300);
        topic.Goal = Trimmed(req.Goal, 1000);
        topic.Homework = Trimmed(req.Homework, 500);
        topic.Hours = req.Hours;
        topic.PlannedOn = date;
        topic.Quarter = quarter;
        topic.Week = week;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }

    /// <summary>Mavzuni o'chiradi (belgilari bilan); keyingi mavzular bir raqam yuqoriga ko'tariladi.</summary>
    public static async Task<string?> DeleteTopicAsync(IAppDbContext db, Guid topicId, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null) return "Mavzu topilmadi";
        var plan = await db.CurriculumPlans.FirstAsync(p => p.Id == topic.PlanId, ct);
        var later = await db.CurriculumTopics.Where(t => t.PlanId == topic.PlanId && t.Order > topic.Order).ToListAsync(ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        db.CurriculumProgress.RemoveRange(await db.CurriculumProgress.Where(p => p.TopicId == topicId).ToListAsync(ct));
        db.CurriculumTopics.Remove(topic);
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await RenumberAsync(db, later.ToDictionary(t => t, t => t.Order - 1), ct);
        await tx.CommitAsync(ct);
        return null;
    }

    /// <summary>
    /// "Dars bo'lmadi": <paramref name="fromTopicId"/> va undan keyingi har mavzu bir darsga suriladi —
    /// keyingi mavzuning sanasini oladi, oxirgisi jadval bo'yicha keyingi dars kuniga o'tadi.
    /// </summary>
    public static async Task<(CurriculumShiftResultDto? Result, string? Error)> ShiftAsync(
        IAppDbContext db, Guid fromTopicId, CancellationToken ct)
    {
        var from = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == fromTopicId, ct);
        if (from is null) return (null, "Mavzu topilmadi");
        var plan = await db.CurriculumPlans.FirstAsync(p => p.Id == from.PlanId, ct);
        var tail = await db.CurriculumTopics.Where(t => t.PlanId == plan.Id && t.Order >= from.Order)
            .OrderBy(t => t.Order).ToListAsync(ct);
        if (tail.FirstOrDefault(t => t.PlannedOn is null) is { } undated)
            return (null, $"{undated.Order}-mavzuda dars sanasi yo'q — avval sanasini kiriting");

        var cal = await CalendarAsync(db, ct);
        var holidays = await HolidaysAsync(db, ct);
        var last = tail[^1].PlannedOn!.Value;
        var owners = OwnersOf(plan, await WorldAsync(db, ct));
        var lessons = await LessonsAsync(db, plan, owners, cal, holidays, ct);
        DateOnly? next = lessons.Values.SelectMany(l => l.Dates).Where(d => d > last).Order().Cast<DateOnly?>().FirstOrDefault();
        if (next is null)
            return (null, cal.YearEnd is { } ye && last.AddDays(7) > ye
                ? $"Oxirgi mavzu ({Short(last)}) o'quv yili oxiridan ({Short(ye)}) chiqib ketadi — mavzularni qisqartiring yoki birlashtiring"
                : $"Dars jadvalida {Short(last)} dan keyin dars topilmadi — oxirgi mavzu sanasini qo'lda kiriting");

        var newDates = tail.Select((t, i) => i + 1 < tail.Count ? tail[i + 1].PlannedOn!.Value : next.Value).ToList();
        for (var i = 0; i < tail.Count; i++)
        {
            var t = tail[i];
            t.PlannedOn = newDates[i];
            var q = cal.Quarters.FirstOrDefault(x => newDates[i] >= x.Start && newDates[i] <= x.End);
            if (q != default)
            {
                t.Quarter = q.Quarter;
                t.Week = WeekOf(q.Start, q.End, newDates[i]);
            }
        }
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (new CurriculumShiftResultDto(tail.Count, Iso(next.Value)), null);
    }

    /// <summary>
    /// Tartib raqamlarini o'zgartiradi. (reja, raqam) noyob — shuning uchun avval vaqtinchalik manfiy
    /// raqamlarga, keyin yakuniy raqamlarga (bir qatorni yangilashda boshqasi bilan to'qnashmaydi).
    /// </summary>
    private static async Task RenumberAsync(IAppDbContext db, Dictionary<CurriculumTopic, int> orders, CancellationToken ct)
    {
        if (orders.Count == 0) return;
        var i = 0;
        foreach (var t in orders.Keys) t.Order = -(++i);
        await db.SaveChangesAsync(ct);
        foreach (var (t, order) in orders) t.Order = order;
        await db.SaveChangesAsync(ct);
    }

    private static string? Trimmed(string? s, int max)
    {
        var v = (s ?? "").Trim();
        return v.Length == 0 ? null : v.Length > max ? v[..max] : v;
    }

    /// <summary>
    /// Ustozning yuklash formasi uchun: u jadvalda dars beradigan (fan, daraja/yo'nalish) juftliklari
    /// va har biri bo'yicha o'z rejasi (bo'lsa).
    /// </summary>
    public static async Task<List<TeacherCurriculumKeyDto>> TeacherKeysAsync(IAppDbContext db, string teacherId, CancellationToken ct)
    {
        var mine = (await TeacherOptionsAsync(db, ct)).FirstOrDefault(t => t.Id == teacherId);
        if (mine is null) return [];
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var tracks = await db.StudyGroups.AsNoTracking().Where(g => g.IsTrack).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var plans = await db.CurriculumPlans.AsNoTracking().Where(p => p.TeacherId == teacherId)
            .Select(p => new { p.Id, p.SubjectId, p.Grade, p.GroupId }).ToListAsync(ct);
        return [.. mine.Keys
            .Select(k =>
            {
                var subject = subjects.GetValueOrDefault(k.SubjectId) ?? "—";
                var who = k.GroupId is { } g ? tracks.GetValueOrDefault(g) ?? "Yo'nalish" : $"{k.Grade}-sinf";
                var plan = plans.FirstOrDefault(p => p.SubjectId == k.SubjectId && p.Grade == k.Grade && p.GroupId == k.GroupId);
                return new TeacherCurriculumKeyDto(k.SubjectId, subject, k.Grade, k.GroupId, $"{who} · {subject}", plan?.Id);
            })
            .OrderBy(k => k.GroupId is null ? 0 : 1).ThenBy(k => k.Grade).ThenBy(k => k.Label)];
    }
}
