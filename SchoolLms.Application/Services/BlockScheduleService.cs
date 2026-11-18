using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  BLOK DARSLARI JADVALDA — docs/modules/blocks.md, TZ 6.3, 7 (3, 4, 5, 9-qoidalar).
// ===========================================================================
//
//  QAYERDA SAQLANADI
//  -----------------
//  Blok darsi har bir blok guruhining O'QUV GURUHI shablonida turadi (owner_kind = 'group'):
//  (kun, dars) da bitta dars, fani — blok fani, o'qituvchisi — guruhning joriy ustozi,
//  `block_id` — blok. Ikkala guruh shabloni doim bir xil kataklarga ega — ularni faqat shu
//  xizmat yozadi. Shuning uchun:
//    · ro'yxat — guruh a'zoligidan (TZ 5.3), sinfning `sub_group` idan emas;
//    · ustoz darsi maosh va hisobotlarda BIR MARTA sanaladi (manba sinflar soniga qaramay);
//    · jurnal qatori guruhga yoziladi (`ck_journal_entries_block_group` shuni talab qiladi).
//  Manba sinf qatorlarida blok faqat KO'RINADI (<see cref="OverlayAsync"/>): u yerda o'sha soat
//  bo'sh turadi va boshqa dars qo'yilmaydi (3-qoida, <see cref="ClashForOwnerAsync"/>).
//
//  HAFTALAR
//  --------
//  Blok guruhining shabloni joriy haftadan boshlab barcha haftalarga avtomatik biriktiriladi.
//  O'zgarish <see cref="LiveScheduleEdit"/> qoidasi bilan yoziladi: o'tgan haftalarda ishlatilgan
//  shablon nusxalanadi, tarix (jurnal, davomat, maosh) o'zgarmaydi.
//
//  QAYSI DARSLAR "BAND" QILADI
//  ---------------------------
//  Joriy va keyingi haftalarga biriktirilgan shablonlardagi darslar — <see cref="ScheduleConflicts"/>
//  kabi: biriktirilmagan qoralama hali hech kimning jadvalida emas.
// ===========================================================================

/// <summary>Blokning bir katagi.</summary>
public sealed record BlockSlot(int Day, int Period);

/// <summary>Manba sinf qatorida ko'rinadigan blok katagi.</summary>
public sealed record BlockOverlayItem(
    string OwnerId, Guid BlockId, string BlockName, string SubjectName, int Day, int Period,
    IReadOnlyList<(string GroupName, string? TeacherName)> Groups);

/// <summary>Blok darslarini jadvalga qo'yish va ularga qarshi tekshiruvlar.</summary>
public sealed class BlockScheduleService(IAppDbContext db)
{
    public const string TemplateName = "Blok darslari";

    private static readonly string[] WeekDays =
        ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba"];

    private static string Cell(int day, int period) =>
        $"{(day is >= 0 and < 6 ? WeekDays[day] : $"{day}-kun")}, {period}-dars";

    /* -------------------------------------------------------------------
     *  1. O'qish
     * ---------------------------------------------------------------- */

    /// <summary>Joriy va keyingi haftalarga biriktirilgan shablon id'lari (har qanday egada).</summary>
    private async Task<HashSet<string>> UpcomingTemplateIdsAsync(CancellationToken ct)
    {
        var boundary = LiveScheduleEdit.EditBoundary();
        var weeks = await LiveScheduleEdit.WeeksAsync(db, ct);
        var rows = await db.WeekAssignments.AsNoTracking()
            .Where(a => a.TemplateId != null)
            .Select(a => new { a.TemplateId, a.Quarter, a.Week }).ToListAsync(ct);
        return rows
            .Where(a => weeks.TryGetValue((a.Quarter, a.Week), out var w) && string.CompareOrdinal(w.Start, boundary) >= 0)
            .Select(a => a.TemplateId!).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Guruhning amaldagi blok shabloni: arxivlanmagan, joriy/keyingi haftalarga biriktirilgani
    /// (bo'lmasa — qoralamasi). Faqat o'tgan haftalardagi eski versiyalar olinmaydi.
    /// </summary>
    private async Task<ScheduleTemplate?> CurrentTemplateAsync(Guid studyGroupId, bool track, CancellationToken ct)
    {
        var id = studyGroupId.ToString();
        var q = db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => t.ClassId == id && t.OwnerKind == LessonOwnerKind.Group && !t.IsArchived);
        var list = track ? await q.ToListAsync(ct) : await q.AsNoTracking().ToListAsync(ct);
        if (list.Count == 0) return null;
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        return list.Where(t => rank(t.Id) != 1).OrderByDescending(t => rank(t.Id)).FirstOrDefault();
    }

