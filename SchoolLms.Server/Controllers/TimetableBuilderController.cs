using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Jadval tuzish vositalari (mijoz, 2026-11-21: "dars jadvali bilan bog'liq hamma narsani boshqarish"):
/// xonalar, yuklama, ustoz bandligi, avtomatik jadval va Excel eksport. Qoidalar —
/// <see cref="TeachingLoadService"/>, hisoblash — <see cref="TimetableSolver"/>.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/schedule")]
public class TimetableBuilderController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Jadval muharriri uchun faol xonalar va har sinfning o'z xonasi.</summary>
    [HttpGet("rooms")]
    public async Task<ActionResult<ScheduleRoomsDto>> Rooms(CancellationToken ct = default) =>
        await TeachingLoadService.RoomsAsync(db, ct);

    // ------------------------------------------------------------------ yuklama

    [HttpGet("loads")]
    public async Task<ActionResult<List<TeachingLoadDto>>> Loads(CancellationToken ct = default) =>
        await TeachingLoadService.ListAsync(db, ct);

    /// <summary>Bitta eganing (sinf yoki guruh) yuklamasini to'liq almashtirish.</summary>
    [HttpPut("loads/{ownerId}")]
    public async Task<IActionResult> SaveLoads(string ownerId, SaveOwnerLoadsRequest req, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return NotFound();
        var rows = req.Rows ?? [];
        if (await TeachingLoadService.SaveOwnerAsync(db, owner, rows, ct) is { } error)
            return BadRequest(new { message = error });
        audit.Record(AuditService.EntityScheduleTemplate, owner.Id, "update",
            $"Yuklama saqlandi: {owner.Name} — {rows.Count} ta fan, haftasiga {rows.Sum(r => r.HoursPerWeek)} soat");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Yuklamani joriy (yoki nomi berilgan) jadvaldan to'ldirish.</summary>
    [HttpPost("loads/fill")]
    public async Task<ActionResult<FillLoadsResultDto>> FillLoads(FillLoadsRequest req, CancellationToken ct = default)
    {
        var r = await TeachingLoadService.FillFromScheduleAsync(db, req, ct);
        audit.Record(AuditService.EntityScheduleTemplate, "loads", "update",
            $"Yuklama jadvaldan to'ldirildi{(string.IsNullOrWhiteSpace(req.TemplateName) ? "" : $" («{req.TemplateName}»)")}: "
            + $"{r.Owners} ta sinf/guruh, {r.Rows} ta qator" + (r.Skipped > 0 ? $", {r.Skipped} tasi o'zgartirilmadi" : ""));
        await db.SaveChangesAsync(ct);
        return r;
    }

    // ------------------------------------------------------------------ o'quv kunlari

    /// <summary>Har sinf/guruhning haftalik o'quv kunlari (sukut: 1–3-sinf 5 kun, qolganlari 6 kun).</summary>
    [HttpGet("study-days")]
    public async Task<ActionResult<List<OwnerStudyDaysDto>>> StudyDays(CancellationToken ct = default) =>
        await TeachingLoadService.StudyDaysAsync(db, ct);

    [HttpPut("study-days/{ownerId}")]
    public async Task<IActionResult> SaveStudyDays(string ownerId, SaveStudyDaysRequest req, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return NotFound();
        if (await TeachingLoadService.SaveStudyDaysAsync(db, owner, req.Days, ct) is { } error)
            return BadRequest(new { message = error });
        audit.Record(AuditService.EntityScheduleTemplate, owner.Id, "update",
            $"O'quv kunlari: {owner.Name} — haftasiga {req.Days} kun");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ ustoz bandligi

    [HttpGet("unavailability")]
    public async Task<ActionResult<List<TeacherUnavailabilityDto>>> Unavailability(CancellationToken ct = default) =>
        await TeachingLoadService.UnavailabilityAsync(db, ct);

    [HttpPut("unavailability/{teacherId}")]
    public async Task<IActionResult> SaveUnavailability(
        string teacherId, SaveUnavailabilityRequest req, CancellationToken ct = default)
    {
        if (await TeachingLoadService.SaveUnavailabilityAsync(db, teacherId, req.Slots ?? [], ct) is { } error)
            return BadRequest(new { message = error });
        audit.Record(AuditService.EntityScheduleTemplate, teacherId, "update",
            $"O'qituvchi bandligi saqlandi: {(req.Slots ?? []).Count} ta vaqt", teacherId: teacherId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ avtomatik jadval

    /// <summary>Yuklamadan yangi QORALAMA jadval — sinflarga "Nashr qilish"siz ta'sir qilmaydi.</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateResultDto>> Generate(GenerateScheduleRequest req, CancellationToken ct = default)
    {
        var (result, error) = await TeachingLoadService.GenerateAsync(db, req, ct);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityScheduleTemplate, result!.Name, "create",
            $"Dars jadvali avtomatik tuzildi: «{result.Name}» — {result.Owners} ta sinf/guruh, "
            + $"{result.Placed}/{result.Total} dars joylashdi" + (result.Unplaced.Count > 0 ? ", qolganlari qo'lda" : ""));
        await db.SaveChangesAsync(ct);
        return result;
    }

    // ------------------------------------------------------------------ eksport

    /// <summary>Tanlangan jadval Excel'da: har kun varag'i va o'qituvchilar varag'i.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Jadval nomi berilmadi" });
        var bytes = await TimetableExport.BuildAsync(db, name, ct);
        if (bytes is null) return NotFound(new { message = "Bu nomdagi jadval topilmadi" });
        return File(bytes, XlsxMime, $"Dars jadvali — {name}.xlsx");
    }
}
