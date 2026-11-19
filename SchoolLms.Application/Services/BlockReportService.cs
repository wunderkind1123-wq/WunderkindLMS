using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  BLOK HISOBOTI — docs/modules/blocks.md, TZ 6.5 (6-bosqich).
// ===========================================================================
//
//  · Davr (haftalik — shanba): har blok guruhi ustozlar kesimida (o'tilgan darslar, baholar,
//    kelmaganlar, gamifikatsiya) va manba sinflar kesimida (baholar sinf bo'yicha jamlanadi —
//    `source_class_id`).
//  · Oylik test natijalari: blok fani bo'yicha oylik baholash (mavsumiy baholash) — guruh bo'yicha
//    va sinf bo'yicha, balldan yuqoridan pastga.
//
//  Faqat xodimlar uchun (admin bo'limi) — guruh nomlari ochiq. Ota-onaga bu hisobot bormaydi.
// ===========================================================================

public sealed record BlockReportGroupRow(
    Guid BlockId, string Block, Guid GroupId, string Group, string Teacher,
    int Lessons, int Grades, double? Average, int Absent, int GamePlus, int GameMinus);

public sealed record BlockReportClassRow(
    Guid BlockId, string Block, string SourceId, string Source, int Grades, double? Average, int Absent,
    int GamePlus, int GameMinus);

public sealed record BlockReportDto(string From, string To, List<BlockReportGroupRow> Groups, List<BlockReportClassRow> Classes);

public sealed record BlockMonthlyRow(string StudentId, string FullName, string Group, string Source, decimal? Score);

public sealed record BlockMonthlyDto(
    Guid BlockId, string Block, int Year, int Month,
    List<BlockMonthlyGroup> ByGroup, List<BlockMonthlyGroup> ByClass);

/// <summary>Bir guruh (yoki bir manba sinf) — o'quvchilar balldan yuqoridan pastga.</summary>
public sealed record BlockMonthlyGroup(string Name, List<BlockMonthlyRow> Rows);

