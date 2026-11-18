using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>Kirish belgilanadigan tadbir (skaner sahifasidagi ro'yxat).</summary>
public sealed record CheckinEventDto(
    Guid Id, int Code, string Title, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Location, DateTime? CheckinClosedAt);

/// <summary>
/// Imtihon kuni kirishni belgilash — bitta mantiq, ikki kirish nuqtasi:
/// admin paneli (<c>OlympiadCheckinController</c>, JWT) va olimpiada botidagi skaner
/// (<c>OlympiadScannerController</c>, Telegram initData + admin raqami).
/// </summary>
public static class OlympiadCheckin
{
    /// <summary>E'lon qilingan yoki yakunlangan, oxirgi 30 kun va kelgusi tadbirlar.</summary>
    public static async Task<List<CheckinEventDto>> EventsAsync(IAppDbContext db, CancellationToken ct)
    {
        var from = AppClock.Today.AddDays(-30);
        return await db.OlympiadEvents.AsNoTracking()
            .Where(e => (e.Status == OlympiadEventStatus.Published || e.Status == OlympiadEventStatus.Finished) && e.Date >= from)
            .OrderBy(e => e.StartsAt)
            .Select(e => new CheckinEventDto(e.Id, e.Code, e.Title, e.Date, e.StartTime, e.EndTime, e.Location, e.CheckinClosedAt))
            .ToListAsync(ct);
    }

    public static async Task<CheckinSummaryDto> SummaryAsync(IAppDbContext db, Guid eventId, CancellationToken ct)
    {
        var counts = await db.OlympiadRegistrations.AsNoTracking().Where(r => r.EventId == eventId)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int C(string s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        var attended = C(OlympiadRegStatus.Attended);
        var total = attended + C(OlympiadRegStatus.Registered) + C(OlympiadRegStatus.NoShow);
        return new CheckinSummaryDto(total, attended, total - attended);
    }

    /// <summary>Ism, familiya, telefon yoki ishtirokchi raqami bo'yicha (kamida 2 belgi).</summary>
    public static async Task<List<OlympiadRegistrationDto>> SearchAsync(IAppDbContext db, Guid eventId, string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        return (await OlympiadQueries.RegistrationsAsync(db, eventId, new(Q: q), ct))
            .Where(r => r.Status != OlympiadRegStatus.Cancelled).Take(30).ToList();
    }

    private static async Task<OlympiadRegistrationDto> DtoAsync(IAppDbContext db, OlympiadRegistration r, CancellationToken ct) =>
        (await OlympiadQueries.RegistrationsAsync(db, r.EventId, new(RegistrationId: r.Id), ct)).First();

    /// <summary>
    /// QR skan: ✅ ok — "Kirdi" belgilandi; ⚠️ already — allaqachon kirgan; ❌ error — boshqa tadbir, bekor qilingan,
    /// kutishda yoki topilmadi.
    /// </summary>
    public static async Task<CheckinResultDto> ScanAsync(IAppDbContext db, AuditService audit, Guid eventId, string? rawToken, string actor, CancellationToken ct)
    {
        var token = (rawToken ?? "").Trim();
        // QR'da faqat token; kimdir havola ko'rinishida skanerlasa ham oxirgi qismini olamiz.
        if (token.Contains('/')) token = token[(token.LastIndexOf('/') + 1)..];
        var reg = token.Length == 0 ? null : await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.QrToken == token, ct);
        if (reg is null) return new("error", "QR kod topilmadi", null);
        if (reg.EventId != eventId)
        {
            var other = await db.OlympiadEvents.AsNoTracking().Where(e => e.Id == reg.EventId).Select(e => e.Title).FirstOrDefaultAsync(ct);
            return new("error", $"Boshqa tadbir: «{other}»", await DtoAsync(db, reg, ct));
        }
        return await MarkAsync(db, audit, reg, "qr", actor, ct);
    }

    /// <summary>Qo'lda belgilash (qidiruvdan). null — yozuv topilmadi.</summary>
    public static async Task<CheckinResultDto?> CheckinAsync(IAppDbContext db, AuditService audit, Guid registrationId, string actor, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, ct);
        return reg is null ? null : await MarkAsync(db, audit, reg, "manual", actor, ct);
    }

    private static async Task<CheckinResultDto> MarkAsync(IAppDbContext db, AuditService audit, OlympiadRegistration reg, string method, string actor, CancellationToken ct)
    {
        switch (reg.Status)
        {
            case OlympiadRegStatus.Cancelled: return new("error", "Yozuv bekor qilingan", await DtoAsync(db, reg, ct));
            case OlympiadRegStatus.Waitlist: return new("error", "Kutish ro'yxatida — joy berilmagan", await DtoAsync(db, reg, ct));
            case OlympiadRegStatus.Attended:
                return new("already", $"Allaqachon kirgan: {reg.CheckedInAt:HH:mm}, {reg.CheckedInBy}", await DtoAsync(db, reg, ct));
        }
        reg.Status = OlympiadRegStatus.Attended;
        reg.CheckedInAt = AppClock.Now;
        reg.CheckedInBy = actor;
        reg.CheckinMethod = method;
        audit.Record("olympiad_registration", reg.Id.ToString(), "checkin",
            $"Olimpiadaga kirdi ({(method == "qr" ? "QR" : "qo'lda")}) — {actor}");
        await db.SaveChangesAsync(ct);
        return new("ok", "Kirdi", await DtoAsync(db, reg, ct));
    }

    /// <summary>Belgilashni bekor qilish (xato bo'lsa).</summary>
    public static async Task<(OlympiadRegistrationDto? Dto, int Status, string? Error)> UndoAsync(
        IAppDbContext db, AuditService audit, Guid registrationId, string actor, CancellationToken ct)
    {
        var reg = await db.OlympiadRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, ct);
        if (reg is null) return (null, 404, "Yozuv topilmadi");
        if (reg.Status is not (OlympiadRegStatus.Attended or OlympiadRegStatus.NoShow))
            return (null, 409, "Bu yozuvda kirish belgilanmagan");
        reg.Status = OlympiadRegStatus.Registered;
        reg.CheckedInAt = null;
        reg.CheckedInBy = "";
        reg.CheckinMethod = "";
        audit.Record("olympiad_registration", reg.Id.ToString(), "checkin_undo", $"Olimpiada kirish belgisi bekor qilindi — {actor}");
        await db.SaveChangesAsync(ct);
        return (await DtoAsync(db, reg, ct), 200, null);
    }
}
