using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

public static class RegisterKind
{
    public const string Registered = "registered";
    public const string Waitlisted = "waitlisted";
    public const string Full = "full";
    public const string Closed = "closed";
    public const string Duplicate = "duplicate";
}

public sealed record RegisterOutcome(string Kind, OlympiadRegistration? Registration, int WaitlistPosition = 0);

/// <summary>
/// Ro'yxatdan o'tish, bekor qilish va kutish ro'yxati. Joy hisobi poyga holatisiz: o'rin
/// <c>UPDATE … WHERE seats_taken &lt; capacity</c> bilan atomar egallanadi (PostgreSQL qatorni qulflab,
/// shartni qayta tekshiradi) — ikki kishi bir vaqtda oxirgi joyni ololmaydi.
/// </summary>
public static class OlympiadRegistrar
{
    /// <summary>Botda ko'rinadimi: e'lon qilingan va ro'yxat oynasi ichida.</summary>
    public static bool IsOpen(OlympiadEvent e, DateTime now) =>
        e.Status == OlympiadEventStatus.Published
        && (e.RegistrationOpensAt is null || now >= e.RegistrationOpensAt)
        && now < (e.RegistrationClosesAt ?? e.StartsAt);

    /// <summary>Joy egallash. <paramref name="force"/> — limitga qaramay (admin qo'lda o'tkazadi).</summary>
    private static async Task<int?> ClaimSeatAsync(IAppDbContext db, Guid eventId, bool force, CancellationToken ct)
    {
        var q = db.OlympiadEvents.Where(e => e.Id == eventId);
        if (!force) q = q.Where(e => e.Capacity == null || e.SeatsTaken < e.Capacity);
        var n = await q.ExecuteUpdateAsync(s => s
            .SetProperty(e => e.SeatsTaken, e => e.SeatsTaken + 1)
            .SetProperty(e => e.LastNumber, e => e.LastNumber + 1), ct);
        if (n == 0) return null;
        return await db.OlympiadEvents.Where(e => e.Id == eventId).Select(e => e.LastNumber).FirstAsync(ct);
    }

