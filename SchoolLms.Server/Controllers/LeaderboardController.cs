using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'quvchilar leaderboardi (client, 2026-11-08; LeaderboardService has the score).
//
//    GET    /api/admin/leaderboard?scope=all|class|track&id=   — the ranking for the academic year
//    GET    /api/admin/leaderboard/reasons                      — reasons for hand-added points
//    POST   /api/admin/leaderboard/reasons                      — a new reason (audited)
//    PUT    /api/admin/leaderboard/reasons/{id}                 — edit a reason (audited)
//    DELETE /api/admin/leaderboard/reasons/{id}                 — delete a reason; awards stay (audited)
//    GET    /api/admin/leaderboard/awards?studentId=            — hand-added points of the year
//    POST   /api/admin/leaderboard/awards                       — add points to a pupil (audited)
//    DELETE /api/admin/leaderboard/awards/{id}                  — take them back (audited)
//
//  The O'quv bo'limi section's permission ("students"): the leaderboard lives there (client, 2026-11-08).
// ===========================================================================

public sealed record LeaderboardReasonRequest(string? Name, int Points, bool? IsActive);
public sealed record LeaderboardAwardRequest(string? StudentId, Guid? ReasonId, string? ReasonName, int? Points, string? Note, string? Date);

[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/leaderboard")]
public class LeaderboardController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MaxPoints = 1000;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? scope, [FromQuery] string? id, CancellationToken ct)
    {
        var result = await LeaderboardService.BuildAsync(db, AppClock.Today, ct);
        IEnumerable<LeaderboardRow> rows = scope switch
        {
            "class" => result.Rows.Where(r => r.ClassId == id),
            "track" => result.Rows.Where(r => id != null && r.TrackIds.Contains(id)),
            _ => result.Rows,
        };

        // Competition ranking: equal totals share a place (1, 2, 2, 4).
        var ranked = LeaderboardService.Rank(rows).Select(x => new
        {
            x.Rank, x.Row.StudentId, x.Row.FullName, x.Row.ClassId, x.Row.ClassName, x.Row.TrackIds,
            x.Row.Grades, x.Row.GradeCount, x.Row.IsTrack, x.Row.Interim, x.Row.InterimCount,
            x.Row.Attendance, x.Row.Lessons, x.Row.Absent, x.Row.MonthlyTest, x.Row.MonthlyCount,
            x.Row.Behaviour, x.Row.ManualPoints, x.Row.Base, x.Row.Total,
        }).ToList();

        return Ok(new
        {
            From = result.From.ToString("yyyy-MM-dd"),
            To = result.To.ToString("yyyy-MM-dd"),
            Weights = new
            {
                Grades = LeaderboardService.GradesWeight, Attendance = LeaderboardService.AttendanceWeight,
                Monthly = LeaderboardService.MonthlyWeight, Behaviour = LeaderboardService.BehaviourWeight,
                BehaviourFull = LeaderboardService.BehaviourFull,
            },
            result.Classes,
            result.Tracks,
            Rows = ranked,
        });
    }

    // =====================================================================
    //  Reasons
    // =====================================================================

    [HttpGet("reasons")]
    public async Task<IActionResult> Reasons(CancellationToken ct) =>
        Ok(await db.LeaderboardReasons.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct));

    [HttpPost("reasons")]
    public async Task<IActionResult> CreateReason(LeaderboardReasonRequest req, CancellationToken ct)
    {
        if (CheckReason(req) is { } error) return BadRequest(new { message = error });
        var reason = new LeaderboardReason
        {
            Name = req.Name!.Trim(), Points = req.Points, IsActive = req.IsActive ?? true, CreatedAt = AppClock.NowInstant,
        };
        db.LeaderboardReasons.Add(reason);
        audit.Record("LeaderboardReason", reason.Id.ToString(), "create",
            $"Leaderboard sababi qo'shildi: \"{reason.Name}\" ({Signed(reason.Points)})", null, new { reason.Name, reason.Points });
        await db.SaveChangesAsync(ct);
        return Ok(reason);
    }

    [HttpPut("reasons/{id:guid}")]
    public async Task<IActionResult> UpdateReason(Guid id, LeaderboardReasonRequest req, CancellationToken ct)
    {
        var reason = await db.LeaderboardReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reason is null) return NotFound(new { message = "Sabab topilmadi" });
        if (CheckReason(req) is { } error) return BadRequest(new { message = error });
        var before = new { reason.Name, reason.Points, reason.IsActive };
        reason.Name = req.Name!.Trim();
        reason.Points = req.Points;
        if (req.IsActive is { } active) reason.IsActive = active;
        audit.Record("LeaderboardReason", id.ToString(), "update", $"Leaderboard sababi o'zgartirildi: \"{reason.Name}\"",
            before, new { reason.Name, reason.Points, reason.IsActive });
        await db.SaveChangesAsync(ct);
        return Ok(reason);
    }

    [HttpDelete("reasons/{id:guid}")]
    public async Task<IActionResult> DeleteReason(Guid id, CancellationToken ct)
    {
        var reason = await db.LeaderboardReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reason is null) return NotFound(new { message = "Sabab topilmadi" });
        db.LeaderboardReasons.Remove(reason);
        audit.Record("LeaderboardReason", id.ToString(), "delete", $"Leaderboard sababi o'chirildi: \"{reason.Name}\"",
            new { reason.Name, reason.Points }, null);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? CheckReason(LeaderboardReasonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Sabab nomini kiriting";
        if (req.Name.Trim().Length > 120) return "Sabab nomi 120 belgidan oshmasin";
        if (req.Points == 0 || Math.Abs(req.Points) > MaxPoints) return $"Ball 0 bo'lmasin va ±{MaxPoints} dan oshmasin";
        return null;
    }

    // =====================================================================
    //  Awards
    // =====================================================================

    [HttpGet("awards")]
    public async Task<IActionResult> Awards([FromQuery] string? studentId, CancellationToken ct)
    {
        var (from, to) = await LeaderboardService.YearAsync(db, AppClock.Today, ct);
        var q = db.LeaderboardAwards.AsNoTracking().Where(a => a.Date >= from && a.Date <= to);
        if (!string.IsNullOrWhiteSpace(studentId)) q = q.Where(a => a.StudentId == studentId);
        var awards = await q.OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        var ids = awards.Select(a => a.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.FullName, s.ClassName }, ct);
        return Ok(awards.Select(a => new
        {
            a.Id, a.StudentId, StudentName = students.GetValueOrDefault(a.StudentId)?.FullName ?? "",
            ClassName = students.GetValueOrDefault(a.StudentId)?.ClassName ?? "",
            a.ReasonId, a.ReasonName, a.Points, a.Note, Date = a.Date.ToString("yyyy-MM-dd"), a.CreatedBy, a.CreatedAt,
        }));
    }

    [HttpPost("awards")]
    public async Task<IActionResult> CreateAward(LeaderboardAwardRequest req, CancellationToken ct)
    {
        var student = string.IsNullOrWhiteSpace(req.StudentId) ? null
            : await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StudentId && !s.IsArchived, ct);
        if (student is null) return BadRequest(new { message = "O'quvchini tanlang" });

        LeaderboardReason? reason = null;
        if (req.ReasonId is { } reasonId)
        {
            reason = await db.LeaderboardReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reasonId, ct);
            if (reason is null) return BadRequest(new { message = "Sabab topilmadi" });
        }
        var name = (reason?.Name ?? req.ReasonName ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Sababni tanlang yoki yozing" });
        if (name.Length > 120) return BadRequest(new { message = "Sabab 120 belgidan oshmasin" });
        var points = req.Points ?? reason?.Points ?? 0;
        if (points == 0 || Math.Abs(points) > MaxPoints)
            return BadRequest(new { message = $"Ball 0 bo'lmasin va ±{MaxPoints} dan oshmasin" });
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > 500 }) return BadRequest(new { message = "Izoh 500 belgidan oshmasin" });
        var date = DateOnly.TryParseExact(req.Date, "yyyy-MM-dd", out var d) ? d : AppClock.Today;

        var award = new LeaderboardAward
        {
            StudentId = student.Id, ReasonId = reason?.Id, ReasonName = name, Points = points, Note = note, Date = date,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            CreatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? "",
            CreatedAt = AppClock.NowInstant,
        };
        db.LeaderboardAwards.Add(award);
        audit.Record("LeaderboardAward", award.Id.ToString(), "create",
            $"{student.FullName}: leaderboard {Signed(points)} ball — \"{name}\"", null,
            new { name, points, note, date = date.ToString("yyyy-MM-dd") }, studentId: student.Id);
        await db.SaveChangesAsync(ct);
        return Ok(new { award.Id });
    }

    [HttpDelete("awards/{id:guid}")]
    public async Task<IActionResult> DeleteAward(Guid id, CancellationToken ct)
    {
        var award = await db.LeaderboardAwards.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (award is null) return NotFound(new { message = "Yozuv topilmadi" });
        db.LeaderboardAwards.Remove(award);
        audit.Record("LeaderboardAward", id.ToString(), "delete",
            $"Leaderboard balli bekor qilindi: {Signed(award.Points)} — \"{award.ReasonName}\"",
            new { award.ReasonName, award.Points, award.Note }, null, studentId: award.StudentId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string Signed(int points) => points > 0 ? $"+{points}" : points.ToString();
}
