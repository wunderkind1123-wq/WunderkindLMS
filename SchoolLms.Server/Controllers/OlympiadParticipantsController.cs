using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>Olimpiada → umumiy ishtirokchilar bazasi, bola kartasi, lidga aylantirish, eksport.</summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Manage, GatedRead = true)]
[Route("api/admin/olympiad/participants")]
public sealed class OlympiadParticipantsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<List<OlympiadParticipantDto>> List([FromQuery] string? q, [FromQuery] int? grade, [FromQuery] string? school,
        [FromQuery] string? lead, [FromQuery] Guid? eventId, CancellationToken ct) =>
        await OlympiadQueries.ParticipantsAsync(db, new(q, grade, school, lead, eventId), ct);

    /// <summary>Filtrlar uchun: maktablar ro'yxati.</summary>
    [HttpGet("schools")]
    public async Task<List<string>> Schools(CancellationToken ct) =>
        await db.OlympiadParticipants.AsNoTracking().Select(p => p.School).Distinct().OrderBy(s => s).ToListAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OlympiadParticipantCardDto>> Card(Guid id, CancellationToken ct)
    {
        var p = (await OlympiadQueries.ParticipantsAsync(db, new(Id: id), ct)).FirstOrDefault();
        if (p is null) return NotFound(new { message = "Ishtirokchi topilmadi" });
        return new OlympiadParticipantCardDto(p, await OlympiadQueries.ParticipantRegsAsync(db, id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, OlympiadParticipantInput input, CancellationToken ct)
    {
        var p = await db.OlympiadParticipants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { message = "Ishtirokchi topilmadi" });
        if (OlympiadEventsController.ValidateParticipant(input) is { } err) return BadRequest(new { message = err });
        var before = new { p.FullName, p.Phone, p.Grade, p.School };
        p.FullName = OlympiadText.CleanSpaces(input.FullName);
        p.Phone = OlympiadText.NormalizePhone(input.Phone)!;
        p.Grade = input.Grade;
        p.School = OlympiadText.CleanSpaces(input.School);
        p.UpdatedAt = AppClock.Now;
        audit.RecordChanges("olympiad_participant", id.ToString(), "Olimpiada ishtirokchisi tahrirlandi",
            before, new { p.FullName, p.Phone, p.Grade, p.School });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("convert-leads")]
    public async Task<ActionResult<ConvertLeadsResult>> ConvertLeads(ConvertLeadsInput input, CancellationToken ct)
    {
        if (input.ParticipantIds.Count == 0) return BadRequest(new { message = "Ishtirokchilarni belgilang" });
        var result = await OlympiadQueries.ConvertToLeadsAsync(db, input.ParticipantIds, ct);
        audit.Record("olympiad_participant", "", "convert_leads",
            $"Olimpiada ishtirokchilari lidga aylantirildi: {result.Created} ta yangi, {result.Linked} ta mavjudga bog'landi");
        await db.SaveChangesAsync(ct);
        return result;
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? q, [FromQuery] int? grade, [FromQuery] string? school,
        [FromQuery] string? lead, [FromQuery] Guid? eventId, CancellationToken ct)
    {
        var rows = await OlympiadQueries.ParticipantsAsync(db, new(q, grade, school, lead, eventId), ct);
        var bytes = ExcelExport.Build("Ishtirokchilar",
            ["F.I.Sh", "Telefon", "Sinf", "Maktab", "Ota-ona (Telegram)", "Username", "Tadbirlar", "Qatnashgan", "Eng yaxshi natija", "Lid"],
            rows.Select(r => (IReadOnlyList<string>)[
                r.FullName, r.Phone, r.Grade.ToString(), r.School, r.ParentName, r.ParentUsername.Length > 0 ? "@" + r.ParentUsername : "",
                r.Events.ToString(), r.Attended.ToString(), r.BestPercent is { } b ? OlympiadText.Num(b) + "%" : "",
                r.LeadId is null ? "" : "Lidga aylantirilgan"]));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "olimpiada-ishtirokchilar.xlsx");
    }
}
