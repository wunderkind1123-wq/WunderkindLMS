using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

// ===========================================================================
//  BLOK GURUHI BELGISI — docs/modules/blocks.md (5-bosqich).
// ===========================================================================
//
//  Blok darsining davomati, bahosi, mavzusi, chorak bahosi, uy vazifasi bali va oraliq test
//  natijasi `block_group_id` va `source_class_id` ni oladi: sinf jurnali, ota-ona va hisobotlar
//  bahoni o'quvchining MANBA sinfi bo'yicha jamlaydi (TZ 6.5), ustoz almashgandan keyin ham
//  baho qaysi guruhda qo'yilgani ma'lum.
//
//  NEGA BU YERDA
//  -------------
//  Bunday qatorni yozadigan joy ko'p: ustoz jurnali, admin jurnali, uy vazifasi jadvali,
//  hamshira davomati, uy vazifasi ballari, oraliq testlar. Har biriga alohida qo'shilsa, bittasi
//  unutiladi va baho manba sinfsiz qoladi. Shuning uchun belgi SAQLASH paytida, bitta joyda
//  qo'yiladi — parol o'zgarishida MCP ruxsatlarini yopish kabi (AppDbContext.SaveChangesAsync).
//
//  ODDIY DARSGA TEGMAYDI
//  ---------------------
//  Faqat `owner_kind = 'group'` qatorlar va faqat ularning egasi blok guruhining o'quv guruhi
//  bo'lsa. Sinf qatorlari uchun hech qanday so'rov yuborilmaydi. Belgi bir marta qo'yiladi:
//  mavjud qiymat qayta yozilmaydi (o'quvchi keyin boshqa guruhga o'tsa ham baho o'z davrida qoladi).
// ===========================================================================

