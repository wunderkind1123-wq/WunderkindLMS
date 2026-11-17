using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Arxivlash sabablari GURUHLARI (2026-11-13) — "Sozlamalar → Arxivlash sabablari → Guruhlar".
/// Guruh = arxiv doskasining ustuni; maktab ularni o'zi qo'shadi, nomlaydi, bo'yaydi,
/// tartiblaydi va o'chiradi (lid bosqichlari kabi). docs/modules/archive-reason-groups.md.
///
/// <para>
/// Ruxsat — <see cref="ArchiveReasonsController"/> bilan bir xil: tahrirlash <c>settings</c>,
/// o'qish xodim uchun ochiq (arxiv doskasi va arxivlash oynasi guruh nomlarini ko'rsatadi).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings")]
[Route("api/admin/archive-reason-groups")]
public class ArchiveReasonGroupsController(AppDbContext db) : ControllerBase
{
    /// <summary>Doska va arxivlash oynasi tanlay oladigan ranglar — client <c>stageColors</c> bilan bir xil.</summary>
    public static readonly IReadOnlySet<string> Colors =
        new HashSet<string> { "slate", "blue", "emerald", "amber", "violet", "rose", "cyan", "orange" };

    public const string HasReasonsMessage =
        "Bu guruhda {0} ta sabab bor — avval ularni boshqa guruhga o'tkazing yoki guruhni faolsizlantiring.";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentArchiveReasonGroupDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var groups = await db.StudentArchiveReasonGroups.AsNoTracking()
            .Where(g => includeInactive || g.IsActive)
            .OrderBy(g => g.Position).ThenBy(g => g.Name)
            .ToListAsync(ct);

        var reasons = await db.StudentArchiveReasons.AsNoTracking()
            .Where(r => r.GroupId != null)
            .GroupBy(r => r.GroupId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Hozir arxivda turganlar — doskadagi ustun soni bilan bir xil.
        var students = await (
                from s in db.Students.AsNoTracking()
                join r in db.StudentArchiveReasons.AsNoTracking() on s.ArchiveReasonId equals r.Id
                where s.IsArchived && r.GroupId != null
                group s by r.GroupId!.Value into g
                select new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return groups.Select(g => ToDto(g, reasons.GetValueOrDefault(g.Id), students.GetValueOrDefault(g.Id))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StudentArchiveReasonGroupDto>> Create(
        SaveArchiveReasonGroupRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (await Invalid(name, req.Color, req.Position, null, ct) is { } bad) return BadRequest(new { message = bad });

        // Tartib berilmasa — oxiriga.
        var position = req.Position
            ?? (await db.StudentArchiveReasonGroups.MaxAsync(g => (int?)g.Position, ct) ?? -1) + 1;

        var row = new StudentArchiveReasonGroup
        {
            Name = name,
            Color = req.Color ?? "slate",
            Position = position,
            IsActive = req.IsActive ?? true,
        };
        db.StudentArchiveReasonGroups.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row, 0, 0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentArchiveReasonGroupDto>> Update(
        Guid id, SaveArchiveReasonGroupRequest req, CancellationToken ct = default)
    {
        var row = await db.StudentArchiveReasonGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (row is null) return NotFound();

        var name = (req.Name ?? "").Trim();
        if (await Invalid(name, req.Color, req.Position, id, ct) is { } bad) return BadRequest(new { message = bad });

        row.Name = name;
        if (req.Color is { } color) row.Color = color;
        if (req.Position is { } position) row.Position = position;
        if (req.IsActive is { } active) row.IsActive = active;
        await db.SaveChangesAsync(ct);

        var reasons = await db.StudentArchiveReasons.CountAsync(r => r.GroupId == id, ct);
        var students = await (
            from s in db.Students
            join r in db.StudentArchiveReasons on s.ArchiveReasonId equals r.Id
            where s.IsArchived && r.GroupId == id
            select s.Id).CountAsync(ct);
        return ToDto(row, reasons, students);
    }

    /// <summary>
    /// Ustunlar tartibi bir so'rovda (doskada/sozlamada sudrab o'zgartirilganda): id'lar yangi tartibda.
    /// Ro'yxatda yo'q guruhlar o'z o'rnida qoladi (oxirida).
    /// </summary>
    [HttpPut("order")]
    public async Task<IActionResult> Reorder([FromBody] IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var rows = await db.StudentArchiveReasonGroups.ToListAsync(ct);
        var index = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var rest = rows.Where(r => !index.ContainsKey(r.Id)).OrderBy(r => r.Position).ToList();
        foreach (var r in rows.Where(r => index.ContainsKey(r.Id))) r.Position = index[r.Id];
        for (var i = 0; i < rest.Count; i++) rest[i].Position = ids.Count + i;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// O'chirish — faqat ichida sabab yo'q guruh. Aks holda o'sha sabablar bilan ketgan o'quvchilar
    /// jimgina "Sababi aniqlanmagan"ga tushib qolardi (FK RESTRICT ham shuni qo'riqlaydi).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.StudentArchiveReasonGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (row is null) return NoContent();

        var reasons = await db.StudentArchiveReasons.CountAsync(r => r.GroupId == id, ct);
        if (reasons > 0) return BadRequest(new { message = string.Format(HasReasonsMessage, reasons) });

        db.StudentArchiveReasonGroups.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> Invalid(string name, string? color, int? position, Guid? self, CancellationToken ct)
    {
        if (name.Length == 0) return "Guruh nomini yozing";
        if (name.Length > 60) return "Guruh nomi 60 belgidan oshmasin";
        if (color is not null && !Colors.Contains(color)) return "Bunday rang yo'q";
        if (position is < 0) return "Tartib raqami manfiy bo'lmasin";
        if (await db.StudentArchiveReasonGroups.AnyAsync(g => g.Name == name && g.Id != self, ct))
            return "Bunday nomli guruh allaqachon bor";
        return null;
    }

    private static StudentArchiveReasonGroupDto ToDto(StudentArchiveReasonGroup g, int reasons, int students) =>
        new(g.Id, g.Name, g.Color, g.Position, g.IsActive, reasons, students);
}
