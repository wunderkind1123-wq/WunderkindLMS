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
    public async Task<List<CheckinEventDto>> Events(CancellationToken ct) => await OlympiadCheckin.EventsAsync(db, ct);

    [HttpGet("events/{id:guid}/summary")]
    public async Task<CheckinSummaryDto> Summary(Guid id, CancellationToken ct) => await OlympiadCheckin.SummaryAsync(db, id, ct);

    /// <summary>Ism, familiya, telefon yoki ishtirokchi raqami bo'yicha.</summary>
    [HttpGet("events/{id:guid}/search")]
    public async Task<List<OlympiadRegistrationDto>> Search(Guid id, [FromQuery] string? q, CancellationToken ct) =>
        await OlympiadCheckin.SearchAsync(db, id, q, ct);

    /// <summary>QR skan — <see cref="OlympiadCheckin.ScanAsync"/>.</summary>
    [HttpPost("scan")]
    public async Task<CheckinResultDto> Scan(CheckinScanInput input, CancellationToken ct) =>
        await OlympiadCheckin.ScanAsync(db, audit, input.EventId, input.Token, ActorName, ct);

    [HttpPost("registrations/{id:guid}/checkin")]
    public async Task<ActionResult<CheckinResultDto>> Checkin(Guid id, CancellationToken ct) =>
        await OlympiadCheckin.CheckinAsync(db, audit, id, ActorName, ct) is { } r ? r : NotFound(new { message = "Yozuv topilmadi" });

    /// <summary>Belgilashni bekor qilish (xato bo'lsa).</summary>
    [HttpPost("registrations/{id:guid}/undo")]
    public async Task<ActionResult<OlympiadRegistrationDto>> Undo(Guid id, CancellationToken ct)
    {
        var (dto, status, error) = await OlympiadCheckin.UndoAsync(db, audit, id, ActorName, ct);
        return dto is not null ? dto : StatusCode(status, new { message = error });
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
        return await OlympiadCheckin.SummaryAsync(db, id, ct);
    }
}
