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

                // a teacher cannot edit plan dates
                using var teacher = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.TeacherAUser.Id, w.TeacherAUser.FullName, w.TeacherAUser.Email));
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

            var session = (await (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceA }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal(2, session.Items.Count);
            Assert.Equal("--", session.Answers);
            Assert.DoesNotContain("correct", JsonSerializer.Serialize(session, Json), StringComparison.OrdinalIgnoreCase);

            // another device is locked out until the admin resets it
            Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceB })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code = first.AccessCode, device = deviceA, order = 1, choice = "B" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await anon.PutAsJsonAsync("/api/public/monitoring/answer", new { code = first.AccessCode, device = deviceA, order = 2, choice = "B" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/monitoring/participants/{first.Id}/reset-device", null)).StatusCode);
            var resumed = (await (await anon.PostAsJsonAsync("/api/public/monitoring/start", new { code = first.AccessCode, device = deviceB }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.Equal("BB", resumed.Answers);

            var finished = (await (await anon.PostAsJsonAsync("/api/public/monitoring/finish", new { code = first.AccessCode, device = deviceB }))
                .Content.ReadFromJsonAsync<MonitoringSessionDto>(Json))!;
            Assert.True(finished.Finished);
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
