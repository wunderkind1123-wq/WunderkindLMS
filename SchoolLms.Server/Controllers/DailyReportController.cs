using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kunlik hisobot (2026-11-16): the PDF the scheduler sends at 17:00 — to download for any day, and to
/// send now to the list (`dailyReport` key). <c>date</c> — "yyyy-MM-dd"; empty — today.
/// </summary>
[ApiController]
[Authorize]
// Gated read: the report rates teachers by name — only holders of the key open it.
[AdminPerm(DailyDirectorReportService.PermissionKey, GatedRead = true)]
[Route("api/admin/daily-report")]
public sealed class DailyReportController(DailyDirectorReportService reports) : ControllerBase
{
    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf([FromQuery] string? date, CancellationToken ct)
    {
        if (!TryDay(date, out var day)) return BadRequest(new { message = "Sana noto'g'ri" });
        var (pdf, fileName, _) = await reports.PdfAsync(day, ct);
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>Who would get it now: how many Telegram chats, and whether the day was already sent.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? date, CancellationToken ct)
    {
        if (!TryDay(date, out var day)) return BadRequest(new { message = "Sana noto'g'ri" });
        return Ok(new { chats = (await reports.ChatsAsync(ct)).Count, sent = await reports.SentAsync(day, ct), sendAt = "17:00" });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromQuery] string? date, CancellationToken ct)
    {
        if (!TryDay(date, out var day)) return BadRequest(new { message = "Sana noto'g'ri" });
        var sent = await reports.SendAsync(day, User.FindFirst(ClaimTypes.NameIdentifier)?.Value, ct);
        return sent == 0
            ? BadRequest(new { message = "Yuborilmadi: «Kunlik hisobot» ruxsati bor va Telegram ulangan hisob topilmadi" })
            : Ok(new { sent });
    }

    private static bool TryDay(string? date, out DateOnly day)
    {
        day = AppClock.Today;
        return string.IsNullOrWhiteSpace(date) || DateOnly.TryParseExact(date, "yyyy-MM-dd", out day);
    }
}
