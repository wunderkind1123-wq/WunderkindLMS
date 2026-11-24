using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

// ===========================================================================
//  DAVOMAT O'ZGARISHLARI JURNALI (2026-11-24)
// ===========================================================================
//
//  23.11 da 3-A o'quvchisining ota-onasiga "kelmadi" xabari ketdi, keyin davomat "keldi" ga
//  o'zgartirildi — va kim, qachon, nimani o'zgartirgani hech qayerda qolmagan edi. Endi har bir
//  dars davomati belgisi (sabab qo'yildi, almashdi, olib tashlandi) audit jurnaliga yoziladi.
//
//  NEGA BU YERDA: davomat ko'p joydan yoziladi — davomat belgilash ekrani, ustoz jurnali (veb va
//  Mini App), admin jurnali, sinf rahbari. Har biriga alohida qo'shilsa bittasi unutiladi, shuning
//  uchun belgi SAQLASH paytida, bitta joyda olinadi (BlockStamping bilan bir xil naqsh).
//
//  Bitta saqlashda bitta o'quvchiga bitta qator (bir nechta dars bo'lsa — hammasi bir izohda).
//  Sinf rahbari kun sababini o'zgartirsa, o'zi `AttendanceDay` qatorini yozadi — takrorlanmaydi.
// ===========================================================================

internal static class AttendanceAudit
{
    private sealed record Change(JournalEntry Entry, string? Before, string? After);

    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct)
    {
        var changes = new List<Change>();
        foreach (var e in db.ChangeTracker.Entries<JournalEntry>())
        {
            switch (e.State)
            {
                case EntityState.Added when e.Entity.ReasonId is not null:
                    changes.Add(new Change(e.Entity, null, e.Entity.ReasonId));
                    break;
                case EntityState.Modified:
                    var p = e.Property(x => x.ReasonId);
                    var before = (string?)p.OriginalValue;
                    if (p.IsModified && before != e.Entity.ReasonId)
                        changes.Add(new Change(e.Entity, before, e.Entity.ReasonId));
                    break;
                case EntityState.Deleted:
                    var original = (string?)e.Property(x => x.ReasonId).OriginalValue;
                    if (original is not null) changes.Add(new Change(e.Entity, original, null));
                    break;
            }
        }
        if (changes.Count == 0) return;

        // Sinf rahbari ekrani o'z qatorini yozadi (HomeroomAbsenceService) — shu o'quvchini o'tkazamiz.
        var dayAudited = db.ChangeTracker.Entries<AuditLog>()
            .Where(a => a.State == EntityState.Added && a.Entity.EntityType == AuditService.EntityAttendanceDay)
            .Select(a => a.Entity.StudentId).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
        changes = [.. changes.Where(c => !dayAudited.Contains(c.Entry.StudentId))];
        if (changes.Count == 0) return;

        var studentIds = changes.Select(c => c.Entry.StudentId).Distinct().ToList();
        var subjectIds = changes.Select(c => c.Entry.SubjectId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id, ct);
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var reasons = await db.AbsenceReasons.AsNoTracking()
            .Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        string Reason(string? id) => id is null ? "keldi" : reasons.GetValueOrDefault(id, "o'chirilgan sabab");
        string Lesson(JournalEntry e) => $"{e.Period}-dars {subjects.GetValueOrDefault(e.SubjectId, "fan")}";
        // A key twice (two halves of one lesson) must not throw inside SaveChanges — the last wins.
        Dictionary<string, string> Map(IEnumerable<Change> items, Func<Change, string> value)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in items) map[Lesson(c.Entry)] = value(c);
            return map;
        }

        var actor = AuditActor.Current;
        foreach (var group in changes.GroupBy(c => (c.Entry.StudentId, c.Entry.Date)))
        {
            var (studentId, date) = group.Key;
            var lessons = group.OrderBy(c => c.Entry.Period).ToList();
            var who = students.TryGetValue(studentId, out var s) ? $"{s.FullName} ({s.ClassName})" : studentId;
            var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : date;
            var action = lessons.All(c => c.Before is null) ? "create" : lessons.All(c => c.After is null) ? "delete" : "update";

            db.AuditLogs.Add(AuditService.Entry(
                AuditService.EntityAttendance, $"{studentId}:{date}", action,
                $"Davomat ({day}) — {who}: "
                    + string.Join("; ", lessons.Select(c => c.Before is null
                        ? $"{Lesson(c.Entry)} «{Reason(c.After)}» qo'yildi"
                        : $"{Lesson(c.Entry)} «{Reason(c.Before)}» → «{Reason(c.After)}»")),
                actor?.Id, actor?.Name,
                before: Map(lessons.Where(c => c.Before is not null), c => Reason(c.Before)),
                after: Map(lessons, c => Reason(c.After)),
                studentId: studentId));
        }
    }
}
