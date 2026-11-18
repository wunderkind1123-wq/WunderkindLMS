using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quv bo'limi (<see cref="CurriculumController"/>) va ustoz (<see cref="TeacherCurriculumController"/>)
/// uchun umumiy: ish reja yuklash va mavzularni tahrirlash. Ruxsat chaqiruvchi controllerda tekshiriladi.
/// </summary>
internal static class CurriculumImport
{
    /// <param name="TeacherId">Ustozning o'z rejasi; null — umumiy reja.</param>
    /// <param name="Strict">Har mavzuda chorak, hafta, mavzu va sana shart (ustoz yuklaganda).</param>
    public sealed record Args(
        IFormFile? File, string? SubjectId, int? Grade, Guid? GroupId, string? Title, string? TeacherId, bool Strict);

    /// <param name="saveOriginal">Saqlashda (dry run emas) asl hujjatni beradi; null — asl hujjat o'zgarmaydi.</param>
    public static async Task<ActionResult<CurriculumImportResultDto>> RunAsync(
        ControllerBase c, AppDbContext db, AuditService audit, string uid, Args a,
        Func<Task<(string Url, string? Name)?>>? saveOriginal, bool dryRun, CancellationToken ct)
    {
        var file = a.File;
        if (file is null || file.Length == 0) return c.BadRequest(new { message = "Excel yoki Word fayl kerak" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return c.BadRequest(new { message = "Ish reja .xlsx (Excel) yoki .docx (Word) formatida bo'lsin. Namunani yuklab oling." });
        var subject = string.IsNullOrWhiteSpace(a.SubjectId) ? null : await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == a.SubjectId, ct);
        if (subject is null) return c.BadRequest(new { message = "Fanni tanlang" });
        var (grade, groupId) = (a.Grade, a.GroupId);
        if ((grade is null) == (groupId is null)) return c.BadRequest(new { message = "Sinf darajasini yoki yo'nalishni tanlang" });
        if (grade is < 0 or > 11) return c.BadRequest(new { message = "Sinf darajasi 0–11" });
        StudyGroup? track = null;
        if (groupId is { } gid)
        {
            track = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid && g.IsTrack, ct);
            if (track is null) return c.BadRequest(new { message = "Yo'nalish guruhi topilmadi" });
        }
        var teacherId = string.IsNullOrWhiteSpace(a.TeacherId) ? null : a.TeacherId;
        Teacher? teacher = null;
        if (teacherId is not null)
        {
            teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
            if (teacher is null) return c.BadRequest(new { message = "Ustoz topilmadi" });
            if (!await CurriculumService.TeacherTeachesKeyAsync(db, teacher.Id, subject.Id, grade, groupId, ct))
                return c.BadRequest(new
                {
                    message = $"{teacher.FullName} dars jadvalida {(track is not null ? track.Name : $"{grade}-sinflar")}da {subject.Name} fanidan dars bermaydi",
                });
        }

        await using var stream = file.OpenReadStream();
        var (parsed, errors) = CurriculumService.Parse(stream, file.FileName);
        var (topics, dateErrors, warnings) = await CurriculumService.ResolveDatesAsync(db, parsed, ct);
        errors.AddRange(dateErrors);
        if (a.Strict && errors.Count == 0) errors.AddRange(CurriculumService.CompletenessErrors(parsed, topics));
        var preview = topics.Select(t => new CurriculumPreviewTopicDto(
            t.Order, t.Quarter, t.Week, t.Section, t.Title, t.Hours, t.PlannedOn?.ToString("yyyy-MM-dd"))).ToList();
        if (errors.Count > 0)
            return c.BadRequest(new CurriculumImportResultDto(null, topics.Count, topics.Sum(t => t.Hours), errors, preview, warnings));
        var fit = await CurriculumService.PreviewFitAsync(db, subject.Id, grade, groupId, teacherId, topics, ct);
        if (dryRun)
            return new CurriculumImportResultDto(null, topics.Count, topics.Sum(t => t.Hours), [], preview, warnings, fit);

        var plan = await db.CurriculumPlans.FirstOrDefaultAsync(p => p.SubjectId == subject.Id
            && (grade != null ? p.Grade == grade : p.GroupId == groupId) && p.TeacherId == teacherId, ct);
        var now = DateTimeOffset.UtcNow;
        var isNew = plan is null;
        if (plan is null)
        {
            plan = new CurriculumPlan
            {
                SubjectId = subject.Id, Grade = grade, GroupId = groupId, TeacherId = teacherId, CreatedBy = uid, CreatedAt = now,
            };
            db.CurriculumPlans.Add(plan);
        }
        var defaultTitle = track is not null ? $"{track.Name} — {subject.Name}" : $"{grade}-sinf {subject.Name}";
        if (teacher is not null) defaultTitle += $" — {teacher.FullName}";
        var title = a.Title?.Trim();
        plan.Title = string.IsNullOrWhiteSpace(title)
            ? defaultTitle[..Math.Min(defaultTitle.Length, 200)]
            : title[..Math.Min(title.Length, 200)];
        if (saveOriginal is not null && await saveOriginal() is { } original)
        {
            plan.FileUrl = original.Url;
            plan.FileName = string.IsNullOrWhiteSpace(original.Name) ? null : original.Name[..Math.Min(original.Name.Length, 255)];
        }
        plan.UpdatedAt = now;
        await CurriculumService.ReplaceTopicsAsync(db, plan, topics, ct);
        audit.Record("curriculum_plan", plan.Id.ToString(), isNew ? "create" : "update",
            $"Ish reja {(isNew ? "yuklandi" : "qayta yuklandi")}{(a.Strict ? " (ustozning o'zi)" : "")}: {plan.Title} ({topics.Count} mavzu)");
        await db.SaveChangesAsync(ct);
        return new CurriculumImportResultDto(plan.Id, topics.Count, topics.Sum(t => t.Hours), [], preview, warnings, fit);
    }

    public static async Task<IActionResult> SaveTopicAsync(
        ControllerBase c, AppDbContext db, AuditService audit, Guid planId, Guid? topicId, CurriculumTopicSaveRequest req, CancellationToken ct)
    {
        if (await CurriculumService.SaveTopicAsync(db, planId, topicId, req, ct) is { } error)
            return c.BadRequest(new { message = error });
        audit.Record("curriculum_plan", planId.ToString(), "update",
            $"Ish reja: mavzu {(topicId is null ? "qo'shildi" : "o'zgartirildi")} — {req.Title.Trim()} ({req.PlannedOn})");
        await db.SaveChangesAsync(ct);
        return c.NoContent();
    }

    public static async Task<IActionResult> DeleteTopicAsync(
        ControllerBase c, AppDbContext db, AuditService audit, Guid topicId, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null) return c.NotFound();
        if (await CurriculumService.DeleteTopicAsync(db, topicId, ct) is { } error)
            return c.BadRequest(new { message = error });
        audit.Record("curriculum_plan", topic.PlanId.ToString(), "update", $"Ish reja: {topic.Order}-mavzu o'chirildi — {topic.Title}");
        await db.SaveChangesAsync(ct);
        return c.NoContent();
    }

    public static async Task<ActionResult<CurriculumShiftResultDto>> ShiftAsync(
        ControllerBase c, AppDbContext db, AuditService audit, Guid fromTopicId, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == fromTopicId, ct);
        if (topic is null) return c.NotFound();
        var (result, error) = await CurriculumService.ShiftAsync(db, fromTopicId, ct);
        if (error is not null) return c.BadRequest(new { message = error });
        audit.Record("curriculum_plan", topic.PlanId.ToString(), "update",
            $"Ish reja: dars bo'lmadi — {topic.Order}-mavzudan {result!.Moved} ta mavzu surildi (oxirgisi {result.LastDate})");
        await db.SaveChangesAsync(ct);
        return result;
    }
}
