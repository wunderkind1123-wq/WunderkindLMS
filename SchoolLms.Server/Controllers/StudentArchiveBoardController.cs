using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Arxiv doskasi (mijoz, 2026-11-13): "arxivga kirganda ularni sabablarga ko'ra
//  ajratib olishimiz uchun ... lidlarga o'xshab ustun". docs/modules/archive-reason-groups.md.
//
//    GET  /api/admin/students/archive-board            — ustunlar = sabab guruhlari
//    PUT  /api/admin/students/{id}/archive-reason      — ketish sababini o'zgartirish (sudrab o'tkazish)
//    POST /api/admin/students/{id}/to-lead             — arxivdagi o'quvchini lidlarga qaytarish
//
//  `students` kaliti, o'qish gated (students yoki students:view) — o'quvchilar ro'yxati kabi.
//  Lidlarga qaytarish xodimdan qo'shimcha `leads` kalitini ham talab qiladi (lid yaratiladi).
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm("students", GatedRead = true)]
[Route("api/admin/students")]
public class StudentArchiveBoardController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Tizim ustuni — sababi yo'q yoki guruhsiz sabab bilan arxivlanganlar.</summary>
    public const string UnassignedColumn = "Sababi aniqlanmagan";

    public const string NotArchivedMessage = "O'quvchi arxivda emas";
    public const string LeadExistsMessage = "Bu o'quvchi uchun lid allaqachon ochilgan";
    public const string NoStagesMessage = "Lidlar doskasida birorta ustun yo'q — avval ustun qo'shing";

    [HttpGet("archive-board")]
    public async Task<ActionResult<ArchiveBoardDto>> Board(CancellationToken ct = default)
    {
        var students = await db.Students.AsNoTracking()
            .Where(s => s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ArchivedAt, s.ArchiveReasonId, s.ArchiveReason, s.ParentPhone })
            .ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();

        var reasons = await db.StudentArchiveReasons.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => new { r.Name, r.GroupId }, ct);
        var groups = await db.StudentArchiveReasonGroups.AsNoTracking()
            .OrderBy(g => g.Position).ThenBy(g => g.Name)
            .ToListAsync(ct);

        // Arxivlash sinfni bo'shatadi — sinf ketish yozuvidan (eng oxirgisi) olinadi.
        var departures = await db.StudentEvents.AsNoTracking()
            .Where(e => e.Kind == StudentEventKind.Left && ids.Contains(e.StudentId))
            .Select(e => new { e.StudentId, e.ClassName, e.OnDate, e.CreatedAt })
            .ToListAsync(ct);
        var classAtLeave = departures
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.OnDate).ThenByDescending(e => e.CreatedAt).First().ClassName);

        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.FromStudentId != null && ids.Contains(l.FromStudentId))
            .Select(l => new { l.Id, l.FromStudentId })
            .ToListAsync(ct);
        var leadOf = leads.GroupBy(l => l.FromStudentId!).ToDictionary(g => g.Key, g => g.First().Id);

        var cards = students
            .Select(s =>
            {
                var reason = s.ArchiveReasonId is { } rid ? reasons.GetValueOrDefault(rid) : null;
                return new
                {
                    GroupId = reason?.GroupId,
                    Card = new ArchiveBoardStudentDto(
                        s.Id, s.FullName, classAtLeave.GetValueOrDefault(s.Id), s.ArchivedAt,
                        s.ArchiveReasonId, reason?.Name, s.ArchiveReason,
                        string.IsNullOrWhiteSpace(s.ParentPhone) ? null : s.ParentPhone,
                        leadOf.GetValueOrDefault(s.Id)),
                };
            })
            .ToList();

        static List<ArchiveBoardStudentDto> Sorted(IEnumerable<ArchiveBoardStudentDto> xs) =>
            [.. xs.OrderByDescending(c => c.ArchivedAt).ThenBy(c => c.FullName, StringComparer.OrdinalIgnoreCase)];

        var known = groups.Select(g => g.Id).ToHashSet();
        var columns = new List<ArchiveBoardColumnDto>
        {
            new(null, UnassignedColumn, "slate",
                Sorted(cards.Where(c => c.GroupId is null || !known.Contains(c.GroupId.Value)).Select(c => c.Card))),
        };
        foreach (var g in groups)
        {
            var inGroup = Sorted(cards.Where(c => c.GroupId == g.Id).Select(c => c.Card));
            // Faolsiz guruh doskada faqat ichida o'quvchi bo'lsa turadi — ular yo'qolib qolmasin.
            if (!g.IsActive && inGroup.Count == 0) continue;
            columns.Add(new ArchiveBoardColumnDto(g.Id, g.IsActive ? g.Name : g.Name + " (faol emas)", g.Color, inGroup));
        }

        return new ArchiveBoardDto(columns, students.Count);
    }

    /// <summary>
    /// Arxivdagi o'quvchining ketish sababini o'zgartirish — doskada kartochka boshqa ustunga
    /// sudralganda yoki kartochkadan. O'quvchi kartochkasi VA uning oxirgi ketish yozuvi
    /// (student_events) birga o'zgaradi: arxiv va "O'quvchilar oqimi" hisobotida bir xil sabab
    /// ko'rinsin. Audit yoziladi.
    /// </summary>
    [HttpPut("{id}/archive-reason")]
    public async Task<ActionResult<ArchiveBoardStudentDto>> SetReason(
        string id, SetArchiveReasonRequest req, CancellationToken ct = default)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return NotFound();
        if (!student.IsArchived) return BadRequest(new { message = NotArchivedMessage });

        var reason = await db.StudentArchiveReasons.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == req.ReasonId && r.IsActive, ct);
        if (reason is null) return BadRequest(new { message = StudentArchiveService.ReasonNotFoundMessage });

        var names = await db.StudentArchiveReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        string NameOf(Guid? rid) => rid is { } r ? names.GetValueOrDefault(r, StudentFlowReport.OtherReason) : StudentFlowReport.OtherReason;

        var before = new Dictionary<string, object?> { ["Sabab"] = NameOf(student.ArchiveReasonId), ["Izoh"] = student.ArchiveReason };

        student.ArchiveReasonId = reason.Id;
        var note = req.Note?.Trim();
        if (!string.IsNullOrEmpty(note)) student.ArchiveReason = note.Length > 500 ? note[..500] : note;

        var latest = await db.StudentEvents
            .Where(e => e.StudentId == id && e.Kind == StudentEventKind.Left)
            .OrderByDescending(e => e.OnDate).ThenByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (latest is not null)
        {
            latest.ReasonId = reason.Id;
            if (!string.IsNullOrEmpty(note)) latest.ReasonText = student.ArchiveReason;
        }

        var after = new Dictionary<string, object?> { ["Sabab"] = reason.Name, ["Izoh"] = student.ArchiveReason };
        audit.Record(AuditService.EntityStudent, student.Id, "archive-reason",
            $"Ketish sababi o'zgardi ({student.FullName}): {before["Sabab"]} → {reason.Name}",
            before: before, after: after, studentId: student.Id);
        await db.SaveChangesAsync(ct);

        var leadId = await db.Leads.AsNoTracking().Where(l => l.FromStudentId == id).Select(l => l.Id).FirstOrDefaultAsync(ct);
        return new ArchiveBoardStudentDto(
            student.Id, student.FullName, latest?.ClassName, student.ArchivedAt, reason.Id, reason.Name,
            student.ArchiveReason, string.IsNullOrWhiteSpace(student.ParentPhone) ? null : student.ParentPhone, leadId);
    }

    /// <summary>
    /// Arxivdagi o'quvchini lidlarga qaytarish: o'quvchi ma'lumotlari bilan lid yaratiladi
    /// (manba <c>archive</c>, <see cref="Lead.FromStudentId"/>), izohda ketish sababi. O'quvchi
    /// ARXIVDA QOLADI — u qaytishga rozi bo'lsa, arxivdan qaytariladi va lid o'zi yopiladi
    /// (StudentsController.Restore). Bitta o'quvchiga bitta ochiq lid.
    /// </summary>
    [HttpPost("{id}/to-lead")]
    public async Task<ActionResult<Lead>> ToLead(string id, ArchiveToLeadRequest? req, CancellationToken ct = default)
    {
        if (User.IsInRole(Roles.Staff) && !User.HasClaim(AdminPermAttribute.ClaimType, "leads"))
            return Forbid();

        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return NotFound();
        if (!student.IsArchived) return BadRequest(new { message = NotArchivedMessage });

        var existing = await db.Leads.AsNoTracking().Where(l => l.FromStudentId == id).Select(l => l.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return Conflict(new { message = LeadExistsMessage, leadId = existing });

        var stages = await db.LeadStages.AsNoTracking().OrderBy(s => s.Order).Select(s => s.Id).ToListAsync(ct);
        if (stages.Count == 0) return BadRequest(new { message = NoStagesMessage });
        var stage = req?.Stage is { } wanted && stages.Contains(wanted) ? wanted : stages[0];

        var reasonName = student.ArchiveReasonId is { } rid
            ? await db.StudentArchiveReasons.AsNoTracking().Where(r => r.Id == rid).Select(r => r.Name).FirstOrDefaultAsync(ct)
            : null;
        var classAtLeave = await db.StudentEvents.AsNoTracking()
            .Where(e => e.StudentId == id && e.Kind == StudentEventKind.Left)
            .OrderByDescending(e => e.OnDate).ThenByDescending(e => e.CreatedAt)
            .Select(e => e.ClassName).FirstOrDefaultAsync(ct);

        var lines = new List<string>
        {
            $"Arxivdan qaytarildi. Ketgan: {student.ArchivedAt ?? "—"}"
                + (string.IsNullOrWhiteSpace(classAtLeave) ? "" : $", {classAtLeave}"),
            $"Ketish sababi: {reasonName ?? StudentFlowReport.OtherReason}"
                + (string.IsNullOrWhiteSpace(student.ArchiveReason) ? "" : $" — {student.ArchiveReason}"),
        };
        if (!string.IsNullOrWhiteSpace(req?.Note)) lines.Add(req.Note.Trim());

        var lead = new Lead
        {
            FullName = student.FullName,
            Gender = student.Gender,
            BirthDate = student.BirthDate,
            ParentFullName = student.ParentFullName,
            ParentPhone = student.ParentPhone,
            TargetGrade = student.TargetGrade ?? 0,
            Note = string.Join("\n", lines),
            Stage = stage,
            Source = LeadSource.Archive,
            FromStudentId = student.Id,
        };
        db.Leads.Add(lead);
        audit.Record(AuditService.EntityLead, lead.Id, "from-archive",
            $"Arxivdagi o'quvchi lidlarga qaytarildi ({student.FullName})", studentId: student.Id);
        await db.SaveChangesAsync(ct);
        return lead;
    }
}
