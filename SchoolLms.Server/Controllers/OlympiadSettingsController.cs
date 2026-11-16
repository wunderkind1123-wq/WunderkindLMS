using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>Olimpiada → sozlamalar: bot tokeni, majburiy kanallar, nizom, eslatmalar, natija, shablonlar, media yuklash.</summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Manage, GatedRead = true)]
[Route("api/admin/olympiad/settings")]
public sealed class OlympiadSettingsController(AppDbContext db, AuditService audit, OlympiadTelegram tg, IWebHostEnvironment env) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";
    private ObjectResult Error(int status, string message) => StatusCode(status, new { message });

    public const string NoTelegramMessage =
        "Sizning Telegram akkauntingiz tizimga ulanmagan. Maktab botida panelga kiring, so'ng olimpiada botiga /start bosing.";

    public static string TestFailMessage(string? error) =>
        error?.Contains("chat not found", StringComparison.OrdinalIgnoreCase) == true
            ? "Avval olimpiada botiga /start bosing — bot sizga birinchi bo'lib yoza olmaydi."
            : $"Yuborilmadi: {error}";

    /// <summary>Joriy xodimning Telegram ID'si (maktab botida ulangan akkaunt). Shaxsiy chat ID = foydalanuvchi ID.</summary>
    public static async Task<long?> MyTelegramIdAsync(AppDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var uid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return null;
        return await db.TelegramAccounts.AsNoTracking().Where(a => a.UserId == uid)
            .Select(a => (long?)a.TelegramUserId).FirstOrDefaultAsync(ct);
    }

    internal static async Task<OlympiadSettings> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var s = await db.OlympiadSettings.FirstOrDefaultAsync(ct);
        if (s is not null) return s;
        s = new OlympiadSettings();
        db.OlympiadSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>Token hech qachon to'liq qaytarilmaydi: "123456789:••••••Ab3d".</summary>
    private static string Mask(string token)
    {
        if (token.Length <= 10) return token.Length == 0 ? "" : "••••";
        var colon = token.IndexOf(':');
        return (colon > 0 ? token[..colon] : token[..4]) + ":••••••" + token[^4..];
    }

    private static OlympiadSettingsDto ToDto(OlympiadSettings s) => new(
        s.BotToken.Length > 0, Mask(s.BotToken), s.BotUsername, s.BotName,
        s.GradeFrom, s.GradeTo, s.DefaultOnFull,
        s.RulesText, s.RulesFileUrl, s.RulesFileName, s.RulesDisplay, s.RulesVersion,
        s.Reminder1Enabled, s.Reminder1Minutes, s.Reminder2Enabled, s.Reminder2Minutes,
        s.ResultShowScore, s.ResultShowRank, s.ResultShowCertificate, s.ResultShowInvite,
        s.InviteText, s.InviteUrl, s.CertificateFormat, s.AdminLink, s.UpdatedAt);

    [HttpGet]
    public async Task<OlympiadSettingsDto> Get(CancellationToken ct) => ToDto(await LoadAsync(db, ct));

    [HttpPut]
    public async Task<ActionResult<OlympiadSettingsDto>> Save(OlympiadSettingsInput i, CancellationToken ct)
    {
        if (i.GradeFrom < 1 || i.GradeTo > 11 || i.GradeFrom > i.GradeTo) return BadRequest(new { message = "Sinf oralig'i 1–11 ichida bo'lsin" });
        if (i.Reminder1Minutes < 1 || i.Reminder2Minutes < 1) return BadRequest(new { message = "Eslatma vaqti noto'g'ri" });
        if (!string.IsNullOrWhiteSpace(i.AdminLink) && !i.AdminLink.Trim().StartsWith("https://", StringComparison.Ordinal))
            return BadRequest(new { message = "Admin havolasi https://t.me/... ko'rinishida bo'lsin" });
        if (!string.IsNullOrWhiteSpace(i.InviteUrl) && !i.InviteUrl.Trim().StartsWith("http", StringComparison.Ordinal))
            return BadRequest(new { message = "Taklif havolasi http(s):// bilan boshlansin" });
        var s = await LoadAsync(db, ct);
        if (s.RulesText != (i.RulesText ?? "") || s.RulesFileUrl != (i.RulesFileUrl ?? "")) s.RulesVersion++;
        s.GradeFrom = i.GradeFrom;
        s.GradeTo = i.GradeTo;
        s.DefaultOnFull = i.DefaultOnFull == OlympiadOnFull.Close ? OlympiadOnFull.Close : OlympiadOnFull.Waitlist;
        s.RulesText = i.RulesText ?? "";
        s.RulesFileUrl = i.RulesFileUrl ?? "";
        s.RulesFileName = i.RulesFileName ?? "";
        s.RulesDisplay = i.RulesDisplay is OlympiadRulesDisplay.Pdf or OlympiadRulesDisplay.Both ? i.RulesDisplay : OlympiadRulesDisplay.Text;
        s.Reminder1Enabled = i.Reminder1Enabled;
        s.Reminder1Minutes = i.Reminder1Minutes;
        s.Reminder2Enabled = i.Reminder2Enabled;
        s.Reminder2Minutes = i.Reminder2Minutes;
        s.ResultShowScore = i.ResultShowScore;
        s.ResultShowRank = i.ResultShowRank;
        s.ResultShowCertificate = i.ResultShowCertificate;
        s.ResultShowInvite = i.ResultShowInvite;
        s.InviteText = (i.InviteText ?? "").Trim();
        s.InviteUrl = (i.InviteUrl ?? "").Trim();
        s.CertificateFormat = i.CertificateFormat == "png" ? "png" : "pdf";
        s.AdminLink = (i.AdminLink ?? "").Trim();
        s.UpdatedAt = AppClock.Now;
        s.UpdatedBy = ActorName;
        audit.Record("olympiad_settings", "1", "update", "Olimpiada sozlamalari saqlandi");
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    /// <summary>Tokenni saqlash — avval <c>getMe</c> bilan tekshiriladi. Bo'sh token — botni o'chirish.</summary>
    [HttpPut("token")]
    public async Task<ActionResult<OlympiadSettingsDto>> SaveToken(BotTokenInput input, CancellationToken ct)
    {
        var token = (input.Token ?? "").Trim();
        var s = await LoadAsync(db, ct);
        if (token.Length > 0)
        {
            var me = await tg.CallAsync("getMe", new { }, ct, token);
            if (!me.Ok) return BadRequest(new { message = "Token noto'g'ri yoki Telegram javob bermadi: " + me.Error });
            s.BotUsername = me.Result?.GetProperty("username").GetString() ?? "";
            s.BotName = me.Result?.GetProperty("first_name").GetString() ?? "";
            // Maktab boti bilan bir xil bo'lmasin — ikkala xizmat bir-birining yangilanishlarini yeb qo'yadi.
            var schoolToken = await db.SchoolMeta.IgnoreQueryFilters().Select(m => m.TelegramBotToken).FirstOrDefaultAsync(t => t != "", ct);
            if (schoolToken == token) return BadRequest(new { message = "Bu maktabning asosiy boti. Olimpiada uchun @BotFather'da yangi bot yarating." });
        }
        else
        {
            s.BotUsername = "";
            s.BotName = "";
        }
        s.BotToken = token;
        s.UpdatedAt = AppClock.Now;
        s.UpdatedBy = ActorName;
        audit.Record("olympiad_settings", "1", "token", token.Length > 0 ? $"Olimpiada boti ulandi: @{s.BotUsername}" : "Olimpiada boti uzildi");
        await db.SaveChangesAsync(ct);
        tg.Set(s.BotToken, s.BotUsername);
        return ToDto(s);
    }

    /// <summary>"Ulanishni tekshirish" — getMe va webhook holati.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        if (!tg.IsConfigured) return Error(409, "Bot tokeni kiritilmagan");
        var me = await tg.CallAsync("getMe", new { }, ct);
        var hook = await tg.CallAsync("getWebhookInfo", new { }, ct);
        return Ok(new
        {
            ok = me.Ok,
            error = me.Ok ? null : me.Error,
            username = me.Result?.GetProperty("username").GetString(),
            name = me.Result?.GetProperty("first_name").GetString(),
            mode = "long polling",
            pendingUpdates = hook.Result is { } h && h.TryGetProperty("pending_update_count", out var p) ? p.GetInt32() : 0,
            botUsers = await db.OlympiadBotUsers.CountAsync(ct),
            blocked = await db.OlympiadBotUsers.CountAsync(u => u.IsBlocked, ct),
        });
    }

    // ======================= Kanallar =======================

    private static OlympiadChannelDto ToDto(OlympiadChannel c) => new(c.Id, c.Title, c.ChatRef, c.InviteLink, c.SortOrder, c.IsActive, c.LastError);

    [HttpGet("channels")]
    public async Task<List<OlympiadChannelDto>> Channels(CancellationToken ct) =>
        (await db.OlympiadChannels.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct)).Select(ToDto).ToList();

    /// <summary>Kanalni tekshiradi: bot admin bo'lishi shart (aks holda a'zolikni tekshirib bo'lmaydi).</summary>
    private async Task<string> ProbeChannelAsync(string chatRef, CancellationToken ct)
    {
        if (!tg.IsConfigured) return "Bot tokeni kiritilmagan — tekshirilmadi";
        object chat = long.TryParse(chatRef, out var id) ? id : chatRef;
        var me = await tg.CallAsync("getMe", new { }, ct);
        if (!me.Ok) return "Bot javob bermadi";
        var botId = me.Result!.Value.GetProperty("id").GetInt64();
        var m = await tg.CallAsync("getChatMember", new Dictionary<string, object?> { ["chat_id"] = chat, ["user_id"] = botId }, ct);
        if (!m.Ok) return m.Error.Contains("not found", StringComparison.OrdinalIgnoreCase) ? "Kanal topilmadi" : "Bot kanalda admin emas";
        var status = m.Result?.GetProperty("status").GetString();
        return status is "administrator" or "creator" ? "" : "Bot kanalda admin emas";
    }

    private static string? Validate(OlympiadChannelInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Title)) return "Kanal nomini kiriting";
        var r = (i.ChatRef ?? "").Trim();
        if (!(r.StartsWith('@') && r.Length > 3) && !(r.StartsWith("-100", StringComparison.Ordinal) && long.TryParse(r, out _)))
            return "Kanal: @username yoki -100… ID";
        if (!r.StartsWith('@') && string.IsNullOrWhiteSpace(i.InviteLink)) return "Yopiq kanal uchun taklif havolasini kiriting";
        // Noto'g'ri URL bo'lsa Telegram butun obuna xabarini rad etadi — foydalanuvchi javobsiz qoladi.
        if (!string.IsNullOrWhiteSpace(i.InviteLink) && !i.InviteLink.Trim().StartsWith("https://", StringComparison.Ordinal))
            return "Taklif havolasi https://t.me/... ko'rinishida bo'lsin";
        return null;
    }

    [HttpPost("channels")]
    public async Task<ActionResult<OlympiadChannelDto>> AddChannel(OlympiadChannelInput i, CancellationToken ct)
    {
        if (Validate(i) is { } err) return BadRequest(new { message = err });
        var c = new OlympiadChannel
        {
            Title = i.Title.Trim(), ChatRef = i.ChatRef.Trim(), InviteLink = (i.InviteLink ?? "").Trim(), SortOrder = i.SortOrder, IsActive = i.IsActive,
        };
        c.LastError = await ProbeChannelAsync(c.ChatRef, ct);
        db.OlympiadChannels.Add(c);
        audit.Record("olympiad_settings", c.Id.ToString(), "channel_add", $"Olimpiada majburiy kanali qo'shildi: {c.Title}");
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    [HttpPut("channels/{id:guid}")]
    public async Task<ActionResult<OlympiadChannelDto>> UpdateChannel(Guid id, OlympiadChannelInput i, CancellationToken ct)
    {
        var c = await db.OlympiadChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Kanal topilmadi" });
        if (Validate(i) is { } err) return BadRequest(new { message = err });
        c.Title = i.Title.Trim();
        c.ChatRef = i.ChatRef.Trim();
        c.InviteLink = (i.InviteLink ?? "").Trim();
        c.SortOrder = i.SortOrder;
        c.IsActive = i.IsActive;
        c.LastError = await ProbeChannelAsync(c.ChatRef, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    [HttpDelete("channels/{id:guid}")]
    public async Task<IActionResult> DeleteChannel(Guid id, CancellationToken ct)
    {
        var c = await db.OlympiadChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Kanal topilmadi" });
        db.OlympiadChannels.Remove(c);
        audit.Record("olympiad_settings", id.ToString(), "channel_delete", $"Olimpiada majburiy kanali o'chirildi: {c.Title}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ======================= Shablonlar =======================

    [HttpGet("templates")]
    public async Task<List<OlympiadTemplateDto>> Templates(CancellationToken ct)
    {
        var rows = await db.OlympiadTemplates.AsNoTracking().ToDictionaryAsync(t => t.Key, ct);
        return OlympiadText.Templates.Select(d =>
        {
            var custom = rows.TryGetValue(d.Key, out var r) && r.Text.Length > 0;
            return new OlympiadTemplateDto(d.Key, d.Title, d.Tokens, custom ? r!.Text : d.Default, d.Default, custom);
        }).ToList();
    }

    /// <summary>Shablonni saqlash. Bo'sh matn — standartga qaytarish.</summary>
    [HttpPut("templates/{key}")]
    public async Task<IActionResult> SaveTemplate(string key, TemplateInput input, CancellationToken ct)
    {
        if (OlympiadText.Def(key) is null) return NotFound(new { message = "Bunday shablon yo'q" });
        var text = input.Text ?? "";
        if (text.Length > 4000) return BadRequest(new { message = "Matn 4000 belgidan oshmasin" });
        var row = await db.OlympiadTemplates.FirstOrDefaultAsync(t => t.Key == key, ct);
        if (row is null) db.OlympiadTemplates.Add(row = new OlympiadTemplate { Key = key });
        row.Text = text.Trim() == OlympiadText.Def(key)!.Default.Trim() ? "" : text;
        row.UpdatedAt = AppClock.Now;
        audit.Record("olympiad_settings", key, "template", $"Olimpiada bot shabloni: {key}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Matnni joriy xodimning Telegramiga sinov uchun yuborish (shablon, eslatma, rassilka).</summary>
    [HttpPost("test-message")]
    public async Task<IActionResult> TestMessage(TestMessageInput input, CancellationToken ct)
    {
        if (!tg.IsConfigured) return Error(409, "Bot tokeni kiritilmagan");
        var chat = await MyTelegramIdAsync(db, User, ct);
        if (chat is null) return Error(409, NoTelegramMessage);
        var html = OlympiadText.Render(input.Text ?? "", new Dictionary<string, string?> { ["ism"] = User.FindFirst(ClaimTypes.Name)?.Value });
        var res = await new OlympiadMessenger(db, tg, env.ContentRootPath)
            .SendAsync(chat.Value, html, input.Attachments ?? [], input.Buttons ?? [], ct);
        return res.Ok ? NoContent() : Error(502, TestFailMessage(res.Error));
    }

    // ======================= Media yuklash =======================

    private static readonly Dictionary<string, (string Kind, long Max)> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ("photo", 10_000_000), [".jpeg"] = ("photo", 10_000_000), [".png"] = ("photo", 10_000_000), [".webp"] = ("photo", 10_000_000),
        [".mp4"] = ("video", 50_000_000), [".mov"] = ("video", 50_000_000),
        [".pdf"] = ("document", 50_000_000), [".doc"] = ("document", 50_000_000), [".docx"] = ("document", 50_000_000),
        [".xls"] = ("document", 50_000_000), [".xlsx"] = ("document", 50_000_000), [".ppt"] = ("document", 50_000_000),
        [".pptx"] = ("document", 50_000_000), [".zip"] = ("document", 50_000_000), [".txt"] = ("document", 50_000_000),
    };

    /// <summary>
    /// Fayl yuklash: rasm ≤ 10 MB, video va fayl ≤ 50 MB. <paramref name="asKind"/>=video_note — dumaloq video
    /// (kvadrat MP4, ≤ 1 daqiqa). Qaytaradi: biriktirma (kind, url, name).
    /// </summary>
    [HttpPost("/api/admin/olympiad/media")]
    [RequestSizeLimit(52_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_000_000)]
    public async Task<ActionResult<OlympiadAttachment>> Upload(IFormFile? file, [FromQuery] string? asKind, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl tanlanmagan" });
        var ext = Path.GetExtension(file.FileName);
        if (!MediaTypes.TryGetValue(ext, out var t)) return BadRequest(new { message = $"{ext} turidagi fayl qabul qilinmaydi" });
        if (file.Length > t.Max) return BadRequest(new { message = $"Fayl {t.Max / 1_000_000} MB dan katta" });
        var kind = asKind switch
        {
            "video_note" when t.Kind == "video" && ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) => "video_note",
            "document" => "document",
            _ => t.Kind,
        };
        var dir = Path.Combine(env.ContentRootPath, "uploads", "olympiad");
        Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, stored)))
            await file.CopyToAsync(fs, ct);
        var name = Path.GetFileName(file.FileName);
        if (name.Length > 120) name = name[^120..];
        return new OlympiadAttachment(kind, $"/uploads/olympiad/{stored}", name);
    }
}
