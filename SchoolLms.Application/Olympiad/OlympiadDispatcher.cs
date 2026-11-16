using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>
/// Rassilka auditoriyasi. Bo'sh filtr — botning hamma foydalanuvchilari. <see cref="Lead"/>: any | converted | not.
/// Bir akkauntga bir rassilka bir marta boradi (bir nechta bolasi bo'lsa ham).
/// </summary>
public sealed record OlympiadAudience(
    List<Guid>? EventIds = null, List<string>? Statuses = null, List<int>? Grades = null,
    List<string>? Schools = null, string? Lead = null)
{
    public static OlympiadAudience Parse(string? json)
    {
        try { return JsonSerializer.Deserialize<OlympiadAudience>(json ?? "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private bool HasKidFilter => EventIds is { Count: > 0 } || Statuses is { Count: > 0 } || Grades is { Count: > 0 }
                                 || Schools is { Count: > 0 } || Lead is "converted" or "not";

    public async Task<List<Guid>> ResolveAsync(IAppDbContext db, CancellationToken ct)
    {
        var users = db.OlympiadBotUsers.AsNoTracking().Where(u => !u.IsBlocked);
        if (!HasKidFilter) return await users.Select(u => u.Id).ToListAsync(ct);

        var kids = db.OlympiadParticipants.AsNoTracking().Where(p => p.BotUserId != null);
        if (Grades is { Count: > 0 } g) kids = kids.Where(p => g.Contains(p.Grade));
        if (Schools is { Count: > 0 } s) kids = kids.Where(p => s.Contains(p.School));
        if (Lead == "converted") kids = kids.Where(p => p.LeadId != null);
        if (Lead == "not") kids = kids.Where(p => p.LeadId == null);
        if (EventIds is { Count: > 0 } || Statuses is { Count: > 0 })
        {
            var regs = db.OlympiadRegistrations.AsNoTracking().AsQueryable();
            if (EventIds is { Count: > 0 } ev) regs = regs.Where(r => ev.Contains(r.EventId));
            if (Statuses is { Count: > 0 } st) regs = regs.Where(r => st.Contains(r.Status));
            else regs = regs.Where(r => r.Status != OlympiadRegStatus.Cancelled);
            kids = kids.Where(p => regs.Any(r => r.ParticipantId == p.Id));
        }
        return await (from u in users
                      where kids.Any(k => k.BotUserId == u.Id)
                      select u.Id).ToListAsync(ct);
    }
}

/// <summary>
/// Navbatni yuritadi: eslatma va rejalashtirilgan rassilkalarni navbatga qo'yadi, keyin kutayotgan
/// xabarlarni ~25 ta/soniya tezlikda yuboradi (429 da <c>retry_after</c> kutiladi). Har yetkazish holati
/// alohida saqlanadi; noyob kalit tufayli takror yuborilmaydi (idempotent).
/// </summary>
public sealed class OlympiadDispatcher(IAppDbContext db, OlympiadTelegram tg, OlympiadMessenger messenger, ILogger logger)
{
    /// <summary>Asosiy tezlik cheklovi OlympiadTelegram ichida (har so'rovga); bu — faqat navbat qatorlari orasidagi nafas.</summary>
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(5);

    public async Task TickAsync(CancellationToken ct)
    {
        if (!tg.IsConfigured) return;
        var now = AppClock.Now;
        await QueueDueRemindersAsync(now, ct);
        await QueueScheduledBroadcastsAsync(now, ct);
        await SendPendingAsync(ct);
    }

    // ---------------- Eslatmalar ----------------

    public static DateTime? DueAt(OlympiadReminder r, OlympiadEvent e) =>
        r.OffsetMinutes is { } off ? e.StartsAt.AddMinutes(-off) : r.SendAt;

    private async Task QueueDueRemindersAsync(DateTime now, CancellationToken ct)
    {
        var due = await (from r in db.OlympiadReminders
                         join e in db.OlympiadEvents on r.EventId equals e.Id
                         where r.Status == "scheduled"
                               && (e.Status == OlympiadEventStatus.Published || e.Status == OlympiadEventStatus.Finished)
                         select new { r, e }).ToListAsync(ct);
        foreach (var x in due)
        {
            if (DueAt(x.r, x.e) is not { } at || at > now) continue;
            // Eskirgan eslatma yuborilmaydi: tadbir boshlangan yoki vaqti 30 daqiqadan ko'proq o'tib ketgan
            // (masalan, tadbir 20 soat oldin e'lon qilingan — "ertaga" eslatmasi 3 soatligi bilan birga bormasin).
            if (x.r.OffsetMinutes is not null && (now >= x.e.StartsAt || now > at.AddMinutes(30)))
            {
                x.r.Status = "cancelled";
                continue;
            }
            var statuses = x.r.Target switch
            {
                OlympiadTarget.Waitlist => new[] { OlympiadRegStatus.Waitlist },
                OlympiadTarget.All => [OlympiadRegStatus.Registered, OlympiadRegStatus.Waitlist, OlympiadRegStatus.Attended],
                _ => [OlympiadRegStatus.Registered, OlympiadRegStatus.Attended],
            };
            var targets = await (from reg in db.OlympiadRegistrations
                                 join p in db.OlympiadParticipants on reg.ParticipantId equals p.Id
                                 where reg.EventId == x.e.Id && statuses.Contains(reg.Status) && p.BotUserId != null
                                 select new { reg.Id, BotUserId = p.BotUserId!.Value }).ToListAsync(ct);
            var count = await AddDeliveriesAsync(OlympiadDeliveryKind.Reminder, x.r.Id,
                targets.Select(t => (t.BotUserId, t.Id, "")), ct);
            x.r.Status = "sent";
            x.r.SentAt = now;
            x.r.SentCount = count;
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Rassilka ----------------

    private async Task QueueScheduledBroadcastsAsync(DateTime now, CancellationToken ct)
    {
        var due = await db.OlympiadBroadcasts
            .Where(b => b.Status == OlympiadBroadcastStatus.Scheduled && b.ScheduledAt <= now).ToListAsync(ct);
        foreach (var b in due) await StartBroadcastAsync(db, b, ct);
    }

    /// <summary>Rassilkani navbatga qo'yadi (darhol yuborish yoki rejalashtirilgan vaqt keldi).</summary>
    public static async Task StartBroadcastAsync(IAppDbContext db, OlympiadBroadcast b, CancellationToken ct)
    {
        var users = await OlympiadAudience.Parse(b.AudienceJson).ResolveAsync(db, ct);
        b.Status = OlympiadBroadcastStatus.Sending;
        b.StartedAt = AppClock.Now;
        b.Total = await AddDeliveriesAsync(db, OlympiadDeliveryKind.Broadcast, b.Id, users.Select(u => (u, Guid.Empty, "")), ct);
        await db.SaveChangesAsync(ct);
    }

    private Task<int> AddDeliveriesAsync(string kind, Guid refId, IEnumerable<(Guid BotUserId, Guid RegistrationId, string Tag)> items, CancellationToken ct) =>
        AddDeliveriesAsync(db, kind, refId, items, ct);

    /// <summary>Navbatga qo'shadi; mavjud (kind, ref, user, reg, tag) qatorlar o'tkazib yuboriladi. Qo'shilganlar soni.</summary>
    /// <param name="requeue">true — mavjud (xato, bloklangan, bekor qilingan yoki yuborilgan) qator qaytadan "pending"
    /// bo'ladi: natijani qayta yuborish yoki ball A→B→A tuzatilganda. Rassilka va eslatmada — false (bir marta).</param>
    public static async Task<int> AddDeliveriesAsync(IAppDbContext db, string kind, Guid refId,
        IEnumerable<(Guid BotUserId, Guid RegistrationId, string Tag)> items, CancellationToken ct, bool requeue = false)
    {
        var rows = await db.OlympiadDeliveries.AsNoTracking()
            .Where(d => d.Kind == kind && d.RefId == refId)
            .Select(d => new { d.Id, d.BotUserId, d.RegistrationId, d.Tag, d.Status }).ToListAsync(ct);
        var byKey = rows.ToDictionary(d => (d.BotUserId, d.RegistrationId, d.Tag));
        var existing = byKey.Keys.ToHashSet();
        var now = AppClock.Now;
        var added = 0;
        var revive = new List<long>();
        foreach (var it in items.Distinct())
        {
            if (!existing.Add(it))
            {
                if (requeue && byKey.TryGetValue(it, out var old) && old.Status != OlympiadDeliveryStatus.Pending) revive.Add(old.Id);
                continue;
            }
            db.OlympiadDeliveries.Add(new OlympiadDelivery
            {
                Kind = kind, RefId = refId, BotUserId = it.BotUserId, RegistrationId = it.RegistrationId, Tag = it.Tag,
                Status = OlympiadDeliveryStatus.Pending, CreatedAt = now,
            });
            added++;
        }
        if (revive.Count > 0)
            added += await db.OlympiadDeliveries.Where(d => revive.Contains(d.Id)).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, OlympiadDeliveryStatus.Pending).SetProperty(d => d.Attempts, 0)
                .SetProperty(d => d.Error, "").SetProperty(d => d.SentAt, (DateTime?)null), ct);
        return added;
    }

    // ---------------- Yuborish ----------------

    private async Task SendPendingAsync(CancellationToken ct)
    {
        var batch = await db.OlympiadDeliveries.Where(d => d.Status == OlympiadDeliveryStatus.Pending)
            .OrderBy(d => d.Id).Take(500).ToListAsync(ct);
        if (batch.Count == 0) { await FinishBroadcastsAsync(ct); return; }

        var userIds = batch.Select(d => d.BotUserId).Distinct().ToList();
        var users = await db.OlympiadBotUsers.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var broadcasts = new Dictionary<Guid, OlympiadBroadcast?>();
        var reminders = new Dictionary<Guid, (OlympiadReminder R, OlympiadEvent E)?>();

        foreach (var d in batch)
        {
            ct.ThrowIfCancellationRequested();
            // Navbat yuklangandan keyin CRM'da bekor qilingan bo'lishi mumkin — har yuborishdan oldin qayta tekshiramiz.
            var fresh = await db.OlympiadDeliveries.AsNoTracking().Where(x => x.Id == d.Id).Select(x => x.Status).FirstOrDefaultAsync(ct);
            if (fresh != OlympiadDeliveryStatus.Pending)
            {
                d.Status = fresh ?? OlympiadDeliveryStatus.Cancelled;
                continue;
            }
            if (!users.TryGetValue(d.BotUserId, out var user) || user.IsBlocked)
            {
                d.Status = OlympiadDeliveryStatus.Blocked;
                continue;
            }
            TgResult? res;
            try
            {
                res = d.Kind switch
                {
                    OlympiadDeliveryKind.Broadcast => await SendBroadcastAsync(d, user, broadcasts, ct),
                    OlympiadDeliveryKind.Reminder => await SendReminderAsync(d, user, reminders, ct),
                    OlympiadDeliveryKind.Result => await SendResultAsync(d, ct),
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Olimpiada: yetkazish {Id} xatosi", d.Id);
                res = new TgResult(false, null, ex.Message, false, 0);
            }

            d.Attempts++;
            if (res is null)
            {
                d.Status = OlympiadDeliveryStatus.Cancelled;
                d.Error = "Ma'lumot topilmadi";
            }
            else if (res.Ok)
            {
                d.Status = OlympiadDeliveryStatus.Sent;
                d.SentAt = AppClock.Now;
                d.Error = "";
            }
            else if (res.Blocked)
            {
                d.Status = OlympiadDeliveryStatus.Blocked;
                d.Error = Trim(res.Error);
                user.IsBlocked = true;
            }
            else if (res.RetryAfter > 0)
            {
                // Telegram tezlikni cheklagan — shu qatorni keyinroq qayta urinamiz.
                d.Attempts--;
                await db.SaveChangesAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(res.RetryAfter + 1), ct);
                return;
            }
            else
            {
                d.Error = Trim(res.Error);
                if (d.Attempts >= 3) d.Status = OlympiadDeliveryStatus.Failed;
            }
            await db.SaveChangesAsync(ct);
            await Task.Delay(Gap, ct);
        }
        await FinishBroadcastsAsync(ct);
    }

    private static string Trim(string s) => s.Length > 290 ? s[..290] : s;

    private async Task<TgResult?> SendBroadcastAsync(OlympiadDelivery d, OlympiadBotUser user,
        Dictionary<Guid, OlympiadBroadcast?> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(d.RefId, out var b))
            cache[d.RefId] = b = await db.OlympiadBroadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.RefId, ct);
        if (b is null || b.Status == OlympiadBroadcastStatus.Cancelled) return null;
        var html = OlympiadText.Render(b.Text, new Dictionary<string, string?> { ["ism"] = user.FirstName });
        return await messenger.SendAsync(user.TelegramUserId, html, OlympiadMessenger.ParseAttachments(b.AttachmentsJson),
            OlympiadMessenger.ParseButtons(b.ButtonsJson), ct);
    }

    private async Task<TgResult?> SendReminderAsync(OlympiadDelivery d, OlympiadBotUser user,
        Dictionary<Guid, (OlympiadReminder R, OlympiadEvent E)?> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(d.RefId, out var re))
        {
            var row = await (from r in db.OlympiadReminders.AsNoTracking()
                             join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                             where r.Id == d.RefId
                             select new { r, e }).FirstOrDefaultAsync(ct);
            cache[d.RefId] = re = row is null ? null : (row.r, row.e);
        }
        if (re is not { } x) return null;
        var reg = await db.OlympiadRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == d.RegistrationId, ct);
        // Bekor qilinganlarga eslatma bormaydi (navbatga qo'yilgandan keyin bekor qilgan bo'lsa ham).
        if (reg is null || reg.Status == OlympiadRegStatus.Cancelled) return null;
        var kid = await db.OlympiadParticipants.AsNoTracking().FirstAsync(p => p.Id == reg.ParticipantId, ct);
        var text = x.R.Text.Length > 0 || x.R.Kind == OlympiadReminderKind.Custom ? x.R.Text
            : await OlympiadText.TemplateAsync(db, x.R.Kind == OlympiadReminderKind.Standard2 ? "reminder_default_3h" : "reminder_default_1d", ct);
        var values = OlympiadText.RegValues(x.E, kid, reg);
        values["ism"] = user.FirstName;
        values["navbat"] = (await OlympiadRegistrar.WaitlistPositionAsync(db, reg, ct)).ToString(CultureInfo.InvariantCulture);
        return await messenger.SendAsync(user.TelegramUserId, OlympiadText.Render(text, values),
            OlympiadMessenger.ParseAttachments(x.R.AttachmentsJson), [], ct);
    }

    /// <summary>Tadbir o'rinlari — bitta yurishda bir marta (har natija uchun qayta hisoblanmasin).</summary>
    private readonly Dictionary<Guid, Dictionary<Guid, int>> _ranks = [];

    private async Task<TgResult?> SendResultAsync(OlympiadDelivery d, CancellationToken ct)
    {
        if (!_ranks.TryGetValue(d.RefId, out var ranks))
            _ranks[d.RefId] = ranks = await OlympiadResults.RanksAsync(db, d.RefId, ct);
        var res = await OlympiadResults.SendResultAsync(db, messenger, d.RegistrationId, ct, ranks: ranks);
        if (res is { Ok: true })
        {
            var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == d.RegistrationId, ct);
            if (reg is not null)
            {
                reg.ResultSentAt = AppClock.Now;
                reg.ResultSentScore = reg.Score;
            }
        }
        return res;
    }

    /// <summary>Rassilka hisoblagichlarini yangilaydi; kutayotgani qolmagan rassilka — "done".</summary>
    private async Task FinishBroadcastsAsync(CancellationToken ct)
    {
        var active = await db.OlympiadBroadcasts.Where(b => b.Status == OlympiadBroadcastStatus.Sending).ToListAsync(ct);
        foreach (var b in active)
        {
            var stats = await db.OlympiadDeliveries.AsNoTracking()
                .Where(d => d.Kind == OlympiadDeliveryKind.Broadcast && d.RefId == b.Id)
                .GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            int Of(string s) => stats.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
            b.Sent = Of(OlympiadDeliveryStatus.Sent);
            b.Failed = Of(OlympiadDeliveryStatus.Failed);
            b.Blocked = Of(OlympiadDeliveryStatus.Blocked);
            if (Of(OlympiadDeliveryStatus.Pending) == 0)
            {
                b.Status = OlympiadBroadcastStatus.Done;
                b.FinishedAt = AppClock.Now;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Har 20 soniyada navbatni yuritadi.</summary>
public sealed class OlympiadScheduler(IServiceProvider sp, ILogger<OlympiadScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var tg = scope.ServiceProvider.GetRequiredService<OlympiadTelegram>();
                var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
                await new OlympiadDispatcher(db, tg, new OlympiadMessenger(db, tg, env.ContentRootPath), logger).TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Olimpiada navbati xato bilan tugadi");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
