using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;
using static SchoolLms.Tests.BlockScheduleTests;

namespace SchoolLms.Tests;

/// <summary>
/// Bloklar — 5-bosqich: ustoz ekranlari (docs/modules/blocks.md, TZ 6.4; mijoz qarori 2).
///
/// <para>
/// <b>Nima isbotlanadi.</b> Blok darsining bahosi, davomati, mavzusi, chorak bahosi va oraliq test
/// natijasi blok guruhi va o'quvchining MANBA sinfi bilan yoziladi (2-ssenariyning yozish qismi);
/// ko'chirilgan o'quvchining eski bahosi eski guruhda qoladi (3-ssenariy); yo'nalishli blokda jurnal
/// bahosi yo'q, oraliq test bor; 3- va 4-sinf bloklarida baho bor. Ustoz ro'yxatida blok guruhi
/// "Ingliz tili, Katta (…)" ko'rinishida, bugungi darslar kunga qarab to'g'ri guruhni beradi (9-ssenariy).
/// </para>
/// <para>
/// <b>Va nima o'zgarmaydi:</b> sinf darsi va oddiy guruh darsining qatoriga belgi qo'yilmaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockTeacherTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("block_teacher");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static SetJournalEntryRequest Entry(string owner, string subject, string student, DateOnly day,
        int? grade = null, string? reason = null) =>
        new(owner, subject, 1, student, Iso(day), 2, grade, reason);

    /* =====================================================================
     *  Belgi: blok guruhi va manba sinf
     * ================================================================== */

    /// <summary>
    /// 2-ssenariy (yozish qismi): ustoz Ijtimoiydagi o'quvchiga baho qo'yadi — qator blok guruhi va
    /// manba sinf "Ijtimoiy" bilan yoziladi. 3- va 4-sinf blokida (manbalari sinflar) baho qo'yiladi.
    /// </summary>
    [Fact]
    public async Task Ssenariy_2_baho_manba_sinf_bilan_yoziladi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var (block, katta, students) = await ClassBlockAsync(db, w);

        Assert.Null(await JournalService.SetEntryAsync(db,
            Entry(katta.StudyGroupId.ToString(), w.English, students[0].Id, Today, grade: 5)));

        var row = await db.JournalEntries.AsNoTracking().SingleAsync(e => e.StudentId == students[0].Id);
        Assert.Equal(katta.Id, row.BlockGroupId);
        Assert.Equal(LessonOwnerKind.Group, row.OwnerKind);
        Assert.Equal(students[0].SourceClass, row.SourceClassId);
        Assert.Equal(5, row.Grade);
        var note = await db.LessonNotes.AsNoTracking().SingleAsync(n => n.ClassId == katta.StudyGroupId.ToString());
        Assert.Equal(katta.Id, note.BlockGroupId);

        Assert.Null(await JournalService.SetQuarterGradeAsync(db,
            new SetQuarterGradeRequest(katta.StudyGroupId.ToString(), w.English, 1, students[0].Id, 5)));
        var quarter = await db.QuarterGrades.AsNoTracking().SingleAsync(q => q.StudentId == students[0].Id);
        Assert.Equal(katta.Id, quarter.BlockGroupId);
        Assert.Equal(students[0].SourceClass, quarter.SourceClassId);
        _ = block;
    }

    /// <summary>
    /// 3-ssenariy: o'quvchi D sanadan Kichikdan Kattaga o'tdi. D gacha qo'yilgan baho Kichikda (eski
    /// guruh), D dan keyingisi Kattada; ikkalasida ham manba sinfi o'sha.
    /// </summary>
    [Fact]
    public async Task Ssenariy_3_kochirilgan_oquvchining_eski_bahosi_eski_guruhda()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var (katta, kichik) = await GroupsAsync(db, eng);
        await AssignAsync(db, eng, kichik, w.Fil1);
        var moveOn = Today.AddDays(-1);

        Assert.Null(await JournalService.SetEntryAsync(db,
            Entry(kichik.StudyGroupId.ToString(), w.English, w.Fil1, Today.AddDays(-3), reason: null) with { Homework = 1 }));
        Assert.Null(await new BlockService(db).MoveAsync(
            await db.Blocks.AsNoTracking().SingleAsync(b => b.Id == eng.Id), [w.Fil1], katta.Id, moveOn, "darajasi yetdi", w.UserId));
        Assert.Null(await JournalService.SetEntryAsync(db,
            Entry(katta.StudyGroupId.ToString(), w.English, w.Fil1, Today) with { Homework = 1 }));

        var rows = await db.JournalEntries.AsNoTracking().Where(e => e.StudentId == w.Fil1).OrderBy(e => e.Date).ToListAsync();
        Assert.Equal([kichik.Id, katta.Id], rows.Select(r => r.BlockGroupId!.Value));
        Assert.All(rows, r => Assert.Equal(w.Fil, r.SourceClassId));
        // Katta guruhining ro'yxati: o'quvchi ko'chirilgan kundan boshlab.
        var owner = (await LessonRoster.LiveOwnersAsync(db))[katta.StudyGroupId.ToString()];
        Assert.Empty(await LessonRoster.ForLessonAsync(db, owner, asOf: moveOn.AddDays(-1)));
        Assert.Single(await LessonRoster.ForLessonAsync(db, owner, asOf: moveOn));
    }

    /// <summary>Regressiya: sinf darsi va yo'nalish guruhi darsining qatoriga blok belgisi qo'yilmaydi.</summary>
    [Fact]
    public async Task Oddiy_darsga_belgi_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var cls = new SchoolClass { Name = "6-A", Grade = 6 };
        db.Classes.Add(cls);
        var pupil = Pupil("Oddiy 1", cls.Name);
        db.Students.Add(pupil);
        await db.SaveChangesAsync();

        Assert.Null(await JournalService.SetEntryAsync(db, Entry(cls.Id, w.Math, pupil.Id, Today, grade: 4)));
        Assert.Null(await JournalService.SetEntryAsync(db, Entry(w.Fil, w.Math, w.Fil1, Today, grade: 4)));

        var rows = await db.JournalEntries.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Null(r.BlockGroupId);
            Assert.Null(r.SourceClassId);
        });
    }

    /* =====================================================================
     *  Yo'nalishli blok: baho yo'q, oraliq test bor (mijoz qarori 2)
     * ================================================================== */

    [Fact]
    public async Task Yonalishli_blokda_baho_yoq_davomat_bor()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var (katta, _) = await GroupsAsync(db, eng);
        await AssignAsync(db, eng, katta, w.Fil1);
        var owner = katta.StudyGroupId.ToString();

        Assert.Equal(BlockJournal.NoGradesMessage,
            await JournalService.SetEntryAsync(db, Entry(owner, w.English, w.Fil1, Today, grade: 5)));
        Assert.Equal(BlockJournal.NoGradesMessage, await JournalService.SetQuarterGradeAsync(db,
            new SetQuarterGradeRequest(owner, w.English, 1, w.Fil1, 5)));
        // Davomat (kelmadi) — yoziladi.
        var reason = new AbsenceReason { Name = "Sababsiz" };
        db.AbsenceReasons.Add(reason);
        await db.SaveChangesAsync();
        Assert.Null(await JournalService.SetEntryAsync(db, Entry(owner, w.English, w.Fil1, Today, reason: reason.Id)));
        var row = await db.JournalEntries.AsNoTracking().SingleAsync();
        Assert.Null(row.Grade);
        Assert.Equal(reason.Id, row.ReasonId);
        Assert.Equal(katta.Id, row.BlockGroupId);
    }

    /// <summary>Yo'nalishli blok guruhida oraliq test olinadi: test blok guruhini, natija manba sinfni oladi.</summary>
    [Fact]
    public async Task Yonalishli_blokda_oraliq_test()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var (katta, _) = await GroupsAsync(db, eng);
        await AssignAsync(db, eng, katta, w.Fil1, w.Ijt1);
        await PlaceAsync(db, eng, new BlockSlot(0, 2));

        var options = await InterimTestService.OptionsAsync(db, scope: null);
        Assert.Contains(options, o => o.GroupId == katta.StudyGroupId && o.Subjects.Any(s => s.Id == w.English));

        var created = await InterimTestService.CreateAsync(db, null,
            new InterimTestRequest(katta.StudyGroupId, w.English, Iso(Today), 20, "Present Simple"), w.UserId, "Admin");
        Assert.Null(created.Error);
        var saved = await InterimTestService.SaveResultsAsync(db, null, created.Value!.Id,
            new InterimResultsRequest([new InterimResultCell(w.Ijt1, 15, false)]), "Admin");
        Assert.Null(saved.Error);

        var test = await db.InterimTests.AsNoTracking().SingleAsync();
        Assert.Equal(katta.Id, test.BlockGroupId);
        var result = await db.InterimTestResults.AsNoTracking().SingleAsync();
        Assert.Equal(w.Ijt, result.SourceClassId);
    }

    /* =====================================================================
     *  Ustoz ekranlari
     * ================================================================== */

    /// <summary>
    /// 9-ssenariy (ustoz tomoni): Katta ingliz ustozining haftasida Dushanba — "Ingliz tili, Katta
    /// (Fil+Ijt Ingliz)", baho yo'q (yo'nalishli blok); Kichik matematika ustozida Seshanba.
    /// </summary>
    [Fact]
    public async Task Ssenariy_9_ustozning_bugungi_darslari()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var math = await BlockAsync(db, w, "Fil+Ijt Matematika", w.Math, BlockDayPattern.Even, [w.Fil, w.Ijt],
            teachers: (w.TeacherC, w.TeacherD));
        await PlaceAsync(db, eng, new BlockSlot(0, 2));
        await PlaceAsync(db, math, new BlockSlot(1, 2));
        var (quarter, week) = await PortalSchedule.CurrentQuarterWeekAsync(db);

        var a = await PortalSchedule.TeacherWeekAsync(db, w.TeacherA, quarter, week);
        var monday = Assert.Single(a, l => l.Day == 0 && l.Period == 2);
        Assert.Equal("Ingliz tili, Katta (Fil+Ijt Ingliz)", monday.ClassName);
        Assert.True(monday.NoGrades);
        Assert.Equal(LessonOwnerKind.Group, monday.OwnerKind);

        var d = await PortalSchedule.TeacherWeekAsync(db, w.TeacherD, quarter, week);
        var tuesday = Assert.Single(d, l => l.Day == 1 && l.Period == 2);
        Assert.Equal("Matematika, Kichik (Fil+Ijt Matematika)", tuesday.ClassName);
        Assert.DoesNotContain(d, l => l.Day == 0 && l.Period == 2);

        // "Guruhlarim": blok guruhi ustozning egalari orasida.
        var teacher = await db.Teachers.SingleAsync(t => t.Id == w.TeacherA);
        var (katta, _) = await GroupsAsync(db, eng);
        Assert.Contains(await TeacherOwnerAccess.OwnersAsync(db, teacher), o => o.Owner.Id == katta.StudyGroupId.ToString());
    }

    /// <summary>Ro'yxatda o'quvchi yonida manba sinfi: "Ali V. · Filologiya" (TZ 6.4).</summary>
    [Fact]
    public async Task Manba_sinf_nomlari()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var (katta, _) = await GroupsAsync(db, eng);
        await AssignAsync(db, eng, katta, w.Fil1, w.Ijt1);

        var names = await BlockJournal.SourceNamesAsync(db, katta.StudyGroupId.ToString());

        Assert.Equal("Filologiya", names[w.Fil1]);
        Assert.Equal("Ijtimoiy", names[w.Ijt1]);
        Assert.Empty(await BlockJournal.SourceNamesAsync(db, w.Fil));
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record ClassPupil(string Id, string SourceClass);

    /// <summary>4-A va 4-B dan ingliz bloki (manbalari sinflar — baho qo'yiladi), hamma Kattada.</summary>
    private static async Task<(Block, BlockGroup, List<ClassPupil>)> ClassBlockAsync(AppDbContext db, World w)
    {
        var a = new SchoolClass { Name = "4-A", Grade = 4 };
        var b = new SchoolClass { Name = "4-B", Grade = 4 };
        db.Classes.AddRange(a, b);
        var pupils = new[] { Pupil("To'rtA 1", a.Name), Pupil("To'rtB 1", b.Name) };
        db.Students.AddRange(pupils);
        await db.SaveChangesAsync();

        var req = new SaveBlockRequest("4-sinf Ingliz", w.English, BlockType.Level, BlockDayPattern.Odd,
            [new BlockSourceRef(LessonOwnerKind.Class, a.Id), new BlockSourceRef(LessonOwnerKind.Class, b.Id)],
            [new SaveBlockGroupRequest("Katta", w.TeacherA), new SaveBlockGroupRequest("Kichik", w.TeacherB)]);
        var service = new BlockService(db);
        Assert.Null(await service.ValidateAsync(req, null));
        var block = await service.CreateAsync(req, w.UserId);
        block.IsActive = true;
        await db.SaveChangesAsync();
        var (katta, _) = await GroupsAsync(db, block);
        await AssignAsync(db, block, katta, pupils[0].Id, pupils[1].Id);
        Assert.True(await BlockJournal.GradesAllowedAsync(db, katta.StudyGroupId.ToString()));
        return (block, katta, [new ClassPupil(pupils[0].Id, a.Id), new ClassPupil(pupils[1].Id, b.Id)]);
    }

    private static Student Pupil(string name, string className) => new()
    {
        FullName = name, FirstName = name.Split(' ')[0], LastName = name.Split(' ')[^1], BirthDate = "2016-01-01",
        Gender = "male", ClassName = className, EnrollmentDate = "2026-09-01",
    };
}
