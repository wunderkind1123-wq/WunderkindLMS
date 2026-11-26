using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hr;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Word / matn testini monitoring bankiga yuklashning oldindan ko'rinishi — o'quv bo'limi
/// (<see cref="MonitoringController"/>) va ustoz (<see cref="TeacherQuestionBanksController"/>) uchun
/// bitta yo'l. Hech narsa saqlanmaydi; savoldagi rasmlar /uploads ga yoziladi (ko'rsatish uchun).
/// </summary>
internal static class MonitoringImportPreview
{
    public static readonly string[] Extensions = [".docx", ".txt"];

    public static async Task<MonitoringImportPreviewDto> RunAsync(IWebHostEnvironment env, IFormFile? file, string? text)
    {
        MonitoringWordImport.Document doc;
        if (file is { Length: > 0 })
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Extensions.Contains(ext))
                throw new MonitoringException(ext == ".doc"
                    ? "Eski .doc format — Word'da «Сохранить как → .docx» qilib qayta yuklang"
                    : "Word (.docx) yoki matn (.txt) fayl yuklang. Excel uchun «Excel'dan import» tugmasi bor");
            if (file.Length > AssignmentQuestionImport.MaxUploadBytes) throw new MonitoringException("Fayl 10 MB dan katta");
            await using var stream = file.OpenReadStream();
            if (ext == ".txt")
            {
                using var reader = new StreamReader(stream);
                doc = new MonitoringWordImport.Document(await reader.ReadToEndAsync(), new Dictionary<string, MonitoringWordImport.Image>());
            }
            else
            {
                // OpenXML needs a seekable stream; the form stream is not always one.
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                buffer.Position = 0;
                try { doc = MonitoringWordImport.Read(buffer); }
                catch (MonitoringException) { throw; }
                catch (Exception) { throw new MonitoringException("Word faylni o'qib bo'lmadi — buzilmagan .docx ekanini tekshiring"); }
            }
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            doc = new MonitoringWordImport.Document(text, new Dictionary<string, MonitoringWordImport.Image>());
        }
        else throw new MonitoringException("Fayl tanlang yoki matnni qo'ying");

        var dir = Path.Combine(env.ContentRootPath, "uploads");
        return await MonitoringWordImport.ToPreviewAsync(doc, async image =>
        {
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid():N}{image.Extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, name), image.Bytes);
            return $"/uploads/{name}";
        });
    }

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
    public const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Savol tahrirlash oynasidan bitta rasm — /uploads ga yozib, manzilini qaytaradi.</summary>
    public static async Task<object> SaveImageAsync(IWebHostEnvironment env, IFormFile? file)
    {
        if (file is not { Length: > 0 }) throw new MonitoringException("Rasm tanlang");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ImageExtensions.Contains(ext)) throw new MonitoringException("Rasm PNG, JPG, GIF yoki WEBP bo'lsin");
        if (file.Length > MaxImageBytes) throw new MonitoringException("Rasm 5 MB dan katta");
        var dir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs);
        return new { url = $"/uploads/{name}" };
    }
}

/// <summary>
/// Ustozning savollar banki (2026-11-24, curriculum-monitoring.md §2.2): ustoz o'zi dars beradigan
/// fan × sinf darajasi (yoki yo'nalish) banklarini ko'radi, yaratadi va to'ldiradi — Word'dan
/// yuklab, savollarni tahrirlab. Shu fandan dars beradigan ustozlar bankni birga to'ldiradi; bankni
/// o'chirish — faqat yaratgan ustozga. Testni o'quv bo'limi tuzadi va natijani faqat u ko'radi
/// (ustozga testlar va natijalar endpointi yo'q).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/question-banks")]
public class TeacherQuestionBanksController(AppDbContext db, AuditService audit, IWebHostEnvironment env) : ControllerBase
{
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

