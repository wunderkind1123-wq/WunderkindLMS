using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'rindosh ustoz (client, 2026-10-07) — the same gate as teacher attendance ("teachers"): the
/// people who mark a teacher absent also name who covers the lessons.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers")]
[Route("api/admin/substitutions")]
public sealed class SubstitutionsController(
    AppDbContext db, TelegramService telegram, ILogger<SubstitutionNotifier> notifierLogger, AuditService audit)
    : ControllerBase
{
    private SubstitutionNotifier Notifier => new(db, telegram, notifierLogger);
    private string ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>One teacher's lessons on a date, their current substitutes, and who is free when.</summary>
    [HttpGet("day")]
    public async Task<ActionResult<SubstitutionDayDto>> Day([FromQuery] string teacherId, [FromQuery] string date, CancellationToken ct)
    {
        if (Parse(date) is not { } d) return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        var day = await LessonSubstitutionService.DayAsync(db, teacherId, d, ct);
        return day is null ? NotFound(new { message = "O'qituvchi topilmadi" }) : day;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubstitutionDto>>> List(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? teacherId, CancellationToken ct)
    {
        var start = Parse(from) ?? AppClock.Today.AddDays(-30);
        var end = Parse(to) ?? AppClock.Today.AddDays(30);
        return await LessonSubstitutionService.ListAsync(db, start, end, teacherId, ct);
    }

    /// <summary>A month of substitutions and per-person counts (o'rindoshlik oylikka bog'liq, 2026-11-11).</summary>
    [HttpGet("month")]
    public async Task<ActionResult<SubstitutionMonthDto>> Month([FromQuery] string? month, CancellationToken ct)
    {
        var m = AppClock.Today;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
            m = parsed;
        }
        return await LessonSubstitutionService.MonthAsync(db, m.Year, m.Month, ct);
    }

    /// <summary>Tashqi o'rindosh cards — searched by name or phone.</summary>
    [HttpGet("externals")]
    public async Task<ActionResult<IEnumerable<ExternalSubstituteDto>>> Externals([FromQuery] string? q, CancellationToken ct) =>
        await LessonSubstitutionService.ExternalListAsync(db, q, ct);

    /// <summary>A new tashqi o'rindosh card — or the existing one with that phone (Existed = true).</summary>
    [HttpPost("externals")]
    public async Task<ActionResult<ExternalSubstituteSaveResult>> CreateExternal(ExternalSubstituteRequest req, CancellationToken ct)
    {
        var (error, result) = await LessonSubstitutionService.ExternalCreateAsync(db, req, ActorId, ct);
        if (error is not null) return BadRequest(new { message = error });
        if (!result!.Existed)
        {
            audit.Record(AuditService.EntitySubstitution, result.Substitute.Id.ToString(), "create",
                $"Tashqi o'rindosh qo'shildi: {result.Substitute.FullName} ({result.Substitute.Phone})");
            await db.SaveChangesAsync(ct);
        }
        return result;
    }

    /// <summary>Names substitutes; each one hears from the bot.</summary>
    [HttpPost]
    public async Task<ActionResult<IEnumerable<SubstitutionDto>>> Create(SubstitutionCreateRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (error, created) = await LessonSubstitutionService.CreateAsync(db, req, ActorId, ct);
        if (error is not null) return BadRequest(new { message = error });
        // Names are read before the save — the rows are in the context already.
        var dtos = await LessonSubstitutionService.ToDtosAsync(db, created, ct);
        foreach (var d in dtos) Record(d, "create", "O'rindosh tayinlandi");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await Notifier.AssignedAsync(dtos, ct);
        return dtos;
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var row = await LessonSubstitutionService.CancelAsync(db, id, ActorId, ct);
        if (row is null) return NotFound(new { message = "O'rindosh dars topilmadi" });
        var dtos = await LessonSubstitutionService.ToDtosAsync(db, [row], ct);
        foreach (var d in dtos) Record(d, "cancel", "O'rindosh bekor qilindi");
        await db.SaveChangesAsync(ct);
        await Notifier.CancelledAsync(dtos, ct);
        return NoContent();
    }

    /// <summary>One audit row per substituted lesson (audit-log.md) — under the absent teacher.</summary>
    private void Record(SubstitutionDto d, string action, string what) =>
        audit.Record(AuditService.EntitySubstitution, d.Id.ToString(), action,
            $"{what}: {d.Date}, {d.Period}-dars, {d.OwnerName} ({d.SubjectName}) — {d.AbsentTeacherName} o'rniga {d.SubstituteTeacherName}",
            after: new Dictionary<string, object?>
            {
                ["Sana"] = d.Date, ["Dars"] = d.Period, ["Sinf"] = d.OwnerName, ["Fan"] = d.SubjectName,
                ["Kelmagan"] = d.AbsentTeacherName, ["O'rindosh"] = d.SubstituteTeacherName, ["Sabab"] = d.Reason,
            },
            teacherId: d.AbsentTeacherId);
}
