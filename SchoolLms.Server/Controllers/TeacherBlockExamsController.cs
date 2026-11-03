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
/// <b>Blok test natijalari</b> — o'qituvchi paneli (mijoz, 2026-11-03). O'qituvchi dars jadvalida o'zi
/// o'tadigan yo'nalish va fan bo'yicha natija kiritadi. Qoidalar:
/// <see cref="TeacherBlockExamService"/>; bu sinf faqat HTTP'ni tarjima qiladi.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/block-exams")]
public sealed class TeacherBlockExamsController(AppDbContext db) : ControllerBase
{
    private const string TeacherNotFoundMessage = "O'qituvchi topilmadi";

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary><c>GET /api/teacher/block-exams</c> — har (imtihon, yo'nalish) uchun bitta qator.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ExamTeacherRowDto>>> List(CancellationToken ct = default)
    {
        if (await TeacherIdAsync(ct) is not { } teacherId) return NotFound(new { message = TeacherNotFoundMessage });
        return await TeacherBlockExamService.ListAsync(db, teacherId, ct);
    }

    /// <summary><c>GET /api/teacher/block-exams/{id}/entry?ownerId=</c> — o'qituvchi bloklari, shu yo'nalish o'quvchilari.</summary>
    [HttpGet("{id:guid}/entry")]
    public async Task<ActionResult<ExamEntryTableDto>> Entry(string id, [FromQuery] string? ownerId, CancellationToken ct = default)
    {
        if (await TeacherIdAsync(ct) is not { } teacherId) return NotFound(new { message = TeacherNotFoundMessage });
        var result = await TeacherBlockExamService.EntryAsync(db, teacherId, id, ownerId, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>POST /api/teacher/block-exams/{id}/entry</c> <c>{ ownerId, rows }</c> → <c>{ saved }</c>.</summary>
    [HttpPost("{id:guid}/entry")]
    public async Task<ActionResult<ExamEntrySaveResultDto>> Save(
        string id, ExamTeacherSaveRequest request, CancellationToken ct = default)
    {
        if (await TeacherIdAsync(ct) is not { } teacherId) return NotFound(new { message = TeacherNotFoundMessage });
        var actor = new ExamActor(Uid, User.FindFirst(ClaimTypes.Name)?.Value ?? "O'qituvchi");
        var result = await TeacherBlockExamService.SaveAsync(db, actor, teacherId, id, request, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    // ------------------------------------------------------------------

    private async Task<string?> TeacherIdAsync(CancellationToken ct) =>
        Uid is null ? null : await db.Teachers.AsNoTracking().Where(t => t.UserId == Uid).Select(t => t.Id).FirstOrDefaultAsync(ct);

    private ObjectResult Fail(ExamError error) => StatusCode(error.Status, error.ToBody());
}
