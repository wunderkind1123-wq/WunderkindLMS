using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Olympiad;

/// <summary>Telegram javobi: ok, natija, xato matni, 403 (bloklagan) va 429 (retry_after).</summary>
public sealed record TgResult(bool Ok, JsonElement? Result, string Error, bool Blocked, int RetryAfter)
{
    public long? MessageId => Result is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("message_id", out var id)
        ? id.GetInt64() : null;
}

/// <summary>Yuboriladigan fayl: bayt yoki keshdagi <c>file_id</c>.</summary>
public sealed record TgFile(string FileName, byte[]? Bytes, string? FileId);

/// <summary>
/// Olimpiada botining Telegram Bot API mijozi — maktab botidan (<c>TelegramService</c>) butunlay alohida:
/// o'z tokeni, o'z holati. Singleton; token sozlamadan yuklanadi va saqlanganda yangilanadi.
/// </summary>
public sealed class OlympiadTelegram(IHttpClientFactory httpFactory, ILogger<OlympiadTelegram> logger)
{
    private volatile string _token = "";
    public string Username { get; private set; } = "";

    public bool IsConfigured => _token.Length > 0;

    /// <summary>Token o'zgarganini fon xizmati sezishi uchun (getUpdates offset'ini tashlaydi).</summary>
    public int Generation { get; private set; }

    public void Set(string? token, string? username)
    {
        var t = (token ?? "").Trim();
        if (t != _token) Generation++;
        _token = t;
        Username = (username ?? "").Trim().TrimStart('@');
    }

    public void Load(IAppDbContext db)
    {
        var s = db.OlympiadSettings.AsNoTracking().FirstOrDefault();
        Set(s?.BotToken, s?.BotUsername);
    }

    private HttpClient Client()
    {
        var c = httpFactory.CreateClient("olympiad-telegram");
        c.Timeout = TimeSpan.FromSeconds(70);
        return c;
    }

    // ---------------- Tezlik cheklovi ----------------
    // Telegram: bot uchun ~30 xabar/soniya. Rassilkada bitta "xabar" 2–3 so'rov bo'lishi mumkin (albom + matn),
    // shuning uchun tezlik HAR SO'ROVGA qo'llanadi: so'rovlar orasida kamida 36 ms (~27/s). getUpdates bundan ozod.
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(36);
    private readonly object _paceLock = new();
    private DateTime _nextSlot = DateTime.MinValue;

    private Task PaceAsync(CancellationToken ct)
    {
        TimeSpan wait;
        lock (_paceLock)
        {
            var now = DateTime.UtcNow;
            var slot = _nextSlot > now ? _nextSlot : now;
            _nextSlot = slot + MinGap;
            wait = slot - now;
        }
        return wait > TimeSpan.Zero ? Task.Delay(wait, ct) : Task.CompletedTask;
    }

    /// <summary>JSON so'rov. <paramref name="token"/> berilsa — shu token bilan (sozlamada "Ulanishni tekshirish").</summary>
    public async Task<TgResult> CallAsync(string method, object payload, CancellationToken ct, string? token = null)
    {
        var tk = token ?? _token;
        if (tk.Length == 0) return new TgResult(false, null, "Bot tokeni sozlanmagan", false, 0);
        if (method != "getUpdates") await PaceAsync(ct);
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Client().PostAsync($"https://api.telegram.org/bot{tk}/{method}", content, ct);
            return await ReadAsync(resp, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Olimpiada bot: {Method} xatosi: {Error}", method, ex.Message);
            return new TgResult(false, null, ex.Message, false, 0);
        }
    }

    /// <summary>Fayl bilan so'rov (sendPhoto/sendDocument/sendVideo/sendVideoNote).</summary>
    public async Task<TgResult> SendFileAsync(string method, string field, long chatId, TgFile file,
        Dictionary<string, string?> extra, CancellationToken ct)
    {
        if (!IsConfigured) return new TgResult(false, null, "Bot tokeni sozlanmagan", false, 0);
        if (file.FileId is not null)
        {
            var payload = new Dictionary<string, object?>(extra.ToDictionary(k => k.Key, k => (object?)k.Value))
            {
                ["chat_id"] = chatId,
                [field] = file.FileId,
            };
            foreach (var k in payload.Where(p => p.Value is null).Select(p => p.Key).ToList()) payload.Remove(k);
            // reply_markup — JSON satri, ob'ekt sifatida yuborish uchun qayta o'qiymiz.
            if (payload.TryGetValue("reply_markup", out var rm) && rm is string rms) payload["reply_markup"] = JsonNode.Parse(rms);
            return await CallAsync(method, payload, ct);
        }
        await PaceAsync(ct);
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            foreach (var (k, v) in extra)
                if (v is not null) form.Add(new StringContent(v), k);
            var bytes = new ByteArrayContent(file.Bytes ?? []);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(bytes, field, file.FileName);
            using var resp = await Client().PostAsync($"https://api.telegram.org/bot{_token}/{method}", form, ct);
            return await ReadAsync(resp, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Olimpiada bot: {Method} xatosi: {Error}", method, ex.Message);
            return new TgResult(false, null, ex.Message, false, 0);
        }
    }

