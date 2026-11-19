using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  BLOKLAR TEKSHIRUVI — TZ 7-bo'lim qoidalari BUTUN ma'lumot ustida (TZ 8: "Tekshiruv").
// ===========================================================================
//
//  Xizmatlar har amalda qoidalarni o'zi ushlaydi. Bu tekshiruv esa bazaning HOZIRGI holatini
//  boshidan oxirigacha ko'radi — migratsiyadan keyin, qo'lda tuzatishdan keyin yoki shunchaki
//  "hammasi joyidami" savoliga javob: buzilgan har bir holat qoida raqami bilan bitta qator.
//  Hech narsani o'zgartirmaydi.
// ===========================================================================

/// <summary>Bitta buzilish: qoida raqami (TZ 7) va tushuntirish.</summary>
public sealed record BlockAuditIssue(int Rule, string Message);

public static class BlockAuditService
{
    private static readonly string[] WeekDays = ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba", "Yakshanba"];

    private static string Cell(int day, int period) =>
        $"{(day is >= 0 and < 7 ? WeekDays[day] : $"{day}-kun")}, {period}-dars";

    public static async Task<List<BlockAuditIssue>> RunAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var issues = new List<BlockAuditIssue>();
        var blocks = await db.Blocks.AsNoTracking().ToListAsync(ct);
        if (blocks.Count == 0) return issues;
        var groups = await db.BlockGroups.AsNoTracking().ToListAsync(ct);
        var sources = await db.BlockClasses.AsNoTracking().ToListAsync(ct);
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        string OwnerName(string id) => owners.TryGetValue(id, out var o) ? o.Name : id;
        var service = new BlockService(db);
        var schedule = new BlockScheduleService(db);
        var today = AppClock.Today;

        var slots = new Dictionary<Guid, List<BlockSlot>>();
        foreach (var b in blocks) slots[b.Id] = await schedule.SlotsAsync(b.Id, ct);

        foreach (var b in blocks)
        {
            var mine = groups.Where(g => g.BlockId == b.Id).OrderBy(g => g.SortOrder).ToList();
            // 7-qoida: aniq 2 ta guruh.
            if (mine.Count != 2) issues.Add(new(7, $"«{b.Name}»: blokda {mine.Count} ta guruh (2 ta bo'lishi kerak)"));
            // 6-qoida: har guruhda bugungi ustoz.
            foreach (var g in mine)
                if (await service.TeacherOnAsync(g.Id, today, ct) is null)
                    issues.Add(new(6, $"«{b.Name}»: {g.Name} guruhga ustoz biriktirilmagan"));
            // 2-qoida: faol blokda hamma taqsimlangan.
            if (b.IsActive)
                foreach (var e in await service.ActivationErrorsAsync(b, today, ct))
                    if (!e.Contains("ustoz", StringComparison.Ordinal)) issues.Add(new(2, $"«{b.Name}»: {e}"));
            // 9-qoida: kun naqshi.
            foreach (var s in slots[b.Id].Where(s => !BlockDayPattern.Allows(b.DayPattern, s.Day)))
                issues.Add(new(9, $"«{b.Name}»: {Cell(s.Day, s.Period)} — {BlockService.DayPatternMessage(b.DayPattern).ToLowerInvariant()}"));

            // 1-qoida: o'quvchi bir sanada bitta guruhda (a'zolik oraliqlari kesishmaydi).
            var sgIds = mine.Select(g => g.StudyGroupId).ToList();
            var members = await db.StudyGroupMembers.AsNoTracking().Where(m => sgIds.Contains(m.GroupId)).ToListAsync(ct);
            foreach (var byStudent in members.GroupBy(m => m.StudentId))
            {
                var rows = byStudent.OrderBy(m => m.JoinedOn).ToList();
                for (var i = 1; i < rows.Count; i++)
                    if (rows[i - 1].LeftOn is not { } left || left >= rows[i].JoinedOn)
                    {
                        var name = await db.Students.Where(s => s.Id == byStudent.Key).Select(s => s.FullName).FirstOrDefaultAsync(ct);
                        issues.Add(new(1, $"«{b.Name}»: {name} bir vaqtda ikki guruhda ({rows[i].JoinedOn:dd.MM.yyyy} dan)"));
                        break;
                    }
            }

            // 8-qoida: manba sinf bo'yicha blokda manba sinf o'quvchisi faqat o'z guruhida.
            if (b.BlockType == BlockType.BaseClass)
                foreach (var m in members.Where(m => m.LeftOn == null && m.SourceClassId != null))
                {
                    var own = mine.FirstOrDefault(g => g.BaseOwnerId == m.SourceClassId);
                    if (own is not null && own.StudyGroupId != m.GroupId)
                    {
                        var name = await db.Students.Where(s => s.Id == m.StudentId).Select(s => s.FullName).FirstOrDefaultAsync(ct);
                        issues.Add(new(8, $"«{b.Name}»: {name} ({OwnerName(m.SourceClassId!)}) o'z guruhida emas"));
                    }
                }
        }

