using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Kunlik hisobot (client, 2026-11-16): the director's 17:00 PDF — lessons conducted or not, the day's
//  marks and the gamification entries, per teacher and per class.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class DailyDirectorReportTests(ApiFixture fixture)
{
    [Fact]
    public async Task Day_shows_the_conducted_lesson_its_marks_and_the_points_by_teacher()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var today = AppClock.Today;
        var iso = today.ToString("yyyy-MM-dd");
        var cls = new SchoolClass { Name = $"DR-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Fizika {tag}" };
        var a = GeneralSettingsFlagsTests.NewStudent($"Aziz {tag}", cls.Name, "+998900000071");
        var b = GeneralSettingsFlagsTests.NewStudent($"Bobur {tag}", cls.Name, "+998900000072");
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.AddRange(a, b);
            await db.SaveChangesAsync();
            var teacher = db.Teachers.Single(t => t.UserId == user.Id);
            var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 0, Period = 3, SubjectId = subject.Id, TeacherId = teacher.Id });
            db.ScheduleTemplates.Add(tpl);
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = cls.Id, SubjectId = subject.Id, Date = iso, Period = 3, Quarter = 2, Conducted = true, Topic = "Nyuton qonunlari",
            });
            db.JournalEntries.AddRange(
                new JournalEntry { ClassId = cls.Id, SubjectId = subject.Id, StudentId = a.Id, Date = iso, Period = 3, Quarter = 2, Grade = 5 },
                new JournalEntry { ClassId = cls.Id, SubjectId = subject.Id, StudentId = b.Id, Date = iso, Period = 3, Quarter = 2, Grade = 3 });
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = a.Id, ReasonId = "r", ReasonName = $"Faol {tag}", Points = 5,
                CreatedAt = iso + "T11:20:00", CreatedBy = teacher.FullName, CreatedByUserId = user.Id,
            });
            await db.SaveChangesAsync();
        });

        DailyDirectorReportModel? model = null;
        await fixture.Api.WithDbAsync(async db => model = await DailyDirectorReport.BuildAsync(db, today));
        Assert.NotNull(model);

        var lesson = Assert.Single(model!.Lessons, l => l.OwnerName == cls.Name);
        Assert.Equal(DailyLessonState.Conducted, lesson.State);
        Assert.True(lesson.HasTopic);
        Assert.Equal(new[] { 5, 3 }, lesson.Grades.OrderByDescending(x => x));

        var row = Assert.Single(model.Teachers, t => t.TeacherId != null && t.Name == user.FullName);
        Assert.Equal(1, row.Conducted);
        Assert.Equal(2, row.Grades);
        Assert.Equal(4.0, row.Average!.Value);
        Assert.Equal(5, row.GamePlus);
        Assert.Equal(1, row.GameEntries);

        var classRow = Assert.Single(model.Classes, c => c.Name == cls.Name);
        Assert.Equal((1, 0, 1, 0), (classRow.Five, classRow.Four, classRow.Three, classRow.Two));
        Assert.Equal(5, classRow.GamePlus);
        Assert.Contains(model.Game, g => g.Reason == $"Faol {tag}" && g.Time == "11:20" && g.ClassName == cls.Name);

        var pdf = new DailyDirectorReportDocument(model).Render();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Pdf_endpoint_needs_the_dailyReport_key()
    {
        using var withKey = await fixture.Api.ClientAsAsync(Roles.Staff, DailyDirectorReportService.PermissionKey);
        var ok = await withKey.GetAsync("/api/admin/daily-report/pdf");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);

        using var without = await fixture.Api.ClientAsAsync(Roles.Staff, "teacherReports");
        Assert.Equal(HttpStatusCode.Forbidden, (await without.GetAsync("/api/admin/daily-report/pdf")).StatusCode);
    }

    [Fact]
    public async Task Nothing_is_sent_before_five_or_without_a_holder()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DailyDirectorReportService>();
        // A Monday at 16:59 — not yet due.
        Assert.Null(await service.RunDueAsync(new DateTime(2026, 11, 16, 16, 59, 0)));
        // Due, but no account holds the key in this world, so nothing goes and nothing is recorded.
        Assert.Equal(0, await service.SendAsync(new DateOnly(2026, 11, 16), actorId: null));
        Assert.False(await service.SentAsync(new DateOnly(2026, 11, 16)));
    }
}
