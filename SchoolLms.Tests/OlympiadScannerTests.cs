using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using Xunit;

namespace SchoolLms.Tests;

/// <summary>
/// Olimpiada botidagi QR skaner (Telegram Mini App, <c>/api/public/olympiad-scanner</c>; mijoz, 2026-11-18):
/// kirish faqat olimpiada boti imzolagan <c>initData</c> bilan va faqat O'Z raqami Sozlamalar → "Bot adminlari"da
/// bo'lgan Telegram foydalanuvchisiga. Imkoniyatlar admin sahifasidagi kirish bilan bir xil.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class OlympiadScannerTests(ApiFixture fixture)
{
    private const string Base = "/api/admin/olympiad";
    private const string Scanner = "/api/public/olympiad-scanner";
    private const string BotToken = "777000:OLYMPIAD-SCANNER-TEST";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long _nextTelegramId = 8_800_000_000;

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
    }

    private static string InitData(long telegramUserId, string token = BotToken) => TelegramInitData.Sign(new Dictionary<string, string>
    {
        ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        ["query_id"] = "AAH" + telegramUserId,
        ["user"] = JsonSerializer.Serialize(new { id = telegramUserId, first_name = "Kirish", last_name = "Xodimi" }),
    }, token);

    private HttpClient As(long telegramUserId, string token = BotToken)
    {
        var c = fixture.Api.AnonymousClient();
        c.DefaultRequestHeaders.Add("X-Telegram-Init-Data", InitData(telegramUserId, token));
        return c;
    }

    [Fact]
    public async Task Faqat_admin_raqamli_telegram_foydalanuvchisi_skanerlay_oladi()
    {
        var adminTg = Interlocked.Increment(ref _nextTelegramId);
        var strangerTg = Interlocked.Increment(ref _nextTelegramId);
        var adminPhone = "+99893" + Random.Shared.Next(1000000, 9999999);
        var otherPhone = "+99894" + Random.Shared.Next(1000000, 9999999);

        // Sozlama yagona qator (Id=1) — boshqa testlar uchun avvalgi holatiga qaytariladi.
        string oldToken = "", oldPhones = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.OlympiadSettings.FirstOrDefaultAsync();
            if (s is null) { s = new OlympiadSettings(); db.OlympiadSettings.Add(s); }
            oldToken = s.BotToken;
            oldPhones = s.AdminPhones;
            s.BotToken = BotToken;
            s.AdminPhones = "+998 90 000 00 00\n" + adminPhone;
            var now = AppClock.Now;
            db.OlympiadBotUsers.Add(new OlympiadBotUser { TelegramUserId = adminTg, Phone = adminPhone, CreatedAt = now, LastSeenAt = now });
            db.OlympiadBotUsers.Add(new OlympiadBotUser { TelegramUserId = strangerTg, Phone = otherPhone, CreatedAt = now, LastSeenAt = now });
            await db.SaveChangesAsync();
        });
        try
        {
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var ev = await OkAsync(admin.PostAsJsonAsync($"{Base}/events", new
            {
                title = "Skaner " + Guid.NewGuid().ToString("N")[..6], description = "",
                date = DateOnly.FromDateTime(DateTime.Today.AddDays(3)).ToString("yyyy-MM-dd"),
                startTime = "10:00:00", endTime = "12:00:00", location = "Maktab", locationMapUrl = "", capacity = (int?)null,
                onFull = "waitlist", registrationOpensAt = (DateTime?)null, registrationClosesAt = (DateTime?)null, maxScore = 100,
                showScore = true, showRank = true, showCertificate = true, showInvite = true,
                rulesText = "", rulesFileUrl = "", rulesFileName = "", rulesDisplay = "text",
            }));
            var eventId = ev.GetProperty("id").GetString()!;
            await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{eventId}/status", new { status = "published" }));
            var reg = await OkAsync(admin.PostAsJsonAsync($"{Base}/events/{eventId}/registrations",
                new { participant = new { fullName = "Skanerov Sardor", phone = otherPhone, grade = 6, school = "Yaypan 5-maktab" } }));
            var regId = Guid.Parse(reg.GetProperty("id").GetString()!);
            string qr = "";
            await fixture.Api.WithDbAsync(async db => qr = (await db.OlympiadRegistrations.FirstAsync(r => r.Id == regId)).QrToken);

            // Imzosiz va boshqa bot tokeni bilan imzolangan — 401.
            using (var anon = fixture.Api.AnonymousClient())
                Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Scanner}/events")).StatusCode);
            using (var forged = As(adminTg, "999:BEGONA-BOT"))
                Assert.Equal(HttpStatusCode.Unauthorized, (await forged.PostAsJsonAsync($"{Scanner}/scan", new { eventId, token = qr })).StatusCode);

            // Imzo to'g'ri, lekin raqami admin ro'yxatida yo'q — 403, belgi qo'yilmaydi.
            using (var stranger = As(strangerTg))
                Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"{Scanner}/scan", new { eventId, token = qr })).StatusCode);

            using var scanner = As(adminTg);
            Assert.Contains("Kirish Xodimi", (await OkAsync(scanner.GetAsync($"{Scanner}/me"))).GetProperty("name").GetString());
            Assert.Contains((await OkAsync(scanner.GetAsync($"{Scanner}/events"))).EnumerateArray(), e => e.GetProperty("id").GetString() == eventId);

            var first = await OkAsync(scanner.PostAsJsonAsync($"{Scanner}/scan", new { eventId, token = qr }));
            Assert.Equal("ok", first.GetProperty("outcome").GetString());
            Assert.Equal("already", (await OkAsync(scanner.PostAsJsonAsync($"{Scanner}/scan", new { eventId, token = qr }))).GetProperty("outcome").GetString());
            var summary = await OkAsync(scanner.GetAsync($"{Scanner}/events/{eventId}/summary"));
            Assert.Equal(1, summary.GetProperty("attended").GetInt32());

            await fixture.Api.WithDbAsync(async db =>
            {
                var r = await db.OlympiadRegistrations.AsNoTracking().FirstAsync(x => x.Id == regId);
                Assert.Equal(OlympiadRegStatus.Attended, r.Status);
                Assert.Contains("Telegram", r.CheckedInBy);
            });

            // Qidiruv va bekor qilish; keyin qo'lda belgilash.
            Assert.Single((await OkAsync(scanner.GetAsync($"{Scanner}/events/{eventId}/search?q=Skanerov"))).EnumerateArray());
            Assert.Equal("registered", (await OkAsync(scanner.PostAsync($"{Scanner}/registrations/{regId}/undo", null))).GetProperty("status").GetString());
            Assert.Equal("ok", (await OkAsync(scanner.PostAsync($"{Scanner}/registrations/{regId}/checkin", null))).GetProperty("outcome").GetString());

            // "Yakunlash" botdagi skanerda yo'q.
            Assert.Equal(HttpStatusCode.NotFound, (await scanner.PostAsync($"{Scanner}/events/{eventId}/finish", null)).StatusCode);

            // Raqam ro'yxatdan olib tashlansa — darhol 403.
            await fixture.Api.WithDbAsync(async db =>
            {
                var s = await db.OlympiadSettings.FirstAsync();
                s.AdminPhones = "+998 90 000 00 00";
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.Forbidden, (await scanner.GetAsync($"{Scanner}/events")).StatusCode);
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                var s = await db.OlympiadSettings.FirstAsync();
                s.BotToken = oldToken;
                s.AdminPhones = oldPhones;
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task Sozlamada_admin_raqamlari_normallanadi_notogrisi_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var current = await OkAsync(admin.GetAsync($"{Base}/settings"));
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText(), Json)!;
        var old = current.GetProperty("adminPhones").GetString();
        try
        {
            body["adminPhones"] = "90 123 45 67\n+998901234567, 998 93 111 22 33";
            var saved = await OkAsync(admin.PutAsJsonAsync($"{Base}/settings", body));
            Assert.Equal("+998901234567\n+998931112233", saved.GetProperty("adminPhones").GetString());

            body["adminPhones"] = "+998901234567\n12345";
            var bad = await admin.PutAsJsonAsync($"{Base}/settings", body);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("12345", await bad.Content.ReadAsStringAsync());
        }
        finally
        {
            body["adminPhones"] = old;
            await admin.PutAsJsonAsync($"{Base}/settings", body);
        }
    }
}