    /// <summary>Albom (2–10 rasm). Rasmlar bayt yoki file_id.</summary>
    public async Task<TgResult> SendMediaGroupAsync(long chatId, IReadOnlyList<TgFile> photos, string? captionHtml, CancellationToken ct)
    {
        if (!IsConfigured) return new TgResult(false, null, "Bot tokeni sozlanmagan", false, 0);
        await PaceAsync(ct);
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            var media = new JsonArray();
            for (var i = 0; i < photos.Count; i++)
            {
                var item = new JsonObject { ["type"] = "photo" };
                if (photos[i].FileId is { } fid) item["media"] = fid;
                else
                {
                    var name = $"p{i}";
                    item["media"] = $"attach://{name}";
                    var bc = new ByteArrayContent(photos[i].Bytes ?? []);
                    bc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(bc, name, photos[i].FileName);
                }
                if (i == 0 && !string.IsNullOrEmpty(captionHtml))
                {
                    item["caption"] = captionHtml;
                    item["parse_mode"] = "HTML";
                }
                media.Add(item);
            }
            form.Add(new StringContent(media.ToJsonString()), "media");
            using var resp = await Client().PostAsync($"https://api.telegram.org/bot{_token}/sendMediaGroup", form, ct);
            return await ReadAsync(resp, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new TgResult(false, null, ex.Message, false, 0);
        }
    }

    private static async Task<TgResult> ReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return new TgResult(true, root.TryGetProperty("result", out var r) ? r.Clone() : null, "", false, 0);
            var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : body;
            var retry = root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var ra)
                ? ra.GetInt32() : 0;
            return new TgResult(false, null, desc, resp.StatusCode == HttpStatusCode.Forbidden, retry);
        }
        catch (JsonException)
        {
            return new TgResult(false, null, $"HTTP {(int)resp.StatusCode}", resp.StatusCode == HttpStatusCode.Forbidden, 0);
        }
    }

    // ---------------- qisqa yordamchilar ----------------

    public Task<TgResult> SendTextAsync(long chatId, string html, object? replyMarkup, CancellationToken ct) =>
        CallAsync("sendMessage", Payload(chatId, html, replyMarkup), ct);

    public Task<TgResult> EditTextAsync(long chatId, long messageId, string html, object? replyMarkup, CancellationToken ct)
    {
        var p = Payload(chatId, html, replyMarkup);
        p["message_id"] = messageId;
        return CallAsync("editMessageText", p, ct);
    }

    public Task<TgResult> AnswerCallbackAsync(string id, string? text, CancellationToken ct) =>
        CallAsync("answerCallbackQuery", new Dictionary<string, object?> { ["callback_query_id"] = id, ["text"] = text }, ct);

    public Task<TgResult> GetUpdatesAsync(long offset, int timeout, CancellationToken ct) =>
        CallAsync("getUpdates", new Dictionary<string, object?>
        {
            ["offset"] = offset,
            ["timeout"] = timeout,
            ["allowed_updates"] = new[] { "message", "callback_query", "my_chat_member" },
        }, ct);

    /// <summary>Kanal a'zoligi: true — a'zo; false — a'zo emas; null — tekshirib bo'lmadi (bot admin emas).</summary>
    public async Task<(bool? Member, string Error)> IsMemberAsync(string chatRef, long userId, CancellationToken ct)
    {
        object chat = long.TryParse(chatRef, out var id) ? id : chatRef;
        var r = await CallAsync("getChatMember", new Dictionary<string, object?> { ["chat_id"] = chat, ["user_id"] = userId }, ct);
        if (!r.Ok) return (null, r.Error);
        var status = r.Result?.GetProperty("status").GetString();
        return (status is "member" or "administrator" or "creator" || (status == "restricted"
            && r.Result!.Value.TryGetProperty("is_member", out var im) && im.GetBoolean()), "");
    }

    private static Dictionary<string, object?> Payload(long chatId, string html, object? replyMarkup)
    {
        var p = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new { is_disabled = true },
        };
        if (replyMarkup is not null) p["reply_markup"] = replyMarkup;
        return p;
    }
}
