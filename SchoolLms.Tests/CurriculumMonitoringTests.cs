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
//  Client request 2026-11-06 (docs/modules/curriculum-monitoring.md):
//  §1 ish reja — Excel import, re-upload keeps marks, teacher marks only own
//     class × subject, report (missing plans);
//  §2 ichki monitoring — paper key + entered answers, topic analysis, national
//     scoring (Rasch / weights), online by personal code with a device lock;
//  §3 certificate points — block-test equivalent from the pupil's track.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CurriculumMonitoringTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record World(string Tag, int Grade, SchoolClass ClassA, SchoolClass ClassB, Subject Subject, Teacher TeacherA, AppUser TeacherAUser, string[] PupilsA, string[] PupilsB);

    /// <summary>Two classes of a fresh grade, a subject taught in both, teacher A in class A only.</summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var grade = 0; // a grade no other test uses for curriculum
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher, fullName: $"Ustoz {tag}");
        Teacher teacherA = null!;
        var other = new Teacher { FullName = $"Boshqa {tag}" };
        var a = new SchoolClass { Name = $"0-A-{tag}", Grade = grade };
        var b = new SchoolClass { Name = $"0-B-{tag}", Grade = grade };
        var subject = new Subject { Name = $"Fan {tag}" };
        var pupilsA = Enumerable.Range(1, 3).Select(i => Pupil($"A{i} {tag}", a.Name)).ToList();
        var pupilsB = Enumerable.Range(1, 2).Select(i => Pupil($"B{i} {tag}", b.Name)).ToList();
        await fixture.Api.WithDbAsync(async db =>
        {
            teacherA = await db.Teachers.FirstAsync(t => t.UserId == user.Id);
            db.Teachers.Add(other);
            db.Classes.AddRange(a, b);
            db.Subjects.Add(subject);
            db.Students.AddRange(pupilsA);
            db.Students.AddRange(pupilsB);
            db.ScheduleTemplates.Add(Template(a.Id, subject.Id, teacherA.Id));
            db.ScheduleTemplates.Add(Template(b.Id, subject.Id, other.Id));
            await db.SaveChangesAsync();
        });
        return new World(tag, grade, a, b, subject, teacherA, user, [.. pupilsA.Select(p => p.Id)], [.. pupilsB.Select(p => p.Id)]);
    }

    private static ScheduleTemplate Template(string classId, string subjectId, string teacherId)
    {
        var tpl = new ScheduleTemplate { ClassId = classId, Name = "Asosiy" };
        tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = subjectId, TeacherId = teacherId });
        return tpl;
    }

    private static Student Pupil(string name, string className) => new()
    {
        FullName = name, FirstName = name.Split(' ')[0], LastName = name.Split(' ')[^1], BirthDate = "2015-01-01",
        Gender = "male", ClassName = className, EnrollmentDate = "2025-09-01",
    };

    private static MultipartFormDataContent PlanForm(byte[] xlsx, string subjectId, int grade)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(xlsx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "reja.xlsx");
        form.Add(new StringContent(subjectId), "subjectId");
        form.Add(new StringContent(grade.ToString()), "grade");
        return form;
    }

    /// <summary>An old-format file (no "Sana" column) — must keep working.</summary>
    private static byte[] Xlsx(params string[][] rows) =>
        ExcelExport.Build("Ish reja", CurriculumService.LegacyColumns, rows.Select(r => (IReadOnlyList<string>)r));

    /// <summary>The current template, with the "Sana" column.</summary>
    private static byte[] XlsxWithDates(params string[][] rows) =>
        ExcelExport.Build("Ish reja", CurriculumService.Columns, rows.Select(r => (IReadOnlyList<string>)r));

    /// <summary>Runs <paramref name="body"/> with the school's quarters replaced, then puts the old ones back.</summary>
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

    // ------------------------------------------------------------------ §1

    [Fact]
    public async Task Ish_reja_yuklanadi_qayta_yuklashda_belgi_saqlanadi_oqituvchi_faqat_oz_sinfini_belgilaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "curriculum");

        // a bad row blocks the whole file
        var bad = await admin.PostAsync("/api/admin/curriculum/plans/import",
            PlanForm(Xlsx(["1", "5", "", "", "Kasrlar", "1", "", ""]), w.Subject.Id, w.Grade));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Chorak", (await bad.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.Errors[0]);

        var first = Xlsx(
            ["1", "1", "1", "Bo'lim 1", "Birinchi mavzu", "1", "", ""],
            ["2", "1", "2", "Bo'lim 1", "Ikkinchi mavzu", "2", "", ""],
            ["3", "2", "", "Bo'lim 2", "Uchinchi mavzu", "1", "", ""]);
        var dry = await admin.PostAsync("/api/admin/curriculum/plans/import?dryRun=true", PlanForm(first, w.Subject.Id, w.Grade));
        var dryBody = (await dry.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!;
        Assert.Null(dryBody.PlanId);
        Assert.Equal(3, dryBody.TopicCount);
        Assert.Equal(4, dryBody.TotalHours);

        var saved = (await (await admin.PostAsync("/api/admin/curriculum/plans/import", PlanForm(first, w.Subject.Id, w.Grade)))
            .Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!;
        var planId = saved.PlanId!.Value;

        try
        {
            var plan = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{planId}", Json))!;
            Assert.Equal([w.ClassA.Name, w.ClassB.Name], plan.Owners.Select(o => o.Name).Order().ToArray());

            // teacher A marks topic 1 in class A; class B is not theirs
            using var teacher = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.TeacherAUser.Id, w.TeacherAUser.FullName, w.TeacherAUser.Email));
            var mine = (await teacher.GetFromJsonAsync<List<TeacherCurriculumDto>>("/api/teacher/curriculum", Json))!;
            var item = Assert.Single(mine, m => m.PlanId == planId);
            Assert.Equal(w.ClassA.Id, item.OwnerId);
            var topic1 = plan.Topics.Single(t => t.Order == 1).Id;
            Assert.Equal(HttpStatusCode.NoContent, (await teacher.PutAsJsonAsync("/api/teacher/curriculum/progress",
                new CurriculumMarkRequest(topic1, LessonOwnerKind.Class, w.ClassA.Id, true))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync("/api/teacher/curriculum/progress",
                new CurriculumMarkRequest(topic1, LessonOwnerKind.Class, w.ClassB.Id, true))).StatusCode);

            // re-upload: topic 1 renamed (mark stays), topic 3 dropped, topic 4 new
            var second = Xlsx(
                ["1", "1", "1", "", "Birinchi mavzu (yangi nom)", "1", "", ""],
                ["2", "1", "2", "", "Ikkinchi mavzu", "2", "", ""],
                ["4", "2", "", "", "To'rtinchi mavzu", "1", "", ""]);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/curriculum/plans/import", PlanForm(second, w.Subject.Id, w.Grade))).StatusCode);
            plan = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{planId}", Json))!;
            Assert.Equal([1, 2, 4], plan.Topics.Select(t => t.Order).ToArray());
            var renamed = plan.Topics.Single(t => t.Order == 1);
            Assert.Equal("Birinchi mavzu (yangi nom)", renamed.Title);
            Assert.Single(renamed.Marks, m => m.OwnerId == w.ClassA.Id);

            // report: class A has 1 passed of 3
            var report = (await admin.GetFromJsonAsync<CurriculumReportDto>("/api/admin/curriculum/report", Json))!;
            var rowA = report.Rows.Single(r => r.PlanId == planId && r.OwnerId == w.ClassA.Id);
            Assert.Equal(1, rowA.PassedCount);
            Assert.Equal(3, rowA.TopicCount);
            Assert.DoesNotContain(report.Missing, m => m.SubjectId == w.Subject.Id && m.Grade == w.Grade);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/curriculum/plans/{planId}");
        }

        // without the plan the pair shows up as missing
        var after = (await admin.GetFromJsonAsync<CurriculumReportDto>("/api/admin/curriculum/report", Json))!;
        Assert.Contains(after.Missing, m => m.SubjectId == w.Subject.Id && m.Grade == w.Grade);
    }

    [Fact]
    public async Task Sana_ustuni_aniq_reja_sanasi_chorakdan_tashqarisi_xato_qolda_tahrir()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "curriculum");
        await WithQuartersAsync(async () =>
        {
            // 29.12 is past the end of quarter 2 → the whole file is rejected, with the row number
            var late = await admin.PostAsync("/api/admin/curriculum/plans/import?dryRun=true", PlanForm(XlsxWithDates(
                ["1", "2", "1", "10.11", "", "Ot", "1", "", ""],
                ["2", "2", "8", "29.12", "", "Bayram oldi", "1", "", ""]), w.Subject.Id, w.Grade));
            Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
            Assert.Equal("3-qator: 29.12 sanasi 2-chorakdan (09.11–26.12) tashqarida",
                Assert.Single((await late.Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.Errors));

            var file = XlsxWithDates(
                ["1", "2", "1", "10.11", "", "Ot", "1", "", ""],
                ["2", "2", "1", "14.11", "", "Sifat", "1", "", ""],
                ["3", "2", "2", "", "", "Son", "1", "", ""],
                ["4", "2", "", "15.11", "", "Yakshanba darsi", "1", "", ""]);
            var dry = (await (await admin.PostAsync("/api/admin/curriculum/plans/import?dryRun=true", PlanForm(file, w.Subject.Id, w.Grade)))
                .Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!;
            Assert.Empty(dry.Errors);
            Assert.Equal(["2026-11-10", "2026-11-14", null, "2026-11-15"], dry.Preview.Select(p => p.PlannedOn));
            Assert.Contains(dry.Warnings, x => x.Contains("yakshanba"));

            var planId = (await (await admin.PostAsync("/api/admin/curriculum/plans/import", PlanForm(file, w.Subject.Id, w.Grade)))
                .Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.PlanId!.Value;
            try
            {
                var plan = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{planId}", Json))!;
                Assert.Equal(["2026-11-10", "2026-11-14", "2026-11-16", "2026-11-15"], plan.Topics.Select(t => t.PlannedOn));
                Assert.Equal([true, true, false, true], plan.Topics.Select(t => t.PlannedExact));

                // admin moves topic 3 to an exact day; the week follows the date
                var topic3 = plan.Topics.Single(t => t.Order == 3).Id;
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/curriculum/topics/{topic3}/planned-on",
                    new CurriculumTopicDateRequest("2026-12-03"))).StatusCode);
                var outside = await admin.PutAsJsonAsync($"/api/admin/curriculum/topics/{topic3}/planned-on", new CurriculumTopicDateRequest("2026-12-29"));
                Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
                plan = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{planId}", Json))!;
                var t3 = plan.Topics.Single(t => t.Order == 3);
                Assert.Equal(("2026-12-03", true, 4), (t3.PlannedOn, t3.PlannedExact, t3.Week!.Value));

                // clearing returns it to the quarter/week estimate
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/curriculum/topics/{topic3}/planned-on",
                    new CurriculumTopicDateRequest(null))).StatusCode);
                plan = (await admin.GetFromJsonAsync<CurriculumPlanDetailDto>($"/api/admin/curriculum/plans/{planId}", Json))!;
                t3 = plan.Topics.Single(t => t.Order == 3);
                Assert.Equal(("2026-11-30", false), (t3.PlannedOn, t3.PlannedExact));

                // the teacher sees the same ahead/behind as the report
                using var teacher = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.TeacherAUser.Id, w.TeacherAUser.FullName, w.TeacherAUser.Email));
                Assert.Equal(HttpStatusCode.NoContent, (await teacher.PutAsJsonAsync("/api/teacher/curriculum/progress",
                    new CurriculumMarkRequest(plan.Topics.Single(t => t.Order == 1).Id, LessonOwnerKind.Class, w.ClassA.Id, true))).StatusCode);
                var mineRow = Assert.Single((await teacher.GetFromJsonAsync<List<TeacherCurriculumDto>>("/api/teacher/curriculum", Json))!,
                    m => m.PlanId == planId);
                var reportRow = (await admin.GetFromJsonAsync<CurriculumReportDto>("/api/admin/curriculum/report", Json))!
                    .Rows.Single(r => r.PlanId == planId && r.OwnerId == w.ClassA.Id);
                Assert.NotNull(mineRow.Expected);
                Assert.Equal((reportRow.Expected, reportRow.Delta, reportRow.ForecastFinish, reportRow.LateWarning),
                    (mineRow.Expected, mineRow.Delta, mineRow.ForecastFinish, mineRow.LateWarning));
                Assert.Equal(1 - mineRow.Expected, mineRow.Delta);

                // a teacher cannot edit plan dates
                Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"/api/admin/curriculum/topics/{topic3}/planned-on",
                    new CurriculumTopicDateRequest("2026-12-01"))).StatusCode);
            }
            finally
            {
                await admin.DeleteAsync($"/api/admin/curriculum/plans/{planId}");
            }
        }, (1, "2026-09-02", "2026-10-31"), (2, "2026-11-09", "2026-12-26"));
    }

    [Fact]
    public async Task Monitoring_natijasi_faqat_monitoring_ruxsatiga_ish_rejani_yozish_curriculumga()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/admin/curriculum/plans/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/monitoring/banks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/monitoring/tests")).StatusCode);
    }

    // ------------------------------------------------------------------ §2 scoring (pure)

    [Fact]
    public void Rasch_T_ball_kuchliroq_oquvchiga_yuqori_teng_javobga_teng()
    {
        var r = MonitoringScore.RaschT([
            [true, true, true, true, false],
            [true, true, true, false, false],
            [true, true, false, false, false],
            [true, false, false, false, false],
            [true, true, true, false, false],
        ]);
        Assert.True(r[0] > r[1] && r[1] > r[2] && r[2] > r[3]);
        Assert.Equal(r[1], r[4], 6);
        Assert.Equal(50, r.Average(), 6); // T is standardised around 50
        Assert.Equal([50.0], MonitoringScore.RaschT([[true, false]]));
        Assert.Equal("A+", MonitoringScore.LevelOf(70));
        Assert.Equal("C", MonitoringScore.LevelOf(46));
        Assert.Null(MonitoringScore.LevelOf(45.9m));
    }

    // ------------------------------------------------------------------ §2 api

    private static async Task<Guid> IdOf(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Qogoz_monitoring_kalit_javoblar_mavzu_tahlili_va_vazn_ball()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring", "curriculum");
        var planId = (await (await admin.PostAsync("/api/admin/curriculum/plans/import", PlanForm(Xlsx(
                ["1", "1", "", "", "Qo'shish", "1", "", ""], ["2", "1", "", "", "Ayirish", "1", "", ""]), w.Subject.Id, w.Grade)))
            .Content.ReadFromJsonAsync<CurriculumImportResultDto>(Json))!.PlanId!.Value;
        try
        {
            var testId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
            {
                title = "1-chorak monitoringi", subjectId = w.Subject.Id, grade = w.Grade, delivery = "paper", scoring = "national",
                nationalMethod = "weights", testDate = "2026-11-06",
                owners = new[] { new { kind = "class", id = w.ClassA.Id }, new { kind = "class", id = w.ClassB.Id } },
                key = "ABCD", optionCount = 4,
            }));
            var detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            Assert.Equal(5, detail.Participants.Count);
            Assert.Equal(2, detail.PlanTopics!.Count);

            // topics: items 1–2 → "Qo'shish", 3–4 → "Ayirish"; item 4 weighs 2
            var add = detail.PlanTopics![0].Id;
            var sub = detail.PlanTopics![1].Id;
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/monitoring/tests/{testId}/items", new[]
            {
                new { order = 1, correctIndex = 0, topicId = (Guid?)add, weight = 1m },
                new { order = 2, correctIndex = 1, topicId = (Guid?)add, weight = 1m },
                new { order = 3, correctIndex = 2, topicId = (Guid?)sub, weight = 1m },
                new { order = 4, correctIndex = 3, topicId = (Guid?)sub, weight = 2m },
            })).StatusCode);

            var p = detail.Participants.Where(x => w.PupilsA.Contains(x.StudentId)).OrderBy(x => x.StudentName).ToList();
            var answers = new[]
            {
                new { participantId = p[0].Id, answers = (string?)"ABCD", absent = false },
                new { participantId = p[1].Id, answers = (string?)"AB", absent = false },   // short → rest blank
                new { participantId = p[2].Id, answers = (string?)null, absent = true },
            };
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/monitoring/tests/{testId}/answers", answers)).StatusCode);
            var tooLong = await admin.PutAsJsonAsync($"/api/admin/monitoring/tests/{testId}/answers",
                new[] { new { participantId = p[1].Id, answers = (string?)"ABCDA", absent = false } });
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

            detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            var full = detail.Participants.Single(x => x.Id == p[0].Id);
            Assert.Equal(4, full.Correct);
            Assert.Equal(75m, full.Points);
            Assert.Equal("A+", full.Level);
            var half = detail.Participants.Single(x => x.Id == p[1].Id);
            Assert.Equal("AB--", half.Answers);
            Assert.Equal(2, half.Correct);
            Assert.Equal(30m, half.Points); // 75 × 2 / 5
            Assert.Equal(MonitoringParticipantStatus.Absent, detail.Participants.Single(x => x.Id == p[2].Id).Status);

            var addStat = detail.Topics.Single(t => t.TopicId == add);
            var subStat = detail.Topics.Single(t => t.TopicId == sub);
            Assert.Equal(100m, addStat.CorrectRate);
            Assert.Equal(50m, subStat.CorrectRate);
            Assert.Equal(subStat.TopicId, detail.Topics[0].TopicId); // the weakest topic first

            // results are for o'quv bo'limi only: a teacher token is refused
            using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/admin/monitoring/tests/{testId}")).StatusCode);
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{testId}");
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/curriculum/plans/{planId}");
        }
    }

    [Fact]
    public async Task Onlayn_monitoring_shaxsiy_kod_qurilma_qulfi_va_yakunlash()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Bank", subjectId = w.Subject.Id, grade = w.Grade }));
        var q1 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "2+2?", options = new[] { "3", "4", "5" }, correctIndex = 1 }));
        var q2 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "3+3?", options = new[] { "6", "7" }, correctIndex = 0 }));

        var now = DateTimeOffset.UtcNow;
        var paperOnlineNoBank = await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
        {
            title = "X", subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-06",
            opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
            owners = new[] { new { kind = "class", id = w.ClassB.Id } }, key = "AB",
        });
        Assert.Equal(HttpStatusCode.BadRequest, paperOnlineNoBank.StatusCode);

        var testId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
        {
            title = "Onlayn", subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-06",
            opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
            owners = new[] { new { kind = "class", id = w.ClassB.Id } }, questionIds = new[] { q1, q2 },
        }));
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/published", null)).StatusCode);
            var detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            Assert.Equal(2, detail.Participants.Count);
            Assert.All(detail.Participants, p => Assert.Equal(MonitoringService.CodeLength, p.AccessCode!.Length));
            var (first, second) = (detail.Participants[0], detail.Participants[1]);

            using var anon = fixture.Api.AnonymousClient();
            var deviceA = "device-a-" + Guid.NewGuid().ToString("N");
            var deviceB = "device-b-" + Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = "ZZZZZZZZ", device = deviceA })).StatusCode);

            // the check before starting shows whose code it is and binds nothing
            var peek = (await (await anon.PostAsJsonAsync("/api/public/monitoring/check", new { code = first.AccessCode, device = deviceB }))
                .Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!;
            Assert.Equal((first.StudentName, w.ClassB.Name, 2, false), (peek.StudentName, peek.OwnerName, peek.ItemCount, peek.InProgress));
            Assert.InRange(peek.TimeLimitMin, 29, 30);

            var session = (await (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceA }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal(2, session.Items.Count);
            Assert.Equal("--", session.Answers);
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/check", new { code = first.AccessCode, device = deviceB })).StatusCode);
            Assert.True((await (await anon.PostAsJsonAsync("/api/public/monitoring/check", new { code = first.AccessCode, device = deviceA }))
                .Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!.InProgress);
            // the answer key never reaches the pupil; the own total does only after finishing
            Assert.DoesNotContain("correctIndex", JsonSerializer.Serialize(session, Json), StringComparison.OrdinalIgnoreCase);
            Assert.Null(session.ResultPercent);

            // another device is locked out until the admin resets it
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceB })).StatusCode);
            // the page counts an answer as saved only on this acknowledgement (a Wi-Fi login page answers 200 too)
            var ack = await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code = first.AccessCode, device = deviceA, order = 1, choice = "B" });
            Assert.Equal(1, (await ack.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saved").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code = first.AccessCode, device = deviceA, order = 2, choice = "B" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/participants/{first.Id}/reset-device", null)).StatusCode);
            var resumed = (await (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceB }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal("BB", resumed.Answers);

            var finished = (await (await anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code = first.AccessCode, device = deviceB }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.True(finished.Finished);
            Assert.Equal((1, 50m), (finished.ResultCorrect, finished.ResultPercent));
            Assert.DoesNotContain("correctIndex", JsonSerializer.Serialize(finished, Json), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceB })).StatusCode);

            // closing: the pupil who never started is absent
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/closed", null)).StatusCode);
            detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            var one = detail.Participants.Single(p => p.Id == first.Id);
            Assert.Equal(MonitoringParticipantStatus.Done, one.Status);
            Assert.Equal(1, one.Correct);
            Assert.Equal(50m, one.Percent);
            Assert.Equal(MonitoringParticipantStatus.Absent, detail.Participants.Single(p => p.Id == second.Id).Status);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{testId}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
        }
    }

    /// <summary>
    /// Test sessiyasi (2026-11-24, §2.5): one code walks a pupil through several subjects in the
    /// sitting's order — a later subject does not open first, the whole sitting stays on one computer,
    /// each subject keeps its own result; at the end the pupil sees "all done".
    /// </summary>
    [Fact]
    public async Task Sessiya_bitta_kod_bilan_fanlar_ketma_ket()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Bank", subjectId = w.Subject.Id, grade = w.Grade }));
        var q1 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "2+2?", options = new[] { "3", "4", "5" }, correctIndex = 1 }));
        var q2 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "3+3?", options = new[] { "6", "7" }, correctIndex = 0 }));
        var now = DateTimeOffset.UtcNow;
        async Task<Guid> TestAsync(string title, Guid[] questions)
        {
            var id = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
            {
                title, subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-24",
                opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
                owners = new[] { new { kind = "class", id = w.ClassA.Id } }, questionIds = questions,
            }));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{id}/status/published", null)).StatusCode);
            return id;
        }
        var first = await TestAsync("1-fan", [q1, q2]);
        var second = await TestAsync("2-fan", [q2]);
        Guid sittingId = default;
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/monitoring/sittings",
                new { title = "Bitta", testDate = "2026-11-24", testIds = new[] { first } })).StatusCode);
            sittingId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/sittings",
                new { title = "Noyabr sessiyasi", testDate = "2026-11-24", testIds = new[] { first, second } }));
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/monitoring/sittings",
                new { title = "Takror", testDate = "2026-11-24", testIds = new[] { second, first } })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/monitoring/tests/{first}")).StatusCode);

            var detail = (await admin.GetFromJsonAsync<MonitoringSittingDetailDto>($"/api/admin/monitoring/sittings/{sittingId}", Json))!;
            Assert.Equal([first, second], detail.Tests.Select(t => t.Id));
            Assert.Equal(3, detail.Rows.Count);
            var row = detail.Rows.Single(r => r.StudentId == w.PupilsA[0]);
            Assert.Equal(MonitoringService.CodeLength, row.Code.Length);

            using var anon = fixture.Api.AnonymousClient();
            var deviceA = "device-a-" + Guid.NewGuid().ToString("N");
            var deviceB = "device-b-" + Guid.NewGuid().ToString("N");
            async Task<MonitoringPeekDto> CheckAsync()
            {
                var res = await anon.PostAsJsonAsync("/api/public/monitoring/check", new { code = row.Code, device = deviceA });
                Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
                return (await res.Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!;
            }
            async Task<MonitoringSessionDto> PostAsync(string path, Guid testId, string device)
            {
                var res = await anon.PostAsJsonAsync($"/api/public/monitoring/{path}", new { code = row.Code, device, testId });
                Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
                return (await res.Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            }

            var peek = await CheckAsync();
            Assert.Equal((first, "Noyabr sessiyasi"), (peek.TestId, peek.SittingTitle));
            Assert.Equal(["current", "waiting"], peek.Steps!.Select(s => s.Status));

            // the order holds: the second subject does not open first
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/start",
                new { code = row.Code, device = deviceA, testId = second })).StatusCode);
            var s1 = await PostAsync("start", first, deviceA);
            Assert.True(s1.InSitting);
            Assert.Equal(first, s1.TestId);
            // the whole sitting is bound to the first computer
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/check",
                new { code = row.Code, device = deviceB })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer",
                new { code = row.Code, device = deviceA, order = 1, choice = "B", testId = first })).StatusCode);
            Assert.True((await PostAsync("finish", first, deviceA)).Finished);

            peek = await CheckAsync();
            Assert.Equal(second, peek.TestId);
            Assert.Equal(["done", "current"], peek.Steps!.Select(s => s.Status));
            Assert.Equal([50m, null], peek.Steps!.Select(s => s.Percent));
            var s2 = await PostAsync("start", second, deviceA);
            Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer",
                new { code = row.Code, device = deviceA, order = 1, choice = "A", testId = second })).StatusCode);
            Assert.True((await PostAsync("finish", s2.TestId!.Value, deviceA)).Finished);
            Assert.True((await CheckAsync()).AllDone);

            // each subject has its own result
            var one = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{first}", Json))!
                .Participants.Single(p => p.StudentId == w.PupilsA[0]);
            var two = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{second}", Json))!
                .Participants.Single(p => p.StudentId == w.PupilsA[0]);
            Assert.Equal((50m, 100m), (one.Percent, two.Percent));
            detail = (await admin.GetFromJsonAsync<MonitoringSittingDetailDto>($"/api/admin/monitoring/sittings/{sittingId}", Json))!;
            Assert.Equal([MonitoringParticipantStatus.Done, MonitoringParticipantStatus.Done],
                detail.Rows.Single(r => r.StudentId == w.PupilsA[0]).Steps);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/sitting-codes/{row.CodeId}/reset-device", null)).StatusCode);
        }
        finally
        {
            if (sittingId != default) await admin.DeleteAsync($"/api/admin/monitoring/sittings/{sittingId}");
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{first}");
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{second}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
        }
    }

    /// <summary>
    /// Sessiya bitta formada (§2.6): every subject becomes an online test from its bank — a random
    /// subset when a count is given — opened at once, all in one transaction (a bad row creates
    /// nothing). The live view follows a pupil through it and the ping keeps them "online".
    /// </summary>
    [Fact]
    public async Task Sessiya_tez_yaratiladi_jonli_kuzatuv_va_ping()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        async Task<Guid> BankAsync(string title, int n)
        {
            var id = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks", new { title, subjectId = w.Subject.Id, grade = w.Grade }));
            for (var i = 1; i <= n; i++)
                await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{id}/questions",
                    new { text = $"{title} {i}", options = new[] { "a", "b", "c" }, correctIndex = 0 }));
            return id;
        }
        var bankA = await BankAsync("Bank A", 3);
        var bankB = await BankAsync("Bank B", 2);
        var now = DateTimeOffset.UtcNow;
        object Payload(int countA) => new
        {
            title = $"Tez {w.Tag}", testDate = "2026-11-25", opensAt = now.AddMinutes(-5), closesAt = now.AddHours(2),
            owners = new[] { new { kind = "class", id = w.ClassA.Id } },
            subjects = new object[] { new { bankId = bankA, questionCount = countA, timeLimitMin = 20 }, new { bankId = bankB, questionCount = (int?)null, timeLimitMin = 15 } },
            shuffle = true, maxLeaves = 1, seasonalYear = (int?)null, seasonalMonth = (int?)null, publish = true,
        };
        async Task<int> TestCountAsync()
        {
            var n = 0;
            await fixture.Api.WithDbAsync(async db => n = await db.MonitoringTests.CountAsync(t => t.Title.StartsWith($"Tez {w.Tag}")));
            return n;
        }

        // a bad row (more questions than the bank has) creates nothing at all
        var bad = await admin.PostAsJsonAsync("/api/admin/monitoring/sittings/quick", Payload(5));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("1–3", await bad.Content.ReadAsStringAsync());
        Assert.Equal(0, await TestCountAsync());

        var sittingId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/sittings/quick", Payload(2)));
        var detail = (await admin.GetFromJsonAsync<MonitoringSittingDetailDto>($"/api/admin/monitoring/sittings/{sittingId}", Json))!;
        try
        {
            Assert.Equal(2, await TestCountAsync());
            Assert.Equal([2, 2], detail.Tests.Select(t => t.ItemCount));
            Assert.Equal([20, 15], detail.Tests.Select(t => t.TimeLimitMin!.Value));
            Assert.All(detail.Tests, t => Assert.Equal(MonitoringStatus.Published, t.Status));
            var test = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{detail.Tests[0].Id}", Json))!;
            Assert.True(test.Shuffle);
            Assert.Equal(1, test.MaxLeaves);

            var live = (await admin.GetFromJsonAsync<MonitoringLiveDto>($"/api/admin/monitoring/sittings/{sittingId}/live", Json))!;
            Assert.Equal(3, live.Rows.Count);
            Assert.All(live.Rows, r => Assert.Equal(("pending", 1, 2), (r.Status, r.Step, r.Steps)));

            var row = detail.Rows.Single(r => r.StudentId == w.PupilsA[0]);
            using var anon = fixture.Api.AnonymousClient();
            var device = "device-live-" + Guid.NewGuid().ToString("N");
            var session = (await (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = row.Code, device }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer",
                new { code = row.Code, device, order = session.Items[0].Order, choice = "A", testId = session.TestId })).StatusCode);
            var ping = await anon.PostAsJsonAsync("/api/public/monitoring/ping", new { code = row.Code, device, testId = session.TestId });
            Assert.True(ping.IsSuccessStatusCode, await ping.Content.ReadAsStringAsync());
            Assert.False((await ping.Content.ReadFromJsonAsync<MonitoringEventResultDto>(Json))!.Finished);

            live = (await admin.GetFromJsonAsync<MonitoringLiveDto>($"/api/admin/monitoring/sittings/{sittingId}/live", Json))!;
            var me = live.Rows.Single(r => r.StudentId == w.PupilsA[0]);
            Assert.Equal(("in_progress", 1, 1, 2), (me.Status, me.Step, me.Answered, me.Total));
            Assert.NotNull(me.LastSeenAt);
            Assert.NotNull(me.Deadline);
            var one = (await admin.GetFromJsonAsync<MonitoringLiveDto>($"/api/admin/monitoring/tests/{detail.Tests[0].Id}/live", Json))!;
            Assert.Equal(1, one.Rows.Single(r => r.StudentId == w.PupilsA[0]).Answered);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/sittings/{sittingId}");
            foreach (var t in detail.Tests) await admin.DeleteAsync($"/api/admin/monitoring/tests/{t.Id}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankA}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankB}");
        }
    }

    /// <summary>
    /// Deploy oldi himoyalari (§2.8): a whole class finishing at the same moment — every finish
    /// succeeds and each pupil gets exactly one Mavsumiy baho (each finish rescores the whole test
    /// and used to race on the unique mark); the readiness check names known problems before the
    /// pupils come; the session carries the server's clock.
    /// </summary>
    [Fact]
    public async Task Bir_vaqtda_yakunlash_tayyorlik_tekshiruvi_va_server_vaqti()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Bank", subjectId = w.Subject.Id, grade = w.Grade }));
        var q1 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "2+2?", options = new[] { "4", "5", "Barcha javoblar to'g'ri" }, correctIndex = 0 }));
        var q2 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "Rasmda nima?", imageUrl = "/uploads/yoqolgan-rasm.png", options = new[] { "a", "b" }, correctIndex = 1 }));
        var now = DateTimeOffset.UtcNow;
        var testId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
        {
            title = "Bir vaqtda", subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-25",
            opensAt = now.AddMinutes(-5), closesAt = now.AddMinutes(20), timeLimitMin = 30, shuffle = true,
            owners = new[] { new { kind = "class", id = w.ClassA.Id }, new { kind = "class", id = w.ClassB.Id } },
            questionIds = new[] { q1, q2 }, seasonalYear = 2026, seasonalMonth = 10,
        }));
        try
        {
            // before opening: not opened (error), positional option + missing image + short window
            var checks = (await admin.GetFromJsonAsync<List<MonitoringCheckDto>>($"/api/admin/monitoring/tests/{testId}/readiness", Json))!;
            Assert.Contains(checks, c => c.Level == "error" && c.Message.Contains("ochilmagan"));
            Assert.Contains(checks, c => c.Level == "error" && c.Message.Contains("rasm serverda topilmadi: 2"));
            Assert.Contains(checks, c => c.Level == "warning" && c.Message.Contains("aralashtirilganda") && c.Message.Contains("1-savol"));
            Assert.Contains(checks, c => c.Level == "warning" && c.Message.Contains("test vaqtidan (30 daq) qisqa"));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/published", null)).StatusCode);
            checks = (await admin.GetFromJsonAsync<List<MonitoringCheckDto>>($"/api/admin/monitoring/tests/{testId}/readiness", Json))!;
            Assert.DoesNotContain(checks, c => c.Message.Contains("ochilmagan"));

            var pupils = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!.Participants;
            Assert.Equal(5, pupils.Count);
            using var anon = fixture.Api.AnonymousClient();
            var devices = pupils.ToDictionary(p => p.Id, _ => "device-" + Guid.NewGuid().ToString("N"));
            foreach (var p in pupils)
            {
                var res = await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = p.AccessCode, device = devices[p.Id] });
                var session = (await res.Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
                Assert.NotNull(session.ServerNow);
                Assert.InRange(session.ServerNow!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
                await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code = p.AccessCode, device = devices[p.Id], order = 1, choice = "A" });
            }

            // the whole class presses "Yakunlash" at once
            var finishes = await Task.WhenAll(pupils.Select(p =>
                anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code = p.AccessCode, device = devices[p.Id] })));
            Assert.All(finishes, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

            var done = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!.Participants;
            Assert.All(done, p => Assert.Equal((MonitoringParticipantStatus.Done, 50m), (p.Status, p.Percent)));
            var marks = 0;
            var ids = pupils.Select(p => p.StudentId).ToList();
            await fixture.Api.WithDbAsync(async db => marks = await db.SeasonalMarks.CountAsync(m =>
                ids.Contains(m.StudentId) && m.SubjectId == w.Subject.Id && m.Year == 2026 && m.Month == 10));
            Assert.Equal(5, marks);

            var time = await anon.GetFromJsonAsync<JsonElement>("/api/public/monitoring/time");
            Assert.True(time.TryGetProperty("now", out _));
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                db.SeasonalMarks.RemoveRange(db.SeasonalMarks.Where(m => m.SubjectId == w.Subject.Id));
                await db.SaveChangesAsync();
            });
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{testId}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
        }
    }

    /// <summary>
    /// Doimiy kod + xona PIN (2026-11-25, §2.9): a pupil keeps one code all year; the room PIN on the
    /// board picks the sitting (or a single online test); without the PIN or with a wrong one there is
    /// no entry; a regenerated code retires the old one; one-time sitting codes still work alone.
    /// </summary>
    [Fact]
    public async Task Doimiy_kod_va_xona_PIN_bilan_kirish()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Bank", subjectId = w.Subject.Id, grade = w.Grade }));
        var q1 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "2+2?", options = new[] { "3", "4" }, correctIndex = 1 }));
        var now = DateTimeOffset.UtcNow;
        async Task<Guid> TestAsync(string title)
        {
            var id = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", new
            {
                title, subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-25",
                opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
                owners = new[] { new { kind = "class", id = w.ClassA.Id } }, questionIds = new[] { q1 },
            }));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{id}/status/published", null)).StatusCode);
            return id;
        }
        var first = await TestAsync("1-fan");
        var second = await TestAsync("2-fan");
        var single = await TestAsync("Yakka test");
        var sittingId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/sittings",
            new { title = "PIN sessiyasi", testDate = "2026-11-25", testIds = new[] { first, second } }));
        try
        {
            var sitting = (await admin.GetFromJsonAsync<MonitoringSittingDetailDto>($"/api/admin/monitoring/sittings/{sittingId}", Json))!;
            var singlePin = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{single}", Json))!.RoomPin;
            Assert.Matches("^[0-9]{4}$", sitting.RoomPin!);
            Assert.Matches("^[0-9]{4}$", singlePin!);
            Assert.NotEqual(sitting.RoomPin, singlePin);

            var gen = await admin.PostAsync("/api/admin/monitoring/student-codes/generate", null);
            Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
            var codes = (await admin.GetFromJsonAsync<List<MonitoringStudentCodeDto>>(
                $"/api/admin/monitoring/student-codes?className={Uri.EscapeDataString(w.ClassA.Name)}", Json))!;
            Assert.Equal(3, codes.Count);
            Assert.All(codes, c => Assert.Equal(MonitoringService.CodeLength, c.Code!.Length));
            var mine = codes.Single(c => c.StudentId == w.PupilsA[0]).Code!;

            using var anon = fixture.Api.AnonymousClient();
            var device = "device-pin-" + Guid.NewGuid().ToString("N");
            Task<HttpResponseMessage> Check(string code, string? pin) =>
                anon.PostAsJsonAsync("/api/public/monitoring/check", new { code, device, pin });

            var noPin = await Check(mine, null);
            Assert.Equal(HttpStatusCode.BadRequest, noPin.StatusCode);
            Assert.Contains("PIN", await noPin.Content.ReadAsStringAsync());
            var wrong = Enumerable.Range(1000, 9000).Select(n => n.ToString()).First(p => p != sitting.RoomPin && p != singlePin);
            Assert.Equal(HttpStatusCode.NotFound, (await Check(mine, wrong)).StatusCode);

            // the sitting's PIN: the pupil's own sitting, in order
            var peek = (await (await Check(mine, sitting.RoomPin)).Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!;
            Assert.Equal(("PIN sessiyasi", first), (peek.SittingTitle, peek.TestId));
            var start = await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = mine, device, pin = sitting.RoomPin, testId = first });
            Assert.True(start.IsSuccessStatusCode, await start.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer",
                new { code = mine, device, pin = sitting.RoomPin, testId = first, order = 1, choice = "B" })).StatusCode);
            var done = (await (await anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code = mine, device, pin = sitting.RoomPin, testId = first }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal((true, 100m), (done.Finished, done.ResultPercent));
            Assert.Equal(second, (await (await Check(mine, sitting.RoomPin)).Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!.TestId);

            // the single test's PIN: that test
            Assert.Equal(single, (await (await Check(mine, singlePin)).Content.ReadFromJsonAsync<MonitoringPeekDto>(Json))!.TestId);

            // a lost card: the new code works, the old one does not
            var regen = await admin.PostAsync($"/api/admin/monitoring/student-codes/{w.PupilsA[0]}/regenerate", null);
            var fresh = (await regen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
            Assert.Equal(HttpStatusCode.NotFound, (await Check(mine, sitting.RoomPin)).StatusCode);
            Assert.True((await Check(fresh, sitting.RoomPin)).IsSuccessStatusCode);

            // one-time sitting codes still need no PIN
            var oneTime = sitting.Rows.Single(r => r.StudentId == w.PupilsA[1]).Code;
            Assert.True((await Check(oneTime, null)).IsSuccessStatusCode);

            // a new PIN retires the old one for new entries
            var rotated = (await (await admin.PostAsync($"/api/admin/monitoring/sittings/{sittingId}/pin", null))
                .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pin").GetString()!;
            Assert.NotEqual(sitting.RoomPin, rotated);
            Assert.Equal(HttpStatusCode.NotFound, (await Check(fresh, sitting.RoomPin)).StatusCode);
            Assert.True((await Check(fresh, rotated)).IsSuccessStatusCode);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/sittings/{sittingId}");
            foreach (var t in new[] { first, second, single }) await admin.DeleteAsync($"/api/admin/monitoring/tests/{t}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
        }
    }

    /// <summary>
    /// Halollik (2026-11-24, curriculum-monitoring.md §2.3): in a shuffled test each pupil has an own
    /// order, stable on re-entry, and answers by the original letter; leaving the test window is
    /// counted (blur + hidden of one trip once), over the limit the test stops and o'quv bo'limi can
    /// let the pupil continue; option pictures travel with their option.
    /// </summary>
    [Fact]
    public async Task Onlayn_test_aralashtiriladi_oynadan_chiqish_sanaladi_variant_rasmi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Bank", subjectId = w.Subject.Id, grade = w.Grade }));
        var questions = $"/api/admin/monitoring/banks/{bankId}/questions";

        // a picture option may have no text; a foreign path and an empty option without a picture are refused
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(questions,
            new { text = "Q", options = new[] { "", "b" }, optionImages = new[] { "/uploads/../appsettings.json", "" }, correctIndex = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(questions,
            new { text = "Q", options = new[] { "", "b" }, correctIndex = 0 })).StatusCode);
        var ids = new List<Guid>();
        for (var i = 1; i <= 8; i++)
            ids.Add(await IdOf(await admin.PostAsJsonAsync(questions,
                new { text = $"{i}-savol", options = new[] { $"{i}a", $"{i}b", $"{i}c", $"{i}d" }, correctIndex = i % 4 })));
        ids.Add(await IdOf(await admin.PostAsJsonAsync(questions,
            new { text = "Qaysi biri uchburchak?", options = new[] { "", "Doira" }, optionImages = new[] { "/uploads/uchburchak.png", "" }, correctIndex = 0 })));

        var now = DateTimeOffset.UtcNow;
        object Payload(int? maxLeaves) => new
        {
            title = "Halol", subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple", testDate = "2026-11-24",
            opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
            owners = new[] { new { kind = "class", id = w.ClassA.Id } }, questionIds = ids, shuffle = true, maxLeaves,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/monitoring/tests", Payload(21))).StatusCode);
        var testId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", Payload(1)));
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/published", null)).StatusCode);
            var detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            Assert.True(detail.Shuffle);
            Assert.Equal(1, detail.MaxLeaves);
            Assert.Equal(["/uploads/uchburchak.png", ""], detail.Items.Single(i => i.Order == 9).OptionImages);
            var pupils = detail.Participants;
            var devices = pupils.Select(_ => "device-" + Guid.NewGuid().ToString("N")).ToArray();

            using var anon = fixture.Api.AnonymousClient();
            async Task<MonitoringSessionDto> StartAsync(int who)
            {
                var res = await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = pupils[who].AccessCode, device = devices[who] });
                Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
                return (await res.Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            }
            async Task<MonitoringEventResultDto> EventAsync(int who, string kind)
            {
                var res = await anon.PostAsJsonAsync("/api/public/monitoring/event", new { code = pupils[who].AccessCode, device = devices[who], kind });
                Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
                return (await res.Content.ReadFromJsonAsync<MonitoringEventResultDto>(Json))!;
            }
            static string Layout(MonitoringSessionDto s) => string.Join(";", s.Items.Select(i => $"{i.Order}:{string.Join(",", i.Keys!)}"));

            var sessions = new List<MonitoringSessionDto>();
            for (var i = 0; i < pupils.Count; i++) sessions.Add(await StartAsync(i));
            Assert.True(sessions.Select(Layout).Distinct().Count() > 1, "every pupil got the same order");
            Assert.Equal(Layout(sessions[0]), Layout(await StartAsync(0)));
            Assert.All(sessions[0].Items, i => Assert.Equal(
                Enumerable.Range(0, i.OptionCount).Select(k => ((char)('A' + k)).ToString()), i.Keys!.Order()));
            var shown = sessions[0].Items.Single(i => i.Order == 9);
            var at = shown.Keys!.IndexOf("A");
            Assert.Equal("/uploads/uchburchak.png", shown.OptionImages![at]);
            Assert.Equal("Doira", shown.Options[1 - at]);

            // all right through the shuffled screen: answers carry the original letter
            foreach (var it in sessions[0].Items)
            {
                var correct = ((char)('A' + detail.Items.Single(x => x.Order == it.Order).CorrectIndex)).ToString();
                Assert.Equal(HttpStatusCode.OK, (await anon.PutAsJsonAsync("/api/public/monitoring/answer",
                    new { code = pupils[0].AccessCode, device = devices[0], order = it.Order, choice = correct })).StatusCode);
            }

            Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/public/monitoring/event",
                new { code = pupils[0].AccessCode, device = devices[0], kind = "hack" })).StatusCode);
            Assert.Equal((1, false), ((await EventAsync(0, "blur")) is var e1 ? (e1.Leaves, e1.Finished) : default));
            Assert.Equal((1, false), ((await EventAsync(0, "hidden")) is var e2 ? (e2.Leaves, e2.Finished) : default));
            Assert.Equal((1, false), ((await EventAsync(0, "copy")) is var e3 ? (e3.Leaves, e3.Finished) : default));
            await Task.Delay(2200);
            Assert.Equal((2, true), ((await EventAsync(0, "fullscreen")) is var e4 ? (e4.Leaves, e4.Finished) : default));

            detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            var stopped = detail.Participants.Single(p => p.Id == pupils[0].Id);
            Assert.Equal(MonitoringParticipantStatus.Done, stopped.Status);
            Assert.Equal(MonitoringFinishReason.Leaves, stopped.FinishReason);
            Assert.Equal(100m, stopped.Percent);
            Assert.Equal(["blur", "hidden", "copy", "fullscreen"], stopped.Events!.Select(e => e.Split(' ')[1]));
            var told = (await (await anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code = pupils[0].AccessCode, device = devices[0] }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.True(told.Finished);
            Assert.Equal(MonitoringFinishReason.Leaves, told.FinishReason);

            // o'quv bo'limi lets the pupil continue: the count restarts, the log stays
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/monitoring/participants/{pupils[1].Id}/reopen", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/participants/{pupils[0].Id}/reopen", null)).StatusCode);
            var back = await StartAsync(0);
            Assert.False(back.Finished);
            Assert.Equal(0, back.Leaves);
            detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            Assert.EndsWith(" reopened", detail.Participants.Single(p => p.Id == pupils[0].Id).Events![^1]);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{testId}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
        }
    }

    /// <summary>
    /// Oylik test (2026-11-24): a finished pupil's percent becomes that month's Mavsumiy baho —
    /// overwriting a hand-typed one, following a key fix — and nothing is written for a pupil who
    /// did not sit it.
    /// </summary>
    [Fact]
    public async Task Oylik_onlayn_test_natijasi_mavsumiy_bahoga_yoziladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");
        var bankId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks",
            new { title = "Oylik", subjectId = w.Subject.Id, grade = w.Grade }));
        var q1 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "2+2?", options = new[] { "3", "4" }, correctIndex = 1 }));
        var q2 = await IdOf(await admin.PostAsJsonAsync($"/api/admin/monitoring/banks/{bankId}/questions",
            new { text = "3+3?", options = new[] { "6", "7" }, correctIndex = 0 }));

        var now = DateTimeOffset.UtcNow;
        object Payload(int? year, int? month) => new
        {
            title = "Noyabr oylik", subjectId = w.Subject.Id, grade = w.Grade, delivery = "online", scoring = "simple",
            testDate = "2026-11-24", opensAt = now.AddMinutes(-5), closesAt = now.AddHours(1), timeLimitMin = 30,
            owners = new[] { new { kind = "class", id = w.ClassA.Id } }, questionIds = new[] { q1, q2 },
            seasonalYear = year, seasonalMonth = month,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/monitoring/tests", Payload(2026, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/monitoring/tests", Payload(2026, 13))).StatusCode);

        var testId = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/tests", Payload(2026, 11)));
        // a teacher already typed a mark for the first pupil this month — the test replaces it
        var typed = new SeasonalMark
        {
            StudentId = w.PupilsA[0], ClassId = w.ClassA.Id, SubjectId = w.Subject.Id,
            PeriodKind = SeasonalPeriodKind.Monthly, Year = 2026, Month = 11, Score = 90m, Comment = "Qo'lda",
        };
        await fixture.Api.WithDbAsync(async db => { db.SeasonalMarks.Add(typed); await db.SaveChangesAsync(); });
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/published", null)).StatusCode);
            var detail = (await admin.GetFromJsonAsync<MonitoringTestDetailDto>($"/api/admin/monitoring/tests/{testId}", Json))!;
            Assert.Equal((2026, 11), (detail.SeasonalYear, detail.SeasonalMonth));
            var first = detail.Participants.Single(p => p.StudentId == w.PupilsA[0]);
            var second = detail.Participants.Single(p => p.StudentId == w.PupilsA[1]);

            using var anon = fixture.Api.AnonymousClient();
            async Task SitAsync(string code, string answers)
            {
                var device = "device-" + Guid.NewGuid().ToString("N");
                Assert.True((await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code, device })).IsSuccessStatusCode);
                for (var i = 0; i < answers.Length; i++)
                    await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code, device, order = i + 1, choice = answers[i].ToString() });
                Assert.True((await anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code, device })).IsSuccessStatusCode);
            }
            await SitAsync(first.AccessCode!, "BB");   // 1 of 2
            await SitAsync(second.AccessCode!, "BA");  // 2 of 2

            async Task<Dictionary<string, SeasonalMark>> MarksAsync()
            {
                Dictionary<string, SeasonalMark> marks = null!;
                await fixture.Api.WithDbAsync(async db => marks = await db.SeasonalMarks.AsNoTracking()
                    .Where(m => m.SubjectId == w.Subject.Id && m.PeriodKey == "M:2026-11").ToDictionaryAsync(m => m.StudentId));
                return marks;
            }
            var marks = await MarksAsync();
            Assert.Equal(2, marks.Count);
            Assert.Equal(50m, marks[w.PupilsA[0]].Score);
            Assert.Equal(typed.Id, marks[w.PupilsA[0]].Id);
            Assert.Equal("Qo'lda", marks[w.PupilsA[0]].Comment);
            Assert.Equal(100m, marks[w.PupilsA[1]].Score);
            Assert.Equal(w.ClassA.Id, marks[w.PupilsA[1]].ClassId);

            // key fix: question 2's answer is B — the marks follow
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/monitoring/tests/{testId}/items",
                new[] { new { order = 2, correctIndex = 1 } })).StatusCode);
            marks = await MarksAsync();
            Assert.Equal(100m, marks[w.PupilsA[0]].Score);
            Assert.Equal(50m, marks[w.PupilsA[1]].Score);

            // closing: the third pupil never sat it — absent, no mark
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/tests/{testId}/status/closed", null)).StatusCode);
            Assert.False((await MarksAsync()).ContainsKey(w.PupilsA[2]));
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/tests/{testId}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
            await fixture.Api.WithDbAsync(async db =>
            {
                db.SeasonalMarks.RemoveRange(db.SeasonalMarks.Where(m => m.SubjectId == w.Subject.Id));
                await db.SaveChangesAsync();
            });
        }
    }

    // ------------------------------------------------------------------ §3

    [Fact]
    public async Task Sertifikat_bali_yonalish_fanlaridan_hisoblanadi_eng_yaxshisi_jamiga_kiradi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var by = owner.Id;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var math = new Subject { Name = $"Matematika {tag}" };
        var physics = new Subject { Name = $"Fizika {tag}" };
        var history = new Subject { Name = $"Tarix {tag}" };
        var type = new CertificateType { Name = $"Milliy {tag}", UsesLevel = true, LevelOptions = "C,C+,B,B+,A,A+", ExamCredits = "A+=100;A=93.3;B=80", CreatedAt = DateTimeOffset.UtcNow };
        var pupil = Pupil($"Sardor {tag}", $"10-{tag}");
        var noTrack = Pupil($"Bek {tag}", $"7-{tag}");
        var track = new StudyGroup { Name = $"Aniq {tag}", IsTrack = true, CreatedBy = by, CreatedAt = DateTimeOffset.UtcNow };
        var today = AppClock.Today;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.AddRange(math, physics, history);
            db.CertificateTypes.Add(type);
            db.Students.AddRange(pupil, noTrack);
            db.StudyGroups.Add(track);
            db.StudyGroupMembers.Add(new StudyGroupMember { GroupId = track.Id, StudentId = pupil.Id, JoinedOn = today.AddDays(-30), CreatedBy = by, CreatedAt = DateTimeOffset.UtcNow });
            Certificate Cert(string studentId, string subjectId, string level, DateOnly? expires = null) => new()
            {
                StudentId = studentId, TypeId = type.Id, SubjectId = subjectId, Level = level, IssuedOn = today.AddDays(-10),
                ExpiresOn = expires, CreatedBy = by, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Certificates.AddRange(
                Cert(pupil.Id, math.Id, "A+"),          // 1st subject: 93, and maths is mandatory too: + 11
                Cert(pupil.Id, math.Id, "B"),           // worse in the same subject: not in the total
                Cert(pupil.Id, physics.Id, "A"),        // 2nd subject: 63 × 93.3% = 58.8
                Cert(pupil.Id, history.Id, "B"),        // mandatory: a flat 11, not 11 × 80%
                Cert(pupil.Id, history.Id, "A+", today.AddDays(-1)), // expired
                Cert(noTrack.Id, math.Id, "A+"));
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/certificates/track-subjects/{track.Id}",
            new TrackBlockSubjectsPayload(math.Id, physics.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/certificates/track-subjects/{track.Id}",
            new TrackBlockSubjectsPayload(math.Id, math.Id))).StatusCode);

        var row = Assert.Single((await admin.GetFromJsonAsync<List<CertificateRatingRowDto>>($"/api/admin/certificates/rating?studentId={pupil.Id}", Json))!);
        Assert.Equal(4, row.CertificateCount);
        Assert.Equal(93m + 11m + 58.8m + 11m, row.TotalPoints);
        Assert.Equal(track.Name, row.TrackName);
        Assert.Single(row.Items, i => i.Points == 104m && i.Role == CertificatePoints.RoleFirst && i.MandatoryPoints == 11m && i.Counted);
        Assert.Single(row.Items, i => i.Points == 58.8m && i.MandatoryPoints == null); // physics is not mandatory
        Assert.Single(row.Items, i => i.Points == 11m && i.Role == CertificatePoints.RoleMandatory && i.MandatoryPoints == null && i.Counted);
        Assert.Single(row.Items, i => i.Points == 85.4m && !i.Counted); // B in maths: 93 × 80% + 11

        var withExpired = Assert.Single((await admin.GetFromJsonAsync<List<CertificateRatingRowDto>>($"/api/admin/certificates/rating?studentId={pupil.Id}&includeExpired=true", Json))!);
        Assert.Equal(5, withExpired.CertificateCount);
        Assert.Equal(row.TotalPoints, withExpired.TotalPoints); // the expired one repeats history — best per subject

        var lone = Assert.Single((await admin.GetFromJsonAsync<List<CertificateRatingRowDto>>($"/api/admin/certificates/rating?studentId={noTrack.Id}", Json))!);
        Assert.Equal(1, lone.CertificateCount);
        Assert.Null(lone.TotalPoints);

        var list = (await admin.GetFromJsonAsync<List<CertificateDto>>($"/api/admin/certificates?studentId={pupil.Id}", Json))!;
        Assert.Contains(list, c => c.BlockPoints == 58.8m && c.BlockRole == CertificatePoints.RoleSecond);
        Assert.Contains(list, c => c.BlockPoints == 104m && c.BlockMandatoryPoints == 11m);

        Assert.True(CertificatePoints.IsMandatorySubject("Ona tili va adabiyot"));
        Assert.True(CertificatePoints.IsMandatorySubject("Matematika (majburiy)"));
        Assert.False(CertificatePoints.IsMandatorySubject("Mantiqiy matematika"));
        Assert.False(CertificatePoints.IsMandatorySubject("Chet tili"));
    }
}
