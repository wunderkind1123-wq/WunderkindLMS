using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>Excel importning bitta qatori (oldindan ko'rish).</summary>
public sealed record ResultImportRow(int Row, string Number, string Name, string ScoreText,
    Guid? RegistrationId, string? FoundName, decimal? Score, string Status, string? Error);

/// <summary>Natija: hisoblash, Excel import (oldindan ko'rish → tasdiqlash), xabar va sertifikat.</summary>
public static class OlympiadResults
{
    /// <summary>Tadbirdagi o'rinlar (faqat balli, bekor qilinmaganlar).</summary>
    public static async Task<Dictionary<Guid, int>> RanksAsync(IAppDbContext db, Guid eventId, CancellationToken ct)
    {
        var scores = await db.OlympiadRegistrations.AsNoTracking()
            .Where(r => r.EventId == eventId && r.Score != null && r.Status != OlympiadRegStatus.Cancelled)
            .Select(r => new { r.Id, Score = r.Score!.Value }).ToListAsync(ct);
        return OlympiadText.Ranks(scores.Select(s => (s.Id, s.Score)));
    }

    /// <summary>
    /// Excel qatorlarini tekshiradi: ustunlar — ishtirokchi raqami, F.I.Sh (faqat tekshirish uchun), ball.
    /// Xatolar: topilmadi, bo'sh ball, ball &gt; max yoki manfiy, takror raqam.
    /// </summary>
    public static async Task<List<ResultImportRow>> PreviewAsync(IAppDbContext db, OlympiadEvent ev,
        IReadOnlyList<string[]> rows, CancellationToken ct)
    {
        var regs = await (from r in db.OlympiadRegistrations.AsNoTracking()
                          join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                          where r.EventId == ev.Id && r.Number > 0 && r.Status != OlympiadRegStatus.Cancelled
                          select new { r.Id, r.Number, p.FullName }).ToListAsync(ct);
        var byNumber = regs.ToDictionary(r => r.Number);
        var seen = new HashSet<int>();
        var result = new List<ResultImportRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            string Cell(int c) => c < cells.Length ? (cells[c] ?? "").Trim() : "";
            var numText = Cell(0);
            var name = Cell(1);
            var scoreText = Cell(2);
            if (numText.Length == 0 && name.Length == 0 && scoreText.Length == 0) continue;
            // Sarlavha qatori (shablonda bor).
            if (i == 0 && !char.IsDigit(numText.LastOrDefault())) continue;

            var rowNo = i + 1;
            var number = OlympiadText.ParseNumber(numText, ev.Code);
            if (number is null || !byNumber.TryGetValue(number.Value, out var reg))
            {
                result.Add(new(rowNo, numText, name, scoreText, null, null, null, "error", "Ishtirokchi topilmadi"));
                continue;
            }
            if (!seen.Add(number.Value))
            {
                result.Add(new(rowNo, numText, name, scoreText, reg.Id, reg.FullName, null, "error", "Takroriy raqam"));
                continue;
            }
            if (scoreText.Length == 0)
            {
                result.Add(new(rowNo, numText, name, scoreText, reg.Id, reg.FullName, null, "error", "Ball bo'sh"));
                continue;
            }
            if (!decimal.TryParse(scoreText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var score))
            {
                result.Add(new(rowNo, numText, name, scoreText, reg.Id, reg.FullName, null, "error", "Ball son emas"));
                continue;
            }
            if (score < 0 || score > ev.MaxScore)
            {
                result.Add(new(rowNo, numText, name, scoreText, reg.Id, reg.FullName, score, "error",
                    $"Ball 0 dan {OlympiadText.Num(ev.MaxScore)} gacha bo'lishi kerak"));
                continue;
            }
            // Ism mos kelmasa — ogohlantirish, xato emas (raqam asosiy).
            var warn = name.Length > 0 && !SameName(name, reg.FullName) ? "Ism mos emas — raqam bo'yicha olinadi" : null;
            result.Add(new(rowNo, numText, name, scoreText, reg.Id, reg.FullName, score, warn is null ? "ok" : "warning", warn));
        }
        return result;
    }

    private static bool SameName(string a, string b)
    {
        static string N(string s) => string.Join(' ', OlympiadText.CleanSpaces(s).ToLowerInvariant()
            .Replace('ʻ', '\'').Replace('’', '\'').Split(' ').Order());
        return N(a) == N(b);
    }

    public static string CertificateFileName(string name, bool png) =>
        $"Sertifikat-{string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim().Replace(' ', '-')}.{(png ? "png" : "pdf")}";

    public static async Task<(byte[] Bytes, bool Png, string FileName)?> CertificateAsync(IAppDbContext db, Guid registrationId, CancellationToken ct,
        Dictionary<Guid, int>? ranks = null)
    {
        var row = await (from r in db.OlympiadRegistrations.AsNoTracking()
                         join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                         join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                         where r.Id == registrationId
                         select new { r, p, e }).FirstOrDefaultAsync(ct);
        if (row?.r.Score is not { } score) return null;
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new OlympiadSettings();
        ranks ??= await RanksAsync(db, row.e.Id, ct);
        var png = s.CertificateFormat == "png";
        var bytes = OlympiadDocuments.Certificate(new OlympiadCertificateModel(
            row.p.FullName, row.p.Grade.ToString(CultureInfo.InvariantCulture), row.p.School, row.e.Title,
            OlympiadText.FormatDate(row.e.Date), OlympiadText.Num(score), OlympiadText.Num(row.e.MaxScore),
            OlympiadText.Num(OlympiadText.Percent(score, row.e.MaxScore)),
            row.e.ShowRank ? ranks.GetValueOrDefault(row.r.Id) : null,
            OlympiadText.Number(row.e.Code, row.r.Number)), png);
        return (bytes, png, CertificateFileName(row.p.FullName, png));
    }

    /// <summary>Natija xabari — faqat yoqilgan qismlar (ball/o'rin qatori o'chiriladi), sertifikat, taklif tugmasi.</summary>
    public static async Task<(string Html, List<OlympiadButton> Buttons, bool WithCertificate)?> ComposeAsync(
        IAppDbContext db, Guid registrationId, CancellationToken ct, Dictionary<Guid, int>? ranks = null)
    {
        var row = await (from r in db.OlympiadRegistrations.AsNoTracking()
                         join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                         join e in db.OlympiadEvents.AsNoTracking() on r.EventId equals e.Id
                         where r.Id == registrationId
                         select new { r, p, e }).FirstOrDefaultAsync(ct);
        if (row?.r.Score is not { } score) return null;
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new OlympiadSettings();
        ranks ??= await RanksAsync(db, row.e.Id, ct);
        var values = OlympiadText.RegValues(row.e, row.p, row.r);
        values["ball"] = OlympiadText.Num(score);
        values["max"] = OlympiadText.Num(row.e.MaxScore);
        values["foiz"] = OlympiadText.Num(OlympiadText.Percent(score, row.e.MaxScore));
        values["orin"] = ranks.GetValueOrDefault(row.r.Id).ToString(CultureInfo.InvariantCulture);
        values["jami"] = ranks.Count.ToString(CultureInfo.InvariantCulture);
        var drop = new List<string>();
        if (!row.e.ShowScore) drop.AddRange(["ball", "max", "foiz"]);
        if (!row.e.ShowRank) drop.AddRange(["orin", "jami"]);
        var html = OlympiadText.Render(await OlympiadText.TemplateAsync(db, "result", ct), values, drop);
        var buttons = new List<OlympiadButton>();
        if (row.e.ShowInvite && s.InviteUrl.StartsWith("http", StringComparison.Ordinal))
            buttons.Add(new OlympiadButton(s.InviteText.Length > 0 ? s.InviteText : "Maktabimizga qabul", s.InviteUrl));
        return (html, buttons, row.e.ShowCertificate);
    }

    /// <summary>Bitta ishtirokchiga natija (navbatdan yoki "Natija va sertifikat" tugmasidan).</summary>
    public static async Task<TgResult?> SendResultAsync(IAppDbContext db, OlympiadMessenger messenger, Guid registrationId,
        CancellationToken ct, long? chatOverride = null, Dictionary<Guid, int>? ranks = null)
    {
        var chat = chatOverride ?? await (from r in db.OlympiadRegistrations
                                          join p in db.OlympiadParticipants on r.ParticipantId equals p.Id
                                          join u in db.OlympiadBotUsers on p.BotUserId equals u.Id
                                          where r.Id == registrationId
                                          select (long?)u.TelegramUserId).FirstOrDefaultAsync(ct);
        if (chat is null) return null;
        var msg = await ComposeAsync(db, registrationId, ct, ranks);
        if (msg is null) return null;
        var (html, buttons, withCert) = msg.Value;
        var files = new List<(string, TgFile)>();
        if (withCert && await CertificateAsync(db, registrationId, ct, ranks) is { } cert)
            files.Add((cert.Png ? "photo" : "document", new TgFile(cert.FileName, cert.Bytes, null)));
        return await messenger.SendAsync(chat.Value, html, [], buttons, ct, files);
    }
}
