using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bloklar — 4-bosqich: jadval (docs/modules/blocks.md, TZ 6.3, 7).
///
/// <para>
/// <b>Nima isbotlanadi.</b> 9-qoida (kun naqshi — rad etish; 10-ssenariy), 3-qoida (manba sinf bir
/// soatda bitta dars yoki blokda; 4-ssenariy), 4-qoida (ustoz), 5-qoida (xona). Blok katagi ikkala
/// guruh shablonida bir xil yoziladi, haftalarga biriktiriladi, o'tgan haftalar tarixi saqlanadi.
/// Faol blok guruhining darsi o'chirgichga qaramasdan tirik: ro'yxat guruh a'zoligidan (1-ssenariy),
/// o'quvchi ingliz va matematikada turli guruhda (9-ssenariy).
/// </para>
/// <para>
/// Har test o'z bazasida — choraklar jadvali umumiy, boshqa testlar unga yozadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockScheduleTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    internal static DateOnly Today => AppClock.Today;

    internal static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /* =====================================================================
     *  Qoidalar
     * ================================================================== */

    /// <summary>10-ssenariy: matematika bloki (juft kunlar) Dushanbaga qo'yilmaydi — 9-qoida, rad etish.</summary>
    [Fact]
    public async Task Ssenariy_10_matematika_bloki_dushanbaga_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var math = await BlockAsync(db, w, "Matematika", w.Math, BlockDayPattern.Even, [w.Fil, w.Ijt]);
        var service = new BlockScheduleService(db);

        Assert.Equal("Bu blok faqat juft kunlarda ishlaydi",
            await service.ValidateAsync(math, [new BlockSlot(0, 2)]));
        Assert.Null(await service.ValidateAsync(math, [new BlockSlot(1, 2)]));
    }

    /// <summary>
    /// 4-ssenariy: Iqtisodiyot-1 ga bir soatda Iqt Ingliz va Aniq+Iqt Matematika qo'yilmaydi (3-qoida).
    /// </summary>
    [Fact]
    public async Task Ssenariy_4_manba_bir_soatda_ikki_blokda_bolmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Iqt Ingliz", w.English, BlockDayPattern.Custom, [w.Iqt1, w.Iqt2]);
        var math = await BlockAsync(db, w, "Aniq+Iqt Matematika", w.Math, BlockDayPattern.Custom, [w.Aniq, w.Iqt1, w.Iqt2],
            teachers: (w.TeacherC, w.TeacherD));
        await PlaceAsync(db, eng, new BlockSlot(0, 2));

        var error = await new BlockScheduleService(db).ValidateAsync(math, [new BlockSlot(0, 2)]);

        Assert.NotNull(error);
        Assert.Contains("«Iqt Ingliz» blokida band", error);
        Assert.Contains("Iqtisodiyot-", error);
        Assert.Null(await new BlockScheduleService(db).ValidateAsync(math, [new BlockSlot(0, 3)]));
    }

    /// <summary>3-qoida: manba sinfning o'zida shu soatda dars bo'lsa, blok qo'yilmaydi.</summary>
    [Fact]
    public async Task Manbada_shu_soatda_dars_bolsa_blok_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);

        var error = await new BlockScheduleService(db).ValidateAsync(eng, [new BlockSlot(0, 1)]);

        Assert.Equal("Filologiya Dushanba, 1-dars da «Matematika» darsida band", error);
    }

    /// <summary>
    /// 3-qoida teskari tomondan: blok soatiga manba sinfga oddiy dars qo'yilmaydi; blok guruhining
    /// jadvalini esa oddiy jadval muharriri o'zgartira olmaydi.
    /// </summary>
    [Fact]
    public async Task Blok_soatiga_manbaga_dars_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        await PlaceAsync(db, eng, new BlockSlot(0, 2));
        var service = new BlockScheduleService(db);

        Assert.Equal("Ijtimoiy Dushanba, 2-dars da «Fil+Ijt Ingliz» blokida band",
            await service.ClashForOwnerAsync(w.Ijt, "Ijtimoiy", [(0, 2)]));
        Assert.Null(await service.ClashForOwnerAsync(w.Ijt, "Ijtimoiy", [(0, 3)]));
        var sg = (await db.BlockGroups.FirstAsync(g => g.BlockId == eng.Id)).StudyGroupId.ToString();
        Assert.Contains("«Bloklar» bo'limidan", await service.ClashForOwnerAsync(sg, "Katta", []));
        // Blokka aloqasi yo'q sinf — tekshiruv hech narsa demaydi.
        Assert.Null(await service.ClashForOwnerAsync(w.OtherClass, "5-A", [(0, 2)]));
    }

    /// <summary>4-qoida: ustoz shu soatda boshqa sinfda dars bersa, blok qo'yilmaydi.</summary>
    [Fact]
    public async Task Ustoz_band_bolsa_blok_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);

        var error = await new BlockScheduleService(db).ValidateAsync(eng, [new BlockSlot(2, 3)]);

        Assert.Equal("Ustoz A Chorshanba, 3-dars da 5-A darsida band", error);
    }

    /// <summary>4-qoida: bitta ustoz bir soatda ikkala guruhda bo'la olmaydi.</summary>
    [Fact]
    public async Task Bitta_ustoz_ikki_guruhda_bolmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt],
            teachers: (w.TeacherB, w.TeacherB));

        var error = await new BlockScheduleService(db).ValidateAsync(eng, [new BlockSlot(0, 2)]);

        Assert.Contains("bir soatda ikkala guruhda bo'la olmaydi", error);
    }

    /// <summary>5-qoida: xona bir soatda faqat bitta guruhda.</summary>
    [Fact]
    public async Task Xona_band_bolsa_blok_qoyilmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var room = new Room { Name = "204" };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Custom, [w.Fil, w.Ijt], room: room.Id);
        var iqt = await BlockAsync(db, w, "Iqt Ingliz", w.English, BlockDayPattern.Custom, [w.Iqt1, w.Iqt2],
            teachers: (w.TeacherC, w.TeacherD), room: room.Id);
        await PlaceAsync(db, eng, new BlockSlot(0, 4));

        var error = await new BlockScheduleService(db).ValidateAsync(iqt, [new BlockSlot(0, 4)]);

        Assert.NotNull(error);
        Assert.StartsWith("204-xona Dushanba, 4-dars da band", error);
    }

    /* =====================================================================
     *  Yozish va tarix
     * ================================================================== */

    /// <summary>
    /// Blok katagi ikkala guruh shablonida bir xil: fani — blok fani, o'qituvchisi — guruh ustozi,
    /// <c>block_id</c> — blok. Shablon joriy haftadan boshlab haftalarga biriktiriladi.
    /// </summary>
    [Fact]
    public async Task Blok_katagi_ikkala_guruhga_yoziladi_va_haftaga_biriktiriladi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);

        await PlaceAsync(db, eng, new BlockSlot(0, 2), new BlockSlot(2, 2), new BlockSlot(4, 2));

        var groups = await db.BlockGroups.Where(g => g.BlockId == eng.Id).OrderBy(g => g.SortOrder).ToListAsync();
        foreach (var (g, teacher) in groups.Zip([w.TeacherA, w.TeacherB]))
        {
            var sg = g.StudyGroupId.ToString();
            var lessons = await db.ScheduleTemplates.Where(t => t.ClassId == sg).SelectMany(t => t.Lessons).ToListAsync();
            Assert.Equal(3, lessons.Count);
            Assert.All(lessons, l =>
            {
                Assert.Equal(eng.Id, l.BlockId);
                Assert.Equal(w.English, l.SubjectId);
                Assert.Equal(teacher, l.TeacherId);
                Assert.Equal(0, l.SubGroup);
            });
            var weeks = await db.WeekAssignments.Where(a => a.ClassId == sg && a.OwnerKind == LessonOwnerKind.Group).ToListAsync();
            Assert.NotEmpty(weeks);
        }
        Assert.Equal([new BlockSlot(0, 2), new BlockSlot(2, 2), new BlockSlot(4, 2)],
            await new BlockScheduleService(db).SlotsAsync(eng.Id));
    }

    /// <summary>
    /// O'tgan haftalarda ishlatilgan blok shabloni o'zgartirilsa — nusxa olinadi (LiveScheduleEdit):
    /// o'tgan haftalar eski shablonda qoladi, yangi katak nusxaga, <c>block_id</c> nusxada saqlanadi.
    /// </summary>
    [Fact]
    public async Task Otgan_haftalar_tarixi_saqlanadi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        await PlaceAsync(db, eng, new BlockSlot(0, 2));
        var sg = (await db.BlockGroups.FirstAsync(g => g.BlockId == eng.Id && g.SortOrder == 1)).StudyGroupId.ToString();
        var first = await db.ScheduleTemplates.SingleAsync(t => t.ClassId == sg);
        // Shablon o'tgan haftada ham ishlatilgan bo'lsin.
        var pastWeek = ScheduleMath.GetQuarterWeeks(Iso(w.QuarterStart), Iso(w.QuarterEnd))
            .First(x => string.CompareOrdinal(x.EndISO, LiveScheduleEdit.EditBoundary()) < 0);
        db.WeekAssignments.Add(new WeekAssignment
        {
            ClassId = sg, OwnerKind = LessonOwnerKind.Group, Quarter = 1, Week = pastWeek.Week, TemplateId = first.Id,
        });
        await db.SaveChangesAsync();

        await PlaceAsync(db, eng, new BlockSlot(2, 2));

        var templates = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
            .Where(t => t.ClassId == sg).ToListAsync();
        Assert.Equal(2, templates.Count);
        var old = templates.Single(t => t.Id == first.Id);
        Assert.EndsWith("gacha", old.Name);
        Assert.Equal([(0, 2)], old.Lessons.Select(l => (l.Day, l.Period)));
        var copy = templates.Single(t => t.Id != first.Id);
        Assert.Equal([(0, 2), (2, 2)], copy.Lessons.OrderBy(l => l.Day).Select(l => (l.Day, l.Period)));
        Assert.All(copy.Lessons, l => Assert.Equal(eng.Id, l.BlockId));
    }

    /// <summary>Ustoz almashganda guruh shablonidagi blok darslarining o'qituvchisi yangilanadi.</summary>
    [Fact]
    public async Task Ustoz_almashsa_jadvaldagi_ustoz_yangilanadi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        await PlaceAsync(db, eng, new BlockSlot(0, 2));
        var katta = await db.BlockGroups.FirstAsync(g => g.BlockId == eng.Id && g.SortOrder == 1);

        Assert.Null(await new BlockScheduleService(db).SyncTeacherAsync(katta, w.TeacherC));

        var sg = katta.StudyGroupId.ToString();
        var lesson = await db.ScheduleTemplates.Where(t => t.ClassId == sg).SelectMany(t => t.Lessons).SingleAsync();
        Assert.Equal(w.TeacherC, lesson.TeacherId);
    }

    /* =====================================================================
     *  Tirik darslar (o'chirgich o'chiq)
     * ================================================================== */

    /// <summary>
    /// 1-ssenariy: Katta guruh darsining ro'yxati — faqat Katta a'zolari, Filologiya va Ijtimoiydan
    /// aralash. Faol blokning guruhi o'chirgich o'chiq bo'lsa ham tirik; faol bo'lmasa — yo'q.
    /// </summary>
    [Fact]
    public async Task Ssenariy_1_katta_guruh_royxati_va_faollik()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt], activate: false);
        var (katta, kichik) = await GroupsAsync(db, eng);
        await AssignAsync(db, eng, katta, w.Fil1, w.Ijt1);
        await AssignAsync(db, eng, kichik, w.Fil2, w.Ijt2);
        var kattaSg = katta.StudyGroupId.ToString();

        Assert.False(await LessonRoster.GroupLessonsEnabledAsync(db));
        Assert.DoesNotContain(kattaSg, (await LessonRoster.GroupScopeAsync(db)).LiveGroupIds);

        eng.IsActive = true;
        await db.SaveChangesAsync();

        Assert.Contains(kattaSg, (await LessonRoster.GroupScopeAsync(db)).LiveGroupIds);
        var owner = (await LessonRoster.LiveOwnersAsync(db))[kattaSg];
        Assert.True(owner.IsBlock);
        Assert.False(owner.HasHalves);
        Assert.True(await LessonRoster.LessonsLiveAsync(db, owner));
        var roster = await LessonRoster.ForLessonAsync(db, owner);
        Assert.Equal(new[] { w.Fil1, w.Ijt1 }.Order(), roster.Select(s => s.Id).Order());
    }

    /// <summary>
    /// 9-ssenariy: o'quvchi ingliz tilida Katta, matematikada Kichik — uning darslari ikkala guruhda,
    /// har biri o'z blokining kunida (Dushanba — ingliz, Seshanba — matematika).
    /// </summary>
    [Fact]
    public async Task Ssenariy_9_ingliz_katta_matematika_kichik()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);
        var eng = await BlockAsync(db, w, "Fil+Ijt Ingliz", w.English, BlockDayPattern.Odd, [w.Fil, w.Ijt]);
        var math = await BlockAsync(db, w, "Fil+Ijt Matematika", w.Math, BlockDayPattern.Even, [w.Fil, w.Ijt],
            teachers: (w.TeacherC, w.TeacherD));
        var (engKatta, _) = await GroupsAsync(db, eng);
        var (_, mathKichik) = await GroupsAsync(db, math);
        await AssignAsync(db, eng, engKatta, w.Fil1);
        await AssignAsync(db, math, mathKichik, w.Fil1);
        await PlaceAsync(db, eng, new BlockSlot(0, 2));
        await PlaceAsync(db, math, new BlockSlot(1, 2));

        var student = await db.Students.SingleAsync(s => s.Id == w.Fil1);
        var owners = await LessonRoster.OwnersOfAsync(db, student);
        Assert.Contains(owners, o => o.Id == engKatta.StudyGroupId.ToString());
        Assert.Contains(owners, o => o.Id == mathKichik.StudyGroupId.ToString());

        var lessons = await db.ScheduleTemplates
            .Where(t => t.ClassId == engKatta.StudyGroupId.ToString() || t.ClassId == mathKichik.StudyGroupId.ToString())
            .SelectMany(t => t.Lessons).ToListAsync();
        Assert.Contains(lessons, l => l.Day == 0 && l.SubjectId == w.English && l.TeacherId == w.TeacherA);
        Assert.Contains(lessons, l => l.Day == 1 && l.SubjectId == w.Math && l.TeacherId == w.TeacherD);
    }

    /// <summary>Regressiya: blok yo'q — tirik guruhlar faqat yo'nalishlar, tekshiruv hech narsa demaydi.</summary>
    [Fact]
    public async Task Bloksiz_holat_ozgarmaydi()
    {
        await using var db = await NewDbAsync();
        var w = await SeedAsync(db);

        var scope = await LessonRoster.GroupScopeAsync(db);
        var tracks = await db.StudyGroups.Where(g => g.IsTrack).Select(g => g.Id.ToString()).ToListAsync();
        Assert.Equal(tracks.Order(), scope.LiveGroupIds.Order());
        Assert.Empty(await LessonRoster.LiveBlockGroupIdsAsync(db));
        Assert.Null(await new BlockScheduleService(db).ClashForOwnerAsync(w.Fil, "Filologiya", [(0, 1)]));
        Assert.Empty(await new BlockScheduleService(db).OverlayAsync());
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    internal sealed record World(
        string UserId, string English, string Math, string TeacherA, string TeacherB, string TeacherC, string TeacherD,
        string Fil, string Ijt, string Aniq, string Iqt1, string Iqt2, string OtherClass,
        string Fil1, string Fil2, string Ijt1, string Ijt2, DateOnly QuarterStart, DateOnly QuarterEnd);

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("block_schedule");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// Bugunni o'z ichiga olgan chorak; beshta yo'nalish (Filologiya va Ijtimoiyda 2 tadan o'quvchi);
    /// Filologiyaning asosiy shablonida Dushanba 1-dars — Matematika; 5-A da Chorshanba 3-dars — Ustoz A.
    /// </summary>
    internal static async Task<World> SeedAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = $"blocks.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        var english = new Subject { Name = "Ingliz tili" };
        var math = new Subject { Name = "Matematika" };
        var teachers = new[] { "Ustoz A", "Ustoz B", "Ustoz C", "Ustoz D" }.Select(n => new Teacher { FullName = n }).ToList();
        var other = new SchoolClass { Name = "5-A", Grade = 5 };
        db.Subjects.AddRange(english, math);
        db.Teachers.AddRange(teachers);
        db.Classes.Add(other);
        var from = Today.AddDays(-40);
        var to = Today.AddDays(60);
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = Iso(from), EndDate = Iso(to) });

        string[] names = ["Filologiya", "Ijtimoiy", "Aniq fan", "Iqtisodiyot-1", "Iqtisodiyot-2"];
        var tracks = names.Select(n => new StudyGroup { Name = n, IsTrack = true, CreatedBy = user.Id }).ToList();
        db.StudyGroups.AddRange(tracks);
        var pupils = new List<Student>();
        for (var i = 0; i < 2; i++)
            for (var k = 0; k < 2; k++)
                pupils.Add(new Student
                {
                    FullName = $"{names[i]} {k}", FirstName = names[i], LastName = $"{k}", BirthDate = "2010-01-01",
                    Gender = "male", ClassName = "10-A", EnrollmentDate = "2026-09-01",
                });
        db.Students.AddRange(pupils);
        await db.SaveChangesAsync();
        for (var i = 0; i < pupils.Count; i++)
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = tracks[i / 2].Id, StudentId = pupils[i].Id, JoinedOn = from, CreatedBy = user.Id,
            });

        var weeks = ScheduleMath.GetQuarterWeeks(Iso(from), Iso(to));
        void Template(string ownerId, string kind, ScheduleLesson lesson)
        {
            var tpl = new ScheduleTemplate { ClassId = ownerId, OwnerKind = kind, Name = "Asosiy", Lessons = [lesson] };
            db.ScheduleTemplates.Add(tpl);
            db.WeekAssignments.AddRange(weeks.Select(x => new WeekAssignment
            {
                ClassId = ownerId, OwnerKind = kind, Quarter = 1, Week = x.Week, TemplateId = tpl.Id,
            }));
        }
        Template(tracks[0].Id.ToString(), LessonOwnerKind.Group,
            new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id, TeacherId = teachers[3].Id });
        Template(other.Id, LessonOwnerKind.Class,
            new ScheduleLesson { Day = 2, Period = 3, SubjectId = english.Id, TeacherId = teachers[0].Id });
        await db.SaveChangesAsync();

        return new World(user.Id, english.Id, math.Id, teachers[0].Id, teachers[1].Id, teachers[2].Id, teachers[3].Id,
            tracks[0].Id.ToString(), tracks[1].Id.ToString(), tracks[2].Id.ToString(), tracks[3].Id.ToString(),
            tracks[4].Id.ToString(), other.Id, pupils[0].Id, pupils[1].Id, pupils[2].Id, pupils[3].Id, from, to);
    }

    internal static async Task<Block> BlockAsync(
        AppDbContext db, World w, string name, string subjectId, string pattern, string[] trackSources,
        (string, string)? teachers = null, Guid? room = null, bool activate = true)
    {
        var (t1, t2) = teachers ?? (w.TeacherA, w.TeacherB);
        var req = new SaveBlockRequest(name, subjectId, BlockType.Level, pattern,
            [.. trackSources.Select(s => new BlockSourceRef(LessonOwnerKind.Group, s))],
            [new SaveBlockGroupRequest("Katta", t1, RoomId: room), new SaveBlockGroupRequest("Kichik", t2, RoomId: room)]);
        var service = new BlockService(db);
        Assert.Null(await service.ValidateAsync(req, null));
        var block = await service.CreateAsync(req, w.UserId);
        block.IsActive = activate;
        await db.SaveChangesAsync();
        return block;
    }

    internal static async Task PlaceAsync(AppDbContext db, Block block, params BlockSlot[] slots)
    {
        var service = new BlockScheduleService(db);
        Assert.Null(await service.ValidateAsync(block, slots));
        Assert.Null(await service.ApplyAsync(block, slots, [], "test"));
    }

    internal static async Task<(BlockGroup, BlockGroup)> GroupsAsync(AppDbContext db, Block block)
    {
        var g = await db.BlockGroups.Where(x => x.BlockId == block.Id).OrderBy(x => x.SortOrder).ToListAsync();
        return (g[0], g[1]);
    }

    internal static async Task AssignAsync(AppDbContext db, Block block, BlockGroup group, params string[] students)
    {
        var block2 = await db.Blocks.AsNoTracking().SingleAsync(b => b.Id == block.Id);
        Assert.Null(await new BlockService(db).AssignAsync(block2, students, group.Id, Today.AddDays(-5), (await db.Users.FirstAsync()).Id));
        await db.SaveChangesAsync();
    }
}
