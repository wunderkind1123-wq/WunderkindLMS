using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Uy vazifasi belgisi → gamifikatsiya bali (client, 2026-11-16): "vazifa degan joydagi ishora
//  belgilari — «−» vazifa chala va «*» vazifa umuman yo'q ga bog'lash … shu ishoralar qo'yilgan vaqt
//  o'quvchiga avtomatik jarima bali qo'yiladi"; and the same for «K», which is lateness to the lesson
//  ("K faqat darsga kechikish uchun"). A cell whose late attendance reason has points of its own gets
//  no «K» entry — the attendance rule already took them; the nurse's roll-call re-syncs its cells.
//
//  THE LINK: a behaviour reason carries the mark it answers (DisciplineReason.HomeworkMark, one reason
//  per mark), set in Ball sabablar. No reason linked to a mark — that mark gives nothing.
//
//  THE RULE, per journal cell (owner, subject, date, period, pupil): the cell's homework mark decides
//  the one automatic entry of that cell (DisciplinePoint.SourceKey = the cell). Every write path of the
//  homework mark (JournalService.SetEntryAsync / ClearEntryAsync, HomeworkCompletionService.SaveAsync —
//  the admin journal, the teacher panel, the Mini App and the homework sheet) calls SyncAsync after its
//  save, so the entry always follows the mark:
//    · a linked mark, no entry        → the entry is written;
//    · the mark changed to another one → the entry takes the new reason and points;
//    · the mark cleared or unlinked    → the entry is removed.
//  An inactive reason gives nothing new, but an entry already written stays. The weekly/monthly limit
//  of a reason (DisciplinePeriod) is for hand-given points — a mark is one lesson, it always counts.
//  The parent is not messaged: these are routine lesson marks, not incidents.
// ===========================================================================

public static class HomeworkDisciplinePoints
{
    /// <summary>Who the automatic entries are "from" in every list.</summary>
    public const string Author = "Uy vazifasi (avtomatik)";

    /// <summary>The marks a reason can be linked to — the client's three.</summary>
    public static readonly IReadOnlyList<int> LinkableMarks = [HomeworkStatus.Partial, HomeworkStatus.NotDone, HomeworkStatus.Late];

    public static bool IsLinkable(int? mark) => mark is null || LinkableMarks.Contains(mark.Value);

    /// <summary>The cell's key in <see cref="DisciplinePoint.SourceKey"/>.</summary>
    public static string Key(string ownerId, string subjectId, string date, int period, string studentId) =>
        $"hw:{ownerId}:{subjectId}:{date}:{period}:{studentId}";

    /// <summary>
    /// Brings the automatic entries of the given cells in line with their homework marks. Cells are
    /// (pupil, date, period) of one owner and subject. Saves.
    /// </summary>
    public static async Task SyncAsync(
        IAppDbContext db, string ownerId, string subjectId,
        IReadOnlyCollection<(string StudentId, string Date, int Period)> cells, CancellationToken ct = default)
    {
        if (cells.Count == 0) return;
        var reasons = await db.DisciplineReasons.AsNoTracking()
            .Where(r => r.HomeworkMark != null).ToListAsync(ct);
        var keys = cells.Select(c => Key(ownerId, subjectId, c.Date, c.Period, c.StudentId)).Distinct().ToList();
        var existing = await db.DisciplinePoints.Where(p => p.SourceKey != null && keys.Contains(p.SourceKey))
            .ToDictionaryAsync(p => p.SourceKey!, StringComparer.Ordinal, ct);
        // Nothing linked and nothing written before — the common case costs one query.
        if (reasons.Count == 0 && existing.Count == 0) return;

        var studentIds = cells.Select(c => c.StudentId).Distinct().ToList();
        var dates = cells.Select(c => c.Date).Distinct().ToList();
        var marks = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == ownerId && e.SubjectId == subjectId
                    && studentIds.Contains(e.StudentId) && dates.Contains(e.Date))
                .Select(e => new { e.StudentId, e.Date, e.Period, e.Homework, e.ReasonId })
                .ToListAsync(ct))
            .GroupBy(e => (e.StudentId, e.Date, e.Period))
            .ToDictionary(g => g.Key, g => (Homework: g.Max(e => e.Homework), ReasonIds: g.Select(e => e.ReasonId).ToList()));
        // «K» is lateness to the lesson (client, 2026-11-16). When the same cell also carries a late
        // attendance reason that has points of its own, that reason already took them — no second entry.
        var latePenalised = (await db.AbsenceReasons.AsNoTracking()
                .Where(r => r.IsLate && r.Points != 0).Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var subject = await db.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";

        var now = AppClock.Now.ToString("o");
        foreach (var c in cells.Distinct())
        {
            var key = Key(ownerId, subjectId, c.Date, c.Period, c.StudentId);
            var cell = marks.GetValueOrDefault((c.StudentId, c.Date, c.Period));
            var mark = cell.Homework;
            var reason = reasons.FirstOrDefault(r => r.HomeworkMark == mark && mark != HomeworkStatus.None);
            if (mark == HomeworkStatus.Late && cell.ReasonIds is { } ids && ids.Any(id => id is not null && latePenalised.Contains(id)))
                reason = null;
            existing.TryGetValue(key, out var point);

            if (reason is null)
            {
                if (point is not null) db.DisciplinePoints.Remove(point);
                continue;
            }
            if (point is not null)
            {
                if (point.ReasonId == reason.Id) continue;
                point.ReasonId = reason.Id;
                point.ReasonName = reason.Name;
                point.Points = reason.Points;
                point.Note = Note(subject, c.Date, c.Period, mark);
                point.UpdatedAt = now;
                point.UpdatedBy = Author;
                continue;
            }
            if (!reason.IsActive || reason.Points == 0) continue;
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = c.StudentId,
                ReasonId = reason.Id,
                ReasonName = reason.Name,
                Points = reason.Points,
                Note = Note(subject, c.Date, c.Period, mark),
                CreatedAt = now,
                CreatedBy = Author,
                SourceKey = key,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>"Matematika, 16.11, 3-dars: «−» Chala".</summary>
    private static string Note(string subject, string date, int period, int mark)
    {
        var day = date.Length >= 10 ? $"{date[8..10]}.{date[5..7]}" : date;
        var subjectPart = string.IsNullOrWhiteSpace(subject) ? "" : subject + ", ";
        return $"{subjectPart}{day}, {period}-dars: «{HomeworkStatus.Symbol(mark)}» {HomeworkStatus.Label(mark)}";
    }
}
