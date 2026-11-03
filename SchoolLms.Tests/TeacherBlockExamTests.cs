using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Blok test (mijoz, 2026-11-03): bir fan ikki blokda bo'lishi mumkin — asosiy 3,1 va majburiy 1,1 —
/// va natijani dars jadvalida shu yo'nalish·fanni o'tadigan o'qituvchi kiritadi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherBlockExamTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly ExamActor Actor = new(null, "Test");
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  Bir fan ikki blokda
     * ================================================================== */

    [Fact]
    public async Task Bir_fan_boshqa_ball_bilan_ikkinchi_blokka_qoshiladi()
    {
        await using var db = await NewDbAsync("two_blocks");
        var w = await SeedAsync(db);

        var exam = await NewExamAsync(db, w, "2026-11-10");
        Assert.Equal([93m, 63m, 11m], exam.Sections.OrderBy(s => s.Order).Select(s => s.MaxScore!.Value));
        Assert.Equal(2, exam.Sections.Count(s => s.SubjectId == w.History));

        var columns = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Columns;
        Assert.Equal([1, 2, 3], columns.Select(c => c.Block));
    }

    [Fact]
    public async Task Bir_xil_ball_yoki_ballsiz_takror_rad_etiladi()
    {
        await using var db = await NewDbAsync("repeat");
        var w = await SeedAsync(db);

        var sameWeight = await CreateAsync(db,
            new ExamSectionInput(w.History, null, 30, null, 0, 1.1m),
            new ExamSectionInput(w.History, null, 10, null, 1, 1.1m));
        Assert.Contains("takrorlangan", sameWeight.Error!.Message);

        var plain = await CreateAsync(db,
            new ExamSectionInput(w.History, null, null, 50m, 0),
            new ExamSectionInput(w.History, null, 10, null, 1, 1.1m));
        Assert.Contains("takrorlangan", plain.Error!.Message);

        var admission = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            "Qabul", ExamKind.Admission, ExamDelivery.Manual, null, 5, "2026-11-10", null, null, null,
            [
                new ExamSectionInput(w.History, null, 30, null, 0, 3.1m),
                new ExamSectionInput(w.History, null, 10, null, 1, 1.1m),
            ]));
        Assert.Contains("takrorlangan", admission.Error!.Message);
    }

    [Fact]
    public async Task Qoralamani_tahrirlash_ikki_blokni_aralashtirmaydi()
    {
        await using var db = await NewDbAsync("edit");
        var w = await SeedAsync(db);
        var exam = await NewExamAsync(db, w, "2026-11-10");
        var ids = exam.Sections.Where(s => s.SubjectId == w.History).ToDictionary(s => s.PointsPerCorrect!.Value, s => s.Id);

        // Bloklar o'rni almashdi, biri ballini o'zgartirdi — tarix 3,1 o'z id'sini saqlaydi.
        var updated = await ExamService.UpdateAsync(db, Actor, exam.Id, new ExamUpsertRequest(
            exam.Title, ExamKind.Block, ExamDelivery.Manual, null, null, "2026-11-10", null, null, null,
            [
                new ExamSectionInput(w.History, null, 10, null, 0, 1.2m),
                new ExamSectionInput(w.History, null, 30, null, 1, 3.1m),
            ]));
        Assert.Null(updated.Error);

        var sections = updated.Value!.Sections.OrderBy(s => s.Order).ToList();
        Assert.Equal(2, sections.Count);
        Assert.Equal(1.2m, sections[0].PointsPerCorrect);
        Assert.Equal(ids[3.1m], sections[1].Id);
    }

    /* =====================================================================
     *  O'qituvchi qamrovi
     * ================================================================== */

    [Fact]
    public async Task Oqituvchi_faqat_oz_yonalishi_va_fanini_koradi()
    {
        await using var db = await NewDbAsync("scope");
        var w = await SeedAsync(db);
        var exam = await PublishedExamAsync(db, w);

        var rows = await TeacherBlockExamService.ListAsync(db, w.HistoryTeacher);
        var row = Assert.Single(rows);
        Assert.Equal(w.Track, row.OwnerId);
        Assert.True(row.IsTrack);
        Assert.Equal(1, row.PupilCount);
        Assert.Equal(0, row.DoneCount);

        var table = (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.Track)).Value!;
        Assert.Equal(2, table.Columns.Count);
        Assert.All(table.Columns, c => Assert.Equal(w.History, c.SubjectId));
        Assert.Equal("Yo'nalish o'quvchisi", Assert.Single(table.Rows).FullName);

        // Fani imtihonda yo'q o'qituvchi, jadvalsiz o'qituvchi va begona ega.
        Assert.Empty(await TeacherBlockExamService.ListAsync(db, w.IdleTeacher));
        Assert.Equal(403, (await TeacherBlockExamService.EntryAsync(db, w.IdleTeacher, exam.Id, w.Track)).Error!.Status);
        Assert.Equal(403, (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.OtherTrack)).Error!.Status);
    }

    [Fact]
    public async Task Qoralama_oqituvchiga_korinmaydi()
    {
        await using var db = await NewDbAsync("draft");
        var w = await SeedAsync(db);
        var exam = await NewExamAsync(db, w, "2026-11-10");
        await AddTracksAsync(db, exam.Id, w);

        Assert.Empty(await TeacherBlockExamService.ListAsync(db, w.HistoryTeacher));
        Assert.Equal(404, (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.Track)).Error!.Status);
    }

    [Fact]
    public async Task Ikki_oqituvchi_ballari_bitta_jamiga_yigiladi()
    {
        await using var db = await NewDbAsync("save");
        var w = await SeedAsync(db);
        var exam = await PublishedExamAsync(db, w);

        var table = (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.Track)).Value!;
        var pid = table.Rows.Single().ParticipantId;
        var main = table.Columns.Single(c => c.PointsPerCorrect == 3.1m);
        var compulsory = table.Columns.Single(c => c.PointsPerCorrect == 1.1m);

        var saved = await TeacherBlockExamService.SaveAsync(db, Actor, w.HistoryTeacher, exam.Id,
            new ExamTeacherSaveRequest(w.Track,
            [
                new ExamEntrySaveRow(pid,
                [
                    new ExamEntrySaveScore(main.SectionId, null, 20),
                    new ExamEntrySaveScore(compulsory.SectionId, null, 5),
                ], false),
            ]));
        Assert.Null(saved.Error);

        var after = (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.Track)).Value!.Rows.Single();
        Assert.Equal(62m, after.Scores[main.SectionId]);
        Assert.Equal(5.5m, after.Scores[compulsory.SectionId]);
        Assert.Null(after.TotalPoints);
        Assert.Equal(1, Assert.Single(await TeacherBlockExamService.ListAsync(db, w.HistoryTeacher)).DoneCount);

        // Boshqa fan o'qituvchisi: uning katagi ham jamiga qo'shiladi, tarix ballari o'chmaydi.
        var maths = (await TeacherBlockExamService.EntryAsync(db, w.MathTeacher, exam.Id, w.Track)).Value!;
        var mathColumn = Assert.Single(maths.Columns);
        Assert.False(maths.Rows.Single().Scores.ContainsKey(main.SectionId));
        Assert.Null((await TeacherBlockExamService.SaveAsync(db, Actor, w.MathTeacher, exam.Id,
            new ExamTeacherSaveRequest(w.Track,
                [new ExamEntrySaveRow(pid, [new ExamEntrySaveScore(mathColumn.SectionId, null, 10)], false)]))).Error);

        var admin = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Rows.Single(r => r.ParticipantId == pid);
        Assert.Equal(62m + 5.5m + 21m, admin.TotalPoints);
    }

    [Fact]
    public async Task Qamrovdan_tashqari_yozish_rad_etiladi()
    {
        await using var db = await NewDbAsync("refuse");
        var w = await SeedAsync(db);
        var exam = await PublishedExamAsync(db, w);

        var full = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!;
        var history = full.Columns.First(c => c.SubjectId == w.History);
        var maths = full.Columns.Single(c => c.SubjectId == w.Math);
        var mine = full.Rows.Single(r => r.FullName == "Yo'nalish o'quvchisi").ParticipantId;
        var foreign = full.Rows.Single(r => r.FullName == "Boshqa yo'nalish").ParticipantId;

        async Task<int?> TryAsync(ExamEntrySaveRow row) =>
            (await TeacherBlockExamService.SaveAsync(db, Actor, w.HistoryTeacher, exam.Id,
                new ExamTeacherSaveRequest(w.Track, [row]))).Error?.Status;

        Assert.Equal(403, await TryAsync(new ExamEntrySaveRow(foreign, [new ExamEntrySaveScore(history.SectionId, null, 1)], false)));
        Assert.Equal(403, await TryAsync(new ExamEntrySaveRow(mine, [new ExamEntrySaveScore(maths.SectionId, null, 1)], false)));
        Assert.Equal(403, await TryAsync(new ExamEntrySaveRow(mine, [], true)));

        // Admin "Kelmadi" belgilagan qator o'qituvchiga yopiq.
        Assert.Null((await ExamService.SaveEntryAsync(db, Actor, exam.Id, [new ExamEntrySaveRow(mine, [], true)])).Error);
        Assert.Equal(409, await TryAsync(new ExamEntrySaveRow(mine, [new ExamEntrySaveScore(history.SectionId, null, 1)], false)));

        Assert.Equal(0, await db.ExamSectionScores.AsNoTracking().CountAsync(s => s.ParticipantId == foreign));
    }

    [Fact]
    public async Task Sertifikat_ikkala_blokni_ham_ozod_qiladi()
    {
        await using var db = await NewDbAsync("credit");
        var w = await SeedAsync(db);
        var exam = await PublishedExamAsync(db, w);

        var user = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"tbe.{Guid.NewGuid():N}"[..16] };
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

        var row = (await TeacherBlockExamService.EntryAsync(db, w.HistoryTeacher, exam.Id, w.Track)).Value!.Rows.Single();
        Assert.Equal(2, row.Credits!.Count);
        Assert.Contains(93m, row.Credits.Values.Select(c => c.Points));
        Assert.Contains(11m, row.Credits.Values.Select(c => c.Points));
        Assert.Equal(1, Assert.Single(await TeacherBlockExamService.ListAsync(db, w.HistoryTeacher)).DoneCount);
    }

    [Fact]
    public async Task Http_oqituvchi_royxatdan_saqlashgacha_boradi_boshqa_rol_kira_olmaydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));
        var (examId, trackId) = ("", "");

        await fixture.Api.WithDbAsync(async db =>
        {
            var history = new Subject { Name = $"Tarix {tag}" };
            var pupil = NewStudent($"O'quvchi {tag}");
            var track = new StudyGroup { Name = $"Ijtimoiy {tag}", IsTrack = true, CreatedAt = AppClock.NowInstant };
            track.CreatedBy = await db.Users.Select(u => u.Id).FirstAsync();
            db.Subjects.Add(history);
            db.Students.Add(pupil);
            db.StudyGroups.Add(track);
            await db.SaveChangesAsync();

            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = track.Id, StudentId = pupil.Id, JoinedOn = new DateOnly(2026, 9, 1),
                CreatedBy = track.CreatedBy, CreatedAt = AppClock.NowInstant,
            });
            var teacherId = await db.Teachers.Where(t => t.UserId == user.Id).Select(t => t.Id).SingleAsync();
            var template = new ScheduleTemplate { ClassId = track.Id.ToString(), Name = tag, OwnerKind = LessonOwnerKind.Group };
            template.Lessons.Add(new ScheduleLesson { TemplateId = template.Id, Day = 0, Period = 1, SubjectId = history.Id, TeacherId = teacherId });
            db.ScheduleTemplates.Add(template);
            await db.SaveChangesAsync();

            var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
                $"HTTP {tag}", ExamKind.Block, ExamDelivery.Manual, null, null, "2026-11-10", null, null, null,
                [
                    new ExamSectionInput(history.Id, null, 30, null, 0, 3.1m),
                    new ExamSectionInput(history.Id, null, 10, null, 1, 1.1m),
                ]));
            examId = created.Value!.Id;
            trackId = track.Id.ToString();
            await ExamService.AddParticipantsAsync(db, Actor, examId,
                new ExamAddParticipantsRequest(null, null, null, [trackId]), callerHasAdmission: false);
            await ExamService.PublishAsync(db, Actor, examId);
        });

        var list = await client.GetFromJsonAsync<JsonElement>("/api/teacher/block-exams");
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(examId, row.GetProperty("examId").GetString());
        Assert.Equal(trackId, row.GetProperty("ownerId").GetString());

        var table = await client.GetFromJsonAsync<JsonElement>($"/api/teacher/block-exams/{examId}/entry?ownerId={trackId}");
        var column = table.GetProperty("columns")[1];
        Assert.Equal(2, column.GetProperty("block").GetInt32());
        var participantId = table.GetProperty("rows")[0].GetProperty("participantId").GetString();

        var save = await client.PostAsJsonAsync($"/api/teacher/block-exams/{examId}/entry", new
        {
            ownerId = trackId,
            rows = new[]
            {
                new { participantId, absent = false, scores = new[] { new { sectionId = column.GetProperty("sectionId").GetString(), correct = 7 } } },
            },
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(1, (await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saved").GetInt32());

        var absent = await client.PostAsJsonAsync($"/api/teacher/block-exams/{examId}/entry", new
        {
            ownerId = trackId,
            rows = new[] { new { participantId, absent = true, scores = Array.Empty<object>() } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, absent.StatusCode);
        Assert.False(string.IsNullOrEmpty((await absent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()));

        var anonymous = await fixture.Api.CreateClient().GetAsync("/api/teacher/block-exams");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var adminClient = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Admin, admin.Id, admin.FullName, admin.Email));
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.GetAsync("/api/teacher/block-exams")).StatusCode);
    }

    /* =====================================================================
     *  Ma'lumot
     * ================================================================== */

    private sealed record World(
        string History, string Math, string Track, string OtherTrack, string Pupil,
        string HistoryTeacher, string MathTeacher, string IdleTeacher);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var history = new Subject { Name = "Tarix" };
        var math = new Subject { Name = "Matematika" };
        var other = new Subject { Name = "Kimyo" };
        db.Subjects.AddRange(history, math, other);

        var pupil = NewStudent("Yo'nalish o'quvchisi");
        var foreign = NewStudent("Boshqa yo'nalish");
        db.Students.AddRange(pupil, foreign);

        var creator = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"tbe.{Guid.NewGuid():N}"[..16] };
        db.Users.Add(creator);
        var track = new StudyGroup { Name = "Ijtimoiy", IsTrack = true, CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant };
        var otherTrack = new StudyGroup { Name = "Tabiiy fanlar", IsTrack = true, CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant };
        db.StudyGroups.AddRange(track, otherTrack);

        var historyTeacher = NewTeacher("Tarix o'qituvchi");
        var mathTeacher = NewTeacher("Matematika o'qituvchi");
        var idleTeacher = NewTeacher("Jadvalsiz o'qituvchi");
        db.Teachers.AddRange(historyTeacher, mathTeacher, idleTeacher);
        await db.SaveChangesAsync();

        db.StudyGroupMembers.AddRange(
            new StudyGroupMember { GroupId = track.Id, StudentId = pupil.Id, JoinedOn = new DateOnly(2026, 9, 1), CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant },
            new StudyGroupMember { GroupId = otherTrack.Id, StudentId = foreign.Id, JoinedOn = new DateOnly(2026, 9, 1), CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant });

        var template = new ScheduleTemplate { ClassId = track.Id.ToString(), Name = "Ijtimoiy", OwnerKind = LessonOwnerKind.Group };
        template.Lessons.Add(new ScheduleLesson { TemplateId = template.Id, Day = 0, Period = 1, SubjectId = history.Id, TeacherId = historyTeacher.Id });
        template.Lessons.Add(new ScheduleLesson { TemplateId = template.Id, Day = 0, Period = 2, SubjectId = math.Id, TeacherId = mathTeacher.Id });
        // Imtihonda yo'q fan — ro'yxatga chiqmasligi kerak.
        template.Lessons.Add(new ScheduleLesson { TemplateId = template.Id, Day = 0, Period = 3, SubjectId = other.Id, TeacherId = idleTeacher.Id });
        db.ScheduleTemplates.Add(template);
        await db.SaveChangesAsync();

        return new World(history.Id, math.Id, track.Id.ToString(), otherTrack.Id.ToString(), pupil.Id,
            historyTeacher.Id, mathTeacher.Id, idleTeacher.Id);
    }

    private static Task<ExamOutcome<ExamDto>> CreateAsync(AppDbContext db, params ExamSectionInput[] sections) =>
        ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            $"DTM {Guid.NewGuid():N}"[..12], ExamKind.Block, ExamDelivery.Manual, null, null, "2026-11-10", null, null, null, sections));

    private static async Task<ExamDto> NewExamAsync(AppDbContext db, World w, string date)
    {
        var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            $"DTM {Guid.NewGuid():N}"[..12], ExamKind.Block, ExamDelivery.Manual, null, null, date, null, null, null,
            [
                new ExamSectionInput(w.History, null, 30, null, 0, 3.1m),
                new ExamSectionInput(w.Math, null, 30, null, 1, 2.1m),
                new ExamSectionInput(w.History, null, 10, null, 2, 1.1m),
            ]));
        Assert.Null(created.Error);
        return created.Value!;
    }

    private static async Task AddTracksAsync(AppDbContext db, string examId, World w) =>
        Assert.Null((await ExamService.AddParticipantsAsync(db, Actor, examId,
            new ExamAddParticipantsRequest(null, null, null, [w.Track, w.OtherTrack]), callerHasAdmission: false)).Error);

    private static async Task<ExamDto> PublishedExamAsync(AppDbContext db, World w)
    {
        var exam = await NewExamAsync(db, w, "2026-11-10");
        await AddTracksAsync(db, exam.Id, w);
        var published = await ExamService.PublishAsync(db, Actor, exam.Id);
        Assert.Null(published.Error);
        return published.Value!;
    }

    private static Teacher NewTeacher(string fullName) => new() { FullName = fullName };

    private static Student NewStudent(string fullName) => new()
    {
        FullName = fullName, LastName = "Familiya", FirstName = "Ism", MiddleName = "Otasi",
        BirthDate = "2009-01-01", Address = "Toshkent", Gender = "male",
        ParentFullName = "Ota-ona", ParentLastName = "Familiya", ParentFirstName = "Ism", ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233", ClassName = "9-A", EnrollmentDate = "2025-09-01",
    };

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("tbe_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }
}
