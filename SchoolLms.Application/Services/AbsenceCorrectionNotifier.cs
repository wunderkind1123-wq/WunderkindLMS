using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// "KELMADI" XABARINI TUZATISH (2026-11-24).
///
/// <para>
/// 23.11 da 3-A o'quvchisining ota-onasiga 10:30 da "maktabga kelmadi" xabari ketdi, 12:37 da esa
/// o'qituvchi davomatni "keldi" ga o'zgartirdi — ota-ona noto'g'ri xabar bilan qoldi. Endi har bir
/// tekshiruvda (AutoMessageScheduler, ikki daqiqada bir) BUGUN xabar olgan o'quvchilarning kuni
/// qayta hisoblanadi: belgilangan darslardan kamida bittasida u bor (yoki kechikib kelgan) bo'lsa,
/// ota-onaga bir marta tuzatish xabari ketadi.
/// </para>
/// <para>
/// Faqat shu kun: ertasi kuni eski kunni tuzatish ota-onani chalg'itadi. Sabab almashsa
/// (Sababsiz → Kasal) bola baribir kelmagan — tuzatish ketmaydi. Bitta xabarga bitta tuzatish.
/// </para>
/// </summary>
public sealed class AbsenceCorrectionNotifier(IAppDbContext db, TelegramService telegram, ILogger logger)
{
    /// <returns>Tuzatish ketgan o'quvchilar soni.</returns>
    public async Task<int> RunAsync(DateOnly today, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return 0;

        var notices = await db.AbsenceNotices
            .Where(n => n.Date == today && n.CorrectedAt == null)
            .ToListAsync(ct);
        if (notices.Count == 0) return 0;

        var iso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var wanted = notices.Select(n => n.StudentId).ToHashSet(StringComparer.Ordinal);
        var days = new Dictionary<string, StudentDay>(StringComparer.Ordinal);
        await AttendanceAnalytics.BuildAsync(db, null, iso, iso, iso, 0,
            onStudentDay: (studentId, date, day) =>
            {
                if (date == iso && wanted.Contains(studentId)) days[studentId] = day;
            });

        var came = notices
            .Where(n => days.TryGetValue(n.StudentId, out var d) && d.Status is StudentDay.Present or StudentDay.Partial)
            .ToList();
        if (came.Count == 0) return 0;

        var ids = came.Select(n => n.StudentId).ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id, ct);
        var chats = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .Select(r => new { r.StudentId, r.ChatId }).ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList(), StringComparer.Ordinal);

        var corrected = 0;
        foreach (var notice in came)
        {
            if (!students.TryGetValue(notice.StudentId, out var s)) continue;
            var day = days[notice.StudentId];
            var text = BuildMessage(s.FullName, s.ClassName, today, late: day.Status == StudentDay.Partial || day.IsLate);

            var delivered = 0;
            foreach (var chat in chats.GetValueOrDefault(notice.StudentId) ?? [])
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) delivered++;

            // Chati yo'q (bot uzilgan) — qayta urinish befoyda, belgini qo'yamiz.
            var hadChats = chats.ContainsKey(notice.StudentId);
            if (hadChats && delivered == 0) continue; // Telegram rad etdi — keyingi tekshiruvda yana.

            notice.CorrectedAt = AppClock.NowInstant;
            notice.CorrectionChats = delivered;
            db.AuditLogs.Add(AuditService.Entry(
                AuditService.EntityAbsenceCorrection, $"{notice.StudentId}:{iso}", "create",
                $"Ota-onaga tuzatish xabari ({today:dd.MM.yyyy}) — {s.FullName} ({s.ClassName}): "
                    + $"\"kelmadi\" xabaridan keyin davomat \"keldi\" ga o'zgartirilgan, {delivered} ta chatga yuborildi",
                actorId: null, studentId: notice.StudentId));
            corrected++;
        }

        await db.SaveChangesAsync(ct);
        if (corrected > 0)
            logger.LogInformation("Kelmaganlik xabari tuzatildi: {Date}, {Count} o'quvchi", iso, corrected);
        return corrected;
    }

    /// <summary>Ota-ona o'qiydigan matn.</summary>
    /// <param name="late">Bola kunning bir qismida yo'q bo'lgan yoki kechikkan — "keyinroq keldi".</param>
    public static string BuildMessage(string fullName, string className, DateOnly day, bool late)
    {
        var date = day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var who = string.IsNullOrWhiteSpace(className) ? fullName : $"{fullName} ({className})";
        return late
            ? $"📋 Davomat — tuzatish\n\nHurmatli ota-ona, farzandingiz {who} {date} kuni maktabga keyinroq keldi. "
              + "Davomat yangilandi — oldingi «kelmadi» xabarini e'tiborsiz qoldiring."
            : $"📋 Davomat — tuzatish\n\nHurmatli ota-ona, farzandingiz {who} {date} kuni maktabda. "
              + "Davomat tuzatildi — oldingi «kelmadi» xabari uchun uzr so'raymiz.";
    }
}
