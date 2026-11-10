using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Oraliq testlar (client, 2026-11-10; rules in InterimTestService).
//
//    teacher: /api/teacher/interim-tests — only the (track group, subject) pairs of their timetable
//    staff:   /api/admin/interim-tests   — the "exams" permission, every track group (audited)
//
//    GET    /options                 — track groups and subjects to create a test in
//    GET    /?groupId&subjectId&month — tests, newest first
//    POST   /                         — new test { groupId, subjectId, date, questionCount, topic }
//    PUT    /{id}                     — date, question count, topic
//    DELETE /{id}
//    GET    /{id}/entry               — roster with results
//    POST   /{id}/entry               — { rows: [{ studentId, correct, absent }] } → { saved }
// ===========================================================================

[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/interim-tests")]
public sealed class TeacherInterimTestsController(AppDbContext db) : ControllerBase
{
    private const string TeacherNotFoundMessage = "O'qituvchi topilmadi";

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) =>
        await ScopeAsync(ct) is { } scope ? Ok(await InterimTestService.OptionsAsync(db, scope, ct)) : NoTeacher();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? groupId, [FromQuery] string? subjectId, [FromQuery] string? month, CancellationToken ct) =>
        await ScopeAsync(ct) is { } scope ? Ok(await InterimTestService.ListAsync(db, scope, groupId, subjectId, month, ct)) : NoTeacher();

    [HttpPost]
    public async Task<IActionResult> Create(InterimTestRequest req, CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope) return NoTeacher();
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "O'qituvchi";
        return Answer(await InterimTestService.CreateAsync(db, scope, req, User.FindFirst(ClaimTypes.NameIdentifier)?.Value, name, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, InterimTestRequest req, CancellationToken ct) =>
        await ScopeAsync(ct) is { } scope ? Answer(await InterimTestService.UpdateAsync(db, scope, id, req, ct)) : NoTeacher();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope) return NoTeacher();
        var result = await InterimTestService.DeleteAsync(db, scope, id, ct);
        return result.Error is { } e ? StatusCode(e.Status, new { message = e.Message }) : NoContent();
    }

    [HttpGet("{id:guid}/entry")]
    public async Task<IActionResult> Entry(Guid id, CancellationToken ct) =>
        await ScopeAsync(ct) is { } scope ? Answer(await InterimTestService.EntryAsync(db, scope, id, ct)) : NoTeacher();

    [HttpPost("{id:guid}/entry")]
    public async Task<IActionResult> Save(Guid id, InterimResultsRequest req, CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope) return NoTeacher();
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "O'qituvchi";
        var result = await InterimTestService.SaveResultsAsync(db, scope, id, req, name, ct);
        return result.Error is { } e ? StatusCode(e.Status, new { message = e.Message }) : Ok(new { saved = result.Value });
    }

    // ------------------------------------------------------------------

    private async Task<HashSet<(Guid GroupId, string SubjectId)>?> ScopeAsync(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var teacherId = uid is null ? null
            : await db.Teachers.AsNoTracking().Where(t => t.UserId == uid).Select(t => t.Id).FirstOrDefaultAsync(ct);
        return teacherId is null ? null : await InterimTestService.TeacherScopeAsync(db, teacherId, ct);
    }

    private NotFoundObjectResult NoTeacher() => NotFound(new { message = TeacherNotFoundMessage });

    private IActionResult Answer<T>(InterimOutcome<T> result) =>
        result.Error is { } e ? StatusCode(e.Status, new { message = e.Message }) : Ok(result.Value);
}

[ApiController]
[Authorize]
[AdminPerm("exams")]
[Route("api/admin/interim-tests")]
public sealed class AdminInterimTestsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) => Ok(await InterimTestService.OptionsAsync(db, null, ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? groupId, [FromQuery] string? subjectId, [FromQuery] string? month, CancellationToken ct) =>
        Ok(await InterimTestService.ListAsync(db, null, groupId, subjectId, month, ct));

    [HttpPost]
    public async Task<IActionResult> Create(InterimTestRequest req, CancellationToken ct)
    {
        var result = await InterimTestService.CreateAsync(db, null, req,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value, User.FindFirst(ClaimTypes.Name)?.Value ?? "", ct);
        if (result.Value is { } t)
        {
            audit.Record("InterimTest", t.Id.ToString(), "create",
                $"Oraliq test qo'shildi: {t.GroupName} · {t.SubjectName}, {t.Date} ({t.QuestionCount} savol)");
            await db.SaveChangesAsync(ct);
        }
        return Answer(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, InterimTestRequest req, CancellationToken ct)
    {
        var result = await InterimTestService.UpdateAsync(db, null, id, req, ct);
        if (result.Value is { } t)
        {
            audit.Record("InterimTest", id.ToString(), "update",
                $"Oraliq test o'zgartirildi: {t.GroupName} · {t.SubjectName}, {t.Date} ({t.QuestionCount} savol)");
            await db.SaveChangesAsync(ct);
        }
        return Answer(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await InterimTestService.DeleteAsync(db, null, id, ct);
        if (result.Error is { } e) return StatusCode(e.Status, new { message = e.Message });
        var t = result.Value!;
        audit.Record("InterimTest", id.ToString(), "delete", $"Oraliq test o'chirildi: {t.Date:yyyy-MM-dd} ({t.QuestionCount} savol)",
            new { t.GroupId, t.SubjectId, Date = t.Date.ToString("yyyy-MM-dd"), t.QuestionCount }, null);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/entry")]
    public async Task<IActionResult> Entry(Guid id, CancellationToken ct) => Answer(await InterimTestService.EntryAsync(db, null, id, ct));

    [HttpPost("{id:guid}/entry")]
    public async Task<IActionResult> Save(Guid id, InterimResultsRequest req, CancellationToken ct)
    {
        var result = await InterimTestService.SaveResultsAsync(db, null, id, req, User.FindFirst(ClaimTypes.Name)?.Value ?? "", ct);
        if (result.Error is { } e) return StatusCode(e.Status, new { message = e.Message });
        if (result.Value > 0)
        {
            audit.Record("InterimTest", id.ToString(), "results", $"Oraliq test natijalari saqlandi: {result.Value} ta o'quvchi");
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { saved = result.Value });
    }

    private IActionResult Answer<T>(InterimOutcome<T> result) =>
        result.Error is { } e ? StatusCode(e.Status, new { message = e.Message }) : Ok(result.Value);
}
