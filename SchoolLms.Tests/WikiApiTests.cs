using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using Xunit;

namespace SchoolLms.Tests;

/// <summary>
/// BILIMLAR BAZASI — HTTP: kim yozadi (xodim, kassir, o'qituvchi — ha; o'quvchi/ota-ona — 403), begona eslatma 404,
/// faqat rahbariyat (admin yoki HR direktor) xodimlarnikini ko'radi, qulf egasini to'xtatadi, 409 to'qnashuv,
/// faqat matn (rasm bloki 400), no-store. Qoidalar: docs/modules/wiki.md.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class WikiApiTests(ApiFixture fixture)
{
    private const string Notes = "/api/me/notes";
    private const string Team = "/api/admin/wiki/team";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<(HttpClient Client, AppUser User)> LoginAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return (fixture.Api.ClientWithToken(fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email)), user);
    }

    private Task AddHrEmployeeAsync(AppUser user, string hrRole = HrRoles.Employee) =>
        fixture.Api.WithDbAsync(async db =>
        {
            db.HrEmployees.Add(new HrEmployee { UserId = user.Id, FullName = user.FullName, HrRole = hrRole });
            await db.SaveChangesAsync();
        });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

    private static async Task<JsonElement> CreateAsync(HttpClient c, string url, object body)
    {
        var r = await c.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ReadAsync(r);
    }

    private static string Paragraph(string text) =>
        JsonSerializer.Serialize(new[] { new { type = "paragraph", content = new[] { new { type = "text", text } } } });

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Admin)]
    public async Task Har_bir_xodim_ozi_yozadi_qidiradi_va_daraxtda_koradi(string role)
    {
        var (c, user) = await LoginAsync(role);
        var page = await CreateAsync(c, $"{Notes}/pages", new { parentId = "", title = "Reja" });
        var id = page.GetProperty("id").GetString();

        var save = await c.PutAsJsonAsync($"{Notes}/pages/{id}",
            new { title = "Reja", icon = "", contentJson = Paragraph("noyabr insayti"), baseVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.True(save.Headers.CacheControl?.NoStore);

        var tree = await ReadAsync(await c.GetAsync(Notes));
        Assert.Equal(user.Id, tree.GetProperty("ownerUserId").GetString());
        Assert.Contains(tree.GetProperty("pages").EnumerateArray(), p => p.GetProperty("id").GetString() == id);

        var hits = await ReadAsync(await c.GetAsync($"{Notes}/search?q=insayt"));
        Assert.Single(hits.EnumerateArray());
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData("parent")]
    public async Task Oquvchi_va_ota_ona_eslatma_yoza_olmaydi(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        // O'quvchi tokeni faol `students` qatorisiz 401 bo'lardi (token revocation) — haqiqiy o'quvchi bo'lsin.
        if (role == Roles.Student)
            await fixture.Api.WithDbAsync(async db =>
            {
                db.Students.Add(new Student { FullName = user.FullName, ClassName = "5-A", EnrollmentDate = "2026-09-01", UserId = user.Id });
                await db.SaveChangesAsync();
            });
        using var c = fixture.Api.ClientWithToken(fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Notes)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"{Notes}/pages", new { title = "x" })).StatusCode);
    }

    [Fact]
    public async Task Begona_eslatma_404_hamkasb_va_menejer_xodimlarni_kormaydi()
    {
        var (a, userA) = await LoginAsync(Roles.Staff);
        var (b, userB) = await LoginAsync(Roles.Staff);
        await AddHrEmployeeAsync(userA);
        await AddHrEmployeeAsync(userB, HrRoles.Manager);
        var id = (await CreateAsync(a, $"{Notes}/pages", new { title = "Shaxsiy" })).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"{Notes}/pages/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"{Notes}/pages/{id}")).StatusCode);

        // Menejer ham rahbariyat emas — tab chiqmaydi, xodim eslatmasi 404.
        var team = await ReadAsync(await b.GetAsync($"{Team}/members"));
        Assert.False(team.GetProperty("isLeader").GetBoolean());
        Assert.Empty(team.GetProperty("members").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"{Team}/members/{userA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"{Team}/pages/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"{Team}/pages/{id}/lock", new { locked = true })).StatusCode);
    }

    [Fact]
    public async Task Admin_xodim_eslatmasini_oqiydi_qulflaydi_va_egasi_faqat_oqiydi()
    {
        var (staff, staffUser) = await LoginAsync(Roles.Staff);
        await AddHrEmployeeAsync(staffUser);
        var (admin, _) = await LoginAsync(Roles.Admin);
        var id = (await CreateAsync(staff, $"{Notes}/pages", new { title = "Insayt" })).GetProperty("id").GetString();

        var team = await ReadAsync(await admin.GetAsync($"{Team}/members"));
        Assert.True(team.GetProperty("isLeader").GetBoolean());
        var me = team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetString() == staffUser.Id);
        Assert.Equal(1, me.GetProperty("noteCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Team}/pages/{id}/lock", new { locked = true })).StatusCode);

        // Egasi: o'qiydi, lekin saqlay, ichiga qo'sha, o'chira olmaydi.
        var mine = await ReadAsync(await staff.GetAsync($"{Notes}/pages/{id}"));
        Assert.True(mine.GetProperty("effectiveLocked").GetBoolean());
        var version = mine.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"{Notes}/pages/{id}",
            new { title = "o'zgardi", icon = "", contentJson = "", baseVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"{Notes}/pages", new { parentId = id, title = "ichki" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"{Notes}/pages/{id}")).StatusCode);

        // Rahbariyat qulflangan eslatmani tahrirlay oladi.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Team}/pages/{id}",
            new { title = "Rahbar izohi", icon = "", contentJson = Paragraph("ko'rib chiqildi"), baseVersion = version })).StatusCode);
    }

    [Fact]
    public async Task HR_direktor_staff_rahbariyat_hisoblanadi()
    {
        var (dir, dirUser) = await LoginAsync(Roles.Staff);
        await AddHrEmployeeAsync(dirUser, HrRoles.Director);
        var (staff, staffUser) = await LoginAsync(Roles.Staff);
        await AddHrEmployeeAsync(staffUser);
        await CreateAsync(staff, $"{Notes}/pages", new { title = "Reja" });

        var team = await ReadAsync(await dir.GetAsync($"{Team}/members"));
        Assert.True(team.GetProperty("isLeader").GetBoolean());
        Assert.Contains(team.GetProperty("members").EnumerateArray(), m => m.GetProperty("userId").GetString() == staffUser.Id);
        Assert.DoesNotContain(team.GetProperty("members").EnumerateArray(), m => m.GetProperty("userId").GetString() == dirUser.Id);

        var tree = await ReadAsync(await dir.GetAsync($"{Team}/members/{staffUser.Id}"));
        Assert.Single(tree.GetProperty("pages").EnumerateArray());
    }

    [Fact]
    public async Task Eski_versiya_ustidan_saqlash_409_rasm_bloki_400()
    {
        var (c, _) = await LoginAsync(Roles.Staff);
        var id = (await CreateAsync(c, $"{Notes}/pages", new { title = "A" })).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"{Notes}/pages/{id}",
            new { title = "A", icon = "", contentJson = Paragraph("1"), baseVersion = 1 })).StatusCode);

        var stale = await c.PutAsJsonAsync($"{Notes}/pages/{id}", new { title = "A", icon = "", contentJson = Paragraph("2"), baseVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True((await ReadAsync(stale)).GetProperty("conflict").GetBoolean());

        var image = JsonSerializer.Serialize(new[] { new { type = "image", props = new { url = "https://x/y.png" } } });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{Notes}/pages/{id}",
            new { title = "A", icon = "", contentJson = image, baseVersion = 2 })).StatusCode);
    }
}