        // 3, 4, 5-qoidalar — joriy va keyingi haftalardagi jadval ustida.
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        var templates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons).Where(t => !t.IsArchived).ToListAsync(ct))
            .Where(t => rank(t.Id) >= 2).ToList();
        var blockOfSg = groups.ToDictionary(g => g.StudyGroupId.ToString(), g => g, StringComparer.Ordinal);
        var blockName = blocks.ToDictionary(b => b.Id, b => b.Name);

        // 3-qoida: manba shu soatda o'z darsida yoki ikki blokda.
        foreach (var b in blocks)
            foreach (var src in sources.Where(s => s.BlockId == b.Id))
            {
                foreach (var t in templates.Where(t => t.ClassId == src.OwnerId))
                    foreach (var l in t.Lessons.Where(l => slots[b.Id].Any(s => s.Day == l.Day && s.Period == l.Period)))
                        issues.Add(new(3, $"{OwnerName(src.OwnerId)} {Cell(l.Day, l.Period)}: o'z darsi va «{b.Name}» bloki bir vaqtda"));
                foreach (var other in sources.Where(o => o.OwnerId == src.OwnerId && o.BlockId != b.Id && o.BlockId.CompareTo(b.Id) > 0))
                    foreach (var s in slots[b.Id].Where(s => slots[other.BlockId].Contains(s)))
                        issues.Add(new(3, $"{OwnerName(src.OwnerId)} {Cell(s.Day, s.Period)}: «{b.Name}» va «{blockName[other.BlockId]}» bloklarida bir vaqtda"));
            }

        // 4-qoida: ustoz bir soatda bitta joyda; 5-qoida: xona bir soatda bitta guruhda.
        var lessons = templates.SelectMany(t => t.Lessons.Select(l => (Owner: t.ClassId, l.Day, l.Period, l.TeacherId, l.SubGroup))).ToList();
        foreach (var clash in lessons.Where(l => l.TeacherId != "")
                     .GroupBy(l => (l.TeacherId, l.Day, l.Period))
                     .Where(g => g.Select(x => x.Owner).Distinct().Count() > 1
                                 && g.Any(x => blockOfSg.ContainsKey(x.Owner))))
        {
            var teacher = await db.Teachers.Where(t => t.Id == clash.Key.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync(ct);
            issues.Add(new(4, $"{teacher} {Cell(clash.Key.Day, clash.Key.Period)}: "
                              + string.Join(", ", clash.Select(x => OwnerName(x.Owner)).Distinct())));
        }
        var rooms = await db.Rooms.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        foreach (var clash in groups.Where(g => g.RoomId != null)
                     .SelectMany(g => slots.GetValueOrDefault(g.BlockId, []).Select(s => (g.RoomId, s, g)))
                     .GroupBy(x => (x.RoomId, x.s))
                     .Where(x => x.Count() > 1))
            issues.Add(new(5, $"{rooms.GetValueOrDefault(clash.Key.RoomId!.Value, "?")}-xona {Cell(clash.Key.s.Day, clash.Key.s.Period)}: "
                              + string.Join(", ", clash.Select(x => $"{blockName[x.g.BlockId]} · {x.g.Name}"))));

        return [.. issues.OrderBy(i => i.Rule).ThenBy(i => i.Message, StringComparer.Ordinal)];
    }
}
