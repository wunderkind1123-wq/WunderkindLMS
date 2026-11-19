using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  BLOK GURUHI — USTOZ EKRANLARI (docs/modules/blocks.md, TZ 6.4; mijoz qarori 2).
// ===========================================================================
//
//  · Yo'nalishli blokda (manbalaridan biri yo'nalish guruhi — Fil+Ijt, Iqt, Aniq+Iqt) jurnal
//    bahosi QO'YILMAYDI: yo'nalish o'quvchilari oraliq test oladi. Davomat, uy vazifasi va xulq
//    — qo'yiladi. 3- va 4-sinf bloklarida (manbalari sinflar) baho odatdagidek.
//  · Ustoz ro'yxatida guruh "Ingliz tili, Katta (Fil+Ijt Ingliz)" ko'rinishida.
//  · O'quvchi yonida uning manba sinfi ("Ali V. · Filologiya").
// ===========================================================================

/// <summary>Blok guruhi haqida ustoz ekranlariga kerakli narsalar.</summary>
public static class BlockJournal
{
    public const string NoGradesMessage =
        "Yo'nalishli blokda jurnal bahosi qo'yilmaydi — o'quvchilar oraliq test oladi. Davomat va uy vazifasi qo'yiladi.";

    /// <summary>Ega (o'quv guruhi id'si) blok guruhimi va unda baho qo'yish mumkinmi.</summary>
    public static async Task<bool> GradesAllowedAsync(IAppDbContext db, string ownerId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(ownerId, out var sg)) return true;
        var blockId = await db.BlockGroups.AsNoTracking().Where(g => g.StudyGroupId == sg)
            .Select(g => (Guid?)g.BlockId).FirstOrDefaultAsync(ct);
        if (blockId is null) return true;
        return !await db.BlockClasses.AnyAsync(c => c.BlockId == blockId && c.OwnerKind == LessonOwnerKind.Group, ct);
    }

    /// <summary>
    /// Yo'nalishli bloklarning guruhlari (o'quv guruhi id'lari) — oraliq test shu guruhlarda olinadi.
    /// </summary>
    public static async Task<HashSet<Guid>> TrackBlockGroupIdsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var trackBlocks = db.BlockClasses.Where(c => c.OwnerKind == LessonOwnerKind.Group).Select(c => c.BlockId);
        return [.. await db.BlockGroups.AsNoTracking()
            .Where(g => trackBlocks.Contains(g.BlockId) && db.Blocks.Any(b => b.Id == g.BlockId && b.IsActive))
            .Select(g => g.StudyGroupId).ToListAsync(ct)];
    }

    /// <summary>O'quv guruhi id'si → "Ingliz tili, Katta (Fil+Ijt Ingliz)" — faqat blok guruhlari.</summary>
    public static async Task<Dictionary<string, string>> LabelsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.BlockGroups.AsNoTracking()
            .Join(db.Blocks, g => g.BlockId, b => b.Id, (g, b) => new { g.StudyGroupId, Group = g.Name, Block = b.Name, b.SubjectId })
            .Join(db.Subjects, x => x.SubjectId, s => s.Id, (x, s) => new { x.StudyGroupId, x.Group, x.Block, Subject = s.Name })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.StudyGroupId.ToString(), r => $"{r.Subject}, {r.Group} ({r.Block})", StringComparer.Ordinal);
    }

    /// <summary>
    /// Ota-ona va o'quvchi ekranlari uchun: berilgan o'quv guruhlaridan BLOK guruhlarining o'rniga
    /// ko'rsatiladigan nom — o'quvchining manba sinfi yoki yo'nalishi. Guruh nomi ("Katta") ota-onaga
    /// chiqmaydi (mijoz qarori, docs/modules/blocks.md). Blok bo'lmagan guruhlar lug'atga kirmaydi.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> ParentNamesAsync(
        IAppDbContext db, string studentId, IReadOnlyCollection<Guid> groupIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, string>();
        if (groupIds.Count == 0) return result;
        var blockGroups = await db.BlockGroups.AsNoTracking().Where(g => groupIds.Contains(g.StudyGroupId))
            .Select(g => g.StudyGroupId).ToListAsync(ct);
        foreach (var sg in blockGroups.Distinct())
        {
            var names = await SourceNamesAsync(db, sg.ToString(), null, ct);
            result[sg] = names.GetValueOrDefault(studentId, "");
        }
        return result;
    }

    /// <summary>
    /// Blok guruhidagi o'quvchilarning manba sinfi nomi (sanada; berilmasa — eng oxirgi a'zolik).
    /// Ega blok guruhi bo'lmasa — bo'sh lug'at.
    /// </summary>
    public static async Task<Dictionary<string, string>> SourceNamesAsync(
        IAppDbContext db, string ownerId, DateOnly? on = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(ownerId, out var sg) || !await db.BlockGroups.AnyAsync(g => g.StudyGroupId == sg, ct))
            return new(StringComparer.Ordinal);
        var members = await db.StudyGroupMembers.AsNoTracking().Where(m => m.GroupId == sg)
            .Select(m => new { m.StudentId, m.JoinedOn, m.LeftOn, m.SourceClassId }).ToListAsync(ct);
        var ids = members.Select(m => m.SourceClassId).Where(id => id != null).Distinct().ToList();
        var classes = await db.Classes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var guids = ids.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).ToList();
        var tracks = await db.StudyGroups.AsNoTracking().Where(g => guids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id.ToString(), g => g.Name, ct);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var byStudent in members.GroupBy(m => m.StudentId))
        {
            var hit = on is { } d
                ? byStudent.FirstOrDefault(m => m.JoinedOn <= d && (m.LeftOn == null || m.LeftOn >= d))
                : byStudent.OrderByDescending(m => m.JoinedOn).First();
            var src = hit?.SourceClassId;
            if (src is null) continue;
            if (classes.TryGetValue(src, out var cn) || tracks.TryGetValue(src, out cn)) result[byStudent.Key] = cn;
        }
        return result;
    }
}