public static class BlockReportService
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Davr hisoboti (ikkala chegara ham kiradi).</summary>
    public static async Task<BlockReportDto> PeriodAsync(IAppDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (f, t) = (Iso(from), Iso(to));
        var groups = await db.BlockGroups.AsNoTracking()
            .Join(db.Blocks, g => g.BlockId, b => b.Id, (g, b) => new { g.Id, g.BlockId, g.SortOrder, Group = g.Name, Block = b.Name })
            .ToListAsync(ct);
        if (groups.Count == 0) return new BlockReportDto(f, t, [], []);
        var ids = groups.Select(g => g.Id).ToList();

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.BlockGroupId != null && ids.Contains(e.BlockGroupId.Value)
                        && string.Compare(e.Date, f) >= 0 && string.Compare(e.Date, t) <= 0)
            .Select(e => new { Group = e.BlockGroupId!.Value, e.SourceClassId, e.Grade, e.ReasonId }).ToListAsync(ct);
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => n.BlockGroupId != null && ids.Contains(n.BlockGroupId.Value) && n.Conducted
                        && string.Compare(n.Date, f) >= 0 && string.Compare(n.Date, t) <= 0)
            .Select(n => new { Group = n.BlockGroupId!.Value, n.Date, n.Period }).ToListAsync(ct);
        // Ball vaqti — "yyyy-MM-ddTHH:mm..." matni: sana bo'yicha solishtiriladi.
        var tEnd = Iso(to.AddDays(1));
        var points = await db.DisciplinePoints.AsNoTracking()
            .Where(p => p.BlockGroupId != null && ids.Contains(p.BlockGroupId.Value)
                        && string.Compare(p.CreatedAt, f) >= 0 && string.Compare(p.CreatedAt, tEnd) < 0)
            .Select(p => new { Group = p.BlockGroupId!.Value, p.SourceClassId, p.Points }).ToListAsync(ct);

        var periods = await db.BlockGroupTeachers.AsNoTracking().Where(x => ids.Contains(x.BlockGroupId)
                && x.ValidFrom <= to && (x.ValidTo == null || x.ValidTo >= from)).ToListAsync(ct);
        var teacherIds = periods.Select(p => p.TeacherId).Distinct().ToList();
        var teacherNames = await db.Teachers.AsNoTracking().Where(x => teacherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        static double? Avg(IEnumerable<int?> grades)
        {
            var list = grades.OfType<int>().ToList();
            return list.Count == 0 ? null : Math.Round(list.Average(), 2);
        }

        var groupRows = groups
            .OrderBy(g => g.Block, StringComparer.Ordinal).ThenBy(g => g.SortOrder)
            .Select(g =>
            {
                var e = entries.Where(x => x.Group == g.Id).ToList();
                var p = points.Where(x => x.Group == g.Id).ToList();
                // Davrda ustoz almashgan bo'lsa — ikkalasi ham ("Eski, Yangi").
                var teacher = string.Join(", ", periods.Where(x => x.BlockGroupId == g.Id).OrderBy(x => x.ValidFrom)
                    .Select(x => teacherNames.GetValueOrDefault(x.TeacherId, "—")).Distinct());
                return new BlockReportGroupRow(g.BlockId, g.Block, g.Id, g.Group, teacher,
                    notes.Where(x => x.Group == g.Id).Select(x => (x.Date, x.Period)).Distinct().Count(),
                    e.Count(x => x.Grade != null), Avg(e.Select(x => x.Grade)), e.Count(x => x.ReasonId != null),
                    p.Where(x => x.Points > 0).Sum(x => x.Points), -p.Where(x => x.Points < 0).Sum(x => x.Points));
            }).ToList();

        var blockOf = groups.ToDictionary(g => g.Id, g => (g.BlockId, g.Block));
        var sourceIds = entries.Select(e => e.SourceClassId).Concat(points.Select(p => p.SourceClassId))
            .OfType<string>().Distinct().ToList();
        var sourceNames = await SourceNamesAsync(db, sourceIds, ct);
        var classRows = entries.Where(e => e.SourceClassId != null)
            .Select(e => (Key: (blockOf[e.Group].BlockId, e.SourceClassId!), e.Grade, Absent: e.ReasonId != null, Points: 0))
            .Concat(points.Where(p => p.SourceClassId != null)
                .Select(p => (Key: (blockOf[p.Group].BlockId, p.SourceClassId!), Grade: (int?)null, Absent: false, p.Points)))
            .GroupBy(x => x.Key)
            .Select(g => new BlockReportClassRow(g.Key.BlockId, groups.First(x => x.BlockId == g.Key.BlockId).Block,
                g.Key.Item2, sourceNames.GetValueOrDefault(g.Key.Item2, "—"),
                g.Count(x => x.Grade != null), Avg(g.Select(x => x.Grade)), g.Count(x => x.Absent),
                g.Where(x => x.Points > 0).Sum(x => x.Points), -g.Where(x => x.Points < 0).Sum(x => x.Points)))
            .OrderBy(r => r.Block, StringComparer.Ordinal).ThenBy(r => r.Source, StringComparer.Ordinal)
            .ToList();

        return new BlockReportDto(f, t, groupRows, classRows);
    }

    /// <summary>
    /// Oylik test natijalari (TZ 6.5): blok fanidan shu oyning oylik bahosi, oy oxirida (yoki bugun)
    /// o'quvchi qaysi guruhda bo'lsa o'sha guruh bo'yicha va manba sinf bo'yicha, balldan yuqoridan pastga.
    /// </summary>
    public static async Task<BlockMonthlyDto?> MonthlyAsync(
        IAppDbContext db, Guid blockId, int year, int month, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == blockId, ct);
        if (block is null) return null;
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var on = last < AppClock.Today ? last : AppClock.Today;

        var groups = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId == blockId).OrderBy(g => g.SortOrder).ToListAsync(ct);
        var sgIds = groups.Select(g => g.StudyGroupId).ToList();
        var members = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => sgIds.Contains(m.GroupId) && m.JoinedOn <= on && (m.LeftOn == null || m.LeftOn >= first))
                .ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            // Oy davomida ko'chgan bo'lsa — oxirgi a'zoligi.
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.JoinedOn).First(), StringComparer.Ordinal);
        var studentIds = members.Keys.ToList();
        var marks = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.SubjectId == block.SubjectId && m.PeriodKind == SeasonalPeriodKind.Monthly
                        && m.Year == year && m.Month == month && studentIds.Contains(m.StudentId))
            .ToDictionaryAsync(m => m.StudentId, m => m.Score, StringComparer.Ordinal, ct);
        var names = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);
        var sourceNames = await SourceNamesAsync(db, [.. members.Values.Select(m => m.SourceClassId).OfType<string>().Distinct()], ct);
        var groupName = groups.ToDictionary(g => g.StudyGroupId, g => g.Name);

        var rows = members.Values.Select(m => new BlockMonthlyRow(
                m.StudentId, names.GetValueOrDefault(m.StudentId, ""), groupName.GetValueOrDefault(m.GroupId, ""),
                m.SourceClassId is null ? "—" : sourceNames.GetValueOrDefault(m.SourceClassId, "—"),
                marks.GetValueOrDefault(m.StudentId)))
            .ToList();
        static List<BlockMonthlyRow> Sorted(IEnumerable<BlockMonthlyRow> r) =>
            [.. r.OrderByDescending(x => x.Score ?? -1).ThenBy(x => x.FullName, StringComparer.Ordinal)];

        return new BlockMonthlyDto(block.Id, block.Name, year, month,
            [.. groups.Select(g => new BlockMonthlyGroup(g.Name, Sorted(rows.Where(r => r.Group == g.Name))))],
            [.. rows.GroupBy(r => r.Source).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new BlockMonthlyGroup(g.Key, Sorted(g)))]);
    }

    /// <summary>Manba id'lari (sinf yoki yo'nalish) → nom.</summary>
    private static async Task<Dictionary<string, string>> SourceNamesAsync(
        IAppDbContext db, List<string> ids, CancellationToken ct)
    {
        var classes = await db.Classes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var guids = ids.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).ToList();
        foreach (var g in await db.StudyGroups.AsNoTracking().Where(g => guids.Contains(g.Id)).Select(g => new { g.Id, g.Name }).ToListAsync(ct))
            classes[g.Id.ToString()] = g.Name;
        return classes;
    }
}
