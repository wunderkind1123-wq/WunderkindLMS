using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Ota-ona kabinetida test natijalari va oylik o'sish (mijoz, 2026-11-03):
/// natija kiritilishi bilan ko'rinadi, chala natija oy hisobiga kirmaydi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PortalExamResultsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly ExamActor Actor = new(null, "Test");
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Natija_kiritilmaguncha_korinmaydi_chala_natijada_jami_yoq()
    {
        await using var db = await NewDbAsync("partial");
        var w = await SeedAsync(db);
        var exam = await NewExamAsync(db, w, "Oktabr", "2026-10-15");

        Assert.Empty((await PortalExamResultsService.ForStudentAsync(db, w.Pupil)).Exams);

        await EnterAsync(db, exam, w.Math, 40m);
        var partial = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);
        var row = Assert.Single(partial.Exams);
        Assert.False(row.IsComplete);
        Assert.Null(row.TotalPoints);
        Assert.Null(row.Percent);
        Assert.Equal(40m, row.Blocks.Single(b => b.SubjectId == w.Math).Points);
        Assert.Null(row.Blocks.Single(b => b.SubjectId == w.History).Points);
        var month = Assert.Single(partial.Months);
        Assert.Null(month.TestPercent);
        Assert.Equal(1, month.PendingCount);

        await EnterAsync(db, exam, w.History, 30m);
        var whole = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);
        Assert.True(whole.Exams.Single().IsComplete);
        Assert.Equal(70m, whole.Exams.Single().TotalPoints);
        Assert.Equal(70m, whole.Exams.Single().Percent);
        Assert.Equal(70m, whole.Months.Single().TestPercent);
        Assert.Equal(0, whole.Months.Single().PendingCount);
    }

    [Fact]
    public async Task Oylik_hisob_ortacha_va_ozgarish_beradi()
    {
        await using var db = await NewDbAsync("months");
        var w = await SeedAsync(db);

        // Sentabr: 60%. Oktabr: 70% va 90% → 80%, +20. Noyabrda test yo'q. Dekabr: 75%, −5 (oktabrga nisbatan).
        await WholeExamAsync(db, w, "S", "2026-09-10", 30m, 30m);
        await WholeExamAsync(db, w, "O1", "2026-10-05", 30m, 40m);
        await WholeExamAsync(db, w, "O2", "2026-10-25", 40m, 50m);
        await WholeExamAsync(db, w, "D", "2026-12-05", 35m, 40m);

        db.SeasonalMarks.AddRange(
            new SeasonalMark { StudentId = w.Pupil, ClassId = w.Class, SubjectId = w.Math, PeriodKind = SeasonalPeriodKind.Monthly, Year = 2026, Month = 10, Score = 80m },
            new SeasonalMark { StudentId = w.Pupil, ClassId = w.Class, SubjectId = w.Math, PeriodKind = SeasonalPeriodKind.Monthly, Year = 2026, Month = 11, Score = 90m },
            // Choraklik baho oy hisobiga kirmaydi.
            new SeasonalMark { StudentId = w.Pupil, ClassId = w.Class, SubjectId = w.Math, PeriodKind = SeasonalPeriodKind.Quarterly, Year = 2026, Quarter = 2, Score = 10m });
        await db.SaveChangesAsync();

        var months = (await PortalExamResultsService.ForStudentAsync(db, w.Pupil)).Months;
        Assert.Equal(["2026-12", "2026-11", "2026-10", "2026-09"], months.Select(m => m.Month));

        var sep = months[3];
        Assert.Equal(60m, sep.TestPercent);
        Assert.Null(sep.TestChange);

        var oct = months[2];
        Assert.Equal(80m, oct.TestPercent);
        Assert.Equal(20m, oct.TestChange);
        Assert.Equal(2, oct.TestCount);
        Assert.Equal(80m, oct.MarkAverage);
        Assert.Null(oct.MarkChange);
        var octMath = oct.Subjects.Single(s => s.SubjectId == w.Math);
        Assert.Equal(70m, octMath.TestPercent);
        Assert.Equal(80m, octMath.Mark);

        var nov = months[1];
        Assert.Null(nov.TestPercent);
        Assert.Equal(90m, nov.MarkAverage);
        Assert.Equal(10m, nov.MarkChange);

        Assert.Equal(75m, months[0].TestPercent);
        Assert.Equal(-5m, months[0].TestChange);
    }

    [Fact]
    public async Task Bir_fan_ikki_blokda_oyda_birlashadi_sertifikat_belgilanadi()
    {
        await using var db = await NewDbAsync("two_blocks");
        var w = await SeedAsync(db);

        var user = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"per.{Guid.NewGuid():N}"[..16] };
        var type = new CertificateType
        {
            Name = "Milliy sertifikat", UsesLevel = true, LevelOptions = "A,A+", ExamCredits = "A+=100", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.CertificateTypes.Add(type);
        await db.SaveChangesAsync();
        db.Certificates.Add(new Certificate
        {
            StudentId = w.Pupil, TypeId = type.Id, SubjectId = w.History, Level = "A+",
            IssuedOn = new DateOnly(2026, 9, 1), CreatedBy = user.Id, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            "DTM", ExamKind.Block, ExamDelivery.Manual, null, null, "2026-10-15", null, null, null,
            [
                new ExamSectionInput(w.History, null, 30, null, 0, 3.1m),
                new ExamSectionInput(w.Math, null, 30, null, 1, 2.1m),
                new ExamSectionInput(w.History, null, 10, null, 2, 1.1m),
            ]));
        var exam = created.Value!;
        await AddPupilsAsync(db, exam.Id, w);
        var math = exam.Sections.Single(s => s.SubjectId == w.Math);
        var pid = await ParticipantAsync(db, exam.Id, w.Pupil);
        Assert.Null((await ExamService.SaveEntryAsync(db, Actor, exam.Id,
            [new ExamEntrySaveRow(pid, [new ExamEntrySaveScore(math.Id, null, 10)], false)])).Error);

        var result = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);
        var row = result.Exams.Single();
        Assert.True(row.IsComplete);
        Assert.Equal([1, 2, 3], row.Blocks.Select(b => b.Block));
        Assert.All(row.Blocks.Where(b => b.SubjectId == w.History), b => Assert.NotNull(b.Certificate));
        Assert.Equal(93m + 21m + 11m, row.TotalPoints);
        // Ikkala tarix bloki bitta fan: (93 + 11) / (93 + 11) = 100%.
        Assert.Equal(100m, result.Months.Single().Subjects.Single(s => s.SubjectId == w.History).TestPercent);
    }

    [Fact]
    public async Task Kelmagan_va_bekor_qilingan_hisobga_kirmaydi_boshqa_oquvchi_korinmaydi()
    {
        await using var db = await NewDbAsync("absent");
        var w = await SeedAsync(db);

        var present = await WholeExamAsync(db, w, "Bor", "2026-10-05", 40m, 40m);

        var missed = await NewExamAsync(db, w, "Kelmadi", "2026-10-10");
        Assert.Null((await ExamService.SaveEntryAsync(db, Actor, missed.Id,
            [new ExamEntrySaveRow(await ParticipantAsync(db, missed.Id, w.Pupil), [], true)])).Error);

        var cancelled = await WholeExamAsync(db, w, "Bekor", "2026-10-20", 1m, 1m);
        Assert.Null((await ExamService.CancelAsync(db, Actor, cancelled.Id, "xato")).Error);

        var result = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);
        Assert.Equal(2, result.Exams.Count);
        Assert.DoesNotContain(result.Exams, e => e.ExamId == cancelled.Id);
        var absent = result.Exams.Single(e => e.ExamId == missed.Id);
        Assert.True(absent.IsAbsent);
        Assert.Empty(absent.Blocks);

        var month = result.Months.Single();
        Assert.Equal(80m, month.TestPercent);
        Assert.Equal(1, month.TestCount);
        Assert.Equal(1, month.AbsentCount);

        // Sinfdoshi bu imtihonda bor, lekin unga natija kiritilmagan.
        Assert.Empty((await PortalExamResultsService.ForStudentAsync(db, w.Classmate)).Exams);
        Assert.NotNull(present);
    }

    [Fact]
    public async Task Sinf_3_8da_faqat_oylik_baholar_blok_test_berilmaydi()
    {
        await using var db = await NewDbAsync("grade5");
        var w = await SeedAsync(db, grade: 5);
        await WholeExamAsync(db, w, "Blok", "2026-10-05", 30m, 40m);
        db.SeasonalMarks.AddRange(
            new SeasonalMark { StudentId = w.Pupil, ClassId = w.Class, SubjectId = w.Math, PeriodKind = SeasonalPeriodKind.Monthly, Year = 2026, Month = 9, Score = 48m },
            new SeasonalMark { StudentId = w.Pupil, ClassId = w.Class, SubjectId = w.Math, PeriodKind = SeasonalPeriodKind.Monthly, Year = 2026, Month = 10, Score = 57m });
        await db.SaveChangesAsync();

        var result = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);

        Assert.False(result.ShowTests);
        Assert.Empty(result.Exams);
        Assert.Equal(["2026-10", "2026-09"], result.Months.Select(m => m.Month));
        Assert.All(result.Months, m => Assert.Null(m.TestPercent));
        Assert.Equal(9m, result.Months[0].MarkChange);
    }

    [Fact]
    public async Task Yonalishdagi_oquvchi_ikkala_kartani_oladi_sinfi_past_bolsa_ham()
    {
        await using var db = await NewDbAsync("track");
        var w = await SeedAsync(db, grade: 5);
        var user = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"per.{Guid.NewGuid():N}"[..16] };
        var track = new StudyGroup { Name = "Filologiya", IsTrack = true, CreatedBy = user.Id, CreatedAt = AppClock.NowInstant };
        db.Users.Add(user);
        db.StudyGroups.Add(track);
        db.StudyGroupMembers.Add(new StudyGroupMember { GroupId = track.Id, StudentId = w.Pupil, JoinedOn = new DateOnly(2026, 9, 1), CreatedBy = user.Id, CreatedAt = AppClock.NowInstant });
        await db.SaveChangesAsync();
        await WholeExamAsync(db, w, "Blok", "2026-10-05", 30m, 40m);

        var result = await PortalExamResultsService.ForStudentAsync(db, w.Pupil);

        Assert.True(result.ShowTests);
        Assert.Single(result.Exams);
        Assert.Equal(70m, result.Months.Single().TestPercent);
    }

    [Fact]
    public async Task Fan_ozgarishi_shu_fan_bor_oldingi_oyga_nisbatan()
    {
        await using var db = await NewDbAsync("subject_change");
        var w = await SeedAsync(db, grade: 5);
        SeasonalMark Mark(string subject, int month, decimal score) => new()
        {
            StudentId = w.Pupil, ClassId = w.Class, SubjectId = subject, PeriodKind = SeasonalPeriodKind.Monthly,
            Year = 2026, Month = month, Score = score,
        };
        // Matematika: 50 → (oktabrda yo'q) → 70. Tarix faqat oktabrda.
        db.SeasonalMarks.AddRange(Mark(w.Math, 9, 50m), Mark(w.History, 10, 80m), Mark(w.Math, 11, 70m), Mark(w.History, 11, 60m));
        await db.SaveChangesAsync();

        var months = (await PortalExamResultsService.ForStudentAsync(db, w.Pupil)).Months;

        Assert.Null(months[2].Subjects.Single(s => s.SubjectId == w.Math).MarkChange);
        Assert.Null(months[1].Subjects.Single(s => s.SubjectId == w.History).MarkChange);
        Assert.Equal(20m, months[0].Subjects.Single(s => s.SubjectId == w.Math).MarkChange);
        Assert.Equal(-20m, months[0].Subjects.Single(s => s.SubjectId == w.History).MarkChange);
    }

    [Fact]
    public async Task Http_admin_studentId_bilan_oladi_kirmagan_kira_olmaydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Admin, admin.Id, admin.FullName, admin.Email));
        var pupilId = "";

        await fixture.Api.WithDbAsync(async db =>
        {
            var subject = new Subject { Name = $"Fan {tag}" };
            var cls = new SchoolClass { Name = $"9-{tag}", Grade = 9 };
            var pupil = NewStudent($"O'quvchi {tag}", cls.Name);
            db.AddRange(subject, cls, pupil);
            await db.SaveChangesAsync();
            pupilId = pupil.Id;

            var exam = (await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
                $"Oylik {tag}", ExamKind.Block, ExamDelivery.Manual, null, null, "2026-10-15", null, null, null,
                [new ExamSectionInput(subject.Id, null, null, 100m, 0)]))).Value!;
            await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
                new ExamAddParticipantsRequest(null, [pupil.Id], null), callerHasAdmission: false);
            await ExamService.SaveEntryAsync(db, Actor, exam.Id,
                [new ExamEntrySaveRow(await ParticipantAsync(db, exam.Id, pupil.Id), [new ExamEntrySaveScore(exam.Sections.Single().Id, 85m)], false)]);
        });

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/student/exam-results?studentId={pupilId}");
        Assert.Equal(85m, body.GetProperty("exams")[0].GetProperty("percent").GetDecimal());
        Assert.Equal("2026-10", body.GetProperty("months")[0].GetProperty("month").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/student/exam-results")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Api.CreateClient().GetAsync("/api/student/exam-results")).StatusCode);
    }

    /* =====================================================================
     *  Ma'lumot
     * ================================================================== */

    private sealed record World(string Math, string History, string Class, string Pupil, string Classmate);

    private static async Task<World> SeedAsync(AppDbContext db, int grade = 9)
    {
        var math = new Subject { Name = "Matematika" };
        var history = new Subject { Name = "Tarix" };
        var cls = new SchoolClass { Name = $"{grade}-A", Grade = grade };
        var pupil = NewStudent("O'quvchi", cls.Name);
        var classmate = NewStudent("Sinfdosh", cls.Name);
        db.AddRange(math, history, cls, pupil, classmate);
        await db.SaveChangesAsync();
        return new World(math.Id, history.Id, cls.Id, pupil.Id, classmate.Id);
    }

    private static async Task<ExamDto> NewExamAsync(AppDbContext db, World w, string title, string date)
    {
        var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            title, ExamKind.Block, ExamDelivery.Manual, null, null, date, null, null, null,
            [
                new ExamSectionInput(w.Math, null, null, 50m, 0),
                new ExamSectionInput(w.History, null, null, 50m, 1),
            ]));
        Assert.Null(created.Error);
        await AddPupilsAsync(db, created.Value!.Id, w);
        return created.Value!;
    }

    private static async Task AddPupilsAsync(AppDbContext db, string examId, World w) =>
        Assert.Null((await ExamService.AddParticipantsAsync(db, Actor, examId,
            new ExamAddParticipantsRequest(null, [w.Pupil, w.Classmate], null), callerHasAdmission: false)).Error);

    private static async Task<ExamDto> WholeExamAsync(AppDbContext db, World w, string title, string date, decimal math, decimal history)
    {
        var exam = await NewExamAsync(db, w, title, date);
        await EnterAsync(db, exam, w.Math, math);
        await EnterAsync(db, exam, w.History, history);
        return exam;
    }

    private static async Task EnterAsync(AppDbContext db, ExamDto exam, string subjectId, decimal points)
    {
        var pupil = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Rows.Single(r => r.FullName == "O'quvchi");
        var section = exam.Sections.Single(s => s.SubjectId == subjectId);
        Assert.Null((await ExamService.SaveEntryAsync(db, Actor, exam.Id,
            [new ExamEntrySaveRow(pupil.ParticipantId, [new ExamEntrySaveScore(section.Id, points)], false)])).Error);
    }

    private static async Task<string> ParticipantAsync(AppDbContext db, string examId, string studentId) =>
        (await ExamService.ListParticipantsAsync(db, examId, new ExamParticipantListQuery { Limit = 200 }))
        .Value!.Items.Single(p => p.StudentId == studentId).Id;

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName, LastName = "Familiya", FirstName = "Ism", MiddleName = "Otasi",
        BirthDate = "2015-01-01", Address = "Toshkent", Gender = "male",
        ParentFullName = "Ota-ona", ParentLastName = "Familiya", ParentFirstName = "Ism", ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233", ClassName = className, EnrollmentDate = "2025-09-01",
    };

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("per_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }
}
