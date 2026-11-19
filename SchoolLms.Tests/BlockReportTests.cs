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
/// Bloklar — 6-bosqich: sinf jurnali va hisobotlar (docs/modules/blocks.md, TZ 6.5).
///
/// <para>
/// <b>Nima isbotlanadi.</b> 2-ssenariy (ko'rish qismi): blok guruhida qo'yilgan baho o'quvchining
/// MANBA sinfi jurnalida "K" belgisi va ustoz ismi bilan ko'rinadi; sinf jurnalidagi blok ustuni faqat
/// ko'rish uchun. 7-ssenariy: 17:00 hisobotida ustozning har bir blok guruhi alohida qator, ballar
/// to'g'ri; sinf kesimida blok bahosi manba sinfga yoziladi. Davr hisoboti va oylik test natijalari.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockReportTests(ApiFixture fixture) : IAsyncLifetime
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
        var database = await fixture.Postgres.CreateDatabaseAsync("block_report");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>Bugungi hafta kuni (0 = Dushanba ... 6 = Yakshanba).</summary>
    private static int TodayDay => ((int)Today.DayOfWeek + 6) % 7;

    private static string Monday => Iso(Today.AddDays(-TodayDay));

    /// <summary>
    /// 2-ssenariy: 4-B o'quvchisiga blokning Katta guruhida 5 qo'yildi — 4-B ning ingliz tili jurnalida
    /// blok ustuni (faqat ko'rish), baho "K" belgisi va ustoz ismi bilan; 4-A jurnalida — yo'q.
    /// </summary>
    [Fact]
    public async Task Ssenariy_2_baho_sinf_jurnalida_korinadi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var c = await ClassBlockAsync(db, w, day: 0);

        Assert.Null(await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(c.Katta.StudyGroupId.ToString(), w.English, 1, c.PupilB, Monday, 2, 5, null)));

        var columns = await JournalService.ComputeColumnsAsync(db, c.ClassB, w.English, 1);
        var column = Assert.Single(columns, x => x.Date == Monday && x.Period == 2);
        Assert.Equal("4-sinf Ingliz", column.Block);

        var entry = Assert.Single(await JournalService.GetEntriesAsync(db, c.ClassB, w.English, 1));
        Assert.Equal(5, entry.Grade);
        Assert.Equal("K", entry.BlockMark);
        Assert.Equal("Ustoz A", entry.BlockTeacher);
        Assert.Empty(await JournalService.GetEntriesAsync(db, c.ClassA, w.English, 1));

        // Sinf jurnalidagi blok katagiga yozib bo'lmaydi — baho blok guruhining jurnalida.
        Assert.Equal(JournalService.BlockColumnMessage, await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(c.ClassB, w.English, 1, c.PupilB, Monday, 2, 4, null)));
    }

    /// <summary>
    /// 7-ssenariy: 17:00 hisobotida ustozning har bir blok guruhi alohida qatorda — darslar, baholar,
    /// gamifikatsiya ballari to'g'ri; sinf kesimida blok bahosi manba sinfga (4-A) yoziladi.
    /// </summary>
    [Fact]
    public async Task Ssenariy_7_kunlik_hisobotda_har_guruh_alohida()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var c = await ClassBlockAsync(db, w, day: TodayDay);
        (await db.Teachers.SingleAsync(t => t.Id == w.TeacherA)).UserId = w.UserId;
        await db.SaveChangesAsync();

        Assert.Null(await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(c.Katta.StudyGroupId.ToString(), w.English, 1, c.PupilA, Iso(Today), 2, 4, null)));
        var reason = new DisciplineReason { Name = "Faol", Points = 3 };
        db.DisciplineReasons.Add(reason);
        db.DisciplinePoints.Add(new DisciplinePoint
        {
            StudentId = c.PupilA, ReasonId = reason.Id, ReasonName = "Faol", Points = 3, CreatedAt = AppClock.Now.ToString("o"),
            CreatedBy = "Ustoz A", CreatedByUserId = w.UserId, BlockGroupId = c.Katta.Id, SourceClassId = c.ClassA,
        });
        await db.SaveChangesAsync();

        var report = await DailyDirectorReport.BuildAsync(db, Today);

        var katta = Assert.Single(report.BlockRows, r => r.Group == "Katta");
        Assert.Equal(("Ustoz A", "4-sinf Ingliz", 1, 1, 3, 0), (katta.Teacher, katta.Block, katta.Lessons, katta.Grades, katta.GamePlus, katta.GameMinus));
        var kichik = Assert.Single(report.BlockRows, r => r.Group == "Kichik");
        Assert.Equal(("Ustoz B", 1, 0), (kichik.Teacher, kichik.Lessons, kichik.Grades));
        Assert.Contains(report.Classes, r => r.Name == "4-A" && r.Four == 1);
        Assert.DoesNotContain(report.Classes, r => r.Name.Contains("·"));
    }

    /// <summary>Davr hisoboti: guruh × ustoz va manba sinf kesimi; oylik test — balldan yuqoridan pastga.</summary>
    [Fact]
    public async Task Davr_va_oylik_hisobot()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var c = await ClassBlockAsync(db, w, day: 0);
        Assert.Null(await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(c.Katta.StudyGroupId.ToString(), w.English, 1, c.PupilA, Monday, 2, 5, null)));
        Assert.Null(await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(c.Katta.StudyGroupId.ToString(), w.English, 1, c.PupilB, Monday, 2, 3, null)));

        var report = await BlockReportService.PeriodAsync(db, Today.AddDays(-7), Today);
        var katta = Assert.Single(report.Groups, g => g.Group == "Katta");
        Assert.Equal((2, 4.0, "Ustoz A"), (katta.Grades, katta.Average!.Value, katta.Teacher));
        Assert.Equal(1, report.Classes.Single(r => r.Source == "4-A").Grades);
        Assert.Equal(3.0, report.Classes.Single(r => r.Source == "4-B").Average);

        foreach (var (pupil, cls, score) in new[] { (c.PupilA, c.ClassA, 71m), (c.PupilB, c.ClassB, 94m) })
            db.SeasonalMarks.Add(new SeasonalMark
            {
                StudentId = pupil, ClassId = cls, SubjectId = w.English, PeriodKind = SeasonalPeriodKind.Monthly,
                Year = Today.Year, Month = Today.Month, Score = score,
            });
        await db.SaveChangesAsync();

        var monthly = await BlockReportService.MonthlyAsync(db, c.Block.Id, Today.Year, Today.Month);
        var group = Assert.Single(monthly!.ByGroup, g => g.Name == "Katta");
        Assert.Equal([94m, 71m], group.Rows.Select(r => r.Score!.Value));
        Assert.Equal(["4-A", "4-B"], monthly.ByClass.Select(g => g.Name));
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record ClassBlock(Block Block, BlockGroup Katta, string ClassA, string ClassB, string PupilA, string PupilB);

    /// <summary>4-A va 4-B dan ingliz bloki (baho qo'yiladi), ikkala o'quvchi Kattada, blok shu kuni 2-darsda.</summary>
    private static async Task<ClassBlock> ClassBlockAsync(AppDbContext db, World w, int day)
    {
        var a = new SchoolClass { Name = "4-A", Grade = 4 };
        var b = new SchoolClass { Name = "4-B", Grade = 4 };
        db.Classes.AddRange(a, b);
        var pupils = new[] { Pupil("To'rtA 1", a.Name), Pupil("To'rtB 1", b.Name) };
        db.Students.AddRange(pupils);
        await db.SaveChangesAsync();

        var req = new SaveBlockRequest("4-sinf Ingliz", w.English, BlockType.Level, BlockDayPattern.Custom,
            [new BlockSourceRef(LessonOwnerKind.Class, a.Id), new BlockSourceRef(LessonOwnerKind.Class, b.Id)],
            [new SaveBlockGroupRequest("Katta", w.TeacherA), new SaveBlockGroupRequest("Kichik", w.TeacherB)]);
        var service = new BlockService(db);
        Assert.Null(await service.ValidateAsync(req, null));
        var block = await service.CreateAsync(req, w.UserId);
        block.IsActive = true;
        await db.SaveChangesAsync();
        // Ustozlar oy boshidan — o'tgan kunlardagi baho ham ustozga bog'lansin.
        foreach (var t in await db.BlockGroupTeachers.ToListAsync()) t.ValidFrom = Today.AddDays(-30);
        await db.SaveChangesAsync();
        var (katta, _) = await GroupsAsync(db, block);
        await AssignAsync(db, block, katta, pupils[0].Id, pupils[1].Id);
        await PlaceAsync(db, block, new BlockSlot(day, 2));
        return new ClassBlock(block, katta, a.Id, b.Id, pupils[0].Id, pupils[1].Id);
    }

    private static Student Pupil(string name, string className) => new()
    {
        FullName = name, FirstName = name.Split(' ')[0], LastName = name.Split(' ')[^1], BirthDate = "2016-01-01",
        Gender = "male", ClassName = className, EnrollmentDate = "2026-09-01",
    };
}
