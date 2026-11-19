using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  O'quvchi hisoboti (TZ "O'quvchi hisobotini haftalik/oylik yuklab olish", 2026-11-17, 1-vazifa):
//  the weekly report builder for any period, its weeks, and the staff PDF endpoint that sends nothing.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class StudentReportsTests(ApiFixture fixture)
{
    // ----- Periods and weeks (no database) ----------------------------------

    [Fact]
    public void A_week_belongs_to_the_month_of_its_thursday()
    {
        // November 2026 (TZ example): 02.11, 09.11, 16.11, 23.11 — 30.11–05.12 is December's first week.
        var nov = WeeklyReport.WeeksOf(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), today: new DateOnly(2027, 1, 10));
        Assert.Equal(
            new[] { new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 23) },
            nov);
        Assert.Equal("Noyabr, 3-hafta", WeeklyReport.WeekTitle(new DateOnly(2026, 11, 16)));
        Assert.Equal("Dekabr, 1-hafta", WeeklyReport.WeekTitle(new DateOnly(2026, 11, 30)));

        // October 2026 starts on a Thursday: its first week is 28.09–03.10, and it has five.
        var oct = WeeklyReport.WeeksOf(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), today: new DateOnly(2027, 1, 10));
        Assert.Equal(5, oct.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), oct[0]);
        Assert.Equal("Oktabr, 1-hafta", WeeklyReport.WeekTitle(oct[0]));

        // "Shu oy" up to today lists the week still running.
        var running = WeeklyReport.WeeksOf(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 18), today: new DateOnly(2026, 11, 18));
        Assert.Equal(new DateOnly(2026, 11, 16), running[^1]);
    }

    [Fact]
    public void Scope_and_previous_period()
    {
        Assert.Equal(ReportScope.Week, WeeklyReport.ScopeOf(new(2026, 11, 9), new(2026, 11, 14)));
        Assert.Equal(ReportScope.Month, WeeklyReport.ScopeOf(new(2026, 11, 1), new(2026, 11, 30)));
        Assert.Equal(ReportScope.Month, WeeklyReport.ScopeOf(new(2026, 11, 1), new(2026, 11, 18)));
        Assert.Equal(ReportScope.Range, WeeklyReport.ScopeOf(new(2026, 11, 2), new(2026, 12, 26)));

        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)),
            WeeklyReport.PreviousOf(ReportScope.Week, new(2026, 11, 9), new(2026, 11, 14)));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
            WeeklyReport.PreviousOf(ReportScope.Month, new(2026, 11, 1), new(2026, 11, 30)));
        Assert.Equal((new DateOnly(2026, 10, 22), new DateOnly(2026, 11, 1)),
            WeeklyReport.PreviousOf(ReportScope.Range, new(2026, 11, 2), new(2026, 11, 12)));
    }

    [Fact]
    public void Presets_follow_the_monday_to_saturday_week()
    {
        // Wednesday 18.11.2026.
        var wed = StudentReportsController.Presets(new DateOnly(2026, 11, 18)).ToDictionary(p => p.Key);
        Assert.Equal(("2026-11-09", "2026-11-14"), (wed["lastWeek"].From, wed["lastWeek"].To));
        Assert.Equal(("2026-11-16", "2026-11-18"), (wed["thisWeek"].From, wed["thisWeek"].To));
        Assert.Equal(("2026-10-01", "2026-10-31"), (wed["lastMonth"].From, wed["lastMonth"].To));
        Assert.Equal(("2026-11-01", "2026-11-18"), (wed["thisMonth"].From, wed["thisMonth"].To));

        // Sunday 22.11.2026: "Shu hafta" is the Monday–Saturday just ended (what parents get that day).
        var sun = StudentReportsController.Presets(new DateOnly(2026, 11, 22)).ToDictionary(p => p.Key);
        Assert.Equal(("2026-11-16", "2026-11-21"), (sun["thisWeek"].From, sun["thisWeek"].To));
        Assert.Equal(("2026-11-09", "2026-11-14"), (sun["lastWeek"].From, sun["lastWeek"].To));
    }

    // ----- The builder over a month -----------------------------------------

    [Fact]
    public async Task Month_report_lists_whole_weeks_matching_the_weekly_reports()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"SR-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Algebra {tag}" };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Aziza Karimova {tag}", cls.Name, "+998900000081");
        var other = GeneralSettingsFlagsTests.NewStudent($"Bekzod Aliyev {tag}", cls.Name, "+998900000082");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.AddRange(pupil, other);
            await db.SaveChangesAsync();
            JournalEntry Mark(Student s, string date, int grade) =>
                new() { ClassId = cls.Id, SubjectId = subject.Id, StudentId = s.Id, Date = date, Period = 2, Quarter = 1, Grade = grade };
            db.JournalEntries.AddRange(
                Mark(pupil, "2026-09-29", 2),   // October's 1st week, but before the month — not in its totals
                Mark(pupil, "2026-10-06", 5), Mark(other, "2026-10-06", 3),
                Mark(pupil, "2026-10-07", 3), Mark(other, "2026-10-07", 3),
                Mark(pupil, "2026-09-15", 4));  // September — the previous month's average
            db.DisciplinePoints.AddRange(
                new DisciplinePoint { StudentId = pupil.Id, ReasonId = "r", ReasonName = $"Faol {tag}", Points = 5, CreatedAt = "2026-10-06T10:00:00", CreatedBy = "t" },
                new DisciplinePoint { StudentId = pupil.Id, ReasonId = "r", ReasonName = $"Kechikdi {tag}", Points = -3, CreatedAt = "2026-11-10T10:00:00", CreatedBy = "t" });
            await db.SaveChangesAsync();
        });

        WeeklyReportModel? month = null, week = null, later = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            month = await WeeklyReport.BuildAsync(db, pupil.Id, new(2026, 10, 1), new(2026, 10, 31));
            week = await WeeklyReport.BuildAsync(db, pupil.Id, new(2026, 10, 5), new(2026, 10, 10));
            later = await WeeklyReport.BuildAsync(db, pupil.Id, new(2026, 11, 9), new(2026, 11, 14));
        });

        Assert.NotNull(month);
        Assert.Equal(ReportScope.Month, month!.Scope);
        Assert.Equal(2, month.Marks.Count);                 // 06.10 and 07.10 — not 29.09
        Assert.Equal(4.0, month.Average);
        Assert.Equal(3.0, month.PreviousAverage);           // September: 15.09 «4» and 29.09 «2»
        Assert.Equal(5, month.Weeks.Count);
        Assert.Equal("Oktabr, 1-hafta", month.Weeks[0].Title);
        Assert.Equal(1, month.Weeks[0].Marks);               // the whole week, 29.09 included
        Assert.Equal(2.0, month.Weeks[0].Average);

        // The month's second week shows the very figures of that week's own report.
        Assert.NotNull(week);
        Assert.Equal(ReportScope.Week, week!.Scope);
        Assert.Empty(week.Weeks);
        Assert.Equal(week.Average, month.Weeks[1].Average);
        Assert.Equal(week.ClassAverage, month.Weeks[1].ClassAverage);
        Assert.Equal(week.Marks.Count, month.Weeks[1].Marks);

        // The behaviour score is the one the period ended with: November's −3 is not in October's.
        Assert.Equal(later!.DisciplineScore + 3, month.DisciplineScore);
        Assert.Contains("oy", month.Overview);
        Assert.DoesNotContain("hafta", month.Overview);

        var pdf = new WeeklyReportDocument(month).Render();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"Hisobot_Aziza_Karimova_{tag}_SR-{tag}_01.10.2026_31.10.2026.pdf", WeeklyReportDocument.StaffFileName(month));
    }

    [Fact]
    public async Task Empty_period_still_gives_a_pdf()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"SE-{tag}", Grade = 5 };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Yangi O'quvchi {tag}", cls.Name, "+998900000083");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
        });

        WeeklyReportModel? model = null;
        // The autumn break, 05–08.11.2026 (TZ acceptance criterion).
        await fixture.Api.WithDbAsync(async db => model = await WeeklyReport.BuildAsync(db, pupil.Id, new(2026, 11, 5), new(2026, 11, 8)));
        Assert.NotNull(model);
        Assert.True(model!.IsEmpty);
        var pdf = new WeeklyReportDocument(model).Render();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ----- The endpoint -----------------------------------------------------

    [Fact]
    public async Task Pdf_endpoint_needs_the_key_writes_the_audit_and_sends_nothing()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"SP-{tag}", Grade = 8 };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Dilnoza Rahimova {tag}", cls.Name, "+998900000084");
        var autoBefore = 0;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
            autoBefore = await db.AutoMessageLog.CountAsync();
        });

        var url = $"/api/admin/student-reports/{pupil.Id}/pdf?from=2026-11-01&to=2026-11-30";
        using var withKey = await fixture.Api.ClientAsAsync(Roles.Staff, StudentReportsController.PermissionKey);
        var ok = await withKey.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"Hisobot_Dilnoza_Rahimova_{tag}_SP-{tag}_01.11.2026_30.11.2026.pdf",
            ok.Content.Headers.ContentDisposition?.FileNameStar ?? ok.Content.Headers.ContentDisposition?.FileName ?? "");

        Assert.Equal(HttpStatusCode.OK, (await withKey.GetAsync("/api/admin/student-reports/periods")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await withKey.GetAsync($"/api/admin/student-reports/{pupil.Id}/pdf?from=2026-11-30&to=2026-11-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await withKey.GetAsync("/api/admin/student-reports/no-such-pupil/pdf?from=2026-11-01&to=2026-11-30")).StatusCode);

        // The report holds marks and behaviour: another key does not open it, not even to read.
        using var without = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.Forbidden, (await without.GetAsync(url)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == AuditService.EntityStudentReport && a.EntityId == pupil.Id).ToListAsync();
            var row = Assert.Single(audit);
            Assert.Equal("download", row.Action);
            Assert.Equal(pupil.Id, row.StudentId);
            Assert.Contains("01.11.2026 – 30.11.2026", row.Summary);
            Assert.Equal(AuditCatalog.Students, AuditCatalog.SectionOf(row.EntityType, row.Summary));
            // Nothing went to parents and nothing landed in "Tarix va statistika".
            Assert.Equal(autoBefore, await db.AutoMessageLog.CountAsync());
        });
    }
}
