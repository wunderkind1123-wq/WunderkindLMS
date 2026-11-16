using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using Xunit;

namespace SchoolLms.Tests;

/// <summary>
/// OLIMPIADA — HTTP: ruxsatlar, limit va kutish ro'yxati (poyga holatisiz), kirish (QR), natija o'rni,
/// lidga aylantirish (takrorsiz). docs/modules/olympiad.md §16.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class OlympiadApiTests(ApiFixture fixture)
{
    private const string Base = "/api/admin/olympiad";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await ReadAsync(r);
    }

    private static object EventBody(string title, int? capacity, string onFull = "waitlist") => new
    {
        title, description = "", date = DateOnly.FromDateTime(DateTime.Today.AddDays(10)).ToString("yyyy-MM-dd"),
        startTime = "10:00:00", endTime = "12:00:00", location = "Maktab", locationMapUrl = "", capacity, onFull,
        registrationOpensAt = (DateTime?)null, registrationClosesAt = (DateTime?)null, maxScore = 100,
        showScore = true, showRank = true, showCertificate = true, showInvite = true,
        rulesText = "", rulesFileUrl = "", rulesFileName = "", rulesDisplay = "text",
    };

    private static object Kid(string name, string phone, int grade = 5) =>
        new { participant = new { fullName = name, phone, grade, school = "Yaypan 5-maktab" } };

    private async Task<(HttpClient Admin, string EventId)> NewEventAsync(int? capacity, string onFull = "waitlist")
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var ev = await OkAsync(admin.PostAsJsonAsync($"{Base}/events", EventBody("Mock " + Guid.NewGuid().ToString("N")[..6], capacity, onFull)));
        var id = ev.GetProperty("id").GetString()!;
        await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{id}/status", new { status = "published" }));
        return (admin, id);
    }

    /// <summary>
    /// Parallel so'rovlar pulda o'nlab bo'sh ulanish qoldiradi (bir necha daqiqa yopilmaydi) — to'liq yurishda
    /// keyingi test sinflariga Postgres ulanishi yetmay qolardi (53300). Shu test ochganlarini darrov yopamiz.
    /// </summary>
    private static void ReleaseIdleConnections() => Npgsql.NpgsqlConnection.ClearAllPools();

    private static string Phone() => "+99890" + Random.Shared.Next(1000000, 9999999);

    [Fact]
    public async Task Ruxsatsiz_xodim_korolmaydi_kirish_ruxsati_faqat_kirish_sahifasini_ochadi()
    {
        var none = await fixture.Api.ClientAsAsync(Roles.Staff, "leads");
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync($"{Base}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("/api/admin/olympiad-checkin/events")).StatusCode);

        var checkin = await fixture.Api.ClientAsAsync(Roles.Staff, OlympiadPerms.Checkin);
        Assert.Equal(HttpStatusCode.Forbidden, (await checkin.GetAsync($"{Base}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await checkin.GetAsync("/api/admin/olympiad-checkin/events")).StatusCode);

        var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, OlympiadPerms.Manage + Roles.ViewSuffix);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Base}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{Base}/events", EventBody("x y", null))).StatusCode);

        var manager = await fixture.Api.ClientAsAsync(Roles.Staff, OlympiadPerms.Manage);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{Base}/events", EventBody("Mock z", null))).StatusCode);
    }

    [Fact]
    public async Task Limit_tolsa_kutishga_tushadi_bekor_qilinsa_navbatdagi_otadi()
    {
        var (admin, ev) = await NewEventAsync(capacity: 1);
        var a = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyev Ali", Phone())));
        var b = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Valiyev Vali", Phone())));
        Assert.Equal("registered", a.GetProperty("status").GetString());
        Assert.EndsWith("-0001", a.GetProperty("number").GetString());
        Assert.Equal("waitlist", b.GetProperty("status").GetString());
        Assert.Equal(1, b.GetProperty("waitlistPosition").GetInt32());

        // Bir bola ikki marta yozilmaydi.
        var dup = await admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations",
            new { participantId = a.GetProperty("participantId").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Base}/registrations/{a.GetProperty("id").GetString()}/cancel", null)).StatusCode);
        var list = await OkAsync(admin.GetAsync($"{Base}/events/{ev}/registrations"));
        var promoted = list.EnumerateArray().Single(r => r.GetProperty("id").GetString() == b.GetProperty("id").GetString());
        Assert.Equal("registered", promoted.GetProperty("status").GetString());
        // Bekor qilinganning raqami qayta berilmaydi.
        Assert.EndsWith("-0002", promoted.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Bir_yozuvni_parallel_ikki_marta_bekor_qilish_hisoblagichni_siljitmaydi()
    {
        var (admin, ev) = await NewEventAsync(capacity: 2);
        var a = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyev Ali", Phone())));
        await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Valiyev Vali", Phone())));
        var c = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Soliyev Soli", Phone())));
        var d = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Qodirov Qodir", Phone())));
        Assert.Equal("waitlist", d.GetProperty("status").GetString());

        var id = a.GetProperty("id").GetString();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.PostAsync($"{Base}/registrations/{id}/cancel", null)));
        ReleaseIdleConnections();

        var list = (await OkAsync(admin.GetAsync($"{Base}/events/{ev}/registrations"))).EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("status").GetString());
        Assert.Equal("registered", list[c.GetProperty("id").GetString()!]);
        Assert.Equal("waitlist", list[d.GetProperty("id").GetString()!]);
        await fixture.Api.WithDbAsync(async db =>
        {
            var e = await db.OlympiadEvents.FirstAsync(x => x.Id == Guid.Parse(ev));
            Assert.Equal(2, e.SeatsTaken);
        });
    }

    [Fact]
    public async Task Yopiq_rejimda_limit_tolsa_yozib_bolmaydi()
    {
        var (admin, ev) = await NewEventAsync(capacity: 1, onFull: "close");
        await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyev Ali", Phone())));
        var r = await admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Valiyev Vali", Phone()));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Bir_vaqtda_yozilganda_limitdan_oshmaydi()
    {
        var (admin, ev) = await NewEventAsync(capacity: 3);
        var calls = Enumerable.Range(0, 12).Select(i =>
            admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid($"Bola Nomer{(char)('A' + i)}", Phone())));
        await Task.WhenAll(calls);
        ReleaseIdleConnections();
        var list = await OkAsync(admin.GetAsync($"{Base}/events/{ev}/registrations"));
        var regs = list.EnumerateArray().ToList();
        Assert.Equal(3, regs.Count(r => r.GetProperty("status").GetString() == "registered"));
        Assert.Equal(9, regs.Count(r => r.GetProperty("status").GetString() == "waitlist"));
        Assert.Equal(new[] { 1, 2, 3 }, regs.Where(r => r.GetProperty("status").GetString() == "registered")
            .Select(r => r.GetProperty("numberValue").GetInt32()).Order().ToArray());
    }

    [Fact]
    public async Task Qr_skan_kirdi_takrorida_ogohlantirish_yakunlashda_kelmaganlar()
    {
        var (admin, ev) = await NewEventAsync(capacity: null);
        var a = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyev Ali", Phone())));
        await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Valiyev Vali", Phone())));
        string token = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var id = Guid.Parse(a.GetProperty("id").GetString()!);
            token = (await db.OlympiadRegistrations.FirstAsync(r => r.Id == id)).QrToken;
        });

        var checker = await fixture.Api.ClientAsAsync(Roles.Staff, OlympiadPerms.Checkin);
        var first = await OkAsync(checker.PostAsJsonAsync("/api/admin/olympiad-checkin/scan", new { eventId = ev, token }));
        Assert.Equal("ok", first.GetProperty("outcome").GetString());
        var again = await OkAsync(checker.PostAsJsonAsync("/api/admin/olympiad-checkin/scan", new { eventId = ev, token }));
        Assert.Equal("already", again.GetProperty("outcome").GetString());
        var bad = await OkAsync(checker.PostAsJsonAsync("/api/admin/olympiad-checkin/scan", new { eventId = ev, token = "yoq" }));
        Assert.Equal("error", bad.GetProperty("outcome").GetString());

        var summary = await OkAsync(checker.PostAsync($"/api/admin/olympiad-checkin/events/{ev}/finish", null));
        Assert.Equal(2, summary.GetProperty("registered").GetInt32());
        Assert.Equal(1, summary.GetProperty("attended").GetInt32());
        var list = await OkAsync(admin.GetAsync($"{Base}/events/{ev}/registrations?status=no_show"));
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task Natija_orni_teng_ballda_bir_xil_va_chegaradan_tashqari_ball_rad_etiladi()
    {
        var (admin, ev) = await NewEventAsync(capacity: null);
        var ids = new List<string>();
        foreach (var n in new[] { "Aliyev Ali", "Valiyev Vali", "Soliyev Soli", "Qodirov Qodir" })
            ids.Add((await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid(n, Phone())))).GetProperty("id").GetString()!);
        var scores = new[] { 90m, 80m, 80m, 70m };
        for (var i = 0; i < 4; i++)
            await OkAsync(admin.PutAsJsonAsync($"{Base}/registrations/{ids[i]}/score", new { score = scores[i] }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Base}/registrations/{ids[0]}/score", new { score = 101 })).StatusCode);

        var list = (await OkAsync(admin.GetAsync($"{Base}/events/{ev}/registrations"))).EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r);
        Assert.Equal(new[] { 1, 2, 2, 4 }, ids.Select(id => list[id].GetProperty("rank").GetInt32()).ToArray());
        Assert.Equal("attended", list[ids[0]].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Lidga_aylantirish_olympiad_manbasi_bilan_va_takror_telefon_yaratilmaydi()
    {
        var (admin, ev) = await NewEventAsync(capacity: null);
        var phone = Phone();
        var a = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyev Ali", phone)));
        var b = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{ev}/registrations", Kid("Aliyeva Laylo", phone)));

        var res = await OkAsync(admin.PostAsJsonAsync($"{Base}/participants/convert-leads",
            new { participantIds = new[] { a.GetProperty("participantId").GetString(), b.GetProperty("participantId").GetString() } }));
        Assert.Equal(1, res.GetProperty("created").GetInt32());
        Assert.Equal(1, res.GetProperty("linked").GetInt32());

        await fixture.Api.WithDbAsync(async db =>
        {
            var key = phone[^9..];
            var leads = await db.Leads.Where(l => l.ParentPhone.EndsWith(key)).ToListAsync();
            var lead = Assert.Single(leads);
            Assert.Equal(LeadSource.Olympiad, lead.Source);
            Assert.Equal("Aliyev Ali", lead.FullName);
        });

        // Ikkinchi marta — o'tkazib yuboriladi.
        var again = await OkAsync(admin.PostAsJsonAsync($"{Base}/participants/convert-leads",
            new { participantIds = new[] { a.GetProperty("participantId").GetString() } }));
        Assert.Equal(1, again.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Yangi_tadbirga_ikkita_standart_eslatma_qoshiladi()
    {
        var (admin, ev) = await NewEventAsync(capacity: null);
        var list = await OkAsync(admin.GetAsync($"{Base}/events/{ev}/reminders"));
        Assert.Equal(new[] { "standard1", "standard2" }, list.EnumerateArray().Select(r => r.GetProperty("kind").GetString()).Order().ToArray());
    }
}
