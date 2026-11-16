using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>CRM ekranlari uchun o'qish so'rovlari va lidga aylantirish.</summary>
public static class OlympiadQueries
{
    public static async Task<List<OlympiadEventDto>> EventsAsync(IAppDbContext db, string botUsername, Guid? onlyId, CancellationToken ct)
    {
        var q = db.OlympiadEvents.AsNoTracking();
        if (onlyId is { } id) q = q.Where(e => e.Id == id);
        var events = await q.OrderByDescending(e => e.StartsAt).ToListAsync(ct);
        var ids = events.Select(e => e.Id).ToList();
        var counts = await db.OlympiadRegistrations.AsNoTracking().Where(r => ids.Contains(r.EventId))
            .GroupBy(r => new { r.EventId, r.Status })
            .Select(g => new { g.Key.EventId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var scored = await db.OlympiadRegistrations.AsNoTracking()
            .Where(r => ids.Contains(r.EventId) && r.Score != null && r.Status != OlympiadRegStatus.Cancelled)
            .GroupBy(r => r.EventId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var now = AppClock.Now;
        return events.Select(e =>
        {
            int C(string s) => counts.FirstOrDefault(c => c.EventId == e.Id && c.Status == s)?.Count ?? 0;
            var attended = C(OlympiadRegStatus.Attended);
            var noShow = C(OlympiadRegStatus.NoShow);
            return new OlympiadEventDto(e.Id, e.Code, e.Title, e.Description, e.Date, e.StartTime, e.EndTime, e.StartsAt,
                e.Location, e.LocationMapUrl, e.Capacity, e.OnFull, e.RegistrationOpensAt, e.RegistrationClosesAt, e.Status,
                e.MaxScore, e.ShowScore, e.ShowRank, e.ShowCertificate, e.ShowInvite,
                e.RulesText, e.RulesFileUrl, e.RulesFileName, e.RulesDisplay, e.RulesVersion,
                C(OlympiadRegStatus.Registered) + attended + noShow, C(OlympiadRegStatus.Waitlist), attended, noShow,
                C(OlympiadRegStatus.Cancelled), scored.FirstOrDefault(s => s.Key == e.Id)?.Count ?? 0,
                OlympiadRegistrar.IsOpen(e, now), e.CheckinClosedAt, e.ResultsSentAt,
                botUsername.Length > 0 ? $"https://t.me/{botUsername}?start=ev_{e.Code}" : "", e.CreatedAt);
        }).ToList();
    }

    public sealed record RegFilter(string? Status = null, int? Grade = null, string? School = null, string? Q = null, Guid? RegistrationId = null);

    public static async Task<List<OlympiadRegistrationDto>> RegistrationsAsync(IAppDbContext db, Guid eventId, RegFilter f, CancellationToken ct)
    {
        var ev = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return [];
        var q = from r in db.OlympiadRegistrations.AsNoTracking()
                join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                join u0 in db.OlympiadBotUsers.AsNoTracking() on p.BotUserId equals u0.Id into uj
                from u in uj.DefaultIfEmpty()
                where r.EventId == eventId
                select new { r, p, u };
        if (f.RegistrationId is { } rid) q = q.Where(x => x.r.Id == rid);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(x => x.r.Status == f.Status);
        if (f.Grade is { } g) q = q.Where(x => x.p.Grade == g);
        if (!string.IsNullOrWhiteSpace(f.School)) q = q.Where(x => x.p.School == f.School);
        var rows = await q.ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var needle = f.Q.Trim().ToLowerInvariant();
            var num = OlympiadText.ParseNumber(needle, ev.Code);
            var digits = new string(needle.Where(char.IsAsciiDigit).ToArray());
            rows = rows.Where(x => x.p.FullName.ToLowerInvariant().Contains(needle)
                                   || (num is { } n && x.r.Number == n)
                                   || (digits.Length >= 4 && x.p.Phone.Contains(digits))).ToList();
        }

        var ranks = await OlympiadResults.RanksAsync(db, eventId, ct);
        var waitOrder = rows.Where(x => x.r.Status == OlympiadRegStatus.Waitlist).OrderBy(x => x.r.WaitlistSeq)
            .Select((x, i) => (x.r.Id, i + 1)).ToDictionary(t => t.Id, t => t.Item2);
        // Kutishdagi o'rin filtrsiz hisoblanishi kerak — filtr qo'llangan bo'lsa bazadan.
        if (f.Status != OlympiadRegStatus.Waitlist && waitOrder.Count > 0 && (f.Q is not null || f.Grade is not null || f.School is not null))
        {
            var all = await db.OlympiadRegistrations.AsNoTracking()
                .Where(r => r.EventId == eventId && r.Status == OlympiadRegStatus.Waitlist).OrderBy(r => r.WaitlistSeq)
                .Select(r => r.Id).ToListAsync(ct);
            waitOrder = all.Select((id, i) => (id, i + 1)).ToDictionary(t => t.id, t => t.Item2);
        }

        return rows.OrderBy(x => x.r.Status == OlympiadRegStatus.Waitlist ? 1 : x.r.Status == OlympiadRegStatus.Cancelled ? 2 : 0)
            .ThenBy(x => x.r.Number).ThenBy(x => x.r.WaitlistSeq)
            .Select(x => new OlympiadRegistrationDto(
                x.r.Id, x.r.EventId, x.p.Id, OlympiadText.Number(ev.Code, x.r.Number), x.r.Number, x.r.Status,
                waitOrder.GetValueOrDefault(x.r.Id),
                x.p.FullName, x.p.Phone, x.p.Grade, x.p.School,
                x.u is null ? "" : x.u.DisplayName, x.u is null ? "" : x.u.Username, x.u is not null,
                x.r.CreatedAt, x.r.RulesAcceptedAt, x.r.RulesVersion, x.r.CheckedInAt, x.r.CheckedInBy, x.r.CheckinMethod,
                x.r.Score, x.r.Score is { } s ? OlympiadText.Percent(s, ev.MaxScore) : null,
                ranks.TryGetValue(x.r.Id, out var rk) ? rk : null,
                x.r.ScoreUpdatedAt, x.r.ScoreUpdatedBy, x.r.ScoreSource, x.r.ResultSentAt,
                x.r.ResultSentAt is not null && x.r.ResultSentScore != x.r.Score,
                x.p.LeadId, x.r.CreatedVia)).ToList();
    }

    public sealed record ParticipantFilter(string? Q = null, int? Grade = null, string? School = null, string? Lead = null, Guid? EventId = null, Guid? Id = null);

    public static async Task<List<OlympiadParticipantDto>> ParticipantsAsync(IAppDbContext db, ParticipantFilter f, CancellationToken ct)
    {
        var q = from p in db.OlympiadParticipants.AsNoTracking()
                join u0 in db.OlympiadBotUsers.AsNoTracking() on p.BotUserId equals u0.Id into uj
                from u in uj.DefaultIfEmpty()
                select new { p, u };
        if (f.Id is { } only) q = q.Where(x => x.p.Id == only);
        if (f.Grade is { } g) q = q.Where(x => x.p.Grade == g);
        if (!string.IsNullOrWhiteSpace(f.School)) q = q.Where(x => x.p.School == f.School);
        if (f.Lead == "converted") q = q.Where(x => x.p.LeadId != null);
        if (f.Lead == "not") q = q.Where(x => x.p.LeadId == null);
        if (f.EventId is { } ev) q = q.Where(x => db.OlympiadRegistrations.Any(r => r.ParticipantId == x.p.Id && r.EventId == ev));
        var rows = await q.ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var needle = f.Q.Trim().ToLowerInvariant();
            var digits = new string(needle.Where(char.IsAsciiDigit).ToArray());
            rows = rows.Where(x => x.p.FullName.ToLowerInvariant().Contains(needle) || x.p.School.ToLowerInvariant().Contains(needle)
                                   || (digits.Length >= 4 && x.p.Phone.Contains(digits))).ToList();
        }
        var ids = rows.Select(x => x.p.Id).ToList();
        var regs = await (from r in db.OlympiadRegistrations.AsNoTracking()
                          join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                          where ids.Contains(r.ParticipantId) && r.Status != OlympiadRegStatus.Cancelled
                          select new { r.ParticipantId, r.Status, r.Score, e.MaxScore }).ToListAsync(ct);
        var byKid = regs.ToLookup(r => r.ParticipantId);
        return rows.Where(x => !x.p.IsHidden || byKid[x.p.Id].Any())
            .OrderByDescending(x => x.p.CreatedAt)
            .Select(x =>
            {
                var mine = byKid[x.p.Id].ToList();
                var best = mine.Where(m => m.Score != null).Select(m => (decimal?)OlympiadText.Percent(m.Score!.Value, m.MaxScore)).Max();
                return new OlympiadParticipantDto(x.p.Id, x.p.FullName, x.p.Phone, x.p.Grade, x.p.School,
                    x.u is null ? "" : x.u.DisplayName, x.u is null ? "" : x.u.Username, x.u is not null,
                    mine.Count, mine.Count(m => m.Status == OlympiadRegStatus.Attended), best,
                    x.p.LeadId, x.p.LeadConvertedAt, x.p.CreatedAt);
            }).ToList();
    }

    public static async Task<List<OlympiadParticipantRegDto>> ParticipantRegsAsync(IAppDbContext db, Guid participantId, CancellationToken ct)
    {
        var rows = await (from r in db.OlympiadRegistrations.AsNoTracking()
                          join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                          where r.ParticipantId == participantId
                          orderby e.StartsAt descending
                          select new { r, e }).ToListAsync(ct);
        var result = new List<OlympiadParticipantRegDto>();
        foreach (var x in rows)
        {
            int? rank = null;
            if (x.r.Score is not null) rank = (await OlympiadResults.RanksAsync(db, x.e.Id, ct)).GetValueOrDefault(x.r.Id);
            result.Add(new OlympiadParticipantRegDto(x.r.Id, x.e.Id, x.e.Title, x.e.Date, OlympiadText.Number(x.e.Code, x.r.Number),
                x.r.Status, x.r.Score, x.e.MaxScore, x.r.Score is { } s ? OlympiadText.Percent(s, x.e.MaxScore) : null, rank));
        }
        return result;
    }

    /// <summary>
    /// "Lidga aylantirish": Lidlar moduliga manba <c>olympiad</c> bilan qo'shadi. Telefon (oxirgi 9 raqam)
    /// lidlarda allaqachon bo'lsa — yangisi yaratilmaydi, bola mavjud lidga bog'lanadi va xabar qaytadi.
    /// </summary>
    public static async Task<ConvertLeadsResult> ConvertToLeadsAsync(IAppDbContext db, IReadOnlyList<Guid> participantIds, CancellationToken ct)
    {
        var kids = await db.OlympiadParticipants.Where(p => participantIds.Contains(p.Id)).ToListAsync(ct);
        var firstStage = await db.LeadStages.AsNoTracking().OrderBy(s => s.Order).Select(s => s.Id).FirstOrDefaultAsync(ct);
        var leads = await db.Leads.AsNoTracking().Select(l => new { l.Id, l.ParentPhone, l.FullName }).ToListAsync(ct);
        var byPhone = leads.Where(l => OlympiadText.PhoneKey(l.ParentPhone).Length == 9)
            .GroupBy(l => OlympiadText.PhoneKey(l.ParentPhone)).ToDictionary(g => g.Key, g => g.First());
        var botUserIds = kids.Where(k => k.BotUserId != null).Select(k => k.BotUserId!.Value).ToList();
        var parents = await db.OlympiadBotUsers.AsNoTracking().Where(u => botUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        var now = AppClock.Now;
        int created = 0, linked = 0, skipped = 0;
        var rankCache = new Dictionary<Guid, Dictionary<Guid, int>>();
        var messages = new List<string>();
        foreach (var k in kids)
        {
            if (k.LeadId is not null) { skipped++; continue; }
            var key = OlympiadText.PhoneKey(k.Phone);
            if (byPhone.TryGetValue(key, out var existing))
            {
                k.LeadId = existing.Id;
                k.LeadConvertedAt = now;
                linked++;
                messages.Add($"{k.FullName}: {k.Phone} raqami lidlarda bor («{existing.FullName}») — yangi lid yaratilmadi, bog'landi.");
                continue;
            }
            var regs = await (from r in db.OlympiadRegistrations.AsNoTracking()
                              join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                              where r.ParticipantId == k.Id && r.Status != OlympiadRegStatus.Cancelled
                              orderby e.StartsAt descending
                              select new { e.Title, e.Date, r.Score, e.MaxScore, e.Id, RegId = r.Id }).ToListAsync(ct);
            var lines = new List<string> { $"Olimpiada ishtirokchisi. Maktab: {k.School}, {k.Grade}-sinf." };
            foreach (var r in regs)
            {
                var res = "";
                if (r.Score is { } sc)
                {
                    if (!rankCache.TryGetValue(r.Id, out var evRanks)) rankCache[r.Id] = evRanks = await OlympiadResults.RanksAsync(db, r.Id, ct);
                    var rank = evRanks.GetValueOrDefault(r.RegId);
                    res = $" — {OlympiadText.Num(sc)}/{OlympiadText.Num(r.MaxScore)} ball ({OlympiadText.Num(OlympiadText.Percent(sc, r.MaxScore))}%), {rank}-o'rin";
                }
                lines.Add($"{r.Title} ({OlympiadText.FormatDate(r.Date)}){res}");
            }
            var parent = k.BotUserId is { } bu && parents.TryGetValue(bu, out var pu) ? pu : null;
            var lead = new Lead
            {
                FullName = k.FullName,
                ParentFullName = parent?.DisplayName ?? "",
                ParentPhone = k.Phone,
                TargetGrade = k.Grade,
                Note = string.Join("\n", lines),
                Stage = firstStage ?? "",
                Source = LeadSource.Olympiad,
            };
            db.Leads.Add(lead);
            byPhone[key] = new { lead.Id, lead.ParentPhone, lead.FullName };
            k.LeadId = lead.Id;
            k.LeadConvertedAt = now;
            created++;
        }
        await db.SaveChangesAsync(ct);
        return new ConvertLeadsResult(created, linked, skipped, messages);
    }
}
