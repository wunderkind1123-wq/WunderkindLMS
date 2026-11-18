using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Bloklar — birlashgan va daraja guruhlari (docs/modules/blocks.md, TZ 6.1–6.2).
///
/// <para>
/// <b>Ruxsat: <c>classes</c></b> — <see cref="StudyGroupsController"/> bilan bir xil darvoza:
/// blok guruhi o'quv guruhi ustiga qurilgan va o'quv bo'limidan boshqariladi.
/// </para>
/// <para>
/// <b>Bu controller dars jadvaliga TEGMAYDI</b> (4-bosqich). Blok yaratish shablon,
/// hafta biriktirish, jurnal yoki davomat qatorini yozmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes")]
[Route("api/admin/blocks")]
public class BlocksController(AppDbContext db, AuditService audit) : ControllerBase
{
    private BlockService Service => new(db);

    private string UserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /* ===================================================================
     *  1. Ro'yxat va kartochka
     * ================================================================ */

    /// <summary>Bloklar ro'yxati (TZ 6.1): nomi, fan, sinflar, guruhlar, ustozlar, o'quvchilar soni.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BlockDto>>> GetAll(
        [FromQuery] string? academicYear = null, CancellationToken ct = default)
    {
        var q = db.Blocks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(academicYear)) q = q.Where(b => b.AcademicYear == academicYear);
        var blocks = await q.OrderBy(b => b.Name).ToListAsync(ct);
        var result = new List<BlockDto>();
        foreach (var b in blocks) result.Add(await ToDtoAsync(b, ct));
        return result;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BlockDto>> Get(Guid id, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        return block is null ? NotFound() : await ToDtoAsync(block, ct);
    }

    /// <summary>
    /// Faol bloklardagi ogohlantirishlar (2- va 6-qoidalar): taqsimlanmagan o'quvchilar,
    /// ustozsiz guruh. Kunlik ogohlantirish shu ro'yxatni ko'rsatadi.
    /// </summary>
    [HttpGet("warnings")]
    public async Task<IActionResult> Warnings(CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var result = new List<object>();
        foreach (var b in await db.Blocks.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct))
        {
            var errors = await Service.ActivationErrorsAsync(b, today, ct);
            if (errors.Count > 0) result.Add(new { blockId = b.Id, name = b.Name, errors });
        }
        return Ok(result);
    }

    /* ===================================================================
     *  2. Yaratish, tahrirlash, faollashtirish, o'chirish
     * ================================================================ */

    [HttpPost]
    public async Task<ActionResult<BlockDto>> Create(SaveBlockRequest req, CancellationToken ct = default)
    {
        var error = await Service.ValidateAsync(req, existing: null, ct);
        if (error is not null) return BadRequest(new { message = error });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var block = await Service.CreateAsync(req, UserId, ct);
        await db.SaveChangesAsync(ct);

        // Manba sinf bo'yicha blok: shu sinflar o'quvchilari darhol o'z guruhida (TZ 6.1).
        await Service.AutoAssignAsync(block, AppClock.Today, UserId, ct);

        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "create",
            $"Yangi blok: {block.Name}",
            after: new { block.Name, block.SubjectId, block.BlockType, block.DayPattern, block.AcademicYear });
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        await tx.CommitAsync(ct);
        return await Get(block.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BlockDto>> Update(Guid id, SaveBlockRequest req, CancellationToken ct = default)
    {
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var error = await Service.ValidateAsync(req, block, ct);
        if (error is not null) return BadRequest(new { message = error });

        var before = new { block.Name, block.DayPattern };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Service.UpdateAsync(block, req, ct);
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update",
            $"Blok yangilandi: {block.Name}", before: before, after: new { block.Name, block.DayPattern });
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        await tx.CommitAsync(ct);
        return await Get(block.Id, ct);
    }

    /// <summary>
    /// Faollashtirish (TZ 7, 2-qoida): avval manba sinf o'quvchilari avtomatik joylanadi,
    /// keyin hamma taqsimlangani va har guruhda ustoz borligi tekshiriladi. To'siq bo'lsa —
    /// blok faol EMAS qoladi va xatolar ro'yxati qaytadi.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<BlockActivationDto>> Activate(Guid id, CancellationToken ct = default)
    {
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        if (block.IsActive) return new BlockActivationDto(true, []);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Service.AutoAssignAsync(block, AppClock.Today, UserId, ct);
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });

        var errors = await Service.ActivationErrorsAsync(block, AppClock.Today, ct);
        if (errors.Count == 0)
        {
            block.IsActive = true;
            audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update", $"Blok faollashtirildi: {block.Name}");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return errors.Count == 0
            ? new BlockActivationDto(true, [])
            : UnprocessableEntity(new BlockActivationDto(false, errors));
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct = default)
    {
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        if (!block.IsActive) return NoContent();
        block.IsActive = false;
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update", $"Blok faolsizlantirildi: {block.Name}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var error = await Service.DeleteAsync(block, ct);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "delete", $"Blok o'chirildi: {block.Name}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    /* ===================================================================
     *  3. Ustoz (sanali)
     * ================================================================ */

    /// <summary>Ustozni almashtirish (TZ 6.1): shu sanadan yangi ustoz, eski baholar eski ustozda.</summary>
    [HttpPost("groups/{groupId:guid}/teacher")]
    public async Task<ActionResult<BlockDto>> ChangeTeacher(
        Guid groupId, BlockTeacherChangeRequest req, CancellationToken ct = default)
    {
        var group = await db.BlockGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return NotFound();
        var from = req.Date ?? AppClock.Today;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var error = await Service.ChangeTeacherAsync(group, req.TeacherId, from, UserId, ct);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityBlock, group.BlockId.ToString(), "update",
            $"Blok guruhi ustozi almashtirildi: {group.Name}", after: new { req.TeacherId, From = from },
            teacherId: req.TeacherId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await Get(group.BlockId, ct);
    }

    /// <summary>Guruh ustozlari tarixi — kim, qaysi sanadan qaysi sanagacha.</summary>
    [HttpGet("groups/{groupId:guid}/teachers")]
    public async Task<IActionResult> TeacherHistory(Guid groupId, CancellationToken ct = default)
    {
        var rows = await db.BlockGroupTeachers.AsNoTracking()
            .Where(t => t.BlockGroupId == groupId)
            .Join(db.Teachers, t => t.TeacherId, x => x.Id,
                (t, x) => new { t.TeacherId, teacherName = x.FullName, t.ValidFrom, t.ValidTo })
            .OrderBy(t => t.ValidFrom).ToListAsync(ct);
        return Ok(rows);
    }

    /* ===================================================================
     *  4. Taqsimlash
     * ================================================================ */

    /// <summary>Taqsimlash ekrani (TZ 6.2): ikki guruh va "Taqsimlanmagan", shu sanada.</summary>
    [HttpGet("{id:guid}/roster")]
    public async Task<ActionResult<BlockRosterDto>> Roster(
        Guid id, [FromQuery] DateOnly? date = null, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        return await Service.RosterAsync(block, date ?? AppClock.Today, ct);
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<BlockRosterDto>> Assign(Guid id, BlockAssignRequest req, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var on = req.Date ?? AppClock.Today;

        var error = await Service.AssignAsync(block, req.StudentIds, req.GroupId, on, UserId, ct);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update",
            $"Blokka taqsimlandi: {block.Name} ({req.StudentIds.Count} o'quvchi)",
            after: new { req.GroupId, req.StudentIds, From = on });
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        return await Service.RosterAsync(block, on, ct);
    }

    /// <summary>Yil o'rtasida ko'chirish (TZ 6.2): sana (standart: bugun) va sabab.</summary>
    [HttpPost("{id:guid}/move")]
    public async Task<ActionResult<BlockRosterDto>> Move(Guid id, BlockMoveRequest req, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var on = req.Date ?? AppClock.Today;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        string? error;
        try
        {
            error = await Service.MoveAsync(block, req.StudentIds, req.GroupId, on, req.Reason, UserId, ct);
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsOneGroupPerSubjectViolation(ex))
        {
            return Conflict(new { message = StudyGroupService.OneGroupPerSubjectMessage });
        }
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update",
            $"Blokda ko'chirildi: {block.Name} ({req.StudentIds.Count} o'quvchi)",
            after: new { req.GroupId, req.StudentIds, From = on, req.Reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await Service.RosterAsync(block, on, ct);
    }

    [HttpPost("{id:guid}/unassign")]
    public async Task<ActionResult<BlockRosterDto>> Unassign(
        Guid id, BlockUnassignRequest req, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var on = req.Date ?? AppClock.Today;

        var error = await Service.UnassignAsync(block, req.StudentIds, on, req.Reason, ct);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AuditService.EntityBlock, block.Id.ToString(), "update",
            $"Blokdan chiqarildi: {block.Name} ({req.StudentIds.Count} o'quvchi)",
            after: new { req.StudentIds, From = on, req.Reason });
        await db.SaveChangesAsync(ct);
        return await Service.RosterAsync(block, on, ct);
    }

    /// <summary>Manba sinf bo'yicha blokda: yangi kelgan o'quvchilarni o'z guruhiga joylash.</summary>
    [HttpPost("{id:guid}/auto-assign")]
    public async Task<IActionResult> AutoAssign(Guid id, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var added = await Service.AutoAssignAsync(block, AppClock.Today, UserId, ct);
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        return Ok(new { added });
    }

    /* ===================================================================
     *  5. Excel: ro'yxatni yuklab olish va import (TZ 6.2)
     * ================================================================ */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Import ustunlari: o'quvchi ID, blok, guruh (TZ 6.2). F.I.SH va manba — faqat
    /// o'qish uchun, import ularni e'tiborga olmaydi.</summary>
    public static readonly string[] ImportHeaders = ["O'quvchi ID", "Blok", "Guruh", "F.I.SH", "Manba"];

    /// <summary>Blok ro'yxati AYNAN import shaklida: "Guruh" ustunini to'ldirib qayta yuklash mumkin.</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] DateOnly? date = null, CancellationToken ct = default)
    {
        var block = await db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        var roster = await Service.RosterAsync(block, date ?? AppClock.Today, ct);
        var rows = roster.Groups
            .SelectMany(g => g.Members.Select(m => (m, g.Name)))
            .Concat(roster.Unassigned.Select(m => (m, "")))
            .OrderBy(x => x.m.SourceName, StringComparer.Ordinal).ThenBy(x => x.m.FullName, StringComparer.Ordinal)
            .Select(x => (IReadOnlyList<string>)[x.m.StudentId, block.Name, x.Item2, x.m.FullName, x.m.SourceName]);
        var bytes = ExcelExport.Build("Taqsimlash", ImportHeaders, rows);
        return File(bytes, XlsxMime, $"blok_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Excel'dan taqsimlash. Har qator: o'quvchi taqsimlanmagan bo'lsa — guruhga qo'shiladi,
    /// boshqa guruhda bo'lsa — <paramref name="date"/> dan ko'chiriladi, "Guruh" bo'sh bo'lsa —
    /// tegilmaydi. HAMMASI YOKI HECH NARSA: birorta qatorda xato bo'lsa, hech narsa saqlanmaydi
    /// va xatolar qator raqami bilan qaytadi.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import(
        IFormFile? file, [FromQuery] DateOnly? date = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, ImportHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        var on = date ?? AppClock.Today;
        var year = BlockService.AcademicYearOf(on);
        var blocks = (await db.Blocks.AsNoTracking().Where(b => b.AcademicYear == year).ToListAsync(ct))
            .ToDictionary(b => b.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var errors = new List<object>();
        int assigned = 0, moved = 0, unchanged = 0;
        var seen = new HashSet<(Guid, string)>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // 0-qator — sarlavha; Excel'dagi qator raqami = indeks + 1.
        for (var i = 1; i < rows.Count; i++)
        {
            var (studentId, blockName, groupName) = (rows[i][0].Trim(), rows[i][1].Trim(), rows[i][2].Trim());
            if (studentId == "" && blockName == "" && groupName == "") continue;
            var rowNo = i + 1;
            if (groupName == "") { unchanged++; continue; }
            if (!blocks.TryGetValue(blockName, out var block))
            {
                errors.Add(new { row = rowNo, message = $"«{blockName}» bloki topilmadi" });
                continue;
            }
            if (!seen.Add((block.Id, studentId)))
            {
                errors.Add(new { row = rowNo, message = "O'quvchi shu blokda takrorlangan" });
                continue;
            }
            var groups = await Service.GroupsAsync(block.Id, ct);
            var target = groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                errors.Add(new { row = rowNo, message = $"«{block.Name}» blokida «{groupName}» guruhi yo'q" });
                continue;
            }

            var current = await Service.GroupOnAsync(block.Id, studentId, on, ct);
            string? error;
            if (current?.Id == target.Id) { unchanged++; continue; }
            if (current is null)
            {
                error = await Service.AssignAsync(block, [studentId], target.Id, on, UserId, ct);
                if (error is null) { await db.SaveChangesAsync(ct); assigned++; }
            }
            else
            {
                error = await Service.MoveAsync(block, [studentId], target.Id, on, "Excel import", UserId, ct);
                if (error is null) moved++;
            }
            if (error is not null) errors.Add(new { row = rowNo, message = error });
        }

        if (errors.Count > 0)
            return BadRequest(new { message = "Faylda xatolar bor — hech narsa saqlanmadi", errors });

        audit.Record(AuditService.EntityBlock, "import", "update",
            $"Bloklar Excel'dan taqsimlandi: {assigned} qo'shildi, {moved} ko'chirildi");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { assigned, moved, unchanged });
    }

    /* ===================================================================
     *  6. O'quvchi: shu sanadagi guruh, tarix, ko'chirishlar hisoboti
     * ================================================================ */

    /// <summary>O'quvchining shu sanadagi blok guruhi (TZ 5.3). Guruhda bo'lmasa — 204.</summary>
    [HttpGet("{id:guid}/students/{studentId}/group")]
    public async Task<IActionResult> StudentGroup(
        Guid id, string studentId, [FromQuery] DateOnly? date = null, CancellationToken ct = default)
    {
        var on = date ?? AppClock.Today;
        var group = await Service.GroupOnAsync(id, studentId, on, ct);
        if (group is null) return NoContent();
        var teacher = await Service.TeacherOnAsync(group.Id, on, ct);
        var teacherName = teacher is null
            ? null
            : await db.Teachers.Where(t => t.Id == teacher.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync(ct);
        return Ok(new BlockStudentGroupDto(id, group.Id, group.Name, teacher?.TeacherId, teacherName));
    }

    /// <summary>O'quvchi profilidagi "Guruhlar tarixi" (TZ 6.2): qachon, qaysi guruh, kim ko'chirgan.</summary>
    [HttpGet("students/{studentId}/history")]
    public async Task<ActionResult<IEnumerable<BlockHistoryItemDto>>> History(string studentId, CancellationToken ct = default)
    {
        var rows = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .Join(db.BlockGroups, m => m.GroupId, g => g.StudyGroupId, (m, g) => new { m, g })
            .Join(db.Blocks, x => x.g.BlockId, b => b.Id, (x, b) => new { x.m, x.g, b })
            .Join(db.Subjects, x => x.b.SubjectId, s => s.Id, (x, s) => new { x.m, x.g, x.b, SubjectName = s.Name })
            .OrderByDescending(x => x.m.JoinedOn).ToListAsync(ct);
        var names = await UserNamesAsync(rows.Select(r => r.m.CreatedBy), ct);
        return rows.Select(r => new BlockHistoryItemDto(
            r.b.Id, r.b.Name, r.SubjectName, r.g.Id, r.g.StudyGroupId, r.g.Name, r.m.JoinedOn, r.m.LeftOn,
            r.m.JoinReason, r.m.LeaveReason, names.GetValueOrDefault(r.m.CreatedBy))).ToList();
    }

    /// <summary>
    /// "Ko'chirishlar" hisoboti (TZ 6.2): davrda bir blok guruhidan ikkinchisiga o'tganlar.
    /// Ko'chirish — bitta blokda D-1 da yopilgan va D da ochilgan ikki a'zolik.
    /// </summary>
    [HttpGet("moves")]
    public async Task<ActionResult<IEnumerable<BlockMoveItemDto>>> Moves(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var end = to ?? AppClock.Today;
        var start = from ?? end.AddDays(-6);
        var rows = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.JoinedOn >= start && m.JoinedOn <= end)
            .Join(db.BlockGroups, m => m.GroupId, g => g.StudyGroupId, (m, g) => new { m, g })
            .ToListAsync(ct);
        if (rows.Count == 0) return new List<BlockMoveItemDto>();

        var studentIds = rows.Select(r => r.m.StudentId).Distinct().ToList();
        var blockIds = rows.Select(r => r.g.BlockId).Distinct().ToList();
        var previous = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => studentIds.Contains(m.StudentId) && m.LeftOn != null)
            .Join(db.BlockGroups.Where(g => blockIds.Contains(g.BlockId)), m => m.GroupId, g => g.StudyGroupId,
                (m, g) => new { m.StudentId, m.LeftOn, g.BlockId, g.Name, g.Id })
            .ToListAsync(ct);
        var blocks = await db.Blocks.AsNoTracking().Where(b => blockIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var names = await UserNamesAsync(rows.Select(r => r.m.CreatedBy), ct);

        var result = new List<BlockMoveItemDto>();
        foreach (var r in rows)
        {
            var prev = previous.FirstOrDefault(p => p.StudentId == r.m.StudentId && p.BlockId == r.g.BlockId
                && p.Id != r.g.Id && p.LeftOn == r.m.JoinedOn.AddDays(-1));
            if (prev is null) continue;
            result.Add(new BlockMoveItemDto(r.m.StudentId, students.GetValueOrDefault(r.m.StudentId, ""),
                r.g.BlockId, blocks.GetValueOrDefault(r.g.BlockId, ""), prev.Name, r.g.Name, r.m.JoinedOn,
                r.m.JoinReason, names.GetValueOrDefault(r.m.CreatedBy)));
        }
        return result.OrderBy(x => x.Date).ThenBy(x => x.FullName, StringComparer.Ordinal).ToList();
    }

    /* ===================================================================
     *  Yordamchilar
     * ================================================================ */

    private async Task<Dictionary<string, string>> UserNamesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private async Task<string?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsOneGroupPerSubjectViolation(ex))
        {
            return StudyGroupService.OneGroupPerSubjectMessage;
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsDuplicateNameViolation(ex))
        {
            return "Shu nomli guruh fanda allaqachon bor — blok yoki guruh nomini o'zgartiring";
        }
    }

    private async Task<BlockDto> ToDtoAsync(Block block, CancellationToken ct)
    {
        var today = AppClock.Today;
        var subject = await db.Subjects.AsNoTracking().Where(s => s.Id == block.SubjectId)
            .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var roster = await Service.RosterAsync(block, today, ct);
        var groups = await Service.GroupsAsync(block.Id, ct);

        var groupDtos = new List<BlockGroupDto>();
        foreach (var g in groups)
        {
            var t = await Service.TeacherOnAsync(g.Id, today, ct)
                    ?? await db.BlockGroupTeachers.AsNoTracking()
                        .Where(x => x.BlockGroupId == g.Id && x.ValidTo == null).FirstOrDefaultAsync(ct);
            var teacherName = t is null
                ? null
                : await db.Teachers.Where(x => x.Id == t.TeacherId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
            groupDtos.Add(new BlockGroupDto(g.Id, g.StudyGroupId, g.Name, g.Code, g.SortOrder,
                g.BaseOwnerKind, g.BaseOwnerId, g.RoomId, t?.TeacherId, teacherName, t?.ValidFrom,
                roster.Groups.First(r => r.GroupId == g.Id).Members.Count));
        }

        var allMembers = roster.Groups.SelectMany(r => r.Members).Concat(roster.Unassigned).ToList();
        var sources = (await Service.SourcesAsync(block.Id, ct)).Select(s => new BlockSourceDto(
            s.Kind, s.Id, s.Name,
            allMembers.Count(m => m.SourceId == s.Id),
            roster.Unassigned.Count(m => m.SourceId == s.Id))).ToList();

        return new BlockDto(block.Id, block.Name, block.SubjectId, subject, block.AcademicYear,
            block.BlockType, block.DayPattern, block.IsActive, sources, groupDtos,
            allMembers.Count, roster.Unassigned.Count);
    }
}