    /// <summary>Ustoz jadvalda dars beradigan (fan, daraja/yo'nalish) juftliklari.</summary>
    private async Task<List<TeacherCurriculumKeyDto>> KeysAsync(CancellationToken ct)
    {
        var me = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == Uid, ct)
            ?? throw new MonitoringException("O'qituvchi profili topilmadi", 403);
        return await CurriculumService.TeacherKeysAsync(db, me.Id, ct);
    }

    private static bool Covers(List<TeacherCurriculumKeyDto> keys, string subjectId, int? grade, Guid? groupId) =>
        keys.Any(k => k.SubjectId == subjectId && (groupId != null ? k.GroupId == groupId : k.GroupId == null && k.Grade == grade));

    /// <summary>Bank ustozniki emas — "topilmadi" (boshqa fanlarning banklari borligi ham bilinmasin).</summary>
    private async Task<MonitoringBank> MyBankAsync(Guid id, CancellationToken ct)
    {
        var bank = await db.MonitoringBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null || !Covers(await KeysAsync(ct), bank.SubjectId, bank.Grade, bank.GroupId))
            throw new MonitoringException("Bank topilmadi", 404);
        return bank;
    }

    /// <summary>
    /// Bank formasi uchun tanlovlar — monitoring formasi bilan bir xil ko'rinish, faqat ustozning
    /// fanlari, darajalari va yo'nalishlari; <c>keys</c> — aniq juftliklar.
    /// </summary>
    [HttpGet("options")]
    public Task<IActionResult> Options(CancellationToken ct = default) => Run(async () =>
    {
        var keys = await KeysAsync(ct);
        var trackIds = keys.Where(k => k.GroupId != null).Select(k => k.GroupId!.Value).Distinct().ToList();
        var tracks = await db.StudyGroups.AsNoTracking().Where(g => trackIds.Contains(g.Id)).OrderBy(g => g.Name)
            .Select(g => new { id = g.Id, name = g.Name }).ToListAsync(ct);
        return new
        {
            subjects = keys.GroupBy(k => k.SubjectId).Select(g => new { id = g.Key, name = g.First().SubjectName }).OrderBy(s => s.name).ToList(),
            grades = keys.Where(k => k.Grade != null).Select(k => k.Grade!.Value).Distinct().Order().ToList(),
            tracks,
            keys,
        };
    });

    [HttpGet]
    public Task<IActionResult> Banks(CancellationToken ct = default) => Run(async () =>
    {
        var keys = await KeysAsync(ct);
        return (await MonitoringService.BanksAsync(db, ct)).Where(b => Covers(keys, b.SubjectId, b.Grade, b.GroupId)).ToList();
    });

    [HttpPost]
    public Task<IActionResult> CreateBank(MonitoringBankPayload p, CancellationToken ct = default) => Run(async () =>
    {
        if (!Covers(await KeysAsync(ct), p.SubjectId, p.Grade, p.GroupId))
            throw new MonitoringException("Siz bu fan va sinfdan dars bermaysiz — jadvalingizdagi fan va sinfni tanlang");
        var bank = await MonitoringService.SaveBankAsync(db, null, p, Uid, ct);
        audit.Record("monitoring_bank", bank.Id.ToString(), "create", $"Ustoz savollar banki yaratdi: {bank.Title}");
        await db.SaveChangesAsync(ct);
        return new { id = bank.Id };
    });

    [HttpPut("{id:guid}")]
    public Task<IActionResult> UpdateBank(Guid id, MonitoringBankPayload p, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        if (!Covers(await KeysAsync(ct), p.SubjectId, p.Grade, p.GroupId))
            throw new MonitoringException("Siz bu fan va sinfdan dars bermaysiz — jadvalingizdagi fan va sinfni tanlang");
        return new { id = (await MonitoringService.SaveBankAsync(db, id, p, Uid, ct)).Id };
    });

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> DeleteBank(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        var mine = await MyBankAsync(id, ct);
        if (mine.CreatedBy != Uid) throw new MonitoringException("Bankni faqat uni yaratgan ustoz yoki o'quv bo'limi o'chiradi", 403);
        var bank = await db.MonitoringBanks.FirstAsync(b => b.Id == id, ct);
        db.MonitoringBanks.Remove(bank);
        audit.Record("monitoring_bank", id.ToString(), "delete", $"Ustoz savollar bankini o'chirdi: {bank.Title}");
        await db.SaveChangesAsync(ct);
        return null;
    });

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Bank(Guid id, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        return await MonitoringService.BankDetailAsync(db, id, ct);
    });

    [HttpPost("{id:guid}/questions")]
    public Task<IActionResult> AddQuestion(Guid id, MonitoringQuestionPayload p, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        return new { id = await MonitoringService.SaveQuestionAsync(db, id, null, p, ct) };
    });

    [HttpPut("{id:guid}/questions/{questionId:guid}")]
    public Task<IActionResult> UpdateQuestion(Guid id, Guid questionId, MonitoringQuestionPayload p, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        return new { id = await MonitoringService.SaveQuestionAsync(db, id, questionId, p, ct) };
    });

    [HttpDelete("{id:guid}/questions/{questionId:guid}")]
    public Task<IActionResult> DeleteQuestion(Guid id, Guid questionId, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        var q = await db.MonitoringQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.BankId == id, ct)
            ?? throw new MonitoringException("Savol topilmadi", 404);
        db.MonitoringQuestions.Remove(q);
        await db.SaveChangesAsync(ct);
        return null;
    });

    /// <summary>Word (.docx) / .txt fayl (multipart <c>file</c>) yoki <c>text</c> — oldindan ko'rish, saqlamaydi.</summary>
    [HttpPost("{id:guid}/import/preview")]
    [RequestSizeLimit(AssignmentQuestionImport.MaxRequestBytes)]
    public Task<IActionResult> ImportPreview(Guid id, IFormFile? file, [FromForm] string? text, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        return await MonitoringImportPreview.RunAsync(env, file, text);
    });

    /// <summary>Savolga rasm (multipart <c>file</c>) — manzil qaytadi, savol saqlanganda biriktiriladi.</summary>
    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(6_000_000)]
    public Task<IActionResult> UploadImage(Guid id, IFormFile? file, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        return await MonitoringImportPreview.SaveImageAsync(env, file);
    });

    /// <summary>Ko'rib chiqilgan savollarni bankka yozadi.</summary>
    [HttpPost("{id:guid}/questions/bulk")]
    public Task<IActionResult> AddMany(Guid id, List<MonitoringQuestionPayload> rows, CancellationToken ct = default) => Run(async () =>
    {
        var bank = await MyBankAsync(id, ct);
        var added = await MonitoringService.AddQuestionsAsync(db, id, rows, ct);
        audit.Record("monitoring_bank", id.ToString(), "import", $"Ustoz «{bank.Title}» bankiga {added} ta savol yukladi");
        await db.SaveChangesAsync(ct);
        return new { added };
    });

    /// <summary>Test tuzadigan Word namuna — ko'rsatma va namuna savollar.</summary>
    [HttpGet("word-template")]
    public IActionResult WordTemplate() => File(MonitoringWordImport.Template(),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", MonitoringWordImport.TemplateFileName);


    /// <summary>
    /// Word namuna — ustozning Telegram chatiga (Mini App ichida fayl yuklab olish ishonchsiz).
    /// 409 — Telegram ulanmagan; 503 — bot sozlanmagan; 502 — Telegram faylni qabul qilmadi.
    /// </summary>
    [HttpPost("word-template/telegram")]
    [EnableRateLimiting("telegram")]
    public async Task<IActionResult> WordTemplateToTelegram([FromServices] TelegramService telegram, CancellationToken ct = default)
    {
        var me = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == Uid, ct);
        if (me is null) return Forbid();
        var chats = await PenaltyNotifier.RecipientsAsync(db, EmployeeKind.Teacher, me.Id, ct);
        if (chats.Count == 0)
            return Conflict(new { code = "telegram_not_linked", message = "Telegram hisobingiz ulanmagan. Ilovani Telegram ichidan oching." });
        if (!telegram.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "telegram_not_configured", message = "Telegram bot sozlanmagan. Maktab ma'muriyatiga murojaat qiling." });

        var bytes = MonitoringWordImport.Template();
        const string caption = "Test savollari namunasi — savollarni shu ko'rinishda yozib, «Test savollarim» → bank → "
            + "«Word'dan yuklash» orqali yuklang. To'g'ri javob: *B) yoki «Javob: C» yoki qalin variant.";
        var delivered = false;
        foreach (var chat in chats)
            delivered |= await telegram.SendDocumentAsync(chat, bytes, MonitoringWordImport.TemplateFileName, caption, ct);
        return delivered
            ? Ok(new { sent = true })
            : StatusCode(StatusCodes.Status502BadGateway,
                new { code = "telegram_send_failed", message = "Telegram faylni qabul qilmadi. Botga /start yozib, qayta urinib ko'ring." });
    }
    [HttpGet("question-template")]
    public IActionResult QuestionTemplate() => File(MonitoringService.QuestionTemplate(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "monitoring-savollar-namuna.xlsx");

    /// <summary>Excel namunasi bo'yicha import (o'quv bo'limi bilan bir xil).</summary>
    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(20_000_000)]
    public Task<IActionResult> Import(Guid id, IFormFile? file, CancellationToken ct = default) => Run(async () =>
    {
        await MyBankAsync(id, ct);
        if (file is null || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new MonitoringException("Savollarni .xlsx (Excel) faylda yuklang");
        await using var stream = file.OpenReadStream();
        var result = await MonitoringService.ImportQuestionsAsync(db, id, stream, ct);
        if (result.Errors.Count > 0) throw new MonitoringException(string.Join("\n", result.Errors.Take(30)));
        return result;
    });
}
