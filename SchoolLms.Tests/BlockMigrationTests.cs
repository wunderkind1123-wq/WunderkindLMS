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
/// Bloklar — 7-bosqich: mavjud ma'lumotni bloklarga ko'chirish (TZ 8, docs/modules/blocks.md).
///
/// <para>
/// <b>Nima isbotlanadi.</b> <c>dryRun</c> — hisobot bor, bazada hech narsa o'zgarmaydi. To'liq
/// ko'chirish: Katta/Kichik ustozlari jadvaldan olinadi, manbalardagi ingliz tili darslari blok
/// kataklariga aylanadi (o'tgan haftalar eski shablonda qoladi), o'quvchilar Katta/Kichik bo'yicha
/// taqsimlanadi, yo'nalish jurnalining eski qatorlari blok guruhi bilan belgilanadi, tekshiruv toza.
/// Birorta xato (naqsh, vaqt to'qnashuvi) — hech narsa saqlanmaydi. Ustoz profillari birlashadi,
/// ikkinchisi arxivlanadi; darslari bir soatga tushsa — to'xtatiladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockMigrationTests(ApiFixture fixture) : IAsyncLifetime
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
        var database = await fixture.Postgres.CreateDatabaseAsync("block_migration");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    [Fact]
    public async Task Dry_run_hech_narsani_ozgartirmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var plan = Plan(w, BlockDayPattern.Odd);
        var templatesBefore = await db.ScheduleTemplates.CountAsync();

        var report = await BlockMigration.RunAsync(db, plan, dryRun: true, w.UserId);

        Assert.True(report.DryRun);
        Assert.False(report.Applied);
        Assert.Empty(report.Errors);
        Assert.Contains(report.Steps, s => s.Contains("yaratildi"));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Blocks.CountAsync());
        Assert.Equal(templatesBefore, await db.ScheduleTemplates.CountAsync());
        Assert.Null(await db.JournalEntries.Select(e => e.BlockGroupId).SingleAsync());
    }

    [Fact]
    public async Task Toliq_kochirish()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);

        var report = await BlockMigration.RunAsync(db, Plan(w, BlockDayPattern.Odd), dryRun: false, w.UserId);

        Assert.True(report.Applied, string.Join("\n", report.Errors));
        Assert.Empty(report.Audit);
        db.ChangeTracker.Clear();
        var block = await db.Blocks.SingleAsync();
        Assert.True(block.IsActive);
        var (katta, kichik) = await GroupsAsync(db, block);
        // Ustozlar jadvaldan: Katta — Ustoz A, Kichik — Ustoz B.
        var service = new BlockService(db);
        Assert.Equal(w.TeacherA, (await service.TeacherOnAsync(katta.Id, Today))!.TeacherId);
        Assert.Equal(w.TeacherB, (await service.TeacherOnAsync(kichik.Id, Today))!.TeacherId);
        // Darslar: blok kataklari, manbalarning amaldagi shablonida ingliz tili yo'q, o'tgan haftalar eski shablonda.
        Assert.Equal([new BlockSlot(0, 2), new BlockSlot(2, 2)], await new BlockScheduleService(db).SlotsAsync(block.Id));
        var filTemplates = await db.ScheduleTemplates.Include(t => t.Lessons).Where(t => t.ClassId == w.Fil).ToListAsync();
        Assert.Equal(2, filTemplates.Count);
        Assert.Contains(filTemplates, t => t.Name.EndsWith("gacha") && t.Lessons.Any(l => l.SubjectId == w.English));
        Assert.Contains(filTemplates, t => !t.Name.EndsWith("gacha") && t.Lessons.All(l => l.SubjectId != w.English));
        // Taqsimlash Katta/Kichik bo'yicha.
        Assert.Equal(katta.Id, (await service.GroupOnAsync(block.Id, w.Fil1, Today))!.Id);
        Assert.Equal(kichik.Id, (await service.GroupOnAsync(block.Id, w.Fil2, Today))!.Id);
        // Yo'nalish jurnalining eski qatori belgilandi.
        var entry = await db.JournalEntries.SingleAsync();
        Assert.Equal(katta.Id, entry.BlockGroupId);
        Assert.Equal(w.Fil, entry.SourceClassId);
    }

    /// <summary>Naqshga zid jadval (matematika — juft kunlar, darslar Du/Chor) — xato, hech narsa saqlanmaydi.</summary>
    [Fact]
    public async Task Xato_bolsa_hech_narsa_saqlanmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);

        var report = await BlockMigration.RunAsync(db, Plan(w, BlockDayPattern.Even), dryRun: false, w.UserId);

        Assert.False(report.Applied);
        Assert.Contains(report.Errors, e => e.Contains("kun naqshiga mos emas"));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Blocks.CountAsync());
        Assert.Equal(0, await db.StudyGroups.CountAsync(g => !g.IsTrack));
    }

    /// <summary>
    /// "Asosiy ← ikkinchi": ikkinchi profilning darsi va guruh biriktirilishi asosiyga o'tadi, takror
    /// bog'lanish olib tashlanadi, ikkinchi profil arxivlanadi.
    /// </summary>
    [Fact]
    public async Task Ustoz_profillari_birlashadi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var second = new Teacher { FullName = "Ustoz A (2-profil)" };
        db.Teachers.Add(second);
        var fil = Guid.Parse(w.Fil);
        db.StudyGroupTeachers.AddRange(
            new StudyGroupTeacher { GroupId = fil, TeacherId = w.TeacherA },
            new StudyGroupTeacher { GroupId = fil, TeacherId = second.Id });
        var tpl = await db.ScheduleTemplates.Include(t => t.Lessons).FirstAsync(t => t.ClassId == w.Ijt);
        tpl.Lessons.Add(new ScheduleLesson { Day = 4, Period = 5, SubjectId = w.Math, TeacherId = second.Id });
        await db.SaveChangesAsync();

        var report = await BlockMigration.RunAsync(db,
            new BlockMigrationPlan([new TeacherMergeItem(w.TeacherA, second.Id)], [], false), dryRun: false, w.UserId);

        Assert.True(report.Applied, string.Join("\n", report.Errors));
        db.ChangeTracker.Clear();
        Assert.True((await db.Teachers.SingleAsync(t => t.Id == second.Id)).IsArchived);
        Assert.False(await db.ScheduleTemplates.SelectMany(t => t.Lessons).AnyAsync(l => l.TeacherId == second.Id));
        Assert.True(await db.ScheduleTemplates.SelectMany(t => t.Lessons).AnyAsync(l => l.TeacherId == w.TeacherA && l.Day == 4 && l.Period == 5));
        Assert.Equal([w.TeacherA], await db.StudyGroupTeachers.Where(t => t.GroupId == fil).Select(t => t.TeacherId).ToListAsync());
    }

    /// <summary>Ikki profilning darslari bir soatda ikki joyda — birlashtirish to'xtatiladi, hech narsa o'zgarmaydi.</summary>
    [Fact]
    public async Task Vaqt_toqnashsa_birlashtirish_toxtatiladi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var second = new Teacher { FullName = "Ustoz A (2-profil)" };
        db.Teachers.Add(second);
        // Ustoz A Du 2-darsda Filologiyada (Katta); ikkinchi profil o'sha soatda 5-A da.
        var tpl = await db.ScheduleTemplates.Include(t => t.Lessons).FirstAsync(t => t.ClassId == w.OtherClass);
        tpl.Lessons.Add(new ScheduleLesson { Day = 0, Period = 2, SubjectId = w.Math, TeacherId = second.Id });
        await db.SaveChangesAsync();

        var report = await BlockMigration.RunAsync(db,
            new BlockMigrationPlan([new TeacherMergeItem(w.TeacherA, second.Id)], [], false), dryRun: false, w.UserId);

        Assert.False(report.Applied);
        Assert.Contains(report.Errors, e => e.Contains("to'xtatildi"));
        db.ChangeTracker.Clear();
        Assert.False((await db.Teachers.SingleAsync(t => t.Id == second.Id)).IsArchived);
    }

    /// <summary>Tekshiruv (TZ 7): manbaga blok soatida dars qo'yilsa — 3-qoida; taqsimlanmagan o'quvchi — 2-qoida.</summary>
    [Fact]
    public async Task Tekshiruv_buzilishlarni_topadi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var report = await BlockMigration.RunAsync(db, Plan(w, BlockDayPattern.Odd), dryRun: false, w.UserId);
        Assert.True(report.Applied, string.Join("\n", report.Errors));
        db.ChangeTracker.Clear();

        var current = (await db.ScheduleTemplates.Include(t => t.Lessons).Where(t => t.ClassId == w.Ijt).ToListAsync())
            .Single(t => !t.Name.EndsWith("gacha"));
        current.Lessons.Add(new ScheduleLesson { Day = 0, Period = 2, SubjectId = w.Math, TeacherId = w.TeacherD });
        var block = await db.Blocks.SingleAsync();
        var member = await db.StudyGroupMembers.FirstAsync(m => m.StudentId == w.Ijt2 && m.LeftOn == null && m.SubjectId == w.English);
        db.StudyGroupMembers.Remove(member);
        await db.SaveChangesAsync();

        var issues = await BlockAuditService.RunAsync(db);

        Assert.Contains(issues, i => i.Rule == 3 && i.Message.Contains("Ijtimoiy"));
        Assert.Contains(issues, i => i.Rule == 2 && i.Message.Contains("1 o'quvchi taqsimlanmagan"));
        _ = block;
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>Fil+Ijt Ingliz: manbalar Filologiya va Ijtimoiy, ustozlar ko'rsatilmagan (jadvaldan), faollashtirish.</summary>
    private static BlockMigrationPlan Plan(World w, string pattern) => new(
        [],
        [
            new BlockPlanItem(new SaveBlockRequest("Fil+Ijt Ingliz", w.English, BlockType.Level, pattern,
                [new BlockSourceRef(LessonOwnerKind.Group, w.Fil), new BlockSourceRef(LessonOwnerKind.Group, w.Ijt)],
                [new SaveBlockGroupRequest("Katta"), new SaveBlockGroupRequest("Kichik")]), Activate: true),
        ]);

    /// <summary>
    /// <see cref="BlockScheduleTests.SeedAsync"/> ustiga: Filologiya va Ijtimoiyda Du va Chor 2-darsda ingliz
    /// tili Katta (1, Ustoz A) / Kichik (2, Ustoz B); o'quvchilarning birinchisi Katta, ikkinchisi Kichik;
    /// Ijtimoiyning bo'sh shabloni; Filologiya jurnalida Fil1 ning ingliz tili qatori (kelmagan).
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var w = await BlockScheduleTests.SeedAsync(db);
        var fil = await db.ScheduleTemplates.Include(t => t.Lessons).SingleAsync(t => t.ClassId == w.Fil);
        var weeks = ScheduleMath.GetQuarterWeeks(Iso(w.QuarterStart), Iso(w.QuarterEnd));
        var ijt = new ScheduleTemplate { ClassId = w.Ijt, OwnerKind = LessonOwnerKind.Group, Name = "Asosiy" };
        db.ScheduleTemplates.Add(ijt);
        db.WeekAssignments.AddRange(weeks.Select(x => new WeekAssignment
        {
            ClassId = w.Ijt, OwnerKind = LessonOwnerKind.Group, Quarter = 1, Week = x.Week, TemplateId = ijt.Id,
        }));
        foreach (var tpl in new[] { fil, ijt })
            foreach (var day in new[] { 0, 2 })
            {
                tpl.Lessons.Add(new ScheduleLesson { Day = day, Period = 2, SubjectId = w.English, TeacherId = w.TeacherA, SubGroup = 1 });
                tpl.Lessons.Add(new ScheduleLesson { Day = day, Period = 2, SubjectId = w.English, TeacherId = w.TeacherB, SubGroup = 2 });
            }
        foreach (var (id, half) in new[] { (w.Fil1, 1), (w.Fil2, 2), (w.Ijt1, 1), (w.Ijt2, 2) })
            (await db.Students.SingleAsync(s => s.Id == id)).SubGroup = half;
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = w.Fil, OwnerKind = LessonOwnerKind.Group, SubjectId = w.English, Quarter = 1, StudentId = w.Fil1,
            Date = Iso(Today.AddDays(-14)), Period = 2, SubGroup = 1, Homework = 1,
        });
        await db.SaveChangesAsync();
        return w;
    }
}
