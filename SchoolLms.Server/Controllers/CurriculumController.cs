using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Ish reja (docs/modules/curriculum-monitoring.md §1) — o'quv bo'limi: yuklash (Excel + asl hujjat),
/// ko'rish, "o'tildi" belgilash, hisobot. Ruxsat — <c>curriculum</c>. O'qituvchi o'z rejalarini
/// <see cref="TeacherCurriculumController"/> orqali belgilaydi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("curriculum")]
[Route("api/admin/curriculum")]
public class CurriculumController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>
    /// Formalar uchun tanlovlar: fanlar, sinf darajalari, yo'nalish guruhlari va ustozlar (har biri
    /// jadvalda dars beradigan fan + daraja/yo'nalish juftliklari bilan).
    /// </summary>
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct = default) => Ok(new
    {
        subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new { id = s.Id, name = s.Name }).ToListAsync(ct),
        grades = await db.Classes.AsNoTracking().Where(c => !c.IsArchived).Select(c => c.Grade).Distinct().OrderBy(g => g).ToListAsync(ct),
        tracks = await db.StudyGroups.AsNoTracking().Where(g => g.IsTrack && !g.IsArchived).OrderBy(g => g.Name)
            .Select(g => new { id = g.Id, name = g.Name }).ToListAsync(ct),
        teachers = await CurriculumService.TeacherOptionsAsync(db, ct),
    });

    [HttpGet("plans")]
    public async Task<ActionResult<IEnumerable<CurriculumPlanListItemDto>>> Plans(CancellationToken ct = default) =>
        await CurriculumService.ListAsync(db, ct);

    [HttpGet("plans/{id:guid}")]
    public async Task<ActionResult<CurriculumPlanDetailDto>> Plan(Guid id, CancellationToken ct = default)
    {
        if (await CurriculumService.DetailAsync(db, id, null, ct) is not { } d) return NotFound();
        return d;
    }

    /// <summary>Rejaning dars jadvaliga mosligi: har sinf/guruh × chorak — rejadagi soat va jadvaldagi dars.</summary>
    [HttpGet("plans/{id:guid}/fit")]
    public async Task<ActionResult<IEnumerable<CurriculumFitDto>>> Fit(Guid id, CancellationToken ct = default)
    {
        if (await CurriculumService.FitAsync(db, id, null, ct) is not { } fit) return NotFound();
        return fit;
    }

    /// <summary>Namuna Excel — "Ish reja" va "Ko'rsatma" varaqlari.</summary>
    [HttpGet("template")]
    public IActionResult Template() => File(CurriculumService.Template(), XlsxMime, "ish-reja-namuna.xlsx");

    /// <summary>
    /// Excel'dan yuklash (multipart): <c>file</c> (.xlsx), <c>subjectId</c>, <c>grade</c> YOKI
    /// <c>groupId</c> (yo'nalish), ixtiyoriy <c>title</c>, <c>originalUrl</c>/<c>originalName</c>
    /// (avval <c>/api/admin/uploads</c> ga yuklangan asl hujjat), ixtiyoriy <c>teacherId</c> — ustozning
    /// o'z rejasi (faqat u jadvalda dars beradigan sinflar uchun). <c>dryRun=true</c> — faqat
    /// tekshiradi, mavzularni va jadvalga mosligini ko'rsatadi. Bir fan + daraja (yoki yo'nalish) +
    /// ustoz uchun reja bitta: qayta yuklash mavzularni tartib raqami bo'yicha yangilaydi va belgilarni saqlaydi.
    /// </summary>
    [HttpPost("plans/import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<CurriculumImportResultDto>> Import(
        IFormFile? file, [FromForm] string? subjectId, [FromForm] int? grade, [FromForm] Guid? groupId,
        [FromForm] string? title, [FromForm] string? originalUrl, [FromForm] string? originalName,
        [FromForm] string? teacherId, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Excel yoki Word fayl kerak" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Ish reja .xlsx (Excel) yoki .docx (Word) formatida bo'lsin. Namunani yuklab oling." });
        var subject = string.IsNullOrWhiteSpace(subjectId) ? null : await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subjectId, ct);
        if (subject is null) return BadRequest(new { message = "Fanni tanlang" });
        if ((grade is null) == (groupId is null)) return BadRequest(new { message = "Sinf darajasini yoki yo'nalishni tanlang" });
        if (grade is < 0 or > 11) return BadRequest(new { message = "Sinf darajasi 0–11" });
        StudyGroup? track = null;
        if (groupId is { } gid)
        {
            track = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid && g.IsTrack, ct);
            if (track is null) return BadRequest(new { message = "Yo'nalish guruhi topilmadi" });
        }
        if (originalUrl is { Length: > 0 } && !originalUrl.StartsWith("/uploads/", StringComparison.Ordinal))
            return BadRequest(new { message = "Asl hujjat manzili noto'g'ri" });
        teacherId = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId;
        Teacher? teacher = null;
        if (teacherId is not null)
        {
            teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
            if (teacher is null) return BadRequest(new { message = "Ustoz topilmadi" });
            if (!await CurriculumService.TeacherTeachesKeyAsync(db, teacher.Id, subject.Id, grade, groupId, ct))
                return BadRequest(new
                {
                    message = $"{teacher.FullName} dars jadvalida {(track is not null ? track.Name : $"{grade}-sinflar")}da {subject.Name} fanidan dars bermaydi",
                });
        }

        await using var stream = file.OpenReadStream();
        var (parsed, errors) = CurriculumService.Parse(stream, file.FileName);
        var (topics, dateErrors, warnings) = await CurriculumService.ResolveDatesAsync(db, parsed, ct);
        errors.AddRange(dateErrors);
        var preview = topics.Select(t => new CurriculumPreviewTopicDto(
            t.Order, t.Quarter, t.Week, t.Section, t.Title, t.Hours, t.PlannedOn?.ToString("yyyy-MM-dd"))).ToList();
        if (errors.Count > 0)
            return BadRequest(new CurriculumImportResultDto(null, topics.Count, topics.Sum(t => t.Hours), errors, preview, warnings));
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
                SubjectId = subject.Id, Grade = grade, GroupId = groupId, TeacherId = teacherId, CreatedBy = Uid, CreatedAt = now,
            };
            db.CurriculumPlans.Add(plan);
        }
        var defaultTitle = track is not null ? $"{track.Name} — {subject.Name}" : $"{grade}-sinf {subject.Name}";
        if (teacher is not null) defaultTitle += $" — {teacher.FullName}";
        plan.Title = string.IsNullOrWhiteSpace(title)
            ? defaultTitle[..Math.Min(defaultTitle.Length, 200)]
            : title.Trim()[..Math.Min(title.Trim().Length, 200)];
        if (originalUrl is { Length: > 0 })
        {
            plan.FileUrl = originalUrl;
            plan.FileName = string.IsNullOrWhiteSpace(originalName) ? null : originalName[..Math.Min(originalName.Length, 255)];
        }
        plan.UpdatedAt = now;
        await CurriculumService.ReplaceTopicsAsync(db, plan, topics, ct);
        audit.Record("curriculum_plan", plan.Id.ToString(), isNew ? "create" : "update",
            $"Ish reja {(isNew ? "yuklandi" : "qayta yuklandi")}: {plan.Title} ({topics.Count} mavzu)");
        await db.SaveChangesAsync(ct);
        return new CurriculumImportResultDto(plan.Id, topics.Count, topics.Sum(t => t.Hours), [], preview, warnings, fit);
    }

    /// <summary>Bitta mavzuning aniq reja sanasini qo'lda o'zgartirish (null — tozalash).</summary>
    [HttpPut("topics/{id:guid}/planned-on")]
    public async Task<IActionResult> SetTopicDate(Guid id, CurriculumTopicDateRequest req, CancellationToken ct = default)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic is null) return NotFound();
        if (await CurriculumService.SetTopicDateAsync(db, id, req.PlannedOn, ct) is { } error)
            return BadRequest(new { message = error });
        audit.Record("curriculum_plan", topic.PlanId.ToString(), "update",
            $"Ish reja: {topic.Order}-mavzu sanasi {(string.IsNullOrWhiteSpace(req.PlannedOn) ? "tozalandi" : $"→ {req.PlannedOn}")}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var plan = await db.CurriculumPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        db.CurriculumPlans.Remove(plan);
        audit.Record("curriculum_plan", plan.Id.ToString(), "delete", $"Ish reja o'chirildi: {plan.Title}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>"O'tildi" belgisini qo'yish yoki olib tashlash (istalgan sinf/guruh uchun).</summary>
    [HttpPut("progress")]
    public async Task<IActionResult> Mark(CurriculumMarkRequest req, CancellationToken ct = default) =>
        await CurriculumService.MarkAsync(db, req, Uid, ct) is { } error ? BadRequest(new { message = error }) : NoContent();

    [HttpGet("report")]
    public async Task<ActionResult<CurriculumReportDto>> Report(CancellationToken ct = default) =>
        await CurriculumService.ReportAsync(db, ct);
}

/// <summary>
/// O'qituvchining ish rejalari (veb panel va Telegram Mini App — bitta token): faqat o'zi dars
/// beradigan sinf/guruh × fan juftliklari. Belgi qo'yish shu juftlik bilan cheklangan.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/curriculum")]
public class TeacherCurriculumController(AppDbContext db) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private Task<Teacher?> MeAsync(CancellationToken ct) =>
        db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == Uid, ct);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherCurriculumDto>>> Mine(CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        return await CurriculumService.ForTeacherAsync(db, me.Id, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CurriculumPlanDetailDto>> Plan(Guid id, CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        if (await MyOwnersAsync(me, id, ct) is not { } mine) return NotFound();
        var detail = await CurriculumService.DetailAsync(db, id, mine, ct);
        if (detail is null || detail.Owners.Count == 0) return NotFound();
        return detail;
    }

    /// <summary>O'z rejasining jadvalga mosligi — faqat o'zi dars beradigan sinf/guruhlar.</summary>
    [HttpGet("{id:guid}/fit")]
    public async Task<ActionResult<IEnumerable<CurriculumFitDto>>> Fit(Guid id, CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        if (await MyOwnersAsync(me, id, ct) is not { } mine) return NotFound();
        if (await CurriculumService.FitAsync(db, id, mine, ct) is not { } fit) return NotFound();
        return fit;
    }

    /// <summary>
    /// Rejaning o'qituvchiga ko'rinadigan egalari (u shu fandan dars beradiganlari); reja yo'q yoki
    /// boshqa ustozning rejasi — null.
    /// </summary>
    private async Task<HashSet<(string, string)>?> MyOwnersAsync(Teacher me, Guid planId, CancellationToken ct)
    {
        var plan = await db.CurriculumPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null || (plan.TeacherId is not null && plan.TeacherId != me.Id)) return null;
        var pairs = await TeacherOwnerAccess.PairsAsync(db, me.Id, ct);
        return pairs.Where(p => p.SubjectId == plan.SubjectId).Select(p => (p.OwnerKind, p.OwnerId)).ToHashSet();
    }

    [HttpPut("progress")]
    public async Task<IActionResult> Mark(CurriculumMarkRequest req, CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TopicId, ct);
        if (topic is null) return NotFound();
        var plan = await db.CurriculumPlans.AsNoTracking().Where(p => p.Id == topic.PlanId)
            .Select(p => new { p.SubjectId, p.TeacherId }).FirstAsync(ct);
        if (plan.TeacherId is not null && plan.TeacherId != me.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bu boshqa ustozning ish rejasi" });
        if (!await TeacherOwnerAccess.TeachesAsync(db, me.Id, req.OwnerId, plan.SubjectId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bu sinf yoki guruhda shu fandan darsingiz yo'q" });
        return await CurriculumService.MarkAsync(db, req, Uid, ct) is { } error ? BadRequest(new { message = error }) : NoContent();
    }
}
