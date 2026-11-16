using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Imtihon kuni kirishni belgilash (QR skaner va qidiruv). Ruxsat: <c>olympiadCheckin</c> (faqat shu sahifa)
/// yoki to'liq <c>olympiad</c>.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Checkin, GatedRead = true, AlsoPerm = OlympiadPerms.Manage)]
[Route("api/admin/olympiad-checkin")]
public sealed class OlympiadCheckinController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";

    /// <summary>Kirish belgilash mumkin bo'lgan tadbirlar: e'lon qilingan yoki yakunlangan, oxirgi 30 kun va kelgusi.</summary>
    [HttpGet("events")]
    public async Task<IActionResult> Events(CancellationToken ct)
    {
        var from = AppClock.Today.AddDays(-30);
        var list = await db.OlympiadEvents.AsNoTracking()
            .Where(e => (e.Status == OlympiadEventStatus.Published || e.Status == OlympiadEventStatus.Finished) && e.Date >= from)
            .OrderBy(e => e.StartsAt)
            .Select(e => new { e.Id, e.Code, e.Title, e.Date, e.StartTime, e.EndTime, e.Location, e.CheckinClosedAt })
            .ToListAsync(ct);
        return Ok(list);
    }

    private async Task<CheckinSummaryDto> SummaryOfAsync(Guid eventId, CancellationToken ct)
    {
        var counts = await db.OlympiadRegistrations.AsNoTracking().Where(r => r.EventId == eventId)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int C(string s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        var attended = C(OlympiadRegStatus.Attended);
        var total = attended + C(OlympiadRegStatus.Registered) + C(OlympiadRegStatus.NoShow);
        return new CheckinSummaryDto(total, attended, total - attended);
    }

    [HttpGet("events/{id:guid}/summary")]
    public async Task<CheckinSummaryDto> Summary(Guid id, CancellationToken ct) => await SummaryOfAsync(id, ct);

    /// <summary>Ism, familiya, telefon yoki ishtirokchi raqami bo'yicha.</summary>
    [HttpGet("events/{id:guid}/search")]
    public async Task<List<OlympiadRegistrationDto>> Search(Guid id, [FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        return (await OlympiadQueries.RegistrationsAsync(db, id, new(Q: q), ct))
            .Where(r => r.Status != OlympiadRegStatus.Cancelled).Take(30).ToList();
    }

    private async Task<OlympiadRegistrationDto> DtoAsync(OlympiadRegistration r, CancellationToken ct) =>
        (await OlympiadQueries.RegistrationsAsync(db, r.EventId, new(RegistrationId: r.Id), ct)).First();

    /// <summary>
    /// QR skan: ✅ ok — "Kirdi" belgilandi; ⚠️ already — allaqachon kirgan; ❌ error — boshqa tadbir, bekor qilingan,
    /// kutishda yoki topilmadi.
    /// </summary>
    [HttpPost("scan")]
    public async Task<CheckinResultDto> Scan(CheckinScanInput input, CancellationToken ct)
    {
        var token = (input.Token ?? "").Trim();
        // QR'da faqat token; kimdir havola ko'rinishida skanerlasa ham oxirgi qismini olamiz.
        if (token.Contains('/')) token = token[(token.LastIndexOf('/') + 1)..];
        var reg = token.Length == 0 ? null : await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.QrToken == token, ct);
        if (reg is null) return new("error", "QR kod topilmadi", null);
        if (reg.EventId != input.EventId)
        {
            var other = await db.OlympiadEvents.AsNoTracking().Where(e => e.Id == reg.EventId).Select(e => e.Title).FirstOrDefaultAsync(ct);
            return new("error", $"Boshqa tadbir: «{other}»", await DtoAsync(reg, ct));
        }
        return await MarkAsync(reg, "qr", ct);
    }

    private async Task<CheckinResultDto> MarkAsync(OlympiadRegistration reg, string method, CancellationToken ct)
    {
        switch (reg.Status)
        {
            case OlympiadRegStatus.Cancelled: return new("error", "Yozuv bekor qilingan", await DtoAsync(reg, ct));
            case OlympiadRegStatus.Waitlist: return new("error", "Kutish ro'yxatida — joy berilmagan", await DtoAsync(reg, ct));
            case OlympiadRegStatus.Attended:
                return new("already", $"Allaqachon kirgan: {reg.CheckedInAt:HH:mm}, {reg.CheckedInBy}", await DtoAsync(reg, ct));
        }
        reg.Status = OlympiadRegStatus.Attended;
        reg.CheckedInAt = AppClock.Now;
        reg.CheckedInBy = ActorName;
        reg.CheckinMethod = method;
        audit.Record("olympiad_registration", reg.Id.ToString(), "checkin", $"Olimpiadaga kirdi ({(method == "qr" ? "QR" : "qo'lda")})");
        await db.SaveChangesAsync(ct);
        return new("ok", "Kirdi", await DtoAsync(reg, ct));
    }

    [HttpPost("registrations/{id:guid}/checkin")]
    public async Task<ActionResult<CheckinResultDto>> Checkin(Guid id, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reg is null) return NotFound(new { message = "Yozuv topilmadi" });
        return await MarkAsync(reg, "manual", ct);
    }

    /// <summary>Belgilashni bekor qilish (xato bo'lsa).</summary>
    [HttpPost("registrations/{id:guid}/undo")]
    public async Task<ActionResult<OlympiadRegistrationDto>> Undo(Guid id, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reg is null) return NotFound(new { message = "Yozuv topilmadi" });
        if (reg.Status is not (OlympiadRegStatus.Attended or OlympiadRegStatus.NoShow))
            return StatusCode(409, new { message = "Bu yozuvda kirish belgilanmagan" });
        reg.Status = OlympiadRegStatus.Registered;
        reg.CheckedInAt = null;
        reg.CheckedInBy = "";
        reg.CheckinMethod = "";
        audit.Record("olympiad_registration", reg.Id.ToString(), "checkin_undo", "Olimpiada kirish belgisi bekor qilindi");
        await db.SaveChangesAsync(ct);
        return await DtoAsync(reg, ct);
    }

    /// <summary>"Yakunlash": belgilanmaganlar <c>no_show</c> bo'ladi, tadbir — "finished".</summary>
    [HttpPost("events/{id:guid}/finish")]
    public async Task<ActionResult<CheckinSummaryDto>> Finish(Guid id, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Tadbir topilmadi" });
        var n = await db.OlympiadRegistrations.Where(r => r.EventId == id && r.Status == OlympiadRegStatus.Registered)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, OlympiadRegStatus.NoShow), ct);
        ev.CheckinClosedAt = AppClock.Now;
        if (ev.Status == OlympiadEventStatus.Published) ev.Status = OlympiadEventStatus.Finished;
        audit.Record("olympiad_event", id.ToString(), "checkin_finish", $"Olimpiada kirishi yakunlandi: {n} ta kelmadi");
        await db.SaveChangesAsync(ct);
        return await SummaryOfAsync(id, ct);
    }
}
