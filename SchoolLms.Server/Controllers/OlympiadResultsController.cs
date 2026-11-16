using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>Olimpiada → natijalar: qo'lda kiritish, Excel import (oldindan ko'rish), sertifikat, yuborish.</summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Manage, GatedRead = true)]
[Route("api/admin/olympiad")]
public sealed class OlympiadResultsController(AppDbContext db, AuditService audit, OlympiadTelegram tg, IWebHostEnvironment env) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";
    private ObjectResult Error(int status, string message) => StatusCode(status, new { message });

    [HttpPut("registrations/{id:guid}/score")]
    public async Task<ActionResult<OlympiadRegistrationDto>> SetScore(Guid id, ScoreInput input, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reg is null) return NotFound(new { message = "Yozuv topilmadi" });
        var ev = await db.OlympiadEvents.AsNoTracking().FirstAsync(e => e.Id == reg.EventId, ct);
        if (reg.Status is OlympiadRegStatus.Cancelled or OlympiadRegStatus.Waitlist)
            return Error(409, "Bekor qilingan yoki kutishdagi ishtirokchiga ball qo'yilmaydi");
        if (input.Score is { } s && (s < 0 || s > ev.MaxScore))
            return BadRequest(new { message = $"Ball 0 dan {OlympiadText.Num(ev.MaxScore)} gacha bo'lishi kerak" });
        var before = reg.Score;
        reg.Score = input.Score;
        reg.ScoreSource = "manual";
        reg.ScoreUpdatedAt = AppClock.Now;
        reg.ScoreUpdatedBy = ActorName;
        // Ball qo'yildi, lekin kirish belgilanmagan — demak keldi (kirish yakunlanganda no_show bo'lgan bo'lsa ham).
        if (input.Score is not null && reg.Status is OlympiadRegStatus.Registered or OlympiadRegStatus.NoShow)
            reg.Status = OlympiadRegStatus.Attended;
        audit.Record("olympiad_registration", id.ToString(), "score",
            $"Olimpiada balli: {(before is { } b ? OlympiadText.Num(b) : "—")} → {(input.Score is { } a ? OlympiadText.Num(a) : "—")}");
        await db.SaveChangesAsync(ct);
        return (await OlympiadQueries.RegistrationsAsync(db, reg.EventId, new(RegistrationId: id), ct)).First();
    }

    /// <summary>Import shabloni: shu tadbirning ro'yxatdagilari raqam va ismi bilan, ball ustuni bo'sh.</summary>
    [HttpGet("events/{id:guid}/results/template")]
    public async Task<IActionResult> Template(Guid id, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Tadbir topilmadi" });
        var rows = (await OlympiadQueries.RegistrationsAsync(db, id, new(), ct))
            .Where(r => r.NumberValue > 0 && r.Status != OlympiadRegStatus.Cancelled).ToList();
        var bytes = ExcelExport.Build("Natijalar", ["Ishtirokchi raqami", "F.I.Sh", "Ball"],
            rows.Select(r => (IReadOnlyList<string>)[r.Number, r.FullName, r.Score is { } s ? OlympiadText.Num(s) : ""]));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"olimpiada-M{ev.Code}-natija-shablon.xlsx");
    }

    [HttpPost("events/{id:guid}/results/preview")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<List<ResultImportRow>>> Preview(Guid id, IFormFile? file, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (file is null || file.Length == 0) return BadRequest(new { message = "Excel faylni tanlang" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Faqat .xlsx fayl" });
        List<string[]> rows;
        try
        {
            await using var s = file.OpenReadStream();
            rows = ExcelImport.ReadRows(s, 3);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — shablondagi .xlsx ni yuklang" });
        }
        return await OlympiadResults.PreviewAsync(db, ev, rows, ct);
    }

    /// <summary>Oldindan ko'rishdan keyin "Tasdiqlash" — faqat shu yerda saqlanadi.</summary>
    [HttpPost("events/{id:guid}/results/import")]
    public async Task<IActionResult> Import(Guid id, ImportConfirmInput input, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (input.Rows.Any(r => r.Score < 0 || r.Score > ev.MaxScore)) return BadRequest(new { message = "Ball chegaradan tashqarida" });
        var ids = input.Rows.Select(r => r.RegistrationId).ToList();
        var regs = await db.OlympiadRegistrations.Where(r => r.EventId == id && ids.Contains(r.Id)
            && r.Status != OlympiadRegStatus.Cancelled && r.Status != OlympiadRegStatus.Waitlist).ToDictionaryAsync(r => r.Id, ct);
        var now = AppClock.Now;
        var saved = 0;
        foreach (var row in input.Rows)
        {
            if (!regs.TryGetValue(row.RegistrationId, out var reg)) continue;
            reg.Score = row.Score;
            reg.ScoreSource = "excel";
            reg.ScoreUpdatedAt = now;
            reg.ScoreUpdatedBy = ActorName;
            if (reg.Status is OlympiadRegStatus.Registered or OlympiadRegStatus.NoShow) reg.Status = OlympiadRegStatus.Attended;
            saved++;
        }
        audit.Record("olympiad_event", id.ToString(), "import", $"Olimpiada natijalari Exceldan yuklandi: {saved} ta");
        await db.SaveChangesAsync(ct);
        return Ok(new { saved });
    }

    /// <summary>Namunaviy natija xabari (HTML) — yuborishdan oldin ko'rish.</summary>
    [HttpGet("events/{id:guid}/results/sample")]
    public async Task<IActionResult> Sample(Guid id, CancellationToken ct)
    {
        var regId = await db.OlympiadRegistrations.AsNoTracking()
            .Where(r => r.EventId == id && r.Score != null && r.Status == OlympiadRegStatus.Attended)
            .OrderByDescending(r => r.Score).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (regId is null) return Error(409, "Hali ball kiritilmagan");
        var msg = await OlympiadResults.ComposeAsync(db, regId.Value, ct);
        var recipients = await RecipientsQuery(id).CountAsync(ct);
        var noTelegram = await db.OlympiadRegistrations.AsNoTracking()
            .Where(r => r.EventId == id && r.Score != null && r.Status == OlympiadRegStatus.Attended)
            .Join(db.OlympiadParticipants, r => r.ParticipantId, p => p.Id, (r, p) => p)
            .CountAsync(p => p.BotUserId == null, ct);
        return Ok(new
        {
            registrationId = regId,
            html = msg?.Html,
            buttons = msg?.Buttons,
            withCertificate = msg?.WithCertificate ?? false,
            recipients,
            noTelegram,
        });
    }

    [HttpGet("registrations/{id:guid}/certificate")]
    public async Task<IActionResult> Certificate(Guid id, CancellationToken ct)
    {
        var cert = await OlympiadResults.CertificateAsync(db, id, ct);
        if (cert is null) return NotFound(new { message = "Ball kiritilmagan" });
        return File(cert.Value.Bytes, cert.Value.Png ? "image/png" : "application/pdf", cert.Value.FileName);
    }

    /// <summary>Natija oladiganlar: kelgan, balli, Telegram akkaunti bor.</summary>
    private IQueryable<OlympiadRegistration> RecipientsQuery(Guid eventId) =>
        from r in db.OlympiadRegistrations
        join p in db.OlympiadParticipants on r.ParticipantId equals p.Id
        where r.EventId == eventId && r.Score != null && r.Status == OlympiadRegStatus.Attended && p.BotUserId != null
        select r;

    /// <summary>"Natijalarni yuborish" — kelmaganlarga bormaydi. <paramref name="changedOnly"/> — "Tuzatilganni qayta yuborish".</summary>
    [HttpPost("events/{id:guid}/results/send")]
    public async Task<IActionResult> Send(Guid id, [FromQuery] bool changedOnly, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (!tg.IsConfigured) return Error(409, "Bot sozlanmagan — Sozlamalar → Bot");
        var targets = await (from r in RecipientsQuery(id)
                             join p in db.OlympiadParticipants on r.ParticipantId equals p.Id
                             select new { r.Id, BotUserId = p.BotUserId!.Value, r.Score, r.ResultSentAt, r.ResultSentScore }).ToListAsync(ct);
        if (changedOnly) targets = targets.Where(t => t.ResultSentAt is not null && t.ResultSentScore != t.Score).ToList();
        else targets = targets.Where(t => t.ResultSentAt is null).ToList();
        var queued = await OlympiadDispatcher.AddDeliveriesAsync(db, OlympiadDeliveryKind.Result, id,
            targets.Select(t => (t.BotUserId, t.Id, OlympiadText.Num(t.Score!.Value))), ct, requeue: true);
        ev.ResultsSentAt ??= AppClock.Now;
        audit.Record("olympiad_event", id.ToString(), "send_results",
            changedOnly ? $"Tuzatilgan natijalar navbatga qo'yildi: {queued}" : $"Natijalar navbatga qo'yildi: {queued}");
        await db.SaveChangesAsync(ct);
        return Ok(new { queued });
    }

    /// <summary>Natijani joriy xodimning Telegramiga sinov uchun yuborish.</summary>
    [HttpPost("registrations/{id:guid}/result-test")]
    public async Task<IActionResult> TestResult(Guid id, CancellationToken ct)
    {
        var chat = await OlympiadSettingsController.MyTelegramIdAsync(db, User, ct);
        if (chat is null) return Error(409, OlympiadSettingsController.NoTelegramMessage);
        var res = await OlympiadResults.SendResultAsync(db, new OlympiadMessenger(db, tg, env.ContentRootPath), id, ct, chat);
        return res is { Ok: true } ? NoContent() : Error(502, OlympiadSettingsController.TestFailMessage(res?.Error));
    }
}
