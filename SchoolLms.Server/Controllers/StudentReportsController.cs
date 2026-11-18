using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'quvchi hisoboti (PDF) — TZ "O'quvchi hisobotini haftalik/oylik yuklab olish", 2026-11-17, 1-vazifa.
//
//  Staff open or download one pupil's report for any period — last week, this month, a quarter, any
//  range — from the pupil's card. It is the very report parents get on Sunday (WeeklyReport.BuildAsync,
//  one builder for every period), so last week's PDF here carries the numbers the parents' PDF did.
//
//  SENDS NOTHING. No Telegram message, no auto-message run, no row in "Tarix va statistika": the
//  Sunday report goes out exactly as before, whether or not anyone downloaded that week here.
//  Every open/download is written to the audit log (who, which pupil, which period, when).
//
//  WHO: admin / superadmin; staff with the `studentReports` key — for reading too (GatedRead), the
//  report holds a pupil's marks and behaviour. Homeroom teachers come with the class bulk download.
//
//    GET /api/admin/student-reports/periods           — the period choices with their dates (server's today)
//    GET /api/admin/student-reports/{studentId}/pdf   — ?from=yyyy-MM-dd&to=yyyy-MM-dd&view=true|false
// ===========================================================================

/// <summary>One period choice: key, label and dates ("yyyy-MM-dd").</summary>
public sealed record StudentReportPeriodDto(string Key, string Label, string From, string To);

/// <summary>A quarter of the school year, cut at today when it is running.</summary>
public sealed record StudentReportQuarterDto(int Quarter, string From, string To, bool Current);

public sealed record StudentReportPeriodsDto(string Today, List<StudentReportPeriodDto> Presets, List<StudentReportQuarterDto> Quarters);

[ApiController]
[Authorize]
[AdminPerm(PermissionKey, GatedRead = true)]
[Route("api/admin/student-reports")]
public sealed class StudentReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Permission key (client: config/constants.ts).</summary>
    public const string PermissionKey = "studentReports";

    /// <summary>The longest period one PDF covers — a school year and a little.</summary>
    public const int MaxDays = 400;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [HttpGet("periods")]
    public async Task<StudentReportPeriodsDto> Periods(CancellationToken ct)
    {
        var today = AppClock.Today;
        return new StudentReportPeriodsDto(Iso(today), Presets(today), await QuartersAsync(today, ct));
    }

    /// <summary>
    /// Week = Monday–Saturday, as the weekly auto-report. "Shu hafta" — this week's Monday up to today
    /// (Saturday at most); "O'tgan hafta" — the Monday–Saturday before it. Month = the 1st to its last day;
    /// "Shu oy" ends today.
    /// </summary>
    public static List<StudentReportPeriodDto> Presets(DateOnly today)
    {
        var monday = WeeklyReport.MondayOf(today);
        var saturday = monday.AddDays(5);
        var lastMonday = monday.AddDays(-7);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfLast = firstOfMonth.AddMonths(-1);
        return
        [
            new("lastWeek", "O'tgan hafta", Iso(lastMonday), Iso(lastMonday.AddDays(5))),
            new("thisWeek", "Shu hafta", Iso(monday), Iso(today < saturday ? today : saturday)),
            new("lastMonth", "O'tgan oy", Iso(firstOfLast), Iso(firstOfMonth.AddDays(-1))),
            new("thisMonth", "Shu oy", Iso(firstOfMonth), Iso(today)),
        ];
    }

    private async Task<List<StudentReportQuarterDto>> QuartersAsync(DateOnly today, CancellationToken ct)
    {
        var rows = await db.Quarters.AsNoTracking().OrderBy(q => q.Quarter).ToListAsync(ct);
        var result = new List<StudentReportQuarterDto>();
        foreach (var q in rows)
        {
            if (!TryDay(q.StartDate, out var start) || !TryDay(q.EndDate, out var end) || start > today || end < start) continue;
            var running = today <= end;
            result.Add(new StudentReportQuarterDto(q.Quarter, Iso(start), Iso(running ? today : end), running));
        }
        return result;
    }

    /// <summary>
    /// One pupil's report PDF for <paramref name="from"/>–<paramref name="to"/>. <paramref name="view"/> —
    /// opened to look at (audit "Ko'rdi"), otherwise downloaded ("Yuklab oldi"). Sends nothing.
    /// </summary>
    [HttpGet("{studentId}/pdf")]
    public async Task<IActionResult> Pdf(
        string studentId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] bool view, CancellationToken ct)
    {
        if (!TryDay(from, out var start) || !TryDay(to, out var end))
            return BadRequest(new { message = "Sana noto'g'ri (kk.oo.yyyy)" });
        if (end < start) return BadRequest(new { message = "Boshlanish sanasi tugash sanasidan keyin bo'lmasin" });
        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
            return BadRequest(new { message = $"Davr {MaxDays} kundan oshmasin" });

        var model = await WeeklyReport.BuildAsync(db, studentId, start, end, ct);
        if (model is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var pdf = new WeeklyReportDocument(model).Render();
        var fileName = WeeklyReportDocument.StaffFileName(model);

        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityStudentReport, studentId, view ? "view" : "download",
            $"Hisobot (PDF) {(view ? "ko'rildi" : "yuklab olindi")}: {model.StudentName} ({model.ClassName}), {start:dd.MM.yyyy} – {end:dd.MM.yyyy}",
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value, User.FindFirst(ClaimTypes.Name)?.Value,
            after: new { from = Iso(start), to = Iso(end), view },
            studentId: studentId));
        await db.SaveChangesAsync(ct);

        return File(pdf, "application/pdf", fileName);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private static bool TryDay(string? text, out DateOnly day) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", Inv, DateTimeStyles.None, out day);
}
