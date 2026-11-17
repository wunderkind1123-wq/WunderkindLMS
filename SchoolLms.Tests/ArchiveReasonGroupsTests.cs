using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Arxivlash sabablari GURUHLARI, arxiv doskasi va "Lidlarga qaytarish" (mijoz, 2026-11-13) —
/// docs/modules/archive-reason-groups.md.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ArchiveReasonGroupsTests(ApiFixture fixture)
{
    private const string Groups = "/api/admin/archive-reason-groups";
    private const string Reasons = "/api/admin/archive-reasons";
    private const string Students = "/api/admin/students";
    private const string Board = "/api/admin/students/archive-board";

    /// <summary>Migratsiya qo'ygan uchta boshlang'ich guruh va bitiruv sababining guruhi.</summary>
    private static readonly Guid LeftThemselves = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Expelled = new("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Natural = new("00000000-0000-0000-0000-0000000000b3");
    private static readonly Guid Graduated = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherSchool = new("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Expulsion = new("00000000-0000-0000-0000-0000000000a6");

    [Fact]
    public async Task Boshlangich_guruhlar_va_sabablar_taqsimlangan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var groups = await RowsAsync(admin, $"{Groups}?includeInactive=true");
        foreach (var id in new[] { LeftThemselves, Expelled, Natural })
            Assert.Contains(groups, g => g.GetProperty("id").GetGuid() == id);

        var reasons = await RowsAsync(admin, $"{Reasons}?includeInactive=true");
        Guid? GroupOf(Guid reason) =>
            reasons.Single(r => r.GetProperty("id").GetGuid() == reason).GetProperty("groupId") is { ValueKind: JsonValueKind.String } g
                ? g.GetGuid() : null;
        Assert.Equal(Natural, GroupOf(Graduated));
        Assert.Equal(LeftThemselves, GroupOf(OtherSchool));
        Assert.Equal(Expelled, GroupOf(Expulsion));
    }

    /// <summary>
    /// Guruh CRUD: nom unikal, rang ro'yxatdan; ichida sabab bor guruh O'CHIRILMAYDI (sabab ko'chirilgach
    /// o'chiriladi); faolsiz guruhga yangi sabab qo'yilmaydi; tartib bir so'rovda.
    /// </summary>
    [Fact]
    public async Task Guruh_crud_va_ichida_sabab_bor_guruh_ochirilmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var created = await admin.PostAsJsonAsync(Groups, new { name = $"Qarzdorlik {tag}", color = "orange" });
        var group = await IdAsync(created);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(Groups, new { name = $"Qarzdorlik {tag}" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(Groups, new { name = $"Rangsiz {tag}", color = "pink" })).StatusCode);

        var reason = await IdAsync(await admin.PostAsJsonAsync(Reasons, new { name = $"Qarzini to'lamadi {tag}", groupId = group }));

        var refused = await admin.DeleteAsync($"{Groups}/{group}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("boshqa guruhga", await MessageAsync(refused));

        // Faolsiz guruhga yangi sabab qo'yilmaydi.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"{Groups}/{group}", new { name = $"Qarzdorlik {tag}", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(Reasons, new { name = $"Yana {tag}", groupId = group })).StatusCode);

        // Sabab boshqa guruhga ko'chiriladi → guruh o'chadi.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"{Reasons}/{reason}", new { name = $"Qarzini to'lamadi {tag}", groupId = Expelled })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Groups}/{group}")).StatusCode);

        // Tartib: berilgan ketma-ketlik saqlanadi.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"{Groups}/order", new[] { Natural, Expelled, LeftThemselves })).StatusCode);
        var order = (await RowsAsync(admin, Groups)).Select(g => g.GetProperty("id").GetGuid()).ToList();
        Assert.True(order.IndexOf(Natural) < order.IndexOf(Expelled) && order.IndexOf(Expelled) < order.IndexOf(LeftThemselves));
        await admin.PutAsJsonAsync($"{Groups}/order", new[] { LeftThemselves, Expelled, Natural });

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Groups)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(Groups, new { name = $"X {tag}" })).StatusCode);
    }

    /// <summary>Sabab turisiz arxivlash rad etiladi — bitta, ommaviy va sinf bilan.</summary>
    [Fact]
    public async Task Sabab_turisiz_arxivlab_bolmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var student = await SeedStudentAsync(tag);

        var single = await admin.PostAsJsonAsync($"{Students}/{student.Id}/archive", new { reason = "Ketdi" });
        Assert.Equal(HttpStatusCode.BadRequest, single.StatusCode);
        Assert.Equal(StudentArchiveService.ReasonTypeRequiredMessage, await MessageAsync(single));

        var bulk = await admin.PostAsJsonAsync($"{Students}/archive-many", new { studentIds = new[] { student.Id }, reason = "Ketdi" });
        Assert.Equal(HttpStatusCode.BadRequest, bulk.StatusCode);
        Assert.Equal(StudentArchiveService.ReasonTypeRequiredMessage, await MessageAsync(bulk));

        var cls = new SchoolClass { Name = $"5-{tag}", Grade = 5 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            var s = await db.Students.SingleAsync(x => x.Id == student.Id);
            s.ClassName = cls.Name;
            await db.SaveChangesAsync();
        });
        var classArchive = await admin.PostAsync($"/api/admin/classes/{cls.Id}/archive", null);
        Assert.Equal(HttpStatusCode.BadRequest, classArchive.StatusCode);
        Assert.Equal(StudentArchiveService.ReasonTypeRequiredMessage, await MessageAsync(classArchive));

        await fixture.Api.WithDbAsync(async db =>
            Assert.False((await db.Students.AsNoTracking().SingleAsync(s => s.Id == student.Id)).IsArchived));
    }

    /// <summary>
    /// Doska: o'quvchi o'z sababining guruh ustunida, sababsiz arxivlangan "Sababi aniqlanmagan"da.
    /// Sababni o'zgartirish kartochka va oxirgi ketish yozuvini birga o'zgartiradi (audit bilan).
    /// </summary>
    [Fact]
    public async Task Doska_ustunlari_va_sababni_ozgartirish()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var grouped = await SeedStudentAsync(tag);
        var legacy = await SeedStudentAsync(tag);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Students}/{grouped.Id}/archive", new { reason = "Yangi maktab", archiveReasonId = OtherSchool })).StatusCode);
        // Eski arxiv: katalog sababisiz (2026-11-13 dan oldingi kabi) — to'g'ridan-to'g'ri bazaga.
        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.SingleAsync(x => x.Id == legacy.Id);
            s.IsArchived = true;
            s.ArchivedAt = "2026-09-01";
            s.ArchiveReason = "ketdi";
            await db.SaveChangesAsync();
        });

        var board = await JsonAsync(await admin.GetAsync(Board));
        string? ColumnOf(string studentId) => board.GetProperty("columns").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("students").EnumerateArray().Any(s => s.GetProperty("id").GetString() == studentId))
            is { ValueKind: JsonValueKind.Object } col
                ? col.GetProperty("id").ValueKind == JsonValueKind.Null ? "none" : col.GetProperty("id").GetString()
                : null;
        Assert.Equal(LeftThemselves.ToString(), ColumnOf(grouped.Id));
        Assert.Equal("none", ColumnOf(legacy.Id));
        Assert.Equal(StudentArchiveBoardController.UnassignedColumn,
            board.GetProperty("columns")[0].GetProperty("name").GetString());

        // Sababni o'zgartirish.
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "students:view");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Board)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync($"{Students}/{grouped.Id}/archive-reason", new { reasonId = Expulsion })).StatusCode);

        var saved = await admin.PutAsJsonAsync($"{Students}/{grouped.Id}/archive-reason", new { reasonId = Expulsion, note = "tartib buzdi" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"{Students}/{(await SeedStudentAsync(tag)).Id}/archive-reason", new { reasonId = Expulsion })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.AsNoTracking().SingleAsync(x => x.Id == grouped.Id);
            Assert.Equal(((Guid?)Expulsion, "tartib buzdi"), (s.ArchiveReasonId, s.ArchiveReason));
            var ev = await db.StudentEvents.AsNoTracking()
                .Where(e => e.StudentId == grouped.Id && e.Kind == StudentEventKind.Left).SingleAsync();
            Assert.Equal(((Guid?)Expulsion, "tartib buzdi"), (ev.ReasonId, ev.ReasonText));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == grouped.Id && a.Action == "archive-reason"));
        });

        board = await JsonAsync(await admin.GetAsync(Board));
        Assert.Equal(Expelled.ToString(), ColumnOf(grouped.Id));
    }

    /// <summary>
    /// Lidlarga qaytarish: lid "archive" manbasi bilan, izohda sabab; bitta o'quvchiga bitta lid;
    /// bunday lid YANGI o'quvchiga aylantirilmaydi; o'quvchi arxivdan qaytarilsa lid yopiladi va
    /// voronkada sanaladi. Xodimga `leads` kaliti ham kerak.
    /// </summary>
    [Fact]
    public async Task Lidlarga_qaytarish_va_arxivdan_qaytganda_lid_yopiladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        await fixture.Api.WithDbAsync(async db =>
        {
            if (!await db.LeadStages.AnyAsync())
            {
                db.LeadStages.Add(new LeadStage { Title = "Yangi", Order = 0 });
                await db.SaveChangesAsync();
            }
        });

        var student = await SeedStudentAsync(tag);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Students}/{student.Id}/to-lead", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Students}/{student.Id}/archive", new { reason = "Qimmat", archiveReasonId = OtherSchool })).StatusCode);

        using var noLeads = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.Forbidden, (await noLeads.PostAsJsonAsync($"{Students}/{student.Id}/to-lead", new { })).StatusCode);

        var created = await admin.PostAsJsonAsync($"{Students}/{student.Id}/to-lead", new { note = "chegirma taklif qilamiz" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var leadId = (await JsonAsync(created)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{Students}/{student.Id}/to-lead", new { })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);
            Assert.Equal((LeadSource.Archive, student.Id), (lead.Source, lead.FromStudentId));
            Assert.Equal(student.FullName, lead.FullName);
            Assert.Contains("Boshqa maktabga o'tdi", lead.Note);
            Assert.Contains("chegirma taklif qilamiz", lead.Note);
        });

        var board = await JsonAsync(await admin.GetAsync(Board));
        var card = board.GetProperty("columns").EnumerateArray().SelectMany(c => c.GetProperty("students").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == student.Id);
        Assert.Equal(leadId, card.GetProperty("leadId").GetString());

        // Yangi o'quvchi qilib bo'lmaydi.
        var enrol = await admin.PostAsJsonAsync($"/api/admin/leads/{leadId}/enrol",
            new
            {
                student = new
                {
                    fullName = student.FullName, birthDate = "2016-03-14", address = "", gender = "male",
                    parentFullName = "Ota", parentPhone = "+998900000002", className = "", targetGrade = 5,
                    enrollmentDate = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.Conflict, enrol.StatusCode);
        Assert.Equal(StudentsController.LeadFromArchiveMessage, await MessageAsync(enrol));

        // Arxivdan qaytarish — lid yopiladi, aylantirish sanaladi.
        int conversionsBefore = 0;
        await fixture.Api.WithDbAsync(async db => conversionsBefore = await db.LeadConversions.CountAsync(c => c.Source == LeadSource.Archive));
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Students}/{student.Id}/restore", new { newPassword = (string?)null })).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.Leads.AnyAsync(l => l.Id == leadId));
            Assert.Equal(conversionsBefore + 1, await db.LeadConversions.CountAsync(c => c.Source == LeadSource.Archive));
        });
    }

    // ----- yordamchilar -----

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Student> SeedStudentAsync(string tag)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"Doska {Guid.NewGuid().ToString("N")[..4]} {tag}", $"AD-{tag}", "+998900000002");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("id").GetGuid();

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url) =>
        [.. (await JsonAsync(await client.GetAsync(url))).EnumerateArray()];

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
