using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Olimpiada botidagi QR skaner (Telegram Mini App, SPA <c>/olimpiada-skaner</c>; mijoz, 2026-11-18):
/// kirish joyidagi admin botdan panelni ochadi, o'quvchi QR kodini skanerlaydi — "Kirdi" belgilanadi.
///
/// <para>
/// <b>Kirish.</b> Login yo'q. Har so'rov <c>X-Telegram-Init-Data</c> sarlavhasida Telegram bergan
/// <c>initData</c> ni olib keladi; u <b>olimpiada boti</b> tokeni bilan tekshiriladi
/// (<see cref="TelegramInitData"/>). Keyin shu Telegram foydalanuvchisi botda O'Z kontakti orqali
/// tasdiqlagan raqam (<see cref="OlympiadBotUser.Phone"/>) Sozlamalar → "Bot adminlari" ro'yxatida
/// bo'lishi shart. Ro'yxatdan o'chirilgan raqam keyingi so'rovdayoq rad etiladi.
/// </para>
///
/// <para>
/// Imkoniyatlar admin sahifasidagi kirish bilan bir xil (<see cref="OlympiadCheckin"/>), faqat
/// "Yakunlash" (kelmaganlarni belgilash, tadbirni yopish) yo'q — u admin panelida qoladi.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/olympiad-scanner")]
[RequestSizeLimit(16 * 1024)]
public sealed class OlympiadScannerController(AppDbContext db, AuditService audit) : ControllerBase, IAsyncActionFilter
{
    public const string InitDataHeader = "X-Telegram-Init-Data";

    /// <summary>Tekshiruvdan o'tgan admin — "🤖 Ism (Telegram)" ko'rinishida <c>CheckedInBy</c> ga yoziladi.</summary>
    private string _actor = "";

    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;
        var settings = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var check = TelegramInitData.Validate(
            context.HttpContext.Request.Headers[InitDataHeader].ToString(), settings?.BotToken, DateTimeOffset.UtcNow);
        if (!check.Ok || check.User is null)
        {
            context.Result = Unauthorized(new { code = "init_data", message = "Sahifani olimpiada botidan qayta oching." });
            return;
        }
        var botUser = await db.OlympiadBotUsers.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == check.User.Id, ct);
        if (botUser is null || !OlympiadText.IsAdminPhone(settings!.AdminPhones, botUser.Phone))
        {
            context.Result = StatusCode(403, new { code = "not_admin", message = "Siz olimpiada adminlari ro'yxatida yo'qsiz." });
            return;
        }
        _actor = $"🤖 {check.User.DisplayName} (Telegram)";
        await next();
    }

    /// <summary>Kim kirdi — sahifa sarlavhasida ko'rsatiladi.</summary>
    [HttpGet("me")]
    public object Me() => new { name = _actor };

    [HttpGet("events")]
    public async Task<List<CheckinEventDto>> Events(CancellationToken ct) => await OlympiadCheckin.EventsAsync(db, ct);

    [HttpGet("events/{id:guid}/summary")]
    public async Task<CheckinSummaryDto> Summary(Guid id, CancellationToken ct) => await OlympiadCheckin.SummaryAsync(db, id, ct);

    [HttpGet("events/{id:guid}/search")]
    public async Task<List<OlympiadRegistrationDto>> Search(Guid id, [FromQuery] string? q, CancellationToken ct) =>
        await OlympiadCheckin.SearchAsync(db, id, q, ct);

    [HttpPost("scan")]
    public async Task<CheckinResultDto> Scan(CheckinScanInput input, CancellationToken ct) =>
        await OlympiadCheckin.ScanAsync(db, audit, input.EventId, input.Token, _actor, ct);

    [HttpPost("registrations/{id:guid}/checkin")]
    public async Task<ActionResult<CheckinResultDto>> Checkin(Guid id, CancellationToken ct) =>
        await OlympiadCheckin.CheckinAsync(db, audit, id, _actor, ct) is { } r ? r : NotFound(new { message = "Yozuv topilmadi" });

    [HttpPost("registrations/{id:guid}/undo")]
    public async Task<ActionResult<OlympiadRegistrationDto>> Undo(Guid id, CancellationToken ct)
    {
        var (dto, status, error) = await OlympiadCheckin.UndoAsync(db, audit, id, _actor, ct);
        return dto is not null ? dto : StatusCode(status, new { message = error });
    }
}
