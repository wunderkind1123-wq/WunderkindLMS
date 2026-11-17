using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Client request 2026-11-17: a plan bound to the teacher's timetable — 5-A and 5-B may have
//  different teachers with different plans — and a check that the plan fits the teacher's lessons.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CurriculumTeacherPlanTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record World(SchoolClass ClassA, SchoolClass ClassB, Subject Subject, Teacher TeacherA, AppUser TeacherAUser, Teacher TeacherB);

    /// <summary>Two classes of grade 0; teacher A teaches class A twice on Tuesdays, teacher B class B once.</summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher, fullName: $"Ustoz A {tag}");
        Teacher teacherA = null!;
        var teacherB = new Teacher { FullName = $"Ustoz B {tag}" };
        var a = new SchoolClass { Name = $"0-A-{tag}", Grade = 0 };
        var b = new SchoolClass { Name = $"0-B-{tag}", Grade = 0 };
        var subject = new Subject { Name = $"Fan {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            teacherA = await db.Teachers.FirstAsync(t => t.UserId == user.Id);
            db.Teachers.Add(teacherB);
            db.Classes.AddRange(a, b);
            db.Subjects.Add(subject);
            var tplA = new ScheduleTemplate { ClassId = a.Id, Name = "Asosiy" };
            tplA.Lessons.Add(new ScheduleLesson { TemplateId = tplA.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = teacherA.Id });
            tplA.Lessons.Add(new ScheduleLesson { TemplateId = tplA.Id, Day = 1, Period = 2, SubjectId = subject.Id, TeacherId = teacherA.Id });
            var tplB = new ScheduleTemplate { ClassId = b.Id, Name = "Asosiy" };
            tplB.Lessons.Add(new ScheduleLesson { TemplateId = tplB.Id, Day = 3, Period = 1, SubjectId = subject.Id, TeacherId = teacherB.Id });
            db.ScheduleTemplates.AddRange(tplA, tplB);
            await db.SaveChangesAsync();
        });
        return new World(a, b, subject, teacherA, user, teacherB);
    }

    private static MultipartFormDataContent PlanForm(byte[] xlsx, string subjectId, string? teacherId = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(xlsx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "reja.xlsx");
        form.Add(new StringContent(subjectId), "subjectId");
        form.Add(new StringContent("0"), "grade");
        if (teacherId is not null) form.Add(new StringContent(teacherId), "teacherId");
        return form;
    }

    private static byte[] Xlsx(params string[][] rows) =>
        ExcelExport.Build("Ish reja", CurriculumService.Columns, rows.Select(r => (IReadOnlyList<string>)r));

    private async Task WithQuartersAsync(Func<Task> body, params (int Quarter, string Start, string End)[] quarters)
    {
        List<QuarterPeriod> saved = [];
        await fixture.Api.WithDbAsync(async db =>
        {
            saved = await db.Quarters.AsNoTracking().ToListAsync();
            await db.Quarters.ExecuteDeleteAsync();
            db.Quarters.AddRange(quarters.Select(q => new QuarterPeriod { Quarter = q.Quarter, StartDate = q.Start, EndDate = q.End }));
            await db.SaveChangesAsync();
        });
        try { await body(); }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                await db.Quarters.ExecuteDeleteAsync();
                db.Quarters.AddRange(saved);
                await db.SaveChangesAsync();
            });
        }
    }

    private static async Task<Guid> Import(HttpClient admin, MultipartFormDataContent form)
    {
        var res = await admin.PostAsync("/api/admin/curriculum/plans/import", form);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.PlanId!.Value;
    }

    [Fact]
    public async Task Ustoz_rejasi_faqat_uning_sinfiga_umumiy_reja_qolganiga_jadvalga_moslik_tekshiriladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "curriculum");
        // 2031-11-10 is a Monday: two school weeks, teacher A has 2 lessons every Tuesday → 4 lessons
        await WithQuartersAsync(async () =>
        {
            var file = Xlsx(
                ["1", "", "", "11.11.2031", "", "Birinchi", "1", "", ""],
                ["2", "", "", "12.11.2031", "", "Ikkinchi", "2", "", ""]);

            // a teacher who does not teach this subject in grade 0 is rejected
            var foreign = await admin.PostAsync("/api/admin/curriculum/plans/import?dryRun=true", PlanForm(file, w.Subject.Id, Guid.NewGuid().ToString()));
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

            var shared = await Import(admin, PlanForm(file, w.Subject.Id));
            Guid own = default;
            try
            {
                var before = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{shared}", Json))!;
                Assert.Equal(2, before.Owners.Count);

                // the dry run already shows the fit for teacher A's lessons only
                var dry = (await (await admin.PostAsync("/api/admin/curriculum/plans/import?dryRun=true", PlanForm(file, w.Subject.Id, w.TeacherA.Id)))
                    .Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!;
                var dryFit = Assert.Single(dry.Fit!);
                Assert.Equal(w.ClassA.Id, dryFit.OwnerId);

                own = await Import(admin, PlanForm(file, w.Subject.Id, w.TeacherA.Id));
                var mine = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{own}", Json))!;
                Assert.Equal(w.TeacherA.Id, mine.TeacherId);
                Assert.Equal([w.ClassA.Id], mine.Owners.Select(o => o.Id));
                var after = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{shared}", Json))!;
                Assert.Equal([w.ClassB.Id], after.Owners.Select(o => o.Id));

                // fit: plan 3 h vs 4 lessons; topic 2 is on a Wednesday — no lesson of teacher A that day
                var fit = Assert.Single((await admin.GetFromJsonAsync<List<CurriculumFitDto>>($"/api/admin/curriculum/plans/{own}/fit", Json))!);
                Assert.Equal((2, 3, 4), (fit.WeeklyLessons, fit.PlanHours, fit.Lessons));
                var q = Assert.Single(fit.Quarters);
                Assert.Equal((3, 4, 2), (q.PlanHours, q.Lessons, q.EstimatedWeeks));
                var issue = Assert.Single(fit.DateIssues);
                Assert.Equal((2, "2031-11-12"), (issue.Order, issue.Date));

                // teacher A sees only their own plan, and the fit of it
                using var teacher = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.TeacherAUser.Id, w.TeacherAUser.FullName, w.TeacherAUser.Email));
                var list = (await teacher.GetFromJsonAsync<List<TeacherCurriculumDto>>("/api/teacher/curriculum", Json))!;
                Assert.Contains(list, x => x.PlanId == own);
                Assert.DoesNotContain(list, x => x.PlanId == shared);
                Assert.Single((await teacher.GetFromJsonAsync<List<CurriculumFitDto>>($"/api/teacher/curriculum/{own}/fit", Json))!);

                // the report lists both classes as covered
                var report = (await admin.GetFromJsonAsync<CurriculumReportDto>("/api/admin/curriculum/report", Json))!;
                Assert.Contains(report.Rows, r => r.PlanId == own && r.OwnerId == w.ClassA.Id);
                Assert.Contains(report.Rows, r => r.PlanId == shared && r.OwnerId == w.ClassB.Id);
                Assert.DoesNotContain(report.Missing, m => m.SubjectId == w.Subject.Id);
            }
            finally
            {
                if (own != default) await admin.DeleteAsync($"/api/admin/curriculum/plans/{own}");
                await admin.DeleteAsync($"/api/admin/curriculum/plans/{shared}");
            }
        }, (1, "2031-11-10", "2031-11-22"));
    }
}
