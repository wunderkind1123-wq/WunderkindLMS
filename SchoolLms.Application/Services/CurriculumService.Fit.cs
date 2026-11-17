using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Ish rejaning dars jadvaliga mosligi (mijoz, 2026-11-17: "ustozning tuzgan ish rejasi o'zining
//  dars soatlariga mos tushadimi yo'qmi tekshirish").
//
//  Har ega (sinf/guruh) uchun har chorakda:
//    * rejadagi soat — mavzuning reja sanasi (aniq yoki chorak/haftadan taxmin) tushgan chorak,
//      sanasi yo'q bo'lsa — mavzuning «Chorak» ustuni;
//    * jadvaldagi dars — chorakning har haftasiga BIRIKTIRILGAN jadvaldagi shu fan darslari (ustoz
//      rejasida — faqat shu ustozniki), yakshanba va bayram kunlarisiz. Haftaga jadval biriktirilmagan
//      bo'lsa — eganing asosiy jadvalidan taxmin qilinadi va bu hafta «taxminiy» deb sanaladi.
//  Bo'lingan darsda (1-/2-guruh) bir vaqtdagi ikki katak — bitta dars.
//
//  Aniq sanali mavzu o'sha kuni dars bo'lmasa (bayram, yakshanba yoki jadvalda shu kuni dars yo'q) —
//  alohida ro'yxatda. O'rinbosarliklar hisobga olinmaydi: reja jadvalga qarab tuziladi.
// ===========================================================================

public static partial class CurriculumService
{
    /// <summary>Saqlangan rejaning jadvalga mosligi. <paramref name="onlyOwners"/> — o'qituvchi uchun filtr.</summary>
    public static async Task<List<CurriculumFitDto>?> FitAsync(
        IAppDbContext db, Guid planId, IReadOnlySet<(string Kind, string Id)>? onlyOwners, CancellationToken ct)
    {
        var plan = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return null;
        var w = await WorldAsync(db, ct);
        var owners = OwnersOf(plan, w).Where(o => onlyOwners is null || onlyOwners.Contains((o.Kind, o.Id))).ToList();
        return await FitAsync(db, plan, plan.Topics, owners, ct);
    }

    /// <summary>Yuklashdan oldin (dry run): hali saqlanmagan mavzular bilan.</summary>
    public static async Task<List<CurriculumFitDto>> PreviewFitAsync(
        IAppDbContext db, string subjectId, int? grade, Guid? groupId, string? teacherId,
        IReadOnlyList<ParsedTopic> parsed, CancellationToken ct)
    {
        var plan = new CurriculumPlan { SubjectId = subjectId, Grade = grade, GroupId = groupId, TeacherId = teacherId };
        var topics = parsed.Select(p => new CurriculumTopic
        {
            PlanId = plan.Id, Order = p.Order, Quarter = p.Quarter, Week = p.Week, Title = p.Title,
            Hours = p.Hours, PlannedOn = p.PlannedOn,
        }).ToList();
        var w = await WorldAsync(db, ct);
        return await FitAsync(db, plan, topics, OwnersOf(plan, w), ct);
    }

    private static async Task<List<CurriculumFitDto>> FitAsync(
        IAppDbContext db, CurriculumPlan plan, IReadOnlyList<CurriculumTopic> topics, List<Owner> owners, CancellationToken ct)
    {
        if (owners.Count == 0) return [];
        var cal = await CalendarAsync(db, ct);
        var holidays = await HolidaysAsync(db, ct);
        var planned = PlannedDates(topics, cal);

        var ownerIds = owners.Select(o => o.Id).ToList();
        var assigned = (await db.WeekAssignments.AsNoTracking()
                .Where(a => a.TemplateId != null && ownerIds.Contains(a.ClassId))
                .Select(a => new { a.OwnerKind, a.ClassId, a.Quarter, a.Week, a.TemplateId }).ToListAsync(ct))
            .GroupBy(a => (a.OwnerKind, a.ClassId, a.Quarter, a.Week))
            .ToDictionary(g => g.Key, g => g.First().TemplateId!);
        var templateIds = assigned.Values.Distinct().ToList();
        var templates = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
            .Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var main = (await TeacherLessons.MainTemplatesAsync(db, ct))
            .ToDictionary(x => (x.Owner.Kind, x.Owner.Id), x => x.Template);

        // the plan's hours per quarter
        int? QuarterOf(CurriculumTopic t)
        {
            if (planned.TryGetValue(t.Id, out var d))
            {
                var q = cal.Quarters.FirstOrDefault(x => d >= x.Start && d <= x.End);
                if (q != default) return q.Quarter;
            }
            return t.Quarter is { } tq && cal.Quarters.Any(x => x.Quarter == tq) ? tq : null;
        }
        var hoursByQuarter = topics.GroupBy(QuarterOf).ToDictionary(g => g.Key ?? 0, g => g.Sum(t => t.Hours));

        // the plan's lessons in a template: same subject (and teacher); split halves at one time = one lesson
        List<(int Day, int Period)> Slots(ScheduleTemplate tpl) =>
        [
            .. tpl.Lessons
                .Where(l => l.SubjectId == plan.SubjectId && (plan.TeacherId is null || l.TeacherId == plan.TeacherId))
                .Select(l => (l.Day, l.Period)).Distinct(),
        ];

        var result = new List<CurriculumFitDto>();
        foreach (var o in owners)
        {
            var mainTpl = main.GetValueOrDefault((o.Kind, o.Id));
            var lessonDates = new HashSet<DateOnly>();
            var quarters = new List<CurriculumFitQuarterDto>();
            foreach (var q in cal.Quarters.OrderBy(x => x.Quarter))
            {
                int lessons = 0, estimated = 0;
                foreach (var wk in ScheduleMath.GetQuarterWeeks(Iso(q.Start), Iso(q.End)))
                {
                    var tpl = assigned.TryGetValue((o.Kind, o.Id, q.Quarter, wk.Week), out var tid) ? templates.GetValueOrDefault(tid) : null;
                    if (tpl is null)
                    {
                        if (mainTpl is null) continue;
                        tpl = mainTpl;
                        estimated++;
                    }
                    var monday = Date(ScheduleMath.MondayOfISO(wk.StartISO));
                    var (from, to) = (Date(wk.StartISO), Date(wk.EndISO));
                    foreach (var (day, _) in Slots(tpl))
                    {
                        var date = monday.AddDays(day);
                        if (date < from || date > to || date.DayOfWeek == DayOfWeek.Sunday || holidays.ContainsKey(date)) continue;
                        lessons++;
                        lessonDates.Add(date);
                    }
                }
                quarters.Add(new CurriculumFitQuarterDto(q.Quarter, hoursByQuarter.GetValueOrDefault(q.Quarter), lessons, estimated));
            }

            var issues = new List<CurriculumFitDateDto>();
            foreach (var t in topics.Where(t => t.PlannedOn is not null).OrderBy(t => t.Order))
            {
                var d = t.PlannedOn!.Value;
                if (lessonDates.Contains(d) || !cal.Quarters.Any(q => d >= q.Start && d <= q.End)) continue;
                var reason = holidays.TryGetValue(d, out var h) ? (string.IsNullOrWhiteSpace(h) ? "dam olish kuni" : $"dam olish kuni ({h})")
                    : d.DayOfWeek == DayOfWeek.Sunday ? "yakshanba"
                    : plan.TeacherId is not null ? $"bu kuni ustozning {o.Name} da darsi yo'q"
                    : $"bu kuni {o.Name} da shu fandan dars yo'q";
                issues.Add(new CurriculumFitDateDto(t.Order, t.Title, Iso(d), reason));
            }

            result.Add(new CurriculumFitDto(
                o.Kind, o.Id, o.Name, mainTpl is null ? 0 : Slots(mainTpl).Count,
                topics.Sum(t => t.Hours), quarters.Sum(q => q.Lessons), hoursByQuarter.GetValueOrDefault(0),
                quarters, issues));
        }
        return result;

        static DateOnly Date(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
