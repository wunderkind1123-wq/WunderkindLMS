using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Ichki monitoring (docs/modules/curriculum-monitoring.md §2) — o'quv bo'limi: savollar banki,
/// testlar, qog'oz javoblarini kiritish, onlayn kodlar, natija va mavzu tahlili. Ruxsat —
/// <c>monitoring</c>. Natija faqat shu yerda: o'qituvchi va ota-onaga chiqmaydi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("monitoring", GatedRead = true)]
[Route("api/admin/monitoring")]
public class MonitoringController(AppDbContext db, AuditService audit, IWebHostEnvironment env) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NoContent() : Ok(result);
        }
        catch (MonitoringException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message });
        }
    }

    // ---------------------------------------------------------------- banks

    /// <summary>Formalar uchun tanlovlar: fanlar, sinf darajalari va yo'nalish guruhlari.</summary>
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct = default) => Ok(new
    {
        subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new { id = s.Id, name = s.Name }).ToListAsync(ct),
        grades = await db.Classes.AsNoTracking().Where(c => !c.IsArchived).Select(c => c.Grade).Distinct().OrderBy(g => g).ToListAsync(ct),
        tracks = await db.StudyGroups.AsNoTracking().Where(g => g.IsTrack && !g.IsArchived).OrderBy(g => g.Name)
            .Select(g => new { id = g.Id, name = g.Name }).ToListAsync(ct),
    });

    [HttpGet("banks")]
    public Task<IActionResult> Banks(CancellationToken ct = default) =>
        Run(async () => await MonitoringService.BanksAsync(db, ct));

    [HttpPost("banks")]
    public Task<IActionResult> CreateBank(MonitoringBankPayload p, CancellationToken ct = default) =>
        Run(async () => new { id = (await MonitoringService.SaveBankAsync(db, null, p, Uid, ct)).Id });

    [HttpPut("banks/{id:guid}")]
    public Task<IActionResult> UpdateBank(Guid id, MonitoringBankPayload p, CancellationToken ct = default) =>
        Run(async () => new { id = (await MonitoringService.SaveBankAsync(db, id, p, Uid, ct)).Id });

    [HttpDelete("banks/{id:guid}")]
    public Task<IActionResult> DeleteBank(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        var bank = await db.MonitoringBanks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new MonitoringException("Bank topilmadi", 404);
        db.MonitoringBanks.Remove(bank);
        audit.Record("monitoring_bank", id.ToString(), "delete", $"Monitoring banki o'chirildi: {bank.Title}");
        await db.SaveChangesAsync(ct);
        return null;
    });

    [HttpGet("banks/{id:guid}")]
    public Task<IActionResult> Bank(Guid id, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.BankDetailAsync(db, id, ct));

    [HttpPost("banks/{id:guid}/questions")]
    public Task<IActionResult> AddQuestion(Guid id, MonitoringQuestionPayload p, CancellationToken ct = default) =>
        Run(async () => new { id = await MonitoringService.SaveQuestionAsync(db, id, null, p, ct) });

    [HttpPut("banks/{id:guid}/questions/{questionId:guid}")]
    public Task<IActionResult> UpdateQuestion(Guid id, Guid questionId, MonitoringQuestionPayload p, CancellationToken ct = default) =>
        Run(async () => new { id = await MonitoringService.SaveQuestionAsync(db, id, questionId, p, ct) });

    [HttpDelete("banks/{id:guid}/questions/{questionId:guid}")]
    public Task<IActionResult> DeleteQuestion(Guid id, Guid questionId, CancellationToken ct = default) => Run(async () =>
    {
        var q = await db.MonitoringQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.BankId == id, ct)
            ?? throw new MonitoringException("Savol topilmadi", 404);
        db.MonitoringQuestions.Remove(q);
        await db.SaveChangesAsync(ct);
        return null;
    });

    [HttpGet("question-template")]
    public IActionResult QuestionTemplate() => File(MonitoringService.QuestionTemplate(), XlsxMime, "monitoring-savollar-namuna.xlsx");

    /// <summary>Ustozlar test tuzadigan Word namuna — ko'rsatma va namuna savollar.</summary>
    [HttpGet("word-template")]
    public IActionResult WordTemplate() => File(MonitoringWordImport.Template(),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", MonitoringWordImport.TemplateFileName);

    [HttpPost("banks/{id:guid}/import")]
    [RequestSizeLimit(20_000_000)]
    public Task<IActionResult> Import(Guid id, IFormFile? file, CancellationToken ct = default) => Run(async () =>
    {
        if (file is null || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new MonitoringException("Savollarni .xlsx (Excel) faylda yuklang");
        await using var stream = file.OpenReadStream();
        var result = await MonitoringService.ImportQuestionsAsync(db, id, stream, ct);
        if (result.Errors.Count > 0) throw new MonitoringException(string.Join("\n", result.Errors.Take(30)));
        return result;
    });

    /// <summary>Word (.docx) / .txt fayl (multipart <c>file</c>) yoki <c>text</c> — oldindan ko'rish, saqlamaydi.</summary>
    [HttpPost("banks/{id:guid}/import/preview")]
    [RequestSizeLimit(AssignmentQuestionImport.MaxRequestBytes)]
    public Task<IActionResult> ImportPreview(Guid id, IFormFile? file, [FromForm] string? text, CancellationToken ct = default) => Run(async () =>
    {
        if (!await db.MonitoringBanks.AnyAsync(b => b.Id == id, ct)) throw new MonitoringException("Bank topilmadi", 404);
        return await MonitoringImportPreview.RunAsync(env, file, text);
    });

    /// <summary>Ko'rib chiqilgan savollarni bankka yozadi.</summary>
    [HttpPost("banks/{id:guid}/images")]
    [RequestSizeLimit(6_000_000)]
    public Task<IActionResult> UploadImage(Guid id, IFormFile? file, CancellationToken ct = default) => Run(async () =>
    {
        if (!await db.MonitoringBanks.AnyAsync(b => b.Id == id, ct)) throw new MonitoringException("Bank topilmadi", 404);
        return await MonitoringImportPreview.SaveImageAsync(env, file);
    });

    [HttpPost("banks/{id:guid}/questions/bulk")]
    public Task<IActionResult> AddMany(Guid id, List<MonitoringQuestionPayload> rows, CancellationToken ct = default) =>
        Run(async () => new { added = await MonitoringService.AddQuestionsAsync(db, id, rows, ct) });

    // ---------------------------------------------------------------- tests

    [HttpGet("tests")]
    public Task<IActionResult> Tests(CancellationToken ct = default) =>
        Run(async () => await MonitoringService.TestsAsync(db, ct));

    [HttpPost("tests")]
    public Task<IActionResult> CreateTest(MonitoringTestPayload p, CancellationToken ct = default) => Run(async () =>
    {
        var test = await MonitoringService.CreateTestAsync(db, p, Uid, ct);
        audit.Record("monitoring_test", test.Id.ToString(), "create", $"Monitoring testi yaratildi: {test.Title}");
        await db.SaveChangesAsync(ct);
        return new { id = test.Id };
    });

    [HttpGet("tests/{id:guid}")]
    public Task<IActionResult> Test(Guid id, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.DetailAsync(db, id, ct));

    [HttpPut("tests/{id:guid}/items")]
    public Task<IActionResult> Items(Guid id, List<MonitoringItemKeyPayload> rows, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.UpdateItemsAsync(db, id, rows, ct); return null; });

    /// <summary><c>published</c> — onlayn testda shaxsiy kodlar shu paytda beriladi; <c>closed</c> — yakunlash.</summary>
    [HttpPost("tests/{id:guid}/status/{status}")]
    public Task<IActionResult> Status(Guid id, string status, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.SetStatusAsync(db, id, status, ct); return null; });

    [HttpPut("tests/{id:guid}/answers")]
    public Task<IActionResult> Answers(Guid id, List<MonitoringAnswersPayload> rows, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.SaveAnswersAsync(db, id, rows, ct); return null; });

    /// <summary>Boshqa qurilmada davom ettirishga ruxsat (kompyuter almashsa).</summary>
    [HttpPost("participants/{id:guid}/reset-device")]
    public Task<IActionResult> ResetDevice(Guid id, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.ResetDeviceAsync(db, id, ct); return null; });

    // ---------------------------------------------------------------- sittings (bir o'tirishda bir nechta fan)

    [HttpGet("sittings")]
    public Task<IActionResult> Sittings(CancellationToken ct = default) =>
        Run(async () => await MonitoringSittingService.ListAsync(db, ct));

    [HttpPost("sittings")]
    public Task<IActionResult> CreateSitting(MonitoringSittingPayload p, CancellationToken ct = default) => Run(async () =>
    {
        var s = await MonitoringSittingService.CreateAsync(db, p, Uid, ct);
        audit.Record("monitoring_sitting", s.Id.ToString(), "create", $"Test sessiyasi yaratildi: {s.Title} ({s.TestIds.Count} fan)");
        await db.SaveChangesAsync(ct);
        return new { id = s.Id };
    });

    [HttpGet("sittings/{id:guid}")]
    public Task<IActionResult> Sitting(Guid id, CancellationToken ct = default) =>
        Run(async () => await MonitoringSittingService.DetailAsync(db, id, ct));

    [HttpDelete("sittings/{id:guid}")]
    public Task<IActionResult> DeleteSitting(Guid id, CancellationToken ct = default) =>
        Run(async () => { await MonitoringSittingService.DeleteAsync(db, id, ct); return null; });

    /// <summary>Sessiyani bitta formada: har fan uchun bankdan test, keyin sessiya (bitta tranzaksiya).</summary>
    [HttpPost("sittings/quick")]
    public Task<IActionResult> QuickSitting(MonitoringQuickSittingPayload p, CancellationToken ct = default) => Run(async () =>
    {
        var s = await MonitoringSittingService.QuickCreateAsync(db, p, Uid, ct);
        audit.Record("monitoring_sitting", s.Id.ToString(), "create", $"Test sessiyasi tez yaratildi: {s.Title} ({s.TestIds.Count} fan)");
        await db.SaveChangesAsync(ct);
        return new { id = s.Id };
    });

    /// <summary>Jonli kuzatuv — test paytida har necha soniyada so'raladi.</summary>
    [HttpGet("sittings/{id:guid}/live")]
    public Task<IActionResult> SittingLive(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        await MonitoringService.FinishExpiredAsync(db, null, ct);
        return await MonitoringSittingService.SittingLiveAsync(db, id, ct);
    });

    [HttpGet("tests/{id:guid}/live")]
    public Task<IActionResult> TestLive(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        await MonitoringService.FinishExpiredAsync(db, id, ct);
        return await MonitoringSittingService.TestLiveAsync(db, id, ct);
    });

    /// <summary>Tayyorlik tekshiruvi — o'quvchilar kirishidan oldin ma'lum muammolar ro'yxati.</summary>
    [HttpGet("sittings/{id:guid}/readiness")]
    public Task<IActionResult> SittingReadiness(Guid id, CancellationToken ct = default) =>
        Run(async () => await MonitoringSittingService.SittingReadinessAsync(db, id, UploadExists, ct));

    [HttpGet("tests/{id:guid}/readiness")]
    public Task<IActionResult> TestReadiness(Guid id, CancellationToken ct = default) =>
        Run(async () => await MonitoringSittingService.TestReadinessAsync(db, id, UploadExists, ct));

    private bool UploadExists(string name) =>
        name.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')
        && System.IO.File.Exists(System.IO.Path.Combine(env.ContentRootPath, "uploads", name));

    [HttpPost("sittings/{id:guid}/pin")]
    public Task<IActionResult> RotateSittingPin(Guid id, CancellationToken ct = default) =>
        Run(async () => new { pin = await MonitoringSittingService.RotatePinAsync(db, id, ct) });

    [HttpPost("tests/{id:guid}/pin")]
    public Task<IActionResult> RotateTestPin(Guid id, CancellationToken ct = default) =>
        Run(async () => new { pin = await MonitoringService.RotateTestPinAsync(db, id, ct) });

    // ---------------------------------------------------------------- doimiy shaxsiy kodlar (§2.9)

    [HttpGet("student-codes")]
    public Task<IActionResult> StudentCodes([FromQuery] string? className, CancellationToken ct = default) =>
        Run(async () => await MonitoringStudentCodeService.ListAsync(db, className, ct));

    /// <summary>Kodi yo'q barcha faol o'quvchilarga kod (bor kodlar o'zgarmaydi).</summary>
    [HttpPost("student-codes/generate")]
    public Task<IActionResult> GenerateStudentCodes(CancellationToken ct = default) => Run(async () =>
    {
        var added = await MonitoringStudentCodeService.GenerateMissingAsync(db, ct);
        audit.Record("monitoring_student_code", "all", "create", $"Doimiy test kodlari berildi: {added} o'quvchi");
        await db.SaveChangesAsync(ct);
        return new { added };
    });

    /// <summary>Kod yo'qolsa — yangisi; eskisi darhol ishlamay qoladi.</summary>
    [HttpPost("student-codes/{studentId}/regenerate")]
    public Task<IActionResult> RegenerateStudentCode(string studentId, CancellationToken ct = default) => Run(async () =>
    {
        var code = await MonitoringStudentCodeService.RegenerateAsync(db, studentId, ct);
        audit.Record("monitoring_student_code", studentId, "update", "Doimiy test kodi almashtirildi");
        await db.SaveChangesAsync(ct);
        return new { code };
    });

    [HttpPost("sitting-codes/{id:guid}/reset-device")]
    public Task<IActionResult> ResetSittingDevice(Guid id, CancellationToken ct = default) =>
        Run(async () => { await MonitoringSittingService.ResetDeviceAsync(db, id, ct); return null; });

    /// <summary>Oynadan chiqish chegarasi bilan to'xtatilgan testni davom ettirish.</summary>
    [HttpPost("participants/{id:guid}/reopen")]
    public Task<IActionResult> Reopen(Guid id, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.ReopenAsync(db, id, ct); return null; });

    [HttpDelete("tests/{id:guid}")]
    public Task<IActionResult> DeleteTest(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        var test = await db.MonitoringTests.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        await MonitoringSittingService.EnsureNotInSittingAsync(db, id, ct);
        db.MonitoringTests.Remove(test);
        audit.Record("monitoring_test", id.ToString(), "delete", $"Monitoring testi o'chirildi: {test.Title}");
        await db.SaveChangesAsync(ct);
        return null;
    });

    [HttpGet("tests/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct = default)
    {
        try
        {
            var detail = await MonitoringService.DetailAsync(db, id, ct);
            return File(MonitoringService.ResultsXlsx(detail), XlsxMime, "monitoring-natijalar.xlsx");
        }
        catch (MonitoringException ex) { return StatusCode(ex.Status, new { message = ex.Message }); }
    }

    /// <summary>Test egalari tanlovi: sinflar va guruhlar (yo'nalishlar avval).</summary>
    [HttpGet("owners")]
    public async Task<IActionResult> Owners(CancellationToken ct = default)
    {
        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .Select(c => new { kind = LessonOwnerKind.Class, id = c.Id, name = c.Name, grade = (int?)c.Grade, isTrack = false }).ToListAsync(ct);
        var groups = await db.StudyGroups.AsNoTracking().Where(g => !g.IsArchived).OrderByDescending(g => g.IsTrack).ThenBy(g => g.Name)
            .Select(g => new { kind = LessonOwnerKind.Group, id = g.Id.ToString(), name = g.Name, grade = (int?)null, isTrack = g.IsTrack }).ToListAsync(ct);
        return Ok(classes.Concat(groups));
    }
}

/// <summary>
/// Onlayn monitoring testi — o'quvchi shaxsiy kodi bilan (akkauntsiz). Qurilma qulfi: test
/// boshlangan qurilmadan boshqasida davom etmaydi (o'quv bo'limi qulfni ochishi mumkin).
/// Natija o'quvchiga ko'rsatilmaydi — faqat "topshirildi".
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/monitoring")]
public class PublicMonitoringController(AppDbContext db) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NoContent() : Ok(result);
        }
        catch (MonitoringException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message });
        }
    }

    /// <summary>Kod kimniki va qaysi test — boshlashdan oldingi tasdiq uchun (hech narsa o'zgarmaydi).</summary>
    [HttpPost("check")]
    [EnableRateLimiting("monitoring")]
    public Task<IActionResult> Check(MonitoringStartRequest req, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.PeekAsync(db, req, ct));

    [HttpPost("start")]
    [EnableRateLimiting("monitoring")]
    public Task<IActionResult> Start(MonitoringStartRequest req, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.StartAsync(db, req, ct));

    /// <summary>
    /// Javob saqlandi — tasdiq <c>{ saved: savol raqami }</c>. Brauzer faqat shu tasdiqni "saqlandi"
    /// deb oladi: Wi-Fi kirish sahifasi yoki begona proksi qaytargan "200" javobni yo'qotmasin.
    /// </summary>
    [HttpPut("answer")]
    public Task<IActionResult> Answer(MonitoringAnswerRequest req, CancellationToken ct = default) =>
        Run(async () => { await MonitoringService.AnswerAsync(db, req, ct); return new { saved = req.Order }; });

    /// <summary>Halollik hodisasi: oynadan chiqish, nusxa olish urinishi va h.k.</summary>
    [HttpPost("event")]
    public Task<IActionResult> Event(MonitoringEventRequest req, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.RecordEventAsync(db, req, ct));

    /// <summary>Server vaqti — "Kompyuterni tekshirish" soat farqi va tarmoq tezligini o'lchaydi.</summary>
    [HttpGet("time")]
    public IActionResult Time() => Ok(new { now = DateTimeOffset.UtcNow });

    [HttpPost("ping")]
    public Task<IActionResult> Ping(MonitoringStartRequest req, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.PingAsync(db, req, ct));

    [HttpPost("finish")]
    public Task<IActionResult> Finish(MonitoringStartRequest req, CancellationToken ct = default) =>
        Run(async () => await MonitoringService.FinishAsync(db, req, ct));
}
