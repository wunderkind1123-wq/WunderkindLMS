using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Kunlik hisobot — sending (client, 2026-11-16: "har kuni darslardan so'ng soat 17:00 da telegram
//  botga pdf file holatda"). Telegram is the only channel (CLAUDE.md).
//
//  WHO GETS IT: every account holding the `dailyReport` key (Boshqaruv → Rollar), on each Telegram
//  account linked to it — the chats the red-list digest uses. No key, no chat — nothing is sent.
//
//  WHEN: school days (not Sunday, not a holiday) from SendAt on. Once a day: the send is recorded in
//  the audit log (entity DailyReport, id = the day), and a day with that row is done — a restart after
//  17:00 does not send twice. A day nobody could receive (no chats) is not recorded, so a key given at
//  17:30 still gets that day's report on the next tick. After midnight the day is gone, not caught up.
// ===========================================================================

/// <summary>Builds and sends the daily report. See the file header.</summary>
public sealed class DailyDirectorReportService(IAppDbContext db, TelegramService telegram, ILogger<DailyDirectorReportService> log)
{
    /// <summary>The key (adminPermissions) that puts an account on the list.</summary>
    public const string PermissionKey = "dailyReport";

    /// <summary>audit_logs.entity_type of a day's send.</summary>
    public const string AuditEntity = "DailyReport";

    /// <summary>School time the report goes out.</summary>
    public static readonly TimeOnly SendAt = new(17, 0);

    /// <summary>The report's PDF for a day.</summary>
    public async Task<(byte[] Pdf, string FileName, DailyDirectorReportModel Model)> PdfAsync(DateOnly day, CancellationToken ct = default)
    {
        var model = await DailyDirectorReport.BuildAsync(db, day, ct);
        return (new DailyDirectorReportDocument(model).Render(), DailyDirectorReportDocument.FileName(model), model);
    }

    /// <summary>Telegram chats of every account with the key.</summary>
    public async Task<List<long>> ChatsAsync(CancellationToken ct = default)
    {
        var holders = await db.Users.AsNoTracking()
            .Where(u => u.Permissions.Contains(PermissionKey))
            .Select(u => u.Id).ToListAsync(ct);
        if (holders.Count == 0) return [];
        return await db.TelegramAccounts.AsNoTracking()
            .Where(a => holders.Contains(a.UserId) && a.TelegramUserId != 0)
            .Select(a => a.TelegramUserId).Distinct().ToListAsync(ct);
    }

    /// <summary>Sends <paramref name="day"/>'s report if it is due and not sent yet; null — nothing was due.</summary>
    public async Task<int?> RunDueAsync(DateTime localNow, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(localNow);
        if (TimeOnly.FromDateTime(localNow) < SendAt) return null;
        if (!telegram.IsConfigured) return null;
        if (!await DailyDirectorReport.IsSchoolDayAsync(db, day, ct)) return null;
        if (await SentAsync(day, ct)) return null;
        return await SendAsync(day, actorId: null, ct);
    }

    /// <summary>Was <paramref name="day"/>'s report already sent.</summary>
    public Task<bool> SentAsync(DateOnly day, CancellationToken ct = default)
    {
        var id = Iso(day);
        return db.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityType == AuditEntity && a.EntityId == id, ct);
    }

    /// <summary>
    /// Builds the report and sends it to every chat on the list; records the send when at least one
    /// chat got it. Returns the chats reached. <paramref name="actorId"/> — a manual send from the panel.
    /// </summary>
    public async Task<int> SendAsync(DateOnly day, string? actorId, CancellationToken ct = default)
    {
        var chats = await ChatsAsync(ct);
        if (chats.Count == 0)
        {
            log.LogInformation("Kunlik hisobot: {Day} — `{Key}` kaliti bor va Telegram ulangan hisob yo'q", day, PermissionKey);
            return 0;
        }

        var (pdf, fileName, model) = await PdfAsync(day, ct);
        var caption = Caption(model);
        var sent = 0;
        foreach (var chat in chats)
            if (await telegram.SendDocumentHtmlAsync(chat, pdf, fileName, caption, ct)) sent++;

        if (sent > 0)
        {
            db.AuditLogs.Add(AuditService.Entry(
                AuditEntity, Iso(day), "send",
                $"Kunlik hisobot yuborildi: {DailyDirectorReport.Label(day)} — {sent}/{chats.Count} chat",
                actorId: actorId));
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Kunlik hisobot: {Day} — chat={Sent}/{Total}", day, sent, chats.Count);
        return sent;
    }

    /// <summary>The document's caption — the day in three lines, the details in the PDF.</summary>
    public static string Caption(DailyDirectorReportModel m)
    {
        var lines = new List<string>
        {
            $"📊 <b>Kunlik o'quv hisoboti</b> — {WebUtility.HtmlEncode(DailyDirectorReport.Label(m.Date))}",
            "",
            m.Planned == 0
                ? "📚 Bugun jadvalda dars yo'q"
                : $"📚 Darslar: {m.Planned} tadan {m.Conducted} tasi o'tildi"
                  + (m.Missed + m.Marked > 0 ? $", <b>{m.Missed + m.Marked} tasi o'tilmagan</b>" : ""),
            m.GradeCount == 0
                ? "📝 Baholar: bugun qo'yilmagan"
                : $"📝 Baholar: {m.GradeCount} ta, o'rtacha {WeeklyReport.Num(m.GradeAverage!.Value)}",
            m.Game.Count == 0
                ? "🎮 Gamifikatsiya: bugun ball qo'yilmagan"
                : $"🎮 Gamifikatsiya: {m.Game.Count} ta yozuv, +{m.GamePlus} / −{m.GameMinus} ball",
        };
        return string.Join('\n', lines);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
}

/// <summary>Ticks every few minutes and sends the daily report when it is due (DailyDirectorReportService.RunDueAsync).</summary>
public sealed class DailyDirectorReportScheduler(IServiceProvider services, ILogger<DailyDirectorReportScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (migrations, the bot) before the first tick.
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var service = new DailyDirectorReportService(
                    scope.ServiceProvider.GetRequiredService<IAppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<TelegramService>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<DailyDirectorReportService>>());
                await service.RunDueAsync(AppClock.Now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Kunlik hisobot yuborilmadi");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