    private static async Task ReleaseSeatAsync(IAppDbContext db, Guid eventId, CancellationToken ct) =>
        await db.OlympiadEvents.Where(e => e.Id == eventId && e.SeatsTaken > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.SeatsTaken, e => e.SeatsTaken - 1), ct);

    public static async Task<int> WaitlistPositionAsync(IAppDbContext db, OlympiadRegistration r, CancellationToken ct) =>
        r.Status != OlympiadRegStatus.Waitlist ? 0
            : await db.OlympiadRegistrations.CountAsync(x => x.EventId == r.EventId
                && x.Status == OlympiadRegStatus.Waitlist && x.WaitlistSeq <= r.WaitlistSeq, ct);

    /// <summary>
    /// Bolani tadbirga yozadi. <paramref name="ignoreWindow"/> — CRM'dan qo'lda qo'shishda muddat tekshirilmaydi.
    /// </summary>
    public static async Task<RegisterOutcome> RegisterAsync(IAppDbContext db, Guid eventId, Guid participantId,
        int rulesVersion, DateTime? rulesAcceptedAt, string via, bool ignoreWindow, CancellationToken ct)
    {
        var now = AppClock.Now;
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || ev.Status is OlympiadEventStatus.Archived or OlympiadEventStatus.Finished)
            return new(RegisterKind.Closed, null);
        if (!ignoreWindow && !IsOpen(ev, now)) return new(RegisterKind.Closed, null);

        await using var tx = await db.BeginTransactionAsync(ct);
        // Kuzatuvsiz o'qiymiz: mavjud (bekor qilingan) qator faqat shartli UPDATE bilan qayta tiriladi.
        var reg = await db.OlympiadRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.ParticipantId == participantId, ct);
        if (reg is not null && reg.Status != OlympiadRegStatus.Cancelled)
            return new(RegisterKind.Duplicate, reg, await WaitlistPositionAsync(db, reg, ct));

        var number = await ClaimSeatAsync(db, eventId, force: false, ct);
        int waitSeq = 0;
        if (number is null)
        {
            if (ev.OnFull != OlympiadOnFull.Waitlist) return new(RegisterKind.Full, null);
            await db.OlympiadEvents.Where(e => e.Id == eventId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastWaitlistSeq, e => e.LastWaitlistSeq + 1), ct);
            waitSeq = await db.OlympiadEvents.Where(e => e.Id == eventId).Select(e => e.LastWaitlistSeq).FirstAsync(ct);
        }

        var status = number is null ? OlympiadRegStatus.Waitlist : OlympiadRegStatus.Registered;
        var num = number ?? 0;
        var token = OlympiadText.NewQrToken();
        if (reg is not null)
        {
            // Qayta yozilish: faqat hali ham "cancelled" bo'lsa (ikki parallel urinishdan bittasi o'tadi).
            var n = await db.OlympiadRegistrations
                .Where(r => r.Id == reg.Id && r.Status == OlympiadRegStatus.Cancelled)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, status).SetProperty(r => r.Number, num).SetProperty(r => r.WaitlistSeq, waitSeq)
                    .SetProperty(r => r.QrToken, token).SetProperty(r => r.RulesVersion, rulesVersion)
                    .SetProperty(r => r.RulesAcceptedAt, rulesAcceptedAt).SetProperty(r => r.CreatedVia, via)
                    .SetProperty(r => r.CancelledAt, (DateTime?)null).SetProperty(r => r.CheckedInAt, (DateTime?)null)
                    .SetProperty(r => r.CheckedInBy, "").SetProperty(r => r.CheckinMethod, "")
                    .SetProperty(r => r.Score, (decimal?)null).SetProperty(r => r.ResultSentAt, (DateTime?)null)
                    .SetProperty(r => r.ResultSentScore, (decimal?)null), ct);
            if (n == 0)
            {
                await tx.RollbackAsync(ct);
                var current = await db.OlympiadRegistrations.AsNoTracking().FirstAsync(r => r.Id == reg.Id, ct);
                return new(RegisterKind.Duplicate, current);
            }
            await tx.CommitAsync(ct);
            reg = await db.OlympiadRegistrations.AsNoTracking().FirstAsync(r => r.Id == reg.Id, ct);
        }
        else
        {
            reg = new OlympiadRegistration
            {
                EventId = eventId, ParticipantId = participantId, CreatedAt = now, Status = status, Number = num,
                WaitlistSeq = waitSeq, QrToken = token, RulesVersion = rulesVersion, RulesAcceptedAt = rulesAcceptedAt, CreatedVia = via,
            };
            db.OlympiadRegistrations.Add(reg);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Ikki marta bosildi: (event, participant) noyobligi ushladi — o'rin ham qaytadi (rollback).
                await tx.RollbackAsync(ct);
                db.OlympiadRegistrations.Remove(reg); // Added qatorni kuzatuvdan chiqaradi
                var existing = await db.OlympiadRegistrations.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.EventId == eventId && r.ParticipantId == participantId, ct);
                return new(RegisterKind.Duplicate, existing);
            }
        }

        return reg.Status == OlympiadRegStatus.Waitlist
            ? new(RegisterKind.Waitlisted, reg, await WaitlistPositionAsync(db, reg, ct))
            : new(RegisterKind.Registered, reg);
    }

    /// <summary>
    /// Holatni faqat kutilgan holatdan o'zgartiradi (shartli UPDATE). 0 — boshqa so'rov ulgurib o'zgartirgan:
    /// shunda o'rin ham egallanmaydi/bo'shatilmaydi — hisoblagich siljimaydi.
    /// </summary>
    private static Task<int> TransitionAsync(IAppDbContext db, Guid id, string from, string to, int number, CancellationToken ct) =>
        db.OlympiadRegistrations.Where(r => r.Id == id && r.Status == from).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, to)
            .SetProperty(r => r.Number, r => to == OlympiadRegStatus.Cancelled ? r.Number : number)
            .SetProperty(r => r.WaitlistSeq, r => to == OlympiadRegStatus.Cancelled ? r.WaitlistSeq : 0)
            .SetProperty(r => r.CancelledAt, r => to == OlympiadRegStatus.Cancelled ? AppClock.Now : r.CancelledAt), ct);

    /// <summary>
    /// Bekor qilish. Joy bo'shasa — kutish ro'yxatidan navbatdagilar o'tadi (qaytariladi).
    /// <paramref name="onlyActive"/> — ota-ona botdan: faqat ro'yxatda yoki kutishdagi yozuv (kirgan bola bekor qilinmaydi).
    /// </summary>
    public static async Task<(OlympiadRegistration? Cancelled, List<OlympiadRegistration> Promoted)> CancelAsync(
        IAppDbContext db, Guid registrationId, CancellationToken ct, bool onlyActive = false)
    {
        var reg = await db.OlympiadRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registrationId, ct);
        if (reg is null || reg.Status is OlympiadRegStatus.Cancelled) return (null, []);
        if (onlyActive && reg.Status is not (OlympiadRegStatus.Registered or OlympiadRegStatus.Waitlist)) return (null, []);
        var heldSeat = OlympiadRegStatus.HoldingSeat.Contains(reg.Status);
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            if (await TransitionAsync(db, reg.Id, reg.Status, OlympiadRegStatus.Cancelled, 0, ct) == 0) return (null, []);
            if (heldSeat) await ReleaseSeatAsync(db, reg.EventId, ct);
            await tx.CommitAsync(ct);
        }
        reg.Status = OlympiadRegStatus.Cancelled;
        var promoted = heldSeat ? await PromoteAsync(db, reg.EventId, ct) : [];
        return (reg, promoted);
    }

    /// <summary>
    /// Bo'sh joy bo'lsa, kutish ro'yxatidan navbat bo'yicha avtomatik o'tkazadi — tadbir boshlanguncha
    /// (ro'yxat yopilgandan keyin ham). Parallel chaqirilsa ham bir bola ikki marta o'tmaydi.
    /// </summary>
    public static async Task<List<OlympiadRegistration>> PromoteAsync(IAppDbContext db, Guid eventId, CancellationToken ct)
    {
        var promoted = new List<OlympiadRegistration>();
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || AppClock.Now >= ev.StartsAt || ev.Status is OlympiadEventStatus.Finished or OlympiadEventStatus.Archived)
            return promoted;
        for (var guard = 0; guard < 1000; guard++)
        {
            var next = await db.OlympiadRegistrations.AsNoTracking()
                .Where(r => r.EventId == eventId && r.Status == OlympiadRegStatus.Waitlist)
                .OrderBy(r => r.WaitlistSeq).FirstOrDefaultAsync(ct);
            if (next is null) break;
            await using var tx = await db.BeginTransactionAsync(ct);
            var number = await ClaimSeatAsync(db, eventId, force: false, ct);
            if (number is null) break; // joy yo'q — tx yopiladi (rollback)
            if (await TransitionAsync(db, next.Id, OlympiadRegStatus.Waitlist, OlympiadRegStatus.Registered, number.Value, ct) == 0)
            {
                await tx.RollbackAsync(ct); // boshqa so'rov o'tkazib ulgurdi — o'rin qaytadi
                continue;
            }
            await tx.CommitAsync(ct);
            next.Status = OlympiadRegStatus.Registered;
            next.Number = number.Value;
            next.WaitlistSeq = 0;
            promoted.Add(next);
        }
        return promoted;
    }

    /// <summary>Admin kutish ro'yxatidan qo'lda o'tkazadi (limitdan oshsa ham).</summary>
    public static async Task<OlympiadRegistration?> ForcePromoteAsync(IAppDbContext db, Guid registrationId, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registrationId, ct);
        if (reg is null || reg.Status != OlympiadRegStatus.Waitlist) return null;
        await using var tx = await db.BeginTransactionAsync(ct);
        var number = await ClaimSeatAsync(db, reg.EventId, force: true, ct);
        if (await TransitionAsync(db, reg.Id, OlympiadRegStatus.Waitlist, OlympiadRegStatus.Registered, number!.Value, ct) == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        await tx.CommitAsync(ct);
        reg.Status = OlympiadRegStatus.Registered;
        reg.Number = number.Value;
        reg.WaitlistSeq = 0;
        return reg;
    }

    /// <summary>
    /// SeatsTaken hisoblagichini haqiqiy qatorlar soni bilan bitta SQL'da tekislaydi (tahrirlashda).
    /// </summary>
    public static Task<int> RecountSeatsAsync(IAppDbContext db, Guid eventId, CancellationToken ct) =>
        db.OlympiadEvents.Where(e => e.Id == eventId).ExecuteUpdateAsync(s => s.SetProperty(e => e.SeatsTaken,
            e => db.OlympiadRegistrations.Count(r => r.EventId == e.Id
                && (r.Status == OlympiadRegStatus.Registered || r.Status == OlympiadRegStatus.Attended || r.Status == OlympiadRegStatus.NoShow))), ct);
    /// <summary>
    /// Tasdiq xabari + QR kartasi (ro'yxatdan o'tdi / kutishdan o'tdi / QR qayta). Bola botdan kelmagan
    /// (CRM'da qo'lda qo'shilgan) bo'lsa — hech narsa yuborilmaydi.
    /// </summary>
    public static async Task<TgResult?> SendTicketAsync(IAppDbContext db, OlympiadMessenger messenger, Guid registrationId,
        string templateKey, CancellationToken ct, object? replyMarkup = null)
    {
        var row = await (from r in db.OlympiadRegistrations.AsNoTracking()
                         join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                         join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                         join u in db.OlympiadBotUsers.AsNoTracking() on p.BotUserId equals u.Id
                         where r.Id == registrationId
                         select new { r, p, e, u }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var (template, attachments) = await OlympiadText.TemplateWithMediaAsync(db, templateKey, ct);
        var html = OlympiadText.Render(template, OlympiadText.RegValues(row.e, row.p, row.r));
        if (row.r.Status is not (OlympiadRegStatus.Registered or OlympiadRegStatus.Attended))
            return await messenger.SendAsync(row.u.TelegramUserId, html, attachments, [], ct, replyMarkup: replyMarkup);
        var number = OlympiadText.Number(row.e.Code, row.r.Number);
        var card = OlympiadDocuments.QrCard(row.r.QrToken, row.p.FullName, number, row.e.Title);
        var res = await messenger.SendAsync(row.u.TelegramUserId, html, attachments, [], ct,
            [("photo", new TgFile($"QR-{number}.png", card, null))], replyMarkup);
        if (res.Blocked) await MarkBlockedAsync(db, row.u.Id, ct);
        return res;
    }

    public static async Task MarkBlockedAsync(IAppDbContext db, Guid botUserId, CancellationToken ct) =>
        await db.OlympiadBotUsers.Where(u => u.Id == botUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBlocked, true), ct);
}
