using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>
/// Olimpiada boti — long polling (maktab boti kabi; webhook uchun tashqi manzil kerak emas).
/// Token sozlanmagan bo'lsa kutadi; sozlamada token o'zgarsa o'zi qayta ulanadi.
/// </summary>
public sealed class OlympiadBotService(IServiceProvider sp, OlympiadTelegram tg, ILogger<OlympiadBotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        long offset = 0;
        var generation = -1;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!tg.IsConfigured)
            {
                // Sozlama boshqa nusxada saqlangan bo'lishi mumkin — vaqti-vaqti bilan bazadan qayta o'qiymiz.
                try
                {
                    using var scope = sp.CreateScope();
                    tg.Load(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
                }
                catch (Exception ex) { logger.LogDebug(ex, "Olimpiada bot: sozlama o'qilmadi"); }
                if (!tg.IsConfigured)
                {
                    try { await Task.Delay(10_000, stoppingToken); } catch (OperationCanceledException) { break; }
                    continue;
                }
            }
            if (generation != tg.Generation)
            {
                generation = tg.Generation;
                offset = 0;
                // Webhook o'rnatilgan bo'lsa getUpdates ishlamaydi — o'chiramiz.
                await tg.CallAsync("deleteWebhook", new { drop_pending_updates = false }, stoppingToken);
                logger.LogInformation("Olimpiada bot ishga tushdi (long polling).");
            }

            try
            {
                var r = await tg.GetUpdatesAsync(offset, 25, stoppingToken);
                if (!r.Ok || r.Result is not { ValueKind: JsonValueKind.Array } updates)
                {
                    await Task.Delay(r.RetryAfter > 0 ? r.RetryAfter * 1000 : 3000, stoppingToken);
                    continue;
                }
                var list = updates.EnumerateArray().ToList();
                if (list.Count == 0) continue;
                // Har foydalanuvchining xabarlari ketma-ket (FSM tartibi buzilmasin), turli foydalanuvchilar — parallel:
                // kanal posti ortidan yuzlab ota-ona kelganda bot "o'lik" bo'lib qolmasin.
                await Parallel.ForEachAsync(list.GroupBy(UserIdOf),
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = stoppingToken },
                    async (group, ct) =>
                    {
                        foreach (var upd in group) await ProcessAsync(upd, ct);
                    });
                offset = list[^1].GetProperty("update_id").GetInt64() + 1;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Olimpiada bot: getUpdates xatosi");
                try { await Task.Delay(3000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private static long UserIdOf(JsonElement upd)
    {
        foreach (var kind in new[] { "message", "callback_query", "my_chat_member" })
            if (upd.TryGetProperty(kind, out var x) && x.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var id))
                return id.GetInt64();
        return 0;
    }

    private async Task ProcessAsync(JsonElement upd, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            var handler = new OlympiadBotHandler(db, tg, new OlympiadMessenger(db, tg, env.ContentRootPath));
            await handler.HandleAsync(upd, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Olimpiada bot: update qayta ishlanmadi");
        }
    }
}

/// <summary>Suhbat holati (FSM) — <see cref="OlympiadBotUser.State"/> da JSON.</summary>
public sealed class BotState
{
    /// <summary>"" | name | phone | grade | school | confirm | edit_name | edit_phone | edit_grade | edit_school</summary>
    public string Step { get; set; } = "";
    public Guid? EventId { get; set; }
    public int RulesVersion { get; set; }
    public DateTime? RulesAcceptedAt { get; set; }
    /// <summary>Nizom kiritilmagan — tasdiq so'ralmadi, keyingi qadamga o'tildi.</summary>
    public bool RulesSkipped { get; set; }
    /// <summary>"Boshqa o'quvchi uchun" — bola tanlash o'tkazib yuboriladi, darhol yangi bola anketasi.</summary>
    public bool NewKid { get; set; }
    /// <summary>Tahrirlanayotgan bola.</summary>
    public Guid? ParticipantId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Grade { get; set; }
    public string School { get; set; } = "";
    /// <summary><c>/start ev_N</c> — obuna tekshiruvidan keyin shu tadbirga davom etish uchun.</summary>
    public int? PendingEventCode { get; set; }
}

/// <summary>Bitta update'ni qayta ishlaydi. Sof mantiq — testlarda to'g'ridan-to'g'ri chaqiriladi.</summary>
public sealed class OlympiadBotHandler(IAppDbContext db, OlympiadTelegram tg, OlympiadMessenger messenger)
{
    private OlympiadBotUser _user = null!;
    private BotState _state = new();
    private long _chat;
    private bool _hasGeneralRules;
    /// <summary>Akkauntdan kamida bitta bola yozilgan — menyuda "Boshqa o'quvchi uchun" tugmasi.</summary>
    private bool _hasRegistrations;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(JsonElement upd, CancellationToken ct)
    {
        if (upd.TryGetProperty("message", out var msg))
        {
            if (!msg.TryGetProperty("chat", out var chat) || chat.GetProperty("type").GetString() != "private") return;
            await LoadUserAsync(msg.GetProperty("from"), ct);
            await OnMessageAsync(msg, ct);
        }
        else if (upd.TryGetProperty("callback_query", out var cb))
        {
            await LoadUserAsync(cb.GetProperty("from"), ct);
            var data = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
            await tg.AnswerCallbackAsync(cb.GetProperty("id").GetString()!, null, ct);
            long? messageId = cb.TryGetProperty("message", out var m) && m.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : null;
            await OnCallbackAsync(data, messageId, ct);
        }
        else if (upd.TryGetProperty("my_chat_member", out var mcm))
        {
            // Foydalanuvchi botni bloklasa yoki qayta ochsa.
            if (mcm.GetProperty("chat").GetProperty("type").GetString() != "private") return;
            await LoadUserAsync(mcm.GetProperty("from"), ct);
            var status = mcm.GetProperty("new_chat_member").GetProperty("status").GetString();
            _user.IsBlocked = status == "kicked";
            await SaveAsync(ct);
        }
    }

    private async Task LoadUserAsync(JsonElement from, CancellationToken ct)
    {
        var id = from.GetProperty("id").GetInt64();
        _chat = id;
        var now = AppClock.Now;
        var existing = await db.OlympiadBotUsers.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        var isNew = existing is null;
        _user = existing ?? new OlympiadBotUser { TelegramUserId = id, CreatedAt = now };
        if (isNew) db.OlympiadBotUsers.Add(_user);
        _user.FirstName = Str(from, "first_name");
        _user.LastName = Str(from, "last_name");
        _user.Username = Str(from, "username");
        _user.LastSeenAt = now;
        try { _state = JsonSerializer.Deserialize<BotState>(_user.State.Length > 0 ? _user.State : "{}", Json) ?? new(); }
        catch (JsonException) { _state = new(); }
        // Umumiy nizom kiritilmagan bo'lsa, menyuda "Nizom" tugmasi ko'rinmaydi.
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        _hasGeneralRules = s is not null && RulesOf(s.RulesText, s.RulesFileUrl, s.RulesDisplay).Any;
        _hasRegistrations = !isNew && await (from r in db.OlympiadRegistrations.AsNoTracking()
                                             join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                                             where p.BotUserId == _user.Id && r.Status != OlympiadRegStatus.Cancelled
                                             select r.Id).AnyAsync(ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Ikki update bir vaqtda kelib, ikkalasi ham yangi qator yaratmoqchi bo'ldi.
            if (!isNew) throw;
            db.OlympiadBotUsers.Remove(_user); // Added holatidagini kuzatuvdan chiqaradi
            _user = await db.OlympiadBotUsers.FirstAsync(u => u.TelegramUserId == id, ct);
        }
    }

    private static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.GetString() ?? "" : "";

    private async Task SaveAsync(CancellationToken ct)
    {
        _user.State = JsonSerializer.Serialize(_state, Json);
        await db.SaveChangesAsync(ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        _state = new BotState();
        await SaveAsync(ct);
    }

    private Task<TgResult> SayAsync(string html, object? markup, CancellationToken ct) => tg.SendTextAsync(_chat, html, markup, ct);

    /// <summary>Shablon xabari — shablonga rasm/video biriktirilgan bo'lsa, ular bilan birga.</summary>
    private async Task<TgResult> SayTemplateAsync(string key, Dictionary<string, string?> values, object? markup, CancellationToken ct)
    {
        var (text, attachments) = await OlympiadText.TemplateWithMediaAsync(db, key, ct);
        var html = OlympiadText.Render(text, values);
        return attachments.Count == 0
            ? await SayAsync(html, markup, ct)
            : await messenger.SendAsync(_chat, html, attachments, [], ct, replyMarkup: markup);
    }

    /// <summary>Nizom ko'rsatiladigan qismlari. <c>Any</c> = false — nizom kiritilmagan, bosqich o'tkazib yuboriladi.</summary>
    private static (bool ShowText, bool ShowFile, bool Any) RulesOf(string text, string fileUrl, string display)
    {
        var showText = (display != OlympiadRulesDisplay.Pdf || fileUrl.Length == 0) && text.Trim().Length > 0;
        var showFile = display != OlympiadRulesDisplay.Text && fileUrl.Length > 0;
        return (showText, showFile, showText || showFile);
    }

    // ======================= Klaviaturalar =======================

    private object MainMenu => new
    {
        keyboard = new[]
        {
            new[] { new { text = _hasRegistrations ? OlympiadText.BtnRegisterAnother : OlympiadText.BtnRegister } },
            new[] { new { text = OlympiadText.BtnMyRegs }, new { text = OlympiadText.BtnKids } },
            _hasGeneralRules
                ? new[] { new { text = OlympiadText.BtnAsk }, new { text = OlympiadText.BtnRules } }
                : new[] { new { text = OlympiadText.BtnAsk } },
        },
        resize_keyboard = true,
        is_persistent = true,
    };

    private static object FormKeys => new
    {
        keyboard = new[] { new[] { new { text = OlympiadText.BtnBack }, new { text = OlympiadText.BtnCancel } } },
        resize_keyboard = true,
    };

    private static object PhoneKeys => new
    {
        keyboard = new object[]
        {
            new[] { new { text = OlympiadText.BtnSendPhone, request_contact = true } },
            new[] { new { text = OlympiadText.BtnBack, request_contact = false }, new { text = OlympiadText.BtnCancel, request_contact = false } },
        },
        resize_keyboard = true,
    };

    private static object Inline(params object[][] rows) => new { inline_keyboard = rows };
    private static object Btn(string text, string data) => new { text, callback_data = data };
    private static object UrlBtn(string text, string url) => new { text, url };

    // ======================= Xabarlar =======================

    private async Task OnMessageAsync(JsonElement msg, CancellationToken ct)
    {
        var text = msg.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";

        if (text.StartsWith("/start", StringComparison.Ordinal))
        {
            _user.IsBlocked = false;
            var arg = text.Length > 6 ? text[6..].Trim() : "";
            if (arg.StartsWith("src_", StringComparison.Ordinal) && _user.Source.Length == 0)
                _user.Source = arg[4..Math.Min(arg.Length, 64)];
            await ResetAsync(ct);
            if (arg.StartsWith("ev_", StringComparison.Ordinal)
                && int.TryParse(arg[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                // Obuna so'ralsa ham yo'qolmasin — "Tekshirish"dan keyin davom etadi.
                _state.PendingEventCode = code;
                await SaveAsync(ct);
            }
            if (!await EnsureSubscribedAsync(ct, force: true)) return;
            await WelcomeAndResumeAsync(ct);
            return;
        }

        if (!await EnsureSubscribedAsync(ct)) return;

        // Menyu tugmalari istalgan qadamda ishlaydi (anketani to'xtatadi).
        switch (text)
        {
            case OlympiadText.BtnRegister: await ResetAsync(ct); await JoinAsync(ct); return;
            case OlympiadText.BtnRegisterAnother: await RegisterAnotherAsync(ct); return;
            case OlympiadText.BtnMyRegs: await ResetAsync(ct); await ShowMyRegsAsync(ct); return;
            case OlympiadText.BtnKids: await ResetAsync(ct); await ShowKidsAsync(ct); return;
            case OlympiadText.BtnAsk: await ResetAsync(ct); await AskAdminAsync(ct); return;
            case OlympiadText.BtnRules: await ResetAsync(ct); await ShowRulesAsync(null, ct); return;
            case OlympiadText.BtnCancel:
                await ResetAsync(ct);
                await SayAsync("Bekor qilindi.", MainMenu, ct);
                return;
        }

        // Kontakt (telefon) — akkaunt egasining raqami ham saqlanadi.
        string? contactPhone = null;
        if (msg.TryGetProperty("contact", out var contact))
        {
            contactPhone = OlympiadText.NormalizePhone(Str(contact, "phone_number"));
            if (contact.TryGetProperty("user_id", out var cu) && cu.GetInt64() == _user.TelegramUserId && contactPhone is not null)
                _user.Phone = contactPhone;
        }

        switch (_state.Step)
        {
            case "name" or "edit_name":
                if (text == OlympiadText.BtnBack) { await BackFromAsync(ct); return; }
                if (OlympiadText.ValidateName(text) is { } nameErr) { await SayAsync(nameErr + "\n\nF.I.Sh ni qayta yozing:", FormKeys, ct); return; }
                _state.Name = OlympiadText.CleanSpaces(text);
                if (_state.Step == "edit_name") { await SaveEditAsync(ct); return; }
                await AskPhoneAsync(ct);
                return;
            case "phone" or "edit_phone":
                if (text == OlympiadText.BtnBack) { await BackFromAsync(ct); return; }
                var phone = contactPhone ?? OlympiadText.NormalizePhone(text);
                if (phone is null) { await SayAsync("Raqam noto'g'ri. Masalan: <b>+998901234567</b>\nQayta kiriting yoki tugmani bosing:", PhoneKeys, ct); return; }
                _state.Phone = phone;
                if (_state.Step == "edit_phone") { await SaveEditAsync(ct); return; }
                await AskGradeAsync(ct);
                return;
            case "grade" or "edit_grade":
                if (text == OlympiadText.BtnBack) { await BackFromAsync(ct); return; }
                await AskGradeAsync(ct, "Sinfni pastdagi tugmalardan tanlang 👇");
                return;
            case "school" or "edit_school":
                if (text == OlympiadText.BtnBack) { await BackFromAsync(ct); return; }
                if (OlympiadText.ValidateSchool(text) is { } schoolErr) { await SayAsync(schoolErr, FormKeys, ct); return; }
                _state.School = OlympiadText.CleanSpaces(text);
                if (_state.Step == "edit_school") { await SaveEditAsync(ct); return; }
                await ShowConfirmAsync(ct);
                return;
            case "confirm":
                if (text == OlympiadText.BtnBack) { await BackFromAsync(ct); return; }
                await ShowConfirmAsync(ct);
                return;
        }

        await SaveAsync(ct);
        await SayAsync("Menyudan tanlang 👇", MainMenu, ct);
    }

    // ======================= Tugmalar =======================

    private async Task OnCallbackAsync(string data, long? messageId, CancellationToken ct)
    {
        if (data == "chk")
        {
            if (await EnsureSubscribedAsync(ct, force: true))
            {
                await SayTemplateAsync("subscribe_ok", new Dictionary<string, string?>(), MainMenu, ct);
                await WelcomeAndResumeAsync(ct);
            }
            return;
        }
        if (!await EnsureSubscribedAsync(ct)) return;

        var (cmd, arg) = data.IndexOf(':') is var i and >= 0 ? (data[..i], data[(i + 1)..]) : (data, "");
        Guid.TryParseExact(arg, "N", out var id);
        switch (cmd)
        {
            case "join": await JoinAsync(ct); break;
            case "another": await RegisterAnotherAsync(ct); break;
            case "ev": await StartEventAsync(id, ct); break;
            case "rules": await AcceptRulesAsync(id, ct); break;
            case "kid": await PickKidAsync(id, ct); break;
            case "kidnew": await AskNameAsync(ct); break;
            case "grade":
                if (_state.Step is "grade" or "edit_grade" && int.TryParse(arg, out var g))
                {
                    _state.Grade = g;
                    if (_state.Step == "edit_grade") await SaveEditAsync(ct);
                    else await AskSchoolAsync(ct);
                }
                break;
            case "conf": await ConfirmAsync(ct); break;
            case "edit":
                if (_state.Step == "confirm") await AskNameAsync(ct, keep: true);
                break;
            case "qr": await ResendQrAsync(id, ct); break;
            case "rc":
                await SayAsync("Rostdan ham bekor qilasizmi?", Inline(
                    [Btn("✅ Ha, bekor qilish", "rcy:" + arg), Btn("Yo'q", "noop")]), ct);
                break;
            case "rcy": await CancelRegAsync(id, ct); break;
            case "res": await ResendResultAsync(id, ct); break;
            case "pe": await ShowKidEditAsync(id, ct); break;
            case "pf":
                // pf:{field}{participantN} — f: n|p|g|s
                if (arg.Length == 33 && Guid.TryParseExact(arg[1..], "N", out var pid)) await StartFieldEditAsync(arg[0], pid, ct);
                break;
            case "pd":
                await SayAsync("Bola ma'lumoti o'chirilsinmi?", Inline([Btn("🗑 Ha, o'chirish", "pdy:" + arg), Btn("Yo'q", "noop")]), ct);
                break;
            case "pdy": await DeleteKidAsync(id, ct); break;
        }
    }

    /// <summary>Salomlashish; deep link bilan kelgan bo'lsa — darhol shu tadbirga yozilish.</summary>
    private async Task WelcomeAndResumeAsync(CancellationToken ct)
    {
        // Salomlashish ostida "Qatnashish" tugmasi. Inline tugma bilan bitta xabarga reply-menyu sig'maydi —
        // menyu keyingi xabarlar bilan (tadbir tanlangach, ro'yxatdan o'tgach) chiqadi.
        await SayTemplateAsync("welcome", new Dictionary<string, string?> { ["ism"] = _user.FirstName },
            Inline([Btn(OlympiadText.BtnJoin, "join")]), ct);
        if (_state.PendingEventCode is { } code)
        {
            _state.PendingEventCode = null;
            await SaveAsync(ct);
            var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Code == code, ct);
            if (ev is not null) await StartEventAsync(ev.Id, ct);
        }
    }

    // ======================= Obuna =======================

    /// <summary>
    /// Majburiy obuna. Natija 5 daqiqa keshlanadi; "Tekshirish" va /start da qayta tekshiriladi.
    /// Bot admin bo'lmagan kanal o'tkazib yuboriladi (foydalanuvchi bloklanmaydi), xato sozlamada ko'rinadi.
    /// </summary>
    private async Task<bool> EnsureSubscribedAsync(CancellationToken ct, bool force = false)
    {
        var now = AppClock.Now;
        if (!force && _user.SubscribedUntil > now) return true;
        var channels = await db.OlympiadChannels.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct);
        if (channels.Count == 0) return true;

        var missing = new List<OlympiadChannel>();
        foreach (var c in channels)
        {
            var (member, error) = await tg.IsMemberAsync(c.ChatRef, _user.TelegramUserId, ct);
            if (member is null)
            {
                var msg = error.Contains("not found", StringComparison.OrdinalIgnoreCase) ? "Kanal topilmadi" : "Bot kanalda admin emas";
                if (c.LastError != msg) c.LastError = msg;
                continue;
            }
            if (c.LastError.Length > 0) c.LastError = "";
            if (member == false) missing.Add(c);
        }
        if (missing.Count == 0)
        {
            _user.SubscribedUntil = now.AddMinutes(5);
            await SaveAsync(ct);
            return true;
        }
        _user.SubscribedUntil = null;
        await SaveAsync(ct);
        var rows = missing.Select(c => new[] { UrlBtn("➕ " + c.Title, c.InviteLink.Length > 0 ? c.InviteLink : "https://t.me/" + c.ChatRef.TrimStart('@')) })
            .Append([Btn("✅ Tekshirish", "chk")]).ToArray();
        await SayTemplateAsync("subscribe_required", new Dictionary<string, string?>(),
            Inline(rows), ct);
        return false;
    }

    // ======================= Ro'yxatdan o'tish =======================

    private async Task<List<OlympiadEvent>> OpenEventsAsync(CancellationToken ct)
    {
        var now = AppClock.Now;
        return (await db.OlympiadEvents.AsNoTracking()
                .Where(e => e.Status == OlympiadEventStatus.Published && e.StartsAt > now)
                .OrderBy(e => e.StartsAt).ToListAsync(ct))
            .Where(e => OlympiadRegistrar.IsOpen(e, now)).ToList();
    }

    /// <summary>"Qatnashish" tugmasi: bitta ochiq tadbir bo'lsa — darhol unga, bir nechta bo'lsa — ro'yxat.</summary>
    private async Task JoinAsync(CancellationToken ct)
    {
        var events = await OpenEventsAsync(ct);
        if (events.Count == 1) { await StartEventAsync(events[0].Id, ct); return; }
        await ShowEventsAsync(ct, events);
    }

    /// <summary>"Boshqa o'quvchi uchun ro'yxatdan o'tish": tadbir (va nizom) → darhol yangi bola anketasi.</summary>
    private async Task RegisterAnotherAsync(CancellationToken ct)
    {
        _state = new BotState { NewKid = true };
        await SaveAsync(ct);
        await JoinAsync(ct);
    }

    private async Task ShowEventsAsync(CancellationToken ct, List<OlympiadEvent>? preloaded = null)
    {
        var events = preloaded ?? await OpenEventsAsync(ct);
        if (events.Count == 0)
        {
            await SayTemplateAsync("no_events", new(), MainMenu, ct);
            return;
        }
        foreach (var e in events)
        {
            var seats = e.Capacity is { } cap ? $"\n🪑 Bo'sh joy: <b>{Math.Max(0, cap - e.SeatsTaken)}</b> / {cap}" : "";
            var desc = e.Description.Length > 0 ? "\n\n" + TelegramHtml.FromMarkup(e.Description) : "";
            var html = $"🏆 <b>{TelegramHtml.Escape(e.Title)}</b>\n📅 {OlympiadText.FormatDate(e.Date)}, {e.StartTime:HH\\:mm}–{e.EndTime:HH\\:mm}\n📍 {TelegramHtml.Escape(e.Location)}{seats}{desc}";
            var rows = new List<object[]> { new[] { Btn("✍️ Ro'yxatdan o'tish", "ev:" + e.Id.ToString("N")) } };
            if (e.LocationMapUrl.StartsWith("http", StringComparison.Ordinal)) rows.Add([UrlBtn("🗺 Xaritada", e.LocationMapUrl)]);
            await SayAsync(html, Inline(rows.ToArray()), ct);
        }
    }

    private async Task<(OlympiadEvent? Event, string? Error)> OpenEventAsync(Guid id, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return (null, "Tadbir topilmadi.");
        if (!OlympiadRegistrar.IsOpen(ev, AppClock.Now))
            return (null, OlympiadText.Render(await OlympiadText.TemplateAsync(db, "registration_closed", ct),
                new Dictionary<string, string?> { ["tadbir"] = ev.Title }));
        return (ev, null);
    }

    private async Task StartEventAsync(Guid eventId, CancellationToken ct)
    {
        var (ev, err) = await OpenEventAsync(eventId, ct);
        if (ev is null) { await SayAsync(err!, MainMenu, ct); return; }
        _state = new BotState { EventId = ev.Id, NewKid = _state.NewKid };
        await SaveAsync(ct);
        await ShowRulesAsync(ev, ct);
    }

    /// <summary>Nizom: tadbirning o'zi bo'lsa shu, aks holda umumiy. <paramref name="ev"/> null — faqat ko'rish (menyudan).</summary>
    private async Task ShowRulesAsync(OlympiadEvent? ev, CancellationToken ct)
    {
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new OlympiadSettings();
        var own = ev is not null && (ev.RulesText.Length > 0 || ev.RulesFileUrl.Length > 0);
        var text = own ? ev!.RulesText : s.RulesText;
        var fileUrl = own ? ev!.RulesFileUrl : s.RulesFileUrl;
        var fileName = own ? ev!.RulesFileName : s.RulesFileName;
        var display = own ? ev!.RulesDisplay : s.RulesDisplay;
        var version = own ? ev!.RulesVersion : s.RulesVersion;

        var (showText, showFile, any) = RulesOf(text, fileUrl, display);
        var attachments = showFile ? new List<OlympiadAttachment> { new("document", fileUrl, fileName) } : [];

        if (ev is null)
        {
            // Nizom kiritilmagan — u haqida hech narsa demaymiz, menyuni yangilaymiz (eski tugma yo'qoladi).
            if (!any) { await SayAsync("Menyudan tanlang 👇", MainMenu, ct); return; }
            var body = showText ? TelegramHtml.FromMarkup(text) : "📜 Nizom";
            await messenger.SendAsync(_chat, body, attachments, [], ct, replyMarkup: MainMenu);
            return;
        }

        if (!any)
        {
            // Nizom yo'q — tasdiq so'ramasdan keyingi qadamga.
            _state.RulesSkipped = true;
            _state.RulesVersion = 0;
            await SaveAsync(ct);
            await ShowKidPickerAsync(ct);
            return;
        }

        _state.RulesVersion = version;
        await SaveAsync(ct);
        var accept = Inline([Btn("✅ Nizom bilan tanishib chiqdim", "rules:" + ev.Id.ToString("N"))]);
        if (showText)
        {
            foreach (var part in OlympiadMessenger.Split(TelegramHtml.FromMarkup(text)))
                await SayAsync(part, null, ct);
        }
        if (showFile) await messenger.SendAsync(_chat, "", attachments, [], ct);
        await SayTemplateAsync("rules_prompt", new Dictionary<string, string?> { ["tadbir"] = ev.Title }, accept, ct);
    }

    private async Task AcceptRulesAsync(Guid eventId, CancellationToken ct)
    {
        if (_state.EventId != eventId) { await StartEventAsync(eventId, ct); return; }
        _state.RulesAcceptedAt = AppClock.Now;
        await SaveAsync(ct);
        await ShowKidPickerAsync(ct);
    }

    private async Task<List<OlympiadParticipant>> KidsAsync(CancellationToken ct) =>
        await db.OlympiadParticipants.Where(p => p.BotUserId == _user.Id && !p.IsHidden).OrderBy(p => p.CreatedAt).ToListAsync(ct);

    private async Task ShowKidPickerAsync(CancellationToken ct)
    {
        List<OlympiadParticipant> kids = _state.NewKid ? [] : await KidsAsync(ct);
        if (kids.Count == 0) { await AskNameAsync(ct); return; }
        var rows = kids.Select(k => new[] { Btn($"👤 {k.FullName} ({k.Grade}-sinf)", "kid:" + k.Id.ToString("N")) })
            .Append([Btn("➕ Yangi bola", "kidnew")]).ToArray();
        await SayAsync("Kimni ro'yxatdan o'tkazamiz?", Inline(rows), ct);
    }

    private async Task PickKidAsync(Guid participantId, CancellationToken ct)
    {
        if (_state.EventId is not { } eventId || (_state.RulesAcceptedAt is null && !_state.RulesSkipped)) { await SayAsync("Avval tadbirni tanlang.", MainMenu, ct); return; }
        var kid = await db.OlympiadParticipants.FirstOrDefaultAsync(p => p.Id == participantId && p.BotUserId == _user.Id, ct);
        if (kid is null) return;
        await FinishRegistrationAsync(eventId, kid, ct);
    }

    private async Task AskNameAsync(CancellationToken ct, bool keep = false)
    {
        if (_state.EventId is null) { await SayAsync("Avval tadbirni tanlang.", MainMenu, ct); return; }
        _state.Step = "name";
        if (!keep) { _state.Name = ""; _state.Phone = ""; _state.Grade = 0; _state.School = ""; }
        await SaveAsync(ct);
        var cur = keep && _state.Name.Length > 0 ? $"\n(hozirgi: <b>{TelegramHtml.Escape(_state.Name)}</b>)" : "";
        await SayAsync("👤 Bolaning <b>familiyasi va ismi</b>ni yozing.\nMasalan: <i>Karimov Aziz</i>" + cur, FormKeys, ct);
    }

    private async Task AskPhoneAsync(CancellationToken ct)
    {
        _state.Step = "phone";
        await SaveAsync(ct);
        await SayAsync("📱 Telefon raqamini yuboring — tugmani bosing yoki yozing.\nMasalan: <b>+998901234567</b>", PhoneKeys, ct);
    }

    private async Task AskGradeAsync(CancellationToken ct, string? hint = null)
    {
        if (_state.Step is not ("grade" or "edit_grade")) _state.Step = "grade";
        await SaveAsync(ct);
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new OlympiadSettings();
        var grades = Enumerable.Range(s.GradeFrom, Math.Max(1, s.GradeTo - s.GradeFrom + 1)).ToList();
        var rows = grades.Chunk(4).Select(ch => ch.Select(g => Btn($"{g}-sinf", "grade:" + g)).ToArray()).ToArray();
        await SayAsync("🏫", FormKeys, ct);
        await SayAsync(hint ?? "Bola nechanchi sinfda o'qiydi?", Inline(rows), ct);
    }

    private async Task AskSchoolAsync(CancellationToken ct)
    {
        _state.Step = "school";
        await SaveAsync(ct);
        await SayAsync("🏫 Qaysi maktabda o'qiydi?\nMasalan: <i>Yaypan 5-maktab</i>", FormKeys, ct);
    }

    private async Task ShowConfirmAsync(CancellationToken ct)
    {
        _state.Step = "confirm";
        await SaveAsync(ct);
        var html = "Ma'lumotlarni tekshiring:\n\n"
                   + $"👤 <b>{TelegramHtml.Escape(_state.Name)}</b>\n📱 {_state.Phone}\n🎓 {_state.Grade}-sinf\n🏫 {TelegramHtml.Escape(_state.School)}";
        await SayAsync(html, Inline([Btn("✅ Tasdiqlash", "conf"), Btn("✏️ Tahrirlash", "edit")]), ct);
    }

    private async Task BackFromAsync(CancellationToken ct)
    {
        switch (_state.Step)
        {
            case "name": await ShowKidPickerOrMenuAsync(ct); break;
            case "phone": await AskNameAsync(ct, keep: true); break;
            case "grade": await AskPhoneAsync(ct); break;
            case "school": await AskGradeAsync(ct); break;
            case "confirm": await AskSchoolAsync(ct); break;
            default:
                // Tahrirlash qadamlari — bola kartasiga qaytamiz.
                var pid = _state.ParticipantId;
                await ResetAsync(ct);
                if (pid is { } p) await ShowKidEditAsync(p, ct);
                else await SayAsync("Menyudan tanlang 👇", MainMenu, ct);
                break;
        }
    }

    private async Task ShowKidPickerOrMenuAsync(CancellationToken ct)
    {
        if (!_state.NewKid && (await KidsAsync(ct)).Count > 0) { _state.Step = ""; await SaveAsync(ct); await ShowKidPickerAsync(ct); }
        else { await ResetAsync(ct); await SayAsync("Bekor qilindi.", MainMenu, ct); }
    }

    private async Task ConfirmAsync(CancellationToken ct)
    {
        if (_state.Step != "confirm" || _state.EventId is not { } eventId) return;
        var now = AppClock.Now;
        var kid = new OlympiadParticipant
        {
            BotUserId = _user.Id,
            FullName = _state.Name,
            Phone = _state.Phone,
            Grade = _state.Grade,
            School = _state.School,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.OlympiadParticipants.Add(kid);
        await db.SaveChangesAsync(ct);
        await FinishRegistrationAsync(eventId, kid, ct);
    }

    private async Task FinishRegistrationAsync(Guid eventId, OlympiadParticipant kid, CancellationToken ct)
    {
        var (ev, err) = await OpenEventAsync(eventId, ct);
        if (ev is null) { await ResetAsync(ct); await SayAsync(err!, MainMenu, ct); return; }
        var outcome = await OlympiadRegistrar.RegisterAsync(db, eventId, kid.Id, _state.RulesVersion,
            _state.RulesSkipped ? null : _state.RulesAcceptedAt ?? AppClock.Now, "bot", false, ct);
        await ResetAsync(ct);
        if (outcome.Kind is RegisterKind.Registered or RegisterKind.Waitlisted) _hasRegistrations = true;
        switch (outcome.Kind)
        {
            case RegisterKind.Registered:
                await OlympiadRegistrar.SendTicketAsync(db, messenger, outcome.Registration!.Id, "registered", ct, MainMenu);
                await OfferAnotherAsync(ct);
                break;
            case RegisterKind.Waitlisted:
                await SayTemplateAsync("waitlisted", new Dictionary<string, string?>
                {
                    ["bola"] = kid.FullName, ["tadbir"] = ev.Title, ["navbat"] = outcome.WaitlistPosition.ToString(CultureInfo.InvariantCulture),
                }, MainMenu, ct);
                await OfferAnotherAsync(ct);
                break;
            case RegisterKind.Full:
                await SayTemplateAsync("event_full",
                    new Dictionary<string, string?> { ["tadbir"] = ev.Title }, MainMenu, ct);
                break;
            case RegisterKind.Duplicate:
                await SayAsync($"ℹ️ <b>{TelegramHtml.Escape(kid.FullName)}</b> bu tadbirga allaqachon yozilgan. «{OlympiadText.BtnMyRegs}» bo'limida ko'rasiz.", MainMenu, ct);
                break;
            default:
                await SayTemplateAsync("registration_closed",
                    new Dictionary<string, string?> { ["tadbir"] = ev.Title }, MainMenu, ct);
                break;
        }
    }

    /// <summary>Muvaffaqiyatli yozilgandan keyin — boshqa o'quvchini ham yozishni taklif qiladi.</summary>
    private Task<TgResult> OfferAnotherAsync(CancellationToken ct) =>
        SayAsync("👨‍👩‍👧‍👦 Boshqa o'quvchi uchun ham ro'yxatdan o'tishni davom ettirasizmi?",
            Inline([Btn("✅ Ha, davom etish", "another"), Btn("Yo'q, rahmat", "noop")]), ct);

    // ======================= Mening yozuvlarim =======================

    private static readonly Dictionary<string, string> StatusLabel = new()
    {
        [OlympiadRegStatus.Registered] = "✅ Ro'yxatda",
        [OlympiadRegStatus.Waitlist] = "⏳ Kutish ro'yxatida",
        [OlympiadRegStatus.Attended] = "🟢 Qatnashdi",
        [OlympiadRegStatus.NoShow] = "⚪️ Kelmadi",
        [OlympiadRegStatus.Cancelled] = "❌ Bekor qilingan",
    };

    private async Task ShowMyRegsAsync(CancellationToken ct)
    {
        var rows = await (from r in db.OlympiadRegistrations.AsNoTracking()
                          join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                          join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                          where p.BotUserId == _user.Id && r.Status != OlympiadRegStatus.Cancelled
                                && e.Status != OlympiadEventStatus.Archived
                          orderby e.StartsAt descending
                          select new { r, p, e }).Take(20).ToListAsync(ct);
        if (rows.Count == 0) { await SayAsync("Hozircha yozuvlar yo'q. «" + OlympiadText.BtnRegister + "» tugmasini bosing.", MainMenu, ct); return; }
        var now = AppClock.Now;
        foreach (var x in rows)
        {
            var position = await OlympiadRegistrar.WaitlistPositionAsync(db, x.r, ct);
            var html = $"🏆 <b>{TelegramHtml.Escape(x.e.Title)}</b>\n📅 {OlympiadText.FormatDate(x.e.Date)}, {x.e.StartTime:HH\\:mm}\n"
                       + $"👤 {TelegramHtml.Escape(x.p.FullName)}\n🔢 {OlympiadText.Number(x.e.Code, x.r.Number)}\n"
                       + StatusLabel.GetValueOrDefault(x.r.Status, x.r.Status) + (position > 0 ? $" (navbat: {position})" : "");
            var buttons = new List<object[]>();
            var id = x.r.Id.ToString("N");
            var upcoming = now < x.e.StartsAt;
            if (x.r.Status == OlympiadRegStatus.Registered) buttons.Add([Btn("🔳 QR kodni olish", "qr:" + id)]);
            if (upcoming && x.r.Status is OlympiadRegStatus.Registered or OlympiadRegStatus.Waitlist)
                buttons.Add([Btn("✏️ Ma'lumotni tahrirlash", "pe:" + x.p.Id.ToString("N")), Btn("❌ Bekor qilish", "rc:" + id)]);
            if (x.r.ResultSentAt is not null) buttons.Add([Btn("📊 Natija va sertifikat", "res:" + id)]);
            await SayAsync(html, buttons.Count > 0 ? Inline(buttons.ToArray()) : null, ct);
        }
    }

    private async Task<OlympiadRegistration?> OwnRegAsync(Guid regId, CancellationToken ct) =>
        await (from r in db.OlympiadRegistrations
               join p in db.OlympiadParticipants on r.ParticipantId equals p.Id
               where r.Id == regId && p.BotUserId == _user.Id
               select r).FirstOrDefaultAsync(ct);

    private async Task ResendQrAsync(Guid regId, CancellationToken ct)
    {
        var reg = await OwnRegAsync(regId, ct);
        if (reg is null || reg.Status != OlympiadRegStatus.Registered) return;
        await OlympiadRegistrar.SendTicketAsync(db, messenger, reg.Id, "registered", ct, MainMenu);
    }

    private async Task CancelRegAsync(Guid regId, CancellationToken ct)
    {
        var reg = await OwnRegAsync(regId, ct);
        if (reg is null) return;
        var ev = await db.OlympiadEvents.AsNoTracking().FirstAsync(e => e.Id == reg.EventId, ct);
        if (AppClock.Now >= ev.StartsAt) { await SayAsync("Tadbir boshlangan — bekor qilib bo'lmaydi.", MainMenu, ct); return; }
        var kid = await db.OlympiadParticipants.AsNoTracking().FirstAsync(p => p.Id == reg.ParticipantId, ct);
        var (cancelled, promoted) = await OlympiadRegistrar.CancelAsync(db, reg.Id, ct, onlyActive: true);
        if (cancelled is null) return;
        await SayTemplateAsync("cancelled",
            new Dictionary<string, string?> { ["bola"] = kid.FullName, ["tadbir"] = ev.Title }, MainMenu, ct);
        foreach (var p in promoted)
            await OlympiadRegistrar.SendTicketAsync(db, messenger, p.Id, "promoted_from_waitlist", ct);
    }

    private async Task ResendResultAsync(Guid regId, CancellationToken ct)
    {
        var reg = await OwnRegAsync(regId, ct);
        if (reg is null || reg.ResultSentAt is null) return;
        await OlympiadResults.SendResultAsync(db, messenger, reg.Id, ct);
    }

    // ======================= Farzandlarim =======================

    private async Task ShowKidsAsync(CancellationToken ct)
    {
        var kids = await KidsAsync(ct);
        if (kids.Count == 0) { await SayAsync("Saqlangan bolalar yo'q. Ro'yxatdan o'tishda qo'shiladi.", MainMenu, ct); return; }
        foreach (var k in kids)
        {
            var html = $"👤 <b>{TelegramHtml.Escape(k.FullName)}</b>\n📱 {k.Phone}\n🎓 {k.Grade}-sinf\n🏫 {TelegramHtml.Escape(k.School)}";
            var id = k.Id.ToString("N");
            await SayAsync(html, Inline([Btn("✏️ Tahrirlash", "pe:" + id), Btn("🗑 O'chirish", "pd:" + id)]), ct);
        }
    }

    private async Task ShowKidEditAsync(Guid participantId, CancellationToken ct)
    {
        var k = await db.OlympiadParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == participantId && p.BotUserId == _user.Id, ct);
        if (k is null) return;
        var id = k.Id.ToString("N");
        var html = $"✏️ Nimani o'zgartiramiz?\n\n👤 <b>{TelegramHtml.Escape(k.FullName)}</b>\n📱 {k.Phone}\n🎓 {k.Grade}-sinf\n🏫 {TelegramHtml.Escape(k.School)}";
        await SayAsync(html, Inline(
            [Btn("👤 F.I.Sh", "pf:n" + id), Btn("📱 Telefon", "pf:p" + id)],
            [Btn("🎓 Sinf", "pf:g" + id), Btn("🏫 Maktab", "pf:s" + id)]), ct);
    }

    private async Task<bool> KidEditableAsync(Guid participantId, CancellationToken ct)
    {
        // Natija e'lon qilingan yoki tadbir boshlangan yozuvi bo'lsa — sertifikat/ro'yxat o'zgarib ketmasin.
        var now = AppClock.Now;
        return !await (from r in db.OlympiadRegistrations
                       join e in db.OlympiadEvents on r.EventId equals e.Id
                       where r.ParticipantId == participantId && r.Status != OlympiadRegStatus.Cancelled
                             && (e.StartsAt <= now || r.ResultSentAt != null)
                       select r.Id).AnyAsync(ct);
    }

    private async Task StartFieldEditAsync(char field, Guid participantId, CancellationToken ct)
    {
        var k = await db.OlympiadParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == participantId && p.BotUserId == _user.Id, ct);
        if (k is null) return;
        if (!await KidEditableAsync(k.Id, ct))
        {
            await SayAsync("Bu bola qatnashgan tadbir boshlangan — ma'lumotni faqat administrator o'zgartira oladi.", MainMenu, ct);
            return;
        }
        _state = new BotState { ParticipantId = k.Id, Name = k.FullName, Phone = k.Phone, Grade = k.Grade, School = k.School };
        switch (field)
        {
            case 'n': _state.Step = "edit_name"; await SaveAsync(ct); await SayAsync("Yangi F.I.Sh ni yozing:", FormKeys, ct); break;
            case 'p': _state.Step = "edit_phone"; await SaveAsync(ct); await SayAsync("Yangi telefon raqamini yuboring:", PhoneKeys, ct); break;
            case 'g': _state.Step = "edit_grade"; await AskGradeAsync(ct); break;
            case 's': _state.Step = "edit_school"; await SaveAsync(ct); await SayAsync("Maktab nomini yozing:", FormKeys, ct); break;
        }
    }

    private async Task SaveEditAsync(CancellationToken ct)
    {
        if (_state.ParticipantId is not { } pid) { await ResetAsync(ct); return; }
        var k = await db.OlympiadParticipants.FirstOrDefaultAsync(p => p.Id == pid && p.BotUserId == _user.Id, ct);
        if (k is not null)
        {
            k.FullName = _state.Name;
            k.Phone = _state.Phone;
            k.Grade = _state.Grade;
            k.School = _state.School;
            k.UpdatedAt = AppClock.Now;
        }
        await ResetAsync(ct);
        await SayAsync("✅ Saqlandi.", MainMenu, ct);
        if (k is not null) await ShowKidEditAsync(k.Id, ct);
    }

    private async Task DeleteKidAsync(Guid participantId, CancellationToken ct)
    {
        var k = await db.OlympiadParticipants.FirstOrDefaultAsync(p => p.Id == participantId && p.BotUserId == _user.Id, ct);
        if (k is null) return;
        var active = await db.OlympiadRegistrations.AnyAsync(r => r.ParticipantId == k.Id
            && (r.Status == OlympiadRegStatus.Registered || r.Status == OlympiadRegStatus.Waitlist), ct);
        if (active) { await SayAsync("Bu bolaning faol yozuvi bor — avval «" + OlympiadText.BtnMyRegs + "»da bekor qiling.", MainMenu, ct); return; }
        k.IsHidden = true;
        k.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        await SayAsync("🗑 O'chirildi.", MainMenu, ct);
    }

    // ======================= Adminga savol =======================

    private async Task AskAdminAsync(CancellationToken ct)
    {
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var link = s?.AdminLink ?? "";
        await SayTemplateAsync("ask_admin", new Dictionary<string, string?> { ["admin_link"] = link },
            link.StartsWith("http", StringComparison.Ordinal) ? Inline([UrlBtn("✉️ Administratorga yozish", link)]) : MainMenu, ct);
    }
}
