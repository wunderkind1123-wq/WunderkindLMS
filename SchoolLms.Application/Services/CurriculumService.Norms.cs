using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Jadval tuzayotganda ish reja soatlari (mijoz, 2026-11-20: "dars jadvali bilan bog'liq hamma narsani
//  boshqarish"). Har ega (sinf/guruh) × fan uchun tanlangan chorakdagi reja soati va undan chiqadigan
//  haftalik dars soni — umumiy jadval muharriri jadvaldagi dars sonini shu bilan solishtiradi.
//
//  Egalar Fit hisobotidan farqli: umumiy reja darajaning BARCHA sinflariga tegishli (jadvalda shu fan
//  hali yo'q sinf ham — aynan u "kam" deb chiqishi kerak); ustoz rejasi — ustoz dars beradigan egalar.
//  Bitta egaga bir nechta reja tushsa (bo'lingan darsning ikki ustozi) — kattasi olinadi.
//  Haftalar soni — chorakdagi o'quv kunlari (dushanba–shanba, bayramlarsiz) / 6.
// ===========================================================================

public static partial class CurriculumService
{
    public static async Task<CurriculumNormsDto> WeeklyNormsAsync(IAppDbContext db, int? quarter, CancellationToken ct)
    {
        var cal = await CalendarAsync(db, ct);
        var q = quarter is { } qn
            ? cal.Quarters.FirstOrDefault(x => x.Quarter == qn)
            : cal.Quarters.FirstOrDefault(x => DateOnly.FromDateTime(DateTime.Today) <= x.End);
        if (q == default) return new CurriculumNormsDto(quarter ?? 0, 0, []);

        var holidays = await HolidaysAsync(db, ct);
        var schoolDays = 0;
        for (var d = q.Start; d <= q.End; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Sunday && !holidays.ContainsKey(d)) schoolDays++;
        var weeks = schoolDays / 6.0;

        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var w = await WorldAsync(db, ct);
        var norms = new Dictionary<(string Kind, string Id, string SubjectId), (int Hours, string Title)>();
        foreach (var plan in plans)
        {
            var planned = PlannedDates(plan.Topics, cal);
            var hours = plan.Topics
                .Where(t => planned.TryGetValue(t.Id, out var d) ? d >= q.Start && d <= q.End : t.Quarter == q.Quarter)
                .Sum(t => t.Hours);
            if (hours <= 0) continue;
            var owners = plan.TeacherId is null
                ? Candidates(plan, w, taughtOnly: false).Where(o => !HasOwnPlans(o, plan, w))
                : OwnersOf(plan, w);
            foreach (var o in owners)
            {
                var key = (o.Kind, o.Id, plan.SubjectId);
                if (!norms.TryGetValue(key, out var cur) || cur.Hours < hours) norms[key] = (hours, plan.Title);
            }
        }

        return new CurriculumNormsDto(q.Quarter, Math.Round(weeks, 1),
        [
            .. norms.Select(kv => new CurriculumNormDto(
                kv.Key.Kind, kv.Key.Id, kv.Key.SubjectId, kv.Value.Hours,
                weeks > 0 ? Math.Round(kv.Value.Hours / weeks, 1) : 0, kv.Value.Title)),
        ]);
    }
}
