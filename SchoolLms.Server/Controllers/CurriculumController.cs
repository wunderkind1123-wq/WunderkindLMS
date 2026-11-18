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
        if (originalUrl is { Length: > 0 } && !originalUrl.StartsWith("/uploads/", StringComparison.Ordinal))
            return BadRequest(new { message = "Asl hujjat manzili noto'g'ri" });
        return await CurriculumImport.RunAsync(this, db, audit, Uid,
            new CurriculumImport.Args(file, subjectId, grade, groupId, title, teacherId, Strict: false),
            originalUrl is { Length: > 0 } ? () => Task.FromResult<(string Url, string? Name)?>((originalUrl, originalName)) : null,
            dryRun, ct);
    }

    /// <summary>Mavzuni qo'shish (<c>id</c> yo'q) — <see cref="CurriculumTopicSaveRequest.AfterOrder"/> dan keyin.</summary>
    [HttpPost("plans/{planId:guid}/topics")]
    public async Task<IActionResult> AddTopic(Guid planId, CurriculumTopicSaveRequest req, CancellationToken ct = default) =>
        await CurriculumImport.SaveTopicAsync(this, db, audit, planId, null, req, ct);

    [HttpPut("topics/{id:guid}")]
    public async Task<IActionResult> SaveTopic(Guid id, CurriculumTopicSaveRequest req, CancellationToken ct = default)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic is null) return NotFound();
        return await CurriculumImport.SaveTopicAsync(this, db, audit, topic.PlanId, id, req, ct);
    }

    [HttpDelete("topics/{id:guid}")]
    public async Task<IActionResult> DeleteTopic(Guid id, CancellationToken ct = default) =>
        await CurriculumImport.DeleteTopicAsync(this, db, audit, id, ct);

    /// <summary>"Dars bo'lmadi" — shu mavzudan boshlab hammasi bir darsga suriladi.</summary>
    [HttpPost("shift")]
    public async Task<ActionResult<CurriculumShiftResultDto>> Shift(CurriculumShiftRequest req, CancellationToken ct = default) =>
        await CurriculumImport.ShiftAsync(this, db, audit, req.FromTopicId, ct);

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
/// Ustoz o'z rejasini o'zi yuklaydi va tahrirlaydi (2026-11-18): fayl to'liq bo'lishi shart (chorak,
/// hafta, mavzu, sana); umumiy reja va boshqa ustozning rejasi — faqat o'qish.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/curriculum")]
public class TeacherCurriculumController(AppDbContext db, AuditService audit, IWebHostEnvironment env) : ControllerBase
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

    // ------------------------------------------------------------ o'z rejasini yuklash va tahrirlash

    /// <summary>Yuklash formasi: ustoz jadvalda dars beradigan (fan, daraja/yo'nalish) juftliklari.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<IEnumerable<TeacherCurriculumKeyDto>>> Options(CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        return await CurriculumService.TeacherKeysAsync(db, me.Id, ct);
    }

    [HttpGet("template")]
    public IActionResult Template() => File(CurriculumService.Template(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ish-reja-namuna.xlsx");

    /// <summary>
    /// O'z rejasini yuklash (multipart): <c>file</c>, <c>subjectId</c>, <c>grade</c> yoki <c>groupId</c>,
    /// ixtiyoriy <c>title</c>. Har mavzuda chorak, hafta, mavzu va sana shart. Yuklangan fayl asl hujjat
    /// sifatida saqlanadi. <c>dryRun=true</c> — faqat tekshiradi.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<CurriculumImportResultDto>> Import(
        IFormFile? file, [FromForm] string? subjectId, [FromForm] int? grade, [FromForm] Guid? groupId,
        [FromForm] string? title, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        return await CurriculumImport.RunAsync(this, db, audit, Uid,
            new CurriculumImport.Args(file, subjectId, grade, groupId, title, me.Id, Strict: true),
            () => SaveOriginalAsync(file!), dryRun, ct);
    }

    /// <summary>Ustoz yuklagan faylning o'zi — rejaning asl hujjati.</summary>
    private async Task<(string Url, string? Name)?> SaveOriginalAsync(IFormFile file)
    {
        if (UploadGuard.Validate(file) is not null) return null;
        var dir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var stored = UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);
        return ($"/uploads/{stored}", file.FileName);
    }

    [HttpPost("{planId:guid}/topics")]
    public async Task<IActionResult> AddTopic(Guid planId, CurriculumTopicSaveRequest req, CancellationToken ct = default) =>
        await OwnPlanAsync(planId, ct) is { } denied ? denied
            : await CurriculumImport.SaveTopicAsync(this, db, audit, planId, null, req, ct);

    [HttpPut("topics/{id:guid}")]
    public async Task<IActionResult> SaveTopic(Guid id, CurriculumTopicSaveRequest req, CancellationToken ct = default)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic is null) return NotFound();
        return await OwnPlanAsync(topic.PlanId, ct) is { } denied ? denied
            : await CurriculumImport.SaveTopicAsync(this, db, audit, topic.PlanId, id, req, ct);
    }

    [HttpDelete("topics/{id:guid}")]
    public async Task<IActionResult> DeleteTopic(Guid id, CancellationToken ct = default)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic is null) return NotFound();
        return await OwnPlanAsync(topic.PlanId, ct) is { } denied ? denied
            : await CurriculumImport.DeleteTopicAsync(this, db, audit, id, ct);
    }

    /// <summary>"Dars bo'lmadi" — shu mavzudan boshlab hammasi bir darsga suriladi.</summary>
    [HttpPost("shift")]
    public async Task<ActionResult<CurriculumShiftResultDto>> Shift(CurriculumShiftRequest req, CancellationToken ct = default)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.FromTopicId, ct);
        if (topic is null) return NotFound();
        if (await OwnPlanAsync(topic.PlanId, ct) is { } denied) return denied;
        return await CurriculumImport.ShiftAsync(this, db, audit, req.FromTopicId, ct);
    }

    /// <summary>Tahrir faqat ustozning o'z rejasida; null — ruxsat bor.</summary>
    private async Task<ActionResult?> OwnPlanAsync(Guid planId, CancellationToken ct)
    {
        if (await MeAsync(ct) is not { } me) return Forbid();
        var owner = await db.CurriculumPlans.AsNoTracking().Where(p => p.Id == planId)
            .Select(p => new { p.TeacherId }).FirstOrDefaultAsync(ct);
        if (owner is null) return NotFound();
        return owner.TeacherId == me.Id ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Faqat o'z ish rejangizni tahrirlay olasiz — umumiy rejani o'quv bo'limi o'zgartiradi" });
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