    /// <summary>Blokning amaldagi kataklari (1-guruh shablonidan — ikkala guruh bir xil).</summary>
    public async Task<List<BlockSlot>> SlotsAsync(Guid blockId, CancellationToken ct = default)
    {
        var group = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId == blockId)
            .OrderBy(g => g.SortOrder).FirstOrDefaultAsync(ct);
        if (group is null) return [];
        var tpl = await CurrentTemplateAsync(group.StudyGroupId, track: false, ct);
        return tpl is null
            ? []
            : [.. tpl.Lessons.Where(l => l.BlockId == blockId).Select(l => new BlockSlot(l.Day, l.Period))
                .Distinct().OrderBy(s => s.Day).ThenBy(s => s.Period)];
    }

    /// <summary>
    /// Barcha bloklarning manba qatorlaridagi kataklari: har manba (sinf yoki yo'nalish) uchun
    /// shu soatda qaysi blok va uning guruhlari ("Katta · ustoz F.I.") — TZ 6.3.
    /// </summary>
    public async Task<List<BlockOverlayItem>> OverlayAsync(CancellationToken ct = default)
    {
        var blocks = await db.Blocks.AsNoTracking().ToListAsync(ct);
        if (blocks.Count == 0) return [];
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var sources = (await db.BlockClasses.AsNoTracking().ToListAsync(ct)).ToLookup(c => c.BlockId);
        var groups = (await db.BlockGroups.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync(ct)).ToLookup(g => g.BlockId);
        var service = new BlockService(db);
        var today = AppClock.Today;
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        var result = new List<BlockOverlayItem>();
        foreach (var b in blocks)
        {
            var slots = await SlotsAsync(b.Id, ct);
            if (slots.Count == 0) continue;
            var labels = new List<(string, string?)>();
            foreach (var g in groups[b.Id])
            {
                var t = await service.TeacherOnAsync(g.Id, today, ct);
                labels.Add((g.Name, t is null ? null : teacherNames.GetValueOrDefault(t.TeacherId)));
            }
            foreach (var src in sources[b.Id])
                foreach (var s in slots)
                    result.Add(new BlockOverlayItem(src.OwnerId, b.Id, b.Name,
                        subjects.GetValueOrDefault(b.SubjectId, ""), s.Day, s.Period, labels));
        }
        return result;
    }

    /* -------------------------------------------------------------------
     *  2. Tekshiruvlar
     * ---------------------------------------------------------------- */

    /// <summary>
    /// 3-qoida teskari tomondan: oddiy darsni <paramref name="ownerId"/> egasining jadvaliga
    /// (kun, dars) ga qo'yishdan oldin. Ega blok manbasi bo'lsa va shu soatda blok bo'lsa —
    /// rad ("Iqtisodiyot-1 bu soatda Iqt Ingliz blokida band"). Ega blok guruhining o'quv
    /// guruhi bo'lsa — uning jadvali faqat bloklar bo'limidan boshqariladi.
    /// </summary>
    public async Task<string?> ClashForOwnerAsync(
        string ownerId, string ownerName, IReadOnlyCollection<(int Day, int Period)> cells, CancellationToken ct = default)
    {
        if (Guid.TryParse(ownerId, out var gid) && await db.BlockGroups.AnyAsync(g => g.StudyGroupId == gid, ct))
            return "Bu blok guruhining jadvali — uni «Bloklar» bo'limidan boshqaring";
        if (cells.Count == 0) return null;

        var blockIds = await db.BlockClasses.AsNoTracking()
            .Where(c => c.OwnerId == ownerId).Select(c => c.BlockId).ToListAsync(ct);
        foreach (var bid in blockIds)
        {
            var slots = await SlotsAsync(bid, ct);
            var hit = slots.FirstOrDefault(s => cells.Any(c => c.Day == s.Day && c.Period == s.Period));
            if (hit is null) continue;
            var name = await db.Blocks.Where(b => b.Id == bid).Select(b => b.Name).FirstAsync(ct);
            return $"{ownerName} {Cell(hit.Day, hit.Period)} da «{name}» blokida band";
        }
        return null;
    }

    /// <summary>
    /// Blok kataklarini qo'yishdan oldingi tekshiruv (9, 3, 4, 5-qoidalar). <paramref name="cells"/> —
    /// qo'shilayotgan kataklar; <paramref name="teacherOverride"/> — ustoz almashtirishda yangi ustoz.
    /// </summary>
    public async Task<string?> ValidateAsync(
        Block block, IReadOnlyCollection<BlockSlot> cells,
        (Guid GroupId, string TeacherId)? teacherOverride = null, CancellationToken ct = default)
    {
        // 9-qoida: kun naqshi — rad etish (mijoz qarori).
        var wrongDay = cells.FirstOrDefault(c => !BlockDayPattern.Allows(block.DayPattern, c.Day));
        if (wrongDay is not null) return BlockService.DayPatternMessage(block.DayPattern);
        if (cells.Count == 0) return null;

        var groups = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId == block.Id)
            .OrderBy(g => g.SortOrder).ToListAsync(ct);
        var service = new BlockService(db);
        var teachers = new List<(BlockGroup Group, string TeacherId)>();
        foreach (var g in groups)
        {
            var id = teacherOverride is { } o && o.GroupId == g.Id
                ? o.TeacherId
                : (await service.TeacherOnAsync(g.Id, AppClock.Today, ct))?.TeacherId;
            if (id is not null) teachers.Add((g, id));
        }
        var teacherNames = await db.Teachers.AsNoTracking()
            .Where(t => teachers.Select(x => x.TeacherId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        // 4-qoida: bitta ustoz bir soatda ikki guruhda bo'la olmaydi.
        var twice = teachers.GroupBy(t => t.TeacherId).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
            return $"{teacherNames.GetValueOrDefault(twice.Key, "Ustoz")} bir soatda ikkala guruhda bo'la olmaydi — guruhlarga turli ustoz biriktiring";

        // Joriy/keyingi haftalardagi boshqa darslar (shu blok guruhlarining shablonlaridan tashqari).
        var own = groups.Select(g => g.StudyGroupId.ToString()).ToList();
        var upcoming = await UpcomingTemplateIdsAsync(ct);
        var templates = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
            .Where(t => upcoming.Contains(t.Id) && !own.Contains(t.ClassId)).ToListAsync(ct);

        var sources = await db.BlockClasses.AsNoTracking().Where(c => c.BlockId == block.Id).ToListAsync(ct);
        var sourceIds = sources.Select(s => s.OwnerId).ToHashSet(StringComparer.Ordinal);
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        // Boshqa blok guruhlari: o'quv guruhi id'si → (blok, guruh).
        var otherGroups = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId != block.Id)
            .Join(db.Blocks, g => g.BlockId, b => b.Id, (g, b) => new { g.StudyGroupId, g.RoomId, b.Id, b.Name })
            .ToListAsync(ct);
        var otherByOwner = otherGroups.ToDictionary(g => g.StudyGroupId.ToString(), StringComparer.Ordinal);
        var otherSources = (await db.BlockClasses.AsNoTracking().Where(c => c.BlockId != block.Id).ToListAsync(ct))
            .ToLookup(c => c.BlockId);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var rooms = await db.Rooms.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var classRooms = await db.Classes.AsNoTracking().Where(c => c.Room != null && c.Room != "")
            .ToDictionaryAsync(c => c.Id, c => c.Room!, ct);
        var sourceNames = sources.ToDictionary(s => s.OwnerId, s => owners.GetValueOrDefault(s.OwnerId)?.Name ?? s.OwnerId,
            StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            foreach (var tpl in templates)
            {
                foreach (var l in tpl.Lessons.Where(l => l.Day == cell.Day && l.Period == cell.Period))
                {
                    var ownerName = owners.GetValueOrDefault(tpl.ClassId)?.Name ?? tpl.ClassId;
                    otherByOwner.TryGetValue(tpl.ClassId, out var other);

                    // 3-qoida: manba sinf shu soatda boshqa darsda yoki boshqa blokda.
                    if (sourceIds.Contains(tpl.ClassId))
                        return $"{ownerName} {Cell(cell.Day, cell.Period)} da «{subjects.GetValueOrDefault(l.SubjectId, "?")}» darsida band";
                    if (other is not null)
                    {
                        var shared = otherSources[other.Id].FirstOrDefault(s => sourceIds.Contains(s.OwnerId));
                        if (shared is not null)
                            return $"{sourceNames[shared.OwnerId]} {Cell(cell.Day, cell.Period)} da «{other.Name}» blokida band";
                    }

                    // 4-qoida: ustoz shu soatda boshqa joyda.
                    var busy = teachers.FirstOrDefault(t => t.TeacherId == l.TeacherId);
                    if (busy.TeacherId is not null)
                        return $"{teacherNames.GetValueOrDefault(busy.TeacherId, "Ustoz")} {Cell(cell.Day, cell.Period)} da "
                               + (other is not null ? $"«{other.Name}» blokida" : $"{ownerName} darsida") + " band";

                    // 5-qoida: xona.
                    foreach (var g in groups.Where(g => g.RoomId is not null))
                    {
                        var roomName = rooms.GetValueOrDefault(g.RoomId!.Value, "");
                        if ((other?.RoomId == g.RoomId)
                            || (classRooms.TryGetValue(tpl.ClassId, out var cr) && string.Equals(cr, roomName, StringComparison.OrdinalIgnoreCase)))
                            return $"{roomName}-xona {Cell(cell.Day, cell.Period)} da band ({ownerName})";
                    }
                }
            }
        }
        return null;
    }

    /* -------------------------------------------------------------------
     *  3. Yozish
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Blok kataklarini qo'shadi va olib tashlaydi — IKKALA guruh shablonida bir xil. Tekshiruv
    /// (<see cref="ValidateAsync"/>) chaqiruvchida. Saqlashni chaqiruvchi tranzaksiya ichida qiladi.
    /// </summary>
    public async Task<string?> ApplyAsync(
        Block block, IReadOnlyCollection<BlockSlot> add, IReadOnlyCollection<BlockSlot> remove, string userId,
        CancellationToken ct = default)
    {
        var groups = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId == block.Id)
            .OrderBy(g => g.SortOrder).ToListAsync(ct);
        var service = new BlockService(db);
        foreach (var g in groups)
        {
            var teacher = await service.TeacherOnAsync(g.Id, AppClock.Today, ct);
            var tpl = await CurrentTemplateAsync(g.StudyGroupId, track: true, ct);
            if (tpl is null)
            {
                tpl = new ScheduleTemplate
                {
                    ClassId = g.StudyGroupId.ToString(), Name = TemplateName, OwnerKind = LessonOwnerKind.Group,
                };
                db.ScheduleTemplates.Add(tpl);
            }
            else
            {
                var target = await LiveScheduleEdit.PrepareAsync(db, tpl, ct);
                if (target.Error is not null) return target.Error;
                tpl = target.Template!;
            }

            // Majburiy bog'lanish: kolleksiyadan olingan dars "yetim" bo'lib qoladi va EF uni o'chiradi
            // (nusxadagi hali saqlanmagan dars esa shunchaki qo'shilmaydi).
            foreach (var s in remove)
                foreach (var l in tpl.Lessons.Where(l => l.Day == s.Day && l.Period == s.Period).ToList())
                    tpl.Lessons.Remove(l);
            foreach (var s in add.Where(s => !tpl.Lessons.Any(l => l.Day == s.Day && l.Period == s.Period)))
                tpl.Lessons.Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = s.Day, Period = s.Period, SubjectId = block.SubjectId,
                    TeacherId = teacher?.TeacherId ?? "", SubGroup = 0, BlockId = block.Id,
                });
            await db.SaveChangesAsync(ct);
            await AssignUpcomingWeeksAsync(tpl, ct);
        }
        return null;
    }

    /// <summary>
    /// Ustoz almashganda guruh shablonidagi blok darslarining o'qituvchisi joriy haftadan yangilanadi.
    /// Baholar qaysi ustozga tegishli ekanini sanali <c>block_group_teachers</c> hal qiladi.
    /// </summary>
    public async Task<string?> SyncTeacherAsync(BlockGroup group, string teacherId, CancellationToken ct = default)
    {
        var tpl = await CurrentTemplateAsync(group.StudyGroupId, track: true, ct);
        if (tpl is null || tpl.Lessons.All(l => l.TeacherId == teacherId)) return null;
        var target = await LiveScheduleEdit.PrepareAsync(db, tpl, ct);
        if (target.Error is not null) return target.Error;
        foreach (var l in target.Template!.Lessons) l.TeacherId = teacherId;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Shablonni joriy haftadan boshlab barcha haftalarga biriktiradi (bor qatorlar yangilanadi).</summary>
    private async Task AssignUpcomingWeeksAsync(ScheduleTemplate tpl, CancellationToken ct)
    {
        var boundary = LiveScheduleEdit.EditBoundary();
        var weeks = (await LiveScheduleEdit.WeeksAsync(db, ct))
            .Where(w => string.CompareOrdinal(w.Value.Start, boundary) >= 0).Select(w => w.Key).ToList();
        var rows = await db.WeekAssignments
            .Where(a => a.ClassId == tpl.ClassId && a.OwnerKind == LessonOwnerKind.Group).ToListAsync(ct);
        foreach (var (quarter, week) in weeks)
        {
            var row = rows.FirstOrDefault(a => a.Quarter == quarter && a.Week == week);
            if (row is null)
                db.WeekAssignments.Add(new WeekAssignment
                {
                    ClassId = tpl.ClassId, OwnerKind = LessonOwnerKind.Group, Quarter = quarter, Week = week,
                    TemplateId = tpl.Id,
                });
            else row.TemplateId = tpl.Id;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kun naqshining kunlari: toq — Du, Chor, Ju; juft — Se, Pay, Sha.</summary>
    public static int[] PatternDays(string pattern) => pattern switch
    {
        BlockDayPattern.Odd => [0, 2, 4],
        BlockDayPattern.Even => [1, 3, 5],
        _ => [0, 1, 2, 3, 4, 5],
    };

}