internal static class BlockStamping
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct)
    {
        var tracker = db.ChangeTracker;
        static bool Fresh(EntityState s) => s is EntityState.Added or EntityState.Modified;

        var entries = tracker.Entries<JournalEntry>().Where(e => Fresh(e.State) && e.Entity.BlockGroupId == null
            && e.Entity.OwnerKind == LessonOwnerKind.Group).Select(e => e.Entity).ToList();
        var notes = tracker.Entries<LessonNote>().Where(e => Fresh(e.State) && e.Entity.BlockGroupId == null
            && e.Entity.OwnerKind == LessonOwnerKind.Group).Select(e => e.Entity).ToList();
        var grades = tracker.Entries<QuarterGrade>().Where(e => Fresh(e.State) && e.Entity.BlockGroupId == null
            && e.Entity.OwnerKind == LessonOwnerKind.Group).Select(e => e.Entity).ToList();
        // Uy vazifasi bali — manbasi kalitda: "hw:ega:fan:sana:soat:o'quvchi" (HomeworkDisciplinePoints).
        var points = tracker.Entries<DisciplinePoint>().Where(e => e.State == EntityState.Added
            && e.Entity.BlockGroupId == null && e.Entity.SourceKey != null && e.Entity.SourceKey.StartsWith("hw:"))
            .Select(e => e.Entity).ToList();
        var tests = tracker.Entries<InterimTest>().Where(e => e.State == EntityState.Added && e.Entity.BlockGroupId == null)
            .Select(e => e.Entity).ToList();
        var results = tracker.Entries<InterimTestResult>().Where(e => e.State == EntityState.Added && e.Entity.SourceClassId == null)
            .Select(e => e.Entity).ToList();
        if (entries.Count + notes.Count + grades.Count + points.Count + tests.Count + results.Count == 0) return;

        var ownerIds = entries.Select(e => e.ClassId).Concat(notes.Select(n => n.ClassId)).Concat(grades.Select(g => g.ClassId))
            .Concat(points.Select(p => PointOwner(p.SourceKey!)))
            .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Concat(tests.Select(t => t.GroupId))
            .Where(g => g != Guid.Empty).Distinct().ToList();

        // Natija testi saqlanayotgan bo'lsa — kontekstdan, aks holda bazadan.
        var resultTests = new Dictionary<Guid, (Guid GroupId, DateOnly Date)>();
        if (results.Count > 0)
        {
            foreach (var t in tracker.Entries<InterimTest>().Select(e => e.Entity))
                resultTests[t.Id] = (t.GroupId, t.Date);
            var missing = results.Select(r => r.TestId).Where(id => !resultTests.ContainsKey(id)).Distinct().ToList();
            if (missing.Count > 0)
                foreach (var t in await db.InterimTests.AsNoTracking().Where(t => missing.Contains(t.Id))
                             .Select(t => new { t.Id, t.GroupId, t.Date }).ToListAsync(ct))
                    resultTests[t.Id] = (t.GroupId, t.Date);
            ownerIds.AddRange(resultTests.Values.Select(v => v.GroupId).Where(g => !ownerIds.Contains(g)));
        }
        if (ownerIds.Count == 0) return;

        var groups = await db.BlockGroups.AsNoTracking().Where(g => ownerIds.Contains(g.StudyGroupId))
            .Select(g => new { g.Id, g.StudyGroupId }).ToDictionaryAsync(g => g.StudyGroupId, g => g.Id, ct);
        if (groups.Count == 0) return;

        var sgIds = groups.Keys.ToList();
        var members = (await db.StudyGroupMembers.AsNoTracking().Where(m => sgIds.Contains(m.GroupId))
                .Select(m => new { m.GroupId, m.StudentId, m.JoinedOn, m.LeftOn, m.SourceClassId }).ToListAsync(ct))
            .ToLookup(m => (m.GroupId, m.StudentId));

        // Sanada ochiq a'zolikning manbasi; sana noma'lum (chorak bahosi) — eng oxirgi a'zolik.
        string? Source(Guid sg, string studentId, DateOnly? on)
        {
            var rows = members[(sg, studentId)];
            var hit = on is { } d
                ? rows.FirstOrDefault(m => m.JoinedOn <= d && (m.LeftOn == null || m.LeftOn >= d))
                : rows.OrderByDescending(m => m.JoinedOn).FirstOrDefault();
            return hit?.SourceClassId;
        }

        bool Block(string ownerId, out Guid sg, out Guid bg)
        {
            bg = Guid.Empty;
            return Guid.TryParse(ownerId, out sg) && groups.TryGetValue(sg, out bg);
        }

        foreach (var e in entries)
            if (Block(e.ClassId, out var sg, out var bg))
            {
                e.BlockGroupId = bg;
                e.SourceClassId ??= Source(sg, e.StudentId, Day(e.Date));
            }
        foreach (var n in notes)
            if (Block(n.ClassId, out _, out var bg)) n.BlockGroupId = bg;
        foreach (var g in grades)
            if (Block(g.ClassId, out var sg, out var bg))
            {
                g.BlockGroupId = bg;
                g.SourceClassId ??= Source(sg, g.StudentId, null);
            }
        foreach (var p in points)
            if (Block(PointOwner(p.SourceKey!), out var sg, out var bg))
            {
                p.BlockGroupId = bg;
                p.SourceClassId ??= Source(sg, p.StudentId, Day(PointDate(p.SourceKey!)));
            }
        foreach (var t in tests)
            if (groups.TryGetValue(t.GroupId, out var bg)) t.BlockGroupId = bg;
        foreach (var r in results)
            if (resultTests.TryGetValue(r.TestId, out var t) && groups.ContainsKey(t.GroupId))
                r.SourceClassId = Source(t.GroupId, r.StudentId, t.Date);
    }

    private static string PointOwner(string key) => key.Split(':') is { Length: >= 2 } p ? p[1] : "";

    private static string PointDate(string key) => key.Split(':') is { Length: >= 4 } p ? p[3] : "";

    private static DateOnly? Day(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
