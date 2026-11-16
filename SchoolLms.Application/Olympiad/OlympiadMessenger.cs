using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>Biriktirma: photo | document | video | video_note. <see cref="Url"/> — <c>/uploads/olympiad/…</c>.</summary>
public sealed record OlympiadAttachment(string Kind, string Url, string Name);

/// <summary>Inline URL tugma.</summary>
public sealed record OlympiadButton(string Text, string Url);

/// <summary>
/// Matn + biriktirmalar + tugmalardan iborat bitta "xabar"ni Telegram cheklovlariga moslab yuboradi:
/// caption 1024 belgi, matn 4096 belgi, albom 10 rasm, dumaloq videoga matn qo'shib bo'lmaydi
/// (matn alohida keyin). Yuklangan fayllarning <c>file_id</c>'si keshlanadi.
/// </summary>
public sealed class OlympiadMessenger(IAppDbContext db, OlympiadTelegram tg, string contentRoot)
{
    public const int CaptionLimit = 1024;
    public const int TextLimit = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<OlympiadAttachment> ParseAttachments(string? json)
    {
        try { return JsonSerializer.Deserialize<List<OlympiadAttachment>>(json ?? "[]", Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static List<OlympiadButton> ParseButtons(string? json)
    {
        try { return JsonSerializer.Deserialize<List<OlympiadButton>>(json ?? "[]", Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static object? Keyboard(IReadOnlyList<OlympiadButton> buttons) =>
        buttons.Count == 0 ? null : new { inline_keyboard = buttons.Select(b => new[] { new { text = b.Text, url = b.Url } }).ToArray() };

    /// <summary>Faylning diskdagi yo'li (faqat <c>/uploads/</c> ichida — yo'l aylanishi yo'q).</summary>
    public string? PathOf(string url)
    {
        if (!url.StartsWith("/uploads/", StringComparison.Ordinal) || url.Contains("..", StringComparison.Ordinal)) return null;
        var p = System.IO.Path.Combine(contentRoot, url.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar));
        return File.Exists(p) ? p : null;
    }

    private async Task<TgFile?> FileOfAsync(OlympiadAttachment a, CancellationToken ct)
    {
        var cached = await db.OlympiadMediaCache.AsNoTracking().FirstOrDefaultAsync(c => c.Path == a.Url, ct);
        if (cached is not null) return new TgFile(a.Name, null, cached.FileId);
        var path = PathOf(a.Url);
        if (path is null) return null;
        return new TgFile(string.IsNullOrWhiteSpace(a.Name) ? System.IO.Path.GetFileName(path) : a.Name,
            await File.ReadAllBytesAsync(path, ct), null);
    }

    private async Task RememberAsync(string url, string? fileId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fileId)) return;
        if (await db.OlympiadMediaCache.AnyAsync(c => c.Path == url, ct)) return;
        var row = new OlympiadMediaCache { Path = url, FileId = fileId, CreatedAt = AppClock.Now };
        db.OlympiadMediaCache.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Parallel yozildi — muhim emas. Lekin Added qator kuzatuvda qolsa, keyingi har SaveChanges yiqiladi.
            db.OlympiadMediaCache.Remove(row);
        }
    }

    private static string? FileIdOf(JsonElement? result, string kind)
    {
        if (result is not { ValueKind: JsonValueKind.Object } r) return null;
        switch (kind)
        {
            case "photo":
                if (r.TryGetProperty("photo", out var ph) && ph.ValueKind == JsonValueKind.Array && ph.GetArrayLength() > 0)
                    return ph[ph.GetArrayLength() - 1].GetProperty("file_id").GetString();
                return null;
            default:
                return r.TryGetProperty(kind, out var el) && el.TryGetProperty("file_id", out var id) ? id.GetString() : null;
        }
    }

    private static (string Method, string Field) MethodOf(string kind) => kind switch
    {
        "photo" => ("sendPhoto", "photo"),
        "video" => ("sendVideo", "video"),
        "video_note" => ("sendVideoNote", "video_note"),
        _ => ("sendDocument", "document"),
    };

    /// <summary>Teglarsiz uzunlik — Telegram cheklovlari ko'rinadigan belgilarga qo'llanadi.</summary>
    public static int VisibleLength(string html) => TelegramHtml.ToPlain(html).Length;

    /// <summary>
    /// Uzun matnni qatorlar bo'yicha bo'laklarga bo'ladi (har biri ≤ 4096). Juda uzun bitta qator ham bo'linadi.
    /// Har bo'lak <see cref="TelegramHtml.Sanitize"/> dan o'tadi — bo'linish teg ichiga tushsa ham teglar
    /// muvozanatlanadi (aks holda Telegram "can't parse entities" bilan rad etardi).
    /// </summary>
    public static List<string> Split(string html)
    {
        const int budget = TextLimit - 200;
        if (VisibleLength(html) <= TextLimit) return [html];
        var lines = new List<string>();
        foreach (var line in html.Split('\n'))
        {
            var l = line;
            while (l.Length > budget)
            {
                var cut = l.LastIndexOf(' ', budget);
                if (cut < budget / 2) cut = budget;
                // Teg ichida kesmaymiz.
                var lt = l.LastIndexOf('<', cut);
                if (lt > l.LastIndexOf('>', cut)) cut = lt > 0 ? lt : cut;
                lines.Add(l[..cut]);
                l = l[cut..].TrimStart();
            }
            lines.Add(l);
        }
        var parts = new List<string>();
        var cur = "";
        foreach (var line in lines)
        {
            if (cur.Length > 0 && VisibleLength(cur + "\n" + line) > budget)
            {
                parts.Add(cur);
                cur = line;
            }
            else cur = cur.Length == 0 ? line : cur + "\n" + line;
        }
        if (cur.Length > 0) parts.Add(cur);
        return parts.Select(TelegramHtml.Sanitize).Where(p => p.Trim().Length > 0).ToList();
    }

    /// <summary>
    /// Bitta xabar. Qaytaradi: oxirgi so'rov natijasi; <see cref="TgResult.Blocked"/> — foydalanuvchi
    /// botni bloklagan. <paramref name="extraFiles"/> — yaratilgan fayllar (QR, sertifikat) keshsiz.
    /// </summary>
    public async Task<TgResult> SendAsync(long chatId, string html, IReadOnlyList<OlympiadAttachment> attachments,
        IReadOnlyList<OlympiadButton> buttons, CancellationToken ct,
        IReadOnlyList<(string Kind, TgFile File)>? extraFiles = null, object? replyMarkup = null)
    {
        var markup = replyMarkup ?? Keyboard(buttons);
        var files = new List<(string Kind, TgFile File, string? Url)>();
        foreach (var a in attachments)
            if (await FileOfAsync(a, ct) is { } f) files.Add((a.Kind, f, a.Url));
        foreach (var (k, f) in extraFiles ?? []) files.Add((k, f, null));

        var hasText = TelegramHtml.ToPlain(html).Trim().Length > 0;
        var textUsed = false;
        TgResult last = new(true, null, "", false, 0);

        // 1) Bitta fayl (dumaloq videodan boshqa) va qisqa matn — caption + tugmalar bitta xabarda.
        if (files.Count == 1 && files[0].Kind != "video_note" && VisibleLength(html) <= CaptionLimit)
        {
            var (method, field) = MethodOf(files[0].Kind);
            last = await tg.SendFileAsync(method, field, chatId, files[0].File, new()
            {
                ["caption"] = hasText ? html : null,
                ["parse_mode"] = hasText ? "HTML" : null,
                ["reply_markup"] = markup is null ? null : JsonSerializer.Serialize(markup),
            }, ct);
            if (last.Ok && files[0].Url is { } u) await RememberAsync(u, FileIdOf(last.Result, files[0].Kind), ct);
            return last;
        }

        // 2) Faqat rasmlar (2–10) — albom. Caption faqat tugma bo'lmasa (albomga tugma qo'shib bo'lmaydi).
        if (files.Count >= 2 && files.All(f => f.Kind == "photo"))
        {
            var captionHere = hasText && markup is null && VisibleLength(html) <= CaptionLimit;
            foreach (var chunk in files.Chunk(10))
            {
                last = await tg.SendMediaGroupAsync(chatId, chunk.Select(c => c.File).ToList(), captionHere && !textUsed ? html : null, ct);
                if (!last.Ok) return last;
                if (captionHere) textUsed = true;
                if (last.Result is { ValueKind: JsonValueKind.Array } arr)
                    for (var i = 0; i < arr.GetArrayLength() && i < chunk.Length; i++)
                        if (chunk[i].Url is { } u) await RememberAsync(u, FileIdOf(arr[i], "photo"), ct);
            }
        }
        else
        {
            // 3) Har bir fayl alohida, matn oxirida.
            foreach (var f in files)
            {
                var (method, field) = MethodOf(f.Kind);
                last = await tg.SendFileAsync(method, field, chatId, f.File, new(), ct);
                if (!last.Ok) return last;
                if (f.Url is { } u) await RememberAsync(u, FileIdOf(last.Result, f.Kind), ct);
            }
        }

        if (!textUsed && (hasText || markup is not null))
        {
            var parts = Split(hasText ? html : "👇");
            for (var i = 0; i < parts.Count; i++)
            {
                last = await tg.SendTextAsync(chatId, parts[i], i == parts.Count - 1 ? markup : null, ct);
                if (!last.Ok) return last;
            }
        }
        return last;
    }
}
