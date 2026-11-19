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
//  Client request 2026-11-18: the teacher uploads their own plan (quarter / week / topic / date all
//  required), edits it as lessons actually go, and "lesson did not happen" shifts the rest forward.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CurriculumTeacherEditTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Grade-0 class A taught by the teacher on Tuesdays, class B by someone else.</summary>
    private async Task<(Subject Subject, SchoolClass ClassA, HttpClient Teacher)> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher, fullName: $"Ustoz {tag}");
        var subject = new Subject { Name = $"Fan {tag}" };
        var a = new SchoolClass { Name = $"0-A-{tag}", Grade = 0 };
        var b = new SchoolClass { Name = $"0-B-{tag}", Grade = 0 };
        await fixture.Api.WithDbAsync(async db =>
        {
            var teacher = await db.Teachers.FirstAsync(t => t.UserId == user.Id);
            var other = new Teacher { FullName = $"Boshqa {tag}" };
            db.Teachers.Add(other);
            db.Classes.AddRange(a, b);
            db.Subjects.Add(subject);
            var tplA = new ScheduleTemplate { ClassId = a.Id, Name = "Asosiy" };
            tplA.Lessons.Add(new ScheduleLesson { TemplateId = tplA.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = teacher.Id });
            var tplB = new ScheduleTemplate { ClassId = b.Id, Name = "Asosiy" };
            tplB.Lessons.Add(new ScheduleLesson { TemplateId = tplB.Id, Day = 3, Period = 1, SubjectId = subject.Id, TeacherId = other.Id });
            db.ScheduleTemplates.AddRange(tplA, tplB);
            await db.SaveChangesAsync();
        });
        return (subject, a, fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private static MultipartFormDataContent Form(string subjectId, params string[][] rows)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(ExcelExport.Build("Ish reja", CurriculumService.Columns, rows.Select(r => (IReadOnlyList<string>)r)));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "reja.xlsx");
        form.Add(new StringContent(subjectId), "subjectId");
        form.Add(new StringContent("0"), "grade");
        return form;
    }

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

    private static CurriculumTopicSaveRequest Topic(string title, string date, int? after = null) =>
        new(title, null, date, null, null, 1, null, null, after);

    [Fact]
    public async Task Ustoz_oz_rejasini_yuklaydi_tahrirlaydi_va_dars_bolmasa_suradi()
    {
        var (subject, classA, teacher) = await SeedAsync();
        using var _ = teacher;
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "curriculum");
        // 2031-11-10 is a Monday; the teacher's lessons: Tuesdays 11, 18, 25
        await WithQuartersAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/teacher/curriculum/template")).StatusCode);
            var keys = (await teacher.GetFromJsonAsync<List<TeacherCurriculumKeyDto>>("/api/teacher/curriculum/options", Json))!;
            Assert.Contains(keys, k => k.SubjectId == subject.Id && k.Grade == 0 && k.PlanId == null);

            // quarter, week and date are required; the week must match the date
            var bad = await teacher.PostAsync("/api/teacher/curriculum/import?dryRun=true", Form(subject.Id,
                ["1", "1", "", "11.11.2031", "", "Birinchi", "1", "", ""],
                ["2", "1", "2", "", "", "Ikkinchi", "1", "", ""],
                ["3", "1", "1", "18.11.2031", "", "Uchinchi", "1", "", ""]));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var errors = (await bad.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.Errors;
            Assert.Contains(errors, e => e.StartsWith("2-qator") && e.Contains("«Hafta»"));
            Assert.Contains(errors, e => e.StartsWith("3-qator") && e.Contains("«Sana»"));
            Assert.Contains(errors, e => e.StartsWith("4-qator") && e.Contains("2-haftasiga"));

            var ok = await teacher.PostAsync("/api/teacher/curriculum/import", Form(subject.Id,
                ["1", "1", "1", "11.11.2031", "", "Birinchi", "1", "", ""],
                ["2", "1", "2", "18.11.2031", "", "Ikkinchi", "1", "", ""]));
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
            var planId = (await ok.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.PlanId!.Value;
            Guid shared = default;
            try
            {
                async Task<CurriculumPlanDetailDto> Detail() =>
                    (await teacher.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/teacher/curriculum/{planId}", Json))!;
                var plan = await Detail();
                Assert.NotNull(plan.TeacherId);
                Assert.NotNull(plan.FileUrl);
                Assert.Equal([classA.Id], plan.Owners.Select(o => o.Id));
                Assert.NotNull(plan.Owners[0].Delta);

                // a topic inserted after №1 pushes №2 down
                var add = await teacher.PostAsJsonAsync($"/api/teacher/curriculum/{planId}/topics", Topic("Qo'shimcha", "2031-11-18", after: 1));
                Assert.True(add.IsSuccessStatusCode, await add.Content.ReadAsStringAsync());
                plan = await Detail();
                Assert.Equal(["Birinchi", "Qo'shimcha", "Ikkinchi"], plan.Topics.Select(t => t.Title));
                Assert.Equal([1, 2, 3], plan.Topics.Select(t => t.Order));

                // a Sunday is refused; an edit recomputes the week from the date
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await teacher.PutAsJsonAsync($"/api/teacher/curriculum/topics/{plan.Topics[0].Id}", Topic("Birinchi", "2031-11-16"))).StatusCode);
                var edit = await teacher.PutAsJsonAsync($"/api/teacher/curriculum/topics/{plan.Topics[0].Id}", Topic("Birinchi (yangi)", "2031-11-12"));
                Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());

                // lesson did not happen on №2: №2 takes №3's date, №3 the next Tuesday
                var shift = await teacher.PostAsJsonAsync("/api/teacher/curriculum/shift", new CurriculumShiftRequest(plan.Topics[1].Id));
                Assert.True(shift.IsSuccessStatusCode, await shift.Content.ReadAsStringAsync());
                Assert.Equal("2031-11-25", (await shift.Content.ReadFromJsonAsync<CurriculumShiftResultDto>(Json))!.LastDate);
                plan = await Detail();
                Assert.Equal(["2031-11-12", "2031-11-18", "2031-11-25"], plan.Topics.Select(t => t.PlannedOn));
                Assert.Equal(3, plan.Topics[2].Week);

                // past the year's last lesson the shift is refused
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await teacher.PostAsJsonAsync("/api/teacher/curriculum/shift", new CurriculumShiftRequest(plan.Topics[2].Id))).StatusCode);

                // delete closes the gap in the numbering
                Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/teacher/curriculum/topics/{plan.Topics[0].Id}")).StatusCode);
                plan = await Detail();
                Assert.Equal([1, 2], plan.Topics.Select(t => t.Order));

                // the shared plan (class B) is not the teacher's to edit
                var sharedRes = await admin.PostAsync("/api/admin/curriculum/plans/import", Form(subject.Id,
                    ["1", "", "", "", "", "Umumiy", "1", "", ""]));
                Assert.True(sharedRes.IsSuccessStatusCode, await sharedRes.Content.ReadAsStringAsync());
                shared = (await sharedRes.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.PlanId!.Value;
                var sharedTopic = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{shared}", Json))!.Topics[0];
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await teacher.PutAsJsonAsync($"/api/teacher/curriculum/topics/{sharedTopic.Id}", Topic("X", "2031-11-18"))).StatusCode);
            }
            finally
            {
                await admin.DeleteAsync($"/api/admin/curriculum/plans/{planId}");
                if (shared != default) await admin.DeleteAsync($"/api/admin/curriculum/plans/{shared}");
            }
        }, (1, "2031-11-10", "2031-11-29"));
    }
}
