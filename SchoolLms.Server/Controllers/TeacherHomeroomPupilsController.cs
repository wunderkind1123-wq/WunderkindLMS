using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  "Sinfim" — marks and behaviour points of the homeroom teacher's pupils (client, 2026-11-04).
//  Mini App and teacher portal. Read-only; rules in HomeroomPupilProgress.
//
//    GET /api/teacher/homeroom/pupils                      — every pupil's behaviour score
//    GET /api/teacher/homeroom/pupils/{studentId}?quarter= — one pupil's marks and points
//
//  "My pupils" — the red list's circle (HomeroomDirectory.PupilsAsync); anyone else answers 404.
// ===========================================================================

[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/homeroom/pupils")]
public class TeacherHomeroomPupilsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<HomeroomPupilScoreDto>>> Scores(CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        return await HomeroomPupilProgress.ScoresAsync(db, teacher.Id, ct);
    }

    [HttpGet("{studentId}")]
    public async Task<ActionResult<HomeroomPupilProgressDto>> Progress(
        string studentId, [FromQuery] int? quarter, CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        var progress = await HomeroomPupilProgress.ForTeacherAsync(db, teacher.Id, studentId, quarter, ct);
        return progress is null ? NotFound() : progress;
    }

    private async Task<Teacher?> MeAsync(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == uid, ct);
    }
}
