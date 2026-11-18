using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bloklar — 2-bosqich: xizmat va API (docs/modules/blocks.md, TZ 6.1–6.2, 7).
///
/// <para>
/// <b>Nima isbotlanadi.</b> TZ 7-bo'limidagi saqlash va taqsimlash qoidalari (1, 2, 6, 7, 8),
/// sanali ko'chirish va ustoz almashtirish (TZ 5.3, 6.1, 6.2), 10-bo'limdagi 3-, 5-, 8- va
/// 9-ssenariylarning a'zolik qismi. Jadval (3–5, 9-qoidalar) — 4-bosqich, jurnal va
/// davomat — 5-bosqich.
/// </para>
/// <para>
/// <b>Va nima O'ZGARMASLIGI.</b> Blok yaratish dars jadvali, jurnal qatori yoki
/// <c>students.sub_group</c> ga tegmaydi, cut-over o'chirgichini yoqmaydi; blokning o'quv
/// guruhini oddiy "Guruhlar" ekranidan o'zgartirib bo'lmaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockTests(ApiFixture fixture)
{
    private const string Blocks = "/api/admin/blocks";

    private static DateOnly Today => AppClock.Today;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /* =====================================================================
     *  1. Ruxsat
     * ================================================================== */

    [Fact]
    public async Task Oqituvchi_bloklarga_kira_olmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Teacher, "classes");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Blocks)).StatusCode);
    }

    /* =====================================================================
     *  2. Saqlash qoidalari (TZ 7: 6, 7)
     * ================================================================== */

    [Fact]
    public async Task Ikki_guruhsiz_blok_saqlanmaydi_7_qoida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync(Blocks, LevelPayload(w, "Bitta " + w.Tag,
            groups: [new { name = "Katta", teacherId = w.TeacherA }]));

        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "Blokda 2 ta guruh bo'lishi kerak");
    }

    [Fact]
    public async Task Ustozsiz_guruh_saqlanmaydi_6_qoida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync(Blocks, LevelPayload(w, "Ustozsiz " + w.Tag,
            groups: [new { name = "Katta", teacherId = w.TeacherA }, new { name = "Kichik", teacherId = (string?)null }]));

        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "Kichik guruhga ustoz biriktirilmagan");
    }

    /// <summary>Bitta manba bitta fandan faqat bitta blokda — aks holda o'quvchi ikki ingliz blokida qolardi.</summary>
    [Fact]
    public async Task Manba_bir_fandan_ikki_blokka_kirmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await CreateAsync(admin, LevelPayload(w, "Birinchi " + w.Tag));

        var res = await admin.PostAsJsonAsync(Blocks, LevelPayload(w, "Ikkinchi " + w.Tag));

        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "allaqachon «Birinchi " + w.Tag + "» blokida");
    }

    /// <summary>
    /// 8-ssenariy: yangi sinf (5-sinf) uchun ingliz bloki kod o'zgartirmasdan, faqat API (admin panel)
    /// orqali ochiladi. Yangi blok FAOL EMAS, guruhlar — Katta/Kichik, har birida ustoz.
    /// </summary>
    [Fact]
    public async Task Ssenariy_8_yangi_sinf_bloki_admin_paneldan_ochiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var block = await CreateAsync(admin, ClassPayload(w, "5-sinf Ingliz " + w.Tag));

        Assert.False(block.GetProperty("isActive").GetBoolean());
        var groups = block.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(["KATTA", "KICHIK"], groups.Select(g => g.GetProperty("code").GetString()));
        Assert.Equal([w.TeacherA, w.TeacherB], groups.Select(g => g.GetProperty("teacherId").GetString()));
        Assert.Equal(4, block.GetProperty("unassignedCount").GetInt32());
    }

    /* =====================================================================
     *  3. Taqsimlash va faollashtirish (TZ 7: 1, 2)
     * ================================================================== */

    [Fact]
    public async Task Taqsimlanmagan_oquvchi_bilan_faollashmaydi_2_qoida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Fil+Ijt " + w.Tag));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();

        await AssignAsync(admin, id, katta, [w.Fil1, w.Ijt1]);
        await AssignAsync(admin, id, kichik, [w.Fil2]);

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/activate", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([$"Ijtimoiy {w.Tag}dan 1 o'quvchi taqsimlanmagan"],
            body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));

        await AssignAsync(admin, id, kichik, [w.Ijt2]);
        res = await admin.PostAsJsonAsync($"{Blocks}/{id}/activate", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await GetAsync(admin, id)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Oquvchi_blokda_bitta_guruhda_1_qoida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Bitta guruh " + w.Tag));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, katta, [w.Fil1]);

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/assign", new { studentIds = new[] { w.Fil1 }, groupId = kichik });

        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "allaqachon «Katta» guruhida");
        // Shu guruhga qayta yuborish — jim o'tadi.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Blocks}/{id}/assign", new { studentIds = new[] { w.Fil1 }, groupId = katta })).StatusCode);
    }

    [Fact]
    public async Task Manbada_yoq_oquvchi_taqsimlanmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Begona " + w.Tag));
        var (katta, _) = Groups(block);

        var res = await admin.PostAsJsonAsync($"{Blocks}/{block.GetProperty("id").GetGuid()}/assign",
            new { studentIds = new[] { w.Class5A1 }, groupId = katta });

        await AssertErrorAsync(res, HttpStatusCode.BadRequest, BlockService.NotInBlockMessage);
    }

    /// <summary>Taqsimlash ekrani har o'quvchining manbasini ko'rsatadi: "Ali V. · Filologiya" (TZ 6.2).</summary>
    [Fact]
    public async Task Royxat_manba_sinfini_korsatadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Manba " + w.Tag));
        var (katta, _) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, katta, [w.Fil1, w.Ijt1]);

        var roster = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/{id}/roster");
        var members = roster.GetProperty("groups")[0].GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("studentId").GetString()!, m => m.GetProperty("sourceName").GetString());
        Assert.Equal($"Filologiya {w.Tag}", members[w.Fil1]);
        Assert.Equal($"Ijtimoiy {w.Tag}", members[w.Ijt1]);
        Assert.Equal(2, roster.GetProperty("unassigned").GetArrayLength());
    }

    /* =====================================================================
     *  4. Sanali ko'chirish (TZ 5.3, 6.2; 3-ssenariy)
     * ================================================================== */

    /// <summary>
    /// 3-ssenariy (a'zolik qismi): o'quvchi D sanadan Kichikdan Kattaga o'tkaziladi. D-1 gacha u
    /// Kichikda, D dan Kattada — bir kunda ikki guruhda emas. Tarix va ko'chirishlar hisobotida ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Ssenariy_3_sana_bilan_kochirish()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Kochirish " + w.Tag));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        var start = Today.AddDays(-20);
        var moveOn = Today.AddDays(-5);
        await AssignAsync(admin, id, kichik, [w.Fil1], start);

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/move",
            new { studentIds = new[] { w.Fil1 }, groupId = katta, date = Iso(moveOn), reason = "darajasi yetdi" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal(kichik, await GroupOnAsync(admin, id, w.Fil1, moveOn.AddDays(-1)));
        Assert.Equal(katta, await GroupOnAsync(admin, id, w.Fil1, moveOn));
        Assert.Equal(katta, await GroupOnAsync(admin, id, w.Fil1, Today));
        Assert.Null(await GroupOnAsync(admin, id, w.Fil1, start.AddDays(-1)));

        var history = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/students/{w.Fil1}/history");
        Assert.Equal(["Katta", "Kichik"], history.EnumerateArray().Select(h => h.GetProperty("groupName").GetString()));
        Assert.Equal("darajasi yetdi", history[0].GetProperty("joinReason").GetString());

        var moves = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/moves?from={Iso(moveOn)}&to={Iso(Today)}");
        var move = Assert.Single(moves.EnumerateArray(), m => m.GetProperty("studentId").GetString() == w.Fil1);
        Assert.Equal("Kichik", move.GetProperty("fromGroup").GetString());
        Assert.Equal("Katta", move.GetProperty("toGroup").GetString());
    }

    /// <summary>
    /// Eski guruhda D dan keyin baho qo'yilgan bo'lsa, D dan ko'chirish rad etiladi: aks holda baho
    /// yangi guruh davriga tushardi. Keyinroq sanadan — mumkin, baho eski guruhda qoladi.
    /// </summary>
    [Fact]
    public async Task Kochirish_qoyilgan_bahoni_yangi_davrga_otkazmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Baho " + w.Tag));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, kichik, [w.Fil1], Today.AddDays(-20));
        var gradeDay = Today.AddDays(-3);
        await fixture.Api.WithDbAsync(async db =>
        {
            var sg = await db.BlockGroups.Where(g => g.Id == kichik).Select(g => g.StudyGroupId).SingleAsync();
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = sg.ToString(), OwnerKind = LessonOwnerKind.Group, SubjectId = w.English,
                Quarter = 2, StudentId = w.Fil1, Date = Iso(gradeDay), Period = 2, Grade = 5,
                BlockGroupId = kichik, SourceClassId = w.Fil,
            });
            await db.SaveChangesAsync();
        });

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/move",
            new { studentIds = new[] { w.Fil1 }, groupId = katta, date = Iso(gradeDay) });
        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "baho yoki davomat qo'yilgan");

        res = await admin.PostAsJsonAsync($"{Blocks}/{id}/move",
            new { studentIds = new[] { w.Fil1 }, groupId = katta, date = Iso(gradeDay.AddDays(1)) });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var entry = await db.JournalEntries.SingleAsync(j => j.StudentId == w.Fil1);
            Assert.Equal(kichik, entry.BlockGroupId);
            // Umumiy bazada guruh jurnal qatori qolmasin: StudyGroupTests.Guruh_yaratish_darslarga_tegmaydi
            // butun bazada guruh qatori yo'qligini (aniq 0) tekshiradi.
            db.JournalEntries.Remove(entry);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Qo'shilgan kunning o'zida ko'chirish — xatoni tuzatish: tarixda ortiqcha qator qolmaydi.</summary>
    [Fact]
    public async Task Shu_kuni_kochirish_tuzatish_hisoblanadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Tuzatish " + w.Tag));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, katta, [w.Fil1]);

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/move", new { studentIds = new[] { w.Fil1 }, groupId = kichik });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var history = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/students/{w.Fil1}/history");
        Assert.Equal("Kichik", Assert.Single(history.EnumerateArray()).GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task Blokdan_chiqarish_sanadan_boshlab()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Chiqarish " + w.Tag));
        var (katta, _) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, katta, [w.Fil1], Today.AddDays(-10));

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/unassign", new { studentIds = new[] { w.Fil1 }, date = Iso(Today) });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(katta, await GroupOnAsync(admin, id, w.Fil1, Today.AddDays(-1)));
        Assert.Null(await GroupOnAsync(admin, id, w.Fil1, Today));
    }

    /* =====================================================================
     *  5. Manba sinf bo'yicha blok (8-qoida; 5-ssenariy)
     * ================================================================== */

    /// <summary>
    /// Aniq+Iqt matematika: Iqt-1 o'quvchilari avtomatik "Iqt-1 guruhi"ga tushadi, Aniq fan
    /// o'quvchisi admin tanlagan guruhga (5-ssenariy), Iqt-1 o'quvchisini Iqt-2 guruhiga o'tkazib
    /// bo'lmaydi (8-qoida).
    /// </summary>
    [Fact]
    public async Task Manba_sinf_bloki_avtomatik_va_8_qoida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, new
        {
            name = "Aniq+Iqt Matematika " + w.Tag,
            subjectId = w.Math,
            blockType = BlockType.BaseClass,
            dayPattern = BlockDayPattern.Even,
            sources = new[] { Track(w.Aniq), Track(w.Iqt1), Track(w.Iqt2) },
            groups = new[]
            {
                new { name = "Iqt-1 guruhi", teacherId = w.TeacherA, baseOwnerKind = "group", baseOwnerId = w.Iqt1 },
                new { name = "Iqt-2 guruhi", teacherId = w.TeacherB, baseOwnerKind = "group", baseOwnerId = w.Iqt2 },
            },
        });
        var (iqt1, iqt2) = Groups(block);
        var id = block.GetProperty("id").GetGuid();

        Assert.Equal(iqt1, await GroupOnAsync(admin, id, w.Iqt1Student, Today));
        Assert.Equal(iqt2, await GroupOnAsync(admin, id, w.Iqt2Student, Today));
        Assert.Null(await GroupOnAsync(admin, id, w.AniqStudent, Today));

        await AssignAsync(admin, id, iqt2, [w.AniqStudent]);
        Assert.Equal(iqt2, await GroupOnAsync(admin, id, w.AniqStudent, Today));
        var group = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/{id}/students/{w.AniqStudent}/group");
        Assert.Equal(w.TeacherB, group.GetProperty("teacherId").GetString());

        var res = await admin.PostAsJsonAsync($"{Blocks}/{id}/move",
            new { studentIds = new[] { w.Iqt1Student }, groupId = iqt2, date = Iso(Today.AddDays(1)) });
        await AssertErrorAsync(res, HttpStatusCode.BadRequest,
            $"Iqtisodiyot-1 {w.Tag} o'quvchisini Iqt-2 guruhiga o'tkazib bo'lmaydi");

        var activate = await admin.PostAsJsonAsync($"{Blocks}/{id}/activate", new { });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
    }

    /* =====================================================================
     *  6. Ikki blok — ikki alohida a'zolik (9-ssenariy, a'zolik qismi)
     * ================================================================== */

    /// <summary>
    /// O'quvchi ingliz tilida Katta, matematikada Kichik: ikkita alohida blok, a'zolik ham alohida.
    /// Ingliz tilida ko'chirish matematikadagi guruhga ta'sir qilmaydi (TZ 6.2).
    /// </summary>
    [Fact]
    public async Task Ssenariy_9_ingliz_va_matematika_alohida()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var eng = await CreateAsync(admin, LevelPayload(w, "Ingliz " + w.Tag));
        var math = await CreateAsync(admin, LevelPayload(w, "Matematika " + w.Tag, subjectId: w.Math, pattern: BlockDayPattern.Even));
        var (engKatta, engKichik) = Groups(eng);
        var (_, mathKichik) = Groups(math);
        var engId = eng.GetProperty("id").GetGuid();
        var mathId = math.GetProperty("id").GetGuid();

        await AssignAsync(admin, engId, engKatta, [w.Fil1], Today.AddDays(-10));
        await AssignAsync(admin, mathId, mathKichik, [w.Fil1], Today.AddDays(-10));
        Assert.Equal(engKatta, await GroupOnAsync(admin, engId, w.Fil1, Today));
        Assert.Equal(mathKichik, await GroupOnAsync(admin, mathId, w.Fil1, Today));

        await admin.PostAsJsonAsync($"{Blocks}/{engId}/move", new { studentIds = new[] { w.Fil1 }, groupId = engKichik });
        Assert.Equal(engKichik, await GroupOnAsync(admin, engId, w.Fil1, Today));
        Assert.Equal(mathKichik, await GroupOnAsync(admin, mathId, w.Fil1, Today));
    }

    /* =====================================================================
     *  7. Ustozni sana bilan almashtirish (TZ 6.1)
     * ================================================================== */

    [Fact]
    public async Task Ustoz_sana_bilan_almashadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, LevelPayload(w, "Ustoz " + w.Tag));
        var (katta, _) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, katta, [w.Fil1]);
        var from = Today.AddDays(3);

        var res = await admin.PostAsJsonAsync($"{Blocks}/groups/{katta}/teacher", new { teacherId = w.TeacherC, date = Iso(from) });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/groups/{katta}/teachers");
        Assert.Equal([w.TeacherA, w.TeacherC], history.EnumerateArray().Select(t => t.GetProperty("teacherId").GetString()));
        Assert.Equal(Iso(from.AddDays(-1)), history[0].GetProperty("validTo").GetString());

        var before = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/{id}/students/{w.Fil1}/group?date={Iso(from.AddDays(-1))}");
        var after = await admin.GetFromJsonAsync<JsonElement>($"{Blocks}/{id}/students/{w.Fil1}/group?date={Iso(from)}");
        Assert.Equal(w.TeacherA, before.GetProperty("teacherId").GetString());
        Assert.Equal(w.TeacherC, after.GetProperty("teacherId").GetString());

        // O'quv guruhi o'qituvchilari: bugun dars beradigan va kelajakdagi ustoz.
        await fixture.Api.WithDbAsync(async db =>
        {
            var sg = await db.BlockGroups.Where(g => g.Id == katta).Select(g => g.StudyGroupId).SingleAsync();
            var teachers = await db.StudyGroupTeachers.Where(t => t.GroupId == sg).Select(t => t.TeacherId).ToListAsync();
            Assert.Equal(new[] { w.TeacherA, w.TeacherC }.Order(), teachers.Order());
        });
    }

    /* =====================================================================
     *  8. O'chirish va himoya
     * ================================================================== */

    [Fact]
    public async Task Faol_blok_ochirilmaydi_faolsizi_toliq_ochiriladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, ClassPayload(w, "Ochirish " + w.Tag));
        var id = block.GetProperty("id").GetGuid();
        var (katta, kichik) = Groups(block);
        await AssignAsync(admin, id, katta, [w.Class5A1, w.Class5B1]);
        await AssignAsync(admin, id, kichik, [w.Class5A2, w.Class5B2]);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Blocks}/{id}/activate", new { })).StatusCode);

        await AssertErrorAsync(await admin.DeleteAsync($"{Blocks}/{id}"), HttpStatusCode.BadRequest, "Faol blokni");

        await admin.PostAsJsonAsync($"{Blocks}/{id}/deactivate", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Blocks}/{id}")).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.Blocks.AnyAsync(b => b.Id == id));
            Assert.False(await db.StudyGroups.AnyAsync(g => g.Name.StartsWith("Ochirish " + w.Tag)));
        });
    }

    /// <summary>Blokning o'quv guruhi oddiy "Guruhlar" ekranidan o'zgarmaydi — sanali qoidalarni chetlab bo'lmaydi.</summary>
    [Fact]
    public async Task Blok_guruhi_oddiy_guruhlar_ekranidan_ozgarmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var block = await CreateAsync(admin, ClassPayload(w, "Himoya " + w.Tag));
        var sg = block.GetProperty("groups")[0].GetProperty("studyGroupId").GetGuid();

        var add = await admin.PostAsJsonAsync($"/api/admin/study-groups/{sg}/members", new { studentIds = new[] { w.Class5A1 } });
        await AssertErrorAsync(add, HttpStatusCode.BadRequest, "blokka tegishli");
        var archive = await admin.PostAsJsonAsync($"/api/admin/study-groups/{sg}/archive", new { });
        await AssertErrorAsync(archive, HttpStatusCode.BadRequest, "blokka tegishli");
        // O'qish ochiq.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/study-groups/{sg}")).StatusCode);
    }

    /* =====================================================================
     *  9. Excel: eksport va import (TZ 6.2)
     * ================================================================== */

    /// <summary>
    /// Import: taqsimlanmagan o'quvchi guruhga qo'shiladi, boshqa guruhdagisi ko'chiriladi, "Guruh"
    /// bo'sh qator tegilmaydi. Eksport shu shaklda: uni to'ldirib qayta yuklash mumkin.
    /// </summary>
    [Fact]
    public async Task Excel_import_qoshadi_va_kochiradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Excel " + w.Tag;
        var block = await CreateAsync(admin, LevelPayload(w, name));
        var (katta, kichik) = Groups(block);
        var id = block.GetProperty("id").GetGuid();
        await AssignAsync(admin, id, kichik, [w.Ijt1], Today.AddDays(-10));

        var export = await admin.GetAsync($"{Blocks}/{id}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exported = ExcelImport.ReadRows(await export.Content.ReadAsStreamAsync(), 5);
        Assert.Equal(5, exported.Count);
        Assert.Equal("Kichik", exported.Single(r => r[0] == w.Ijt1)[2]);

        var res = await ImportAsync(admin,
            [w.Fil1, name, "Katta"], [w.Fil2, name, "kichik"], [w.Ijt1, name, "Katta"], [w.Ijt2, name, ""]);

        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        var result = JsonDocument.Parse(body).RootElement;
        Assert.Equal(2, result.GetProperty("assigned").GetInt32());
        Assert.Equal(1, result.GetProperty("moved").GetInt32());
        Assert.Equal(katta, await GroupOnAsync(admin, id, w.Fil1, Today));
        Assert.Equal(kichik, await GroupOnAsync(admin, id, w.Fil2, Today));
        Assert.Equal(katta, await GroupOnAsync(admin, id, w.Ijt1, Today));
        Assert.Equal(kichik, await GroupOnAsync(admin, id, w.Ijt1, Today.AddDays(-1)));
        Assert.Null(await GroupOnAsync(admin, id, w.Ijt2, Today));
    }

    /// <summary>Hammasi yoki hech narsa: bitta xato qator bo'lsa, to'g'ri qatorlar ham saqlanmaydi.</summary>
    [Fact]
    public async Task Excel_importda_xato_bolsa_hech_narsa_saqlanmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Xato Excel " + w.Tag;
        var block = await CreateAsync(admin, LevelPayload(w, name));
        var id = block.GetProperty("id").GetGuid();

        var res = await ImportAsync(admin, [w.Fil1, name, "Katta"], [w.Fil2, name, "O'rta"], [w.Class5A1, name, "Katta"]);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("errors")
            .EnumerateArray().Select(e => (e.GetProperty("row").GetInt32(), e.GetProperty("message").GetString()!)).ToList();
        Assert.Equal([3, 4], errors.Select(e => e.Item1));
        Assert.Contains("«O'rta» guruhi yo'q", errors[0].Item2);
        Assert.Contains(BlockService.NotInBlockMessage, errors[1].Item2);
        Assert.Null(await GroupOnAsync(admin, id, w.Fil1, Today));
    }

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient admin, params string[][] rows)
    {
        var bytes = ExcelExport.Build("Taqsimlash", ["O'quvchi ID", "Blok", "Guruh"], rows);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "bloklar.xlsx");
        return await admin.PostAsync($"{Blocks}/import", content);
    }

    /* =====================================================================
     *  10. Regressiya: oddiy darslar o'zgarmaydi
     * ================================================================== */

    /// <summary>
    /// Blok yaratish, taqsimlash, ko'chirish va faollashtirish dars jadvali, jurnal, mavzu
    /// qatorlariga va <c>students.sub_group</c> ga tegmaydi, cut-over o'chirgichini yoqmaydi.
    /// </summary>
    [Fact]
    public async Task Blok_amallari_oddiy_darslarga_tegmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var before = await SnapshotAsync(w);

        var block = await CreateAsync(admin, ClassPayload(w, "Regressiya " + w.Tag));
        var id = block.GetProperty("id").GetGuid();
        var (katta, kichik) = Groups(block);
        await AssignAsync(admin, id, katta, [w.Class5A1, w.Class5B1], Today.AddDays(-7));
        await AssignAsync(admin, id, kichik, [w.Class5A2, w.Class5B2], Today.AddDays(-7));
        await admin.PostAsJsonAsync($"{Blocks}/{id}/move", new { studentIds = new[] { w.Class5A2 }, groupId = katta });
        await admin.PostAsJsonAsync($"{Blocks}/{id}/activate", new { });

        Assert.Equal(before, await SnapshotAsync(w));
    }

    private async Task<string> SnapshotAsync(World w)
    {
        var result = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            // Faqat SHU testning sinflari, o'quvchilari va guruhlari — boshqa kolleksiyalar parallel
            // ravishda butun bazaga qator yozadi, umumiy son bu yerda hech narsani isbotlamaydi.
            var students = await db.Students.Where(s => s.ClassName == w.Class5AName || s.ClassName == w.Class5BName)
                .OrderBy(s => s.Id).ToListAsync();
            var studentIds = students.Select(s => s.Id).ToList();
            var owners = new List<string> { w.Class5A, w.Class5B };
            owners.AddRange((await db.StudyGroups.Where(g => g.Name.Contains(w.Tag)).Select(g => g.Id).ToListAsync())
                .Select(id => id.ToString()));
            var blockIds = await db.Blocks.Where(b => b.Name.Contains(w.Tag)).Select(b => b.Id).ToListAsync();
            result = string.Join(",", students.Select(s => s.Id + ":" + s.SubGroup))
                     + $"|templates={await db.ScheduleTemplates.CountAsync(t => owners.Contains(t.ClassId))}"
                     + $"|lessons={await db.ScheduleTemplates.SelectMany(t => t.Lessons)
                         .CountAsync(l => l.BlockId != null && blockIds.Contains(l.BlockId.Value))}"
                     + $"|journal={await db.JournalEntries.CountAsync(j => studentIds.Contains(j.StudentId))}"
                     + $"|notes={await db.LessonNotes.CountAsync(n => owners.Contains(n.ClassId))}"
                     + $"|switch={await LessonRoster.GroupLessonsEnabledAsync(db)}";
        });
        return result;
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record World(
        string Tag, string English, string Math, string TeacherA, string TeacherB, string TeacherC,
        string Fil, string Ijt, string Fil1, string Fil2, string Ijt1, string Ijt2,
        string Aniq, string Iqt1, string Iqt2, string AniqStudent, string Iqt1Student, string Iqt2Student,
        string Class5A, string Class5AName, string Class5B, string Class5BName,
        string Class5A1, string Class5A2, string Class5B1, string Class5B2);

    private static object Track(string id) => new { kind = "group", id };

    private static object LevelPayload(World w, string name, object[]? groups = null,
        string? subjectId = null, string pattern = BlockDayPattern.Odd) => new
    {
        name,
        subjectId = subjectId ?? w.English,
        blockType = BlockType.Level,
        dayPattern = pattern,
        sources = new[] { Track(w.Fil), Track(w.Ijt) },
        groups = groups ?? [new { name = "Katta", teacherId = w.TeacherA }, new { name = "Kichik", teacherId = w.TeacherB }],
    };

    private static object ClassPayload(World w, string name) => new
    {
        name,
        subjectId = w.English,
        blockType = BlockType.Level,
        dayPattern = BlockDayPattern.Odd,
        sources = new[] { new { kind = "class", id = w.Class5A }, new { kind = "class", id = w.Class5B } },
        groups = new[] { new { name = "Katta", teacherId = w.TeacherA }, new { name = "Kichik", teacherId = w.TeacherB } },
    };

    private static async Task<JsonElement> CreateAsync(HttpClient admin, object payload)
    {
        var res = await admin.PostAsJsonAsync(Blocks, payload);
        Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<JsonElement> GetAsync(HttpClient admin, Guid id) =>
        admin.GetFromJsonAsync<JsonElement>($"{Blocks}/{id}");

    private static (Guid First, Guid Second) Groups(JsonElement block)
    {
        var g = block.GetProperty("groups");
        return (g[0].GetProperty("id").GetGuid(), g[1].GetProperty("id").GetGuid());
    }

    private static async Task AssignAsync(HttpClient admin, Guid blockId, Guid groupId, string[] students, DateOnly? on = null)
    {
        var res = await admin.PostAsJsonAsync($"{Blocks}/{blockId}/assign",
            new { studentIds = students, groupId, date = on is null ? null : Iso(on.Value) });
        Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    private static async Task<Guid?> GroupOnAsync(HttpClient admin, Guid blockId, string studentId, DateOnly on)
    {
        var res = await admin.GetAsync($"{Blocks}/{blockId}/students/{studentId}/group?date={Iso(on)}");
        if (res.StatusCode == HttpStatusCode.NoContent) return null;
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupId").GetGuid();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage res, HttpStatusCode status, string contains)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"{res.StatusCode}: {body}");
        Assert.Contains(contains, JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
    }

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var english = new Subject { Name = $"Ingliz tili {tag}" };
        var math = new Subject { Name = $"Matematika {tag}" };
        var teachers = Enumerable.Range(0, 3).Select(i => new Teacher { FullName = $"Blok ustozi {i} {tag}" }).ToList();
        var c5a = new SchoolClass { Name = $"B{tag}-5A", Grade = 5 };
        var c5b = new SchoolClass { Name = $"B{tag}-5B", Grade = 5 };
        var classStudents = new[] { c5a, c5a, c5b, c5b }.Select((c, i) => NewStudent($"Sinf{i} {tag}", c.Name)).ToList();

        string[] trackNames = ["Filologiya", "Ijtimoiy", "Aniq fan", "Iqtisodiyot-1", "Iqtisodiyot-2"];
        var tracks = trackNames.Select(n => new StudyGroup { Name = $"{n} {tag}", IsTrack = true }).ToList();
        // Filologiya va Ijtimoiyda 2 tadan, qolganlarida 1 tadan o'quvchi.
        var trackStudents = new List<(StudyGroup Track, Student Student)>();
        foreach (var (t, i) in tracks.Select((t, i) => (t, i)))
            for (var k = 0; k < (i < 2 ? 2 : 1); k++)
                trackStudents.Add((t, NewStudent($"{trackNames[i]}{k} {tag}", $"B{tag}-10")));

        await fixture.Api.WithDbAsync(async db =>
        {
            var userId = await SystemUserIdAsync(db);
            db.Subjects.AddRange(english, math);
            db.Teachers.AddRange(teachers);
            db.Classes.AddRange(c5a, c5b);
            db.Students.AddRange(classStudents);
            foreach (var t in tracks) t.CreatedBy = userId;
            db.StudyGroups.AddRange(tracks);
            db.Students.AddRange(trackStudents.Select(x => x.Student));
            await db.SaveChangesAsync();
            db.StudyGroupMembers.AddRange(trackStudents.Select(x => new StudyGroupMember
            {
                GroupId = x.Track.Id, StudentId = x.Student.Id, JoinedOn = Today.AddDays(-60), CreatedBy = userId,
            }));
            await db.SaveChangesAsync();
        });

        string S(int track, int k) => trackStudents.Where(x => x.Track == tracks[track]).ElementAt(k).Student.Id;
        return new World(tag, english.Id, math.Id, teachers[0].Id, teachers[1].Id, teachers[2].Id,
            tracks[0].Id.ToString(), tracks[1].Id.ToString(), S(0, 0), S(0, 1), S(1, 0), S(1, 1),
            tracks[2].Id.ToString(), tracks[3].Id.ToString(), tracks[4].Id.ToString(), S(2, 0), S(3, 0), S(4, 0),
            c5a.Id, c5a.Name, c5b.Id, c5b.Name,
            classStudents[0].Id, classStudents[1].Id, classStudents[2].Id, classStudents[3].Id);
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2012-01-01",
        Gender = "male",
        ClassName = className,
        EnrollmentDate = "2026-09-01",
    };

    private static async Task<string> SystemUserIdAsync(AppDbContext db)
    {
        var existing = await db.Users.FirstOrDefaultAsync();
        if (existing is not null) return existing.Id;
        var user = new AppUser { FullName = "Seed", Role = Roles.Admin, Email = $"blocks.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
