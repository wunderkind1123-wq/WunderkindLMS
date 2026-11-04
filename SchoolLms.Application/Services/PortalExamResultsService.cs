using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  OTA-ONA KABINETIDA TEST NATIJALARI VA OYLIK O'SISH — mijoz, 2026-11-03:
//  "ota ona profiliga oylik va blok test natijalarini ko'rinadigan qism qo'sh ...
//  har oylik natijalar hisob-kitobi ham bo'lsin ... farzandidagi o'sishni kuzata olsin".
//  docs/modules/parent-exam-results.md
// ===========================================================================
//
//  QAYSI NATIJA
//  ------------
//  Imtihonlar bo'limidagi test (blok yoki oylik — farqi imtihon turida) natija kiritilishi bilan
//  ko'rinadi; alohida tasdiqlash yo'q. Bekor qilingan imtihon ko'rinmaydi. Mavsumiy baholashning oylik
//  bahosi ham oy hisobiga kiradi: 3–8-sinflarda oylik natija shu yerga ham yoziladi.
//
//  CHALA NATIJA
//  ------------
//  Har fanni boshqa o'qituvchi kiritadi, shuning uchun bir qism bloklar hali bo'sh bo'lishi mumkin.
//  Kiritilgan bloklar darrov ko'rinadi, lekin jami va foiz berilmaydi va oy hisobiga kirmaydi: aks holda
//  oxirgi o'qituvchi kiritguncha o'quvchi "pasayib ketgandek" ko'rinardi.
//
//  KIMGA TEST NATIJALARI
//  ---------------------
//  Mijoz, 2026-11-04: blok test faqat yo'nalish guruhlaridan olinadi (9–11-sinflar, DTM shaklida);
//  3–8-sinflarning oylik testi mavsumiy baholashda va "Oylik o'sish"da turadi. Shuning uchun "Test natijalari"
//  va oydagi test foizi faqat yo'nalishdagi yoki 9-sinfdan yuqori o'quvchiga beriladi (ShowTests).
//
//  OY HISOBI
//  ---------
//  Test foizi — oydagi to'liq testlar foizining o'rtachasi (har test teng og'irlikda; testlarning
//  maksimal bali har xil bo'lsa ham). "Kelmadi" o'rtachaga kirmaydi, soni alohida. O'zgarish — shu
//  ko'rsatkich bor bo'lgan oldingi oyga nisbatan.
// ===========================================================================

public static class PortalExamResultsService
{
    /// <summary>Yo'nalish guruhlari shu sinfdan boshlanadi (docs/modules/track-groups-as-classes.md).</summary>
    private const int FirstTrackGrade = 9;

    public static async Task<PortalExamResultsDto> ForStudentAsync(
        IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var showTests = await TakesBlockTestsAsync(db, studentId, ct);
        var sittings = await (
                from p in db.ExamParticipants.AsNoTracking()
                join e in db.Exams.AsNoTracking() on p.ExamId equals e.Id
                join t in db.ExamTypes.AsNoTracking() on e.ExamTypeId equals t.Id into tj
                from t in tj.DefaultIfEmpty()
                where showTests && p.StudentId == studentId && e.Kind == ExamKind.Block && e.Status != ExamStatus.Cancelled
                      && (p.Status == ExamParticipantStatus.Finished || p.Status == ExamParticipantStatus.Absent)
                select new
                {
                    p.Id, p.ExamId, p.Status, p.TotalPoints, p.MaxPoints, p.Percent,
                    e.Title, e.ExamDate, e.CreatedAt, TypeName = t == null ? null : t.Name,
                })
            .ToListAsync(ct);

        var examIds = sittings.Select(s => s.ExamId).ToList();
        var participantIds = sittings.Select(s => s.Id).ToList();
        var sections = (await (
                    from s in db.ExamSections.AsNoTracking()
                    join subject in db.Subjects.AsNoTracking() on s.SubjectId equals subject.Id
                    where examIds.Contains(s.ExamId)
                    orderby s.Order, s.Id
                    select new { s.Id, s.ExamId, s.SubjectId, SubjectName = subject.Name, s.MaxScore, s.Order })
                .ToListAsync(ct))
            .ToLookup(s => s.ExamId);
        var scores = await db.ExamSectionScores.AsNoTracking()
            .Where(s => participantIds.Contains(s.ParticipantId))
            .Select(s => new { s.ParticipantId, s.SectionId, s.Points, s.CertificateId })
            .ToDictionaryAsync(s => (s.ParticipantId, s.SectionId), ct);

        var certificateIds = scores.Values.Select(s => s.CertificateId).OfType<Guid>().Distinct().ToList();
        var certificates = certificateIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await (from c in db.Certificates.AsNoTracking()
                      join t in db.CertificateTypes.AsNoTracking() on c.TypeId equals t.Id
                      where certificateIds.Contains(c.Id)
                      select new { c.Id, t.Name, c.Level, c.Score }).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => ExamCertificateCredits.LabelOf(c.Name, c.Level, c.Score));

        var exams = new List<PortalExamResultDto>(sittings.Count);
        // Oy hisobi uchun: oy → (to'liq test foizi, fan bo'yicha ball).
        var tests = new List<(string Month, decimal Percent)>();
        var subjectPoints = new List<(string Month, string SubjectId, string Name, decimal Points, decimal Max)>();
        var absent = new List<string>();
        var pending = new List<string>();

        foreach (var s in sittings.OrderByDescending(x => x.ExamDate, StringComparer.Ordinal).ThenByDescending(x => x.CreatedAt))
        {
            var isAbsent = s.Status == ExamParticipantStatus.Absent;
            var blocks = sections[s.ExamId].Select(x =>
            {
                var hit = scores.GetValueOrDefault((s.Id, x.Id));
                return new PortalExamBlockDto(
                    x.Order + 1, x.SubjectId, x.SubjectName, hit?.Points, x.MaxScore,
                    hit?.CertificateId is { } id ? certificates.GetValueOrDefault(id, "Sertifikat") : null);
            }).ToList();
            var complete = !isAbsent && blocks.All(b => b.Points is not null);

            exams.Add(new PortalExamResultDto(
                s.ExamId, s.Title, s.TypeName, s.ExamDate, isAbsent, complete,
                complete ? s.TotalPoints : null, complete ? s.MaxPoints : null, complete ? s.Percent : null,
                isAbsent ? [] : blocks));

            if (MonthOf(s.ExamDate) is not { } month) continue;
            if (isAbsent) absent.Add(month);
            else if (!complete || s.Percent is null) pending.Add(month);
            else
            {
                tests.Add((month, s.Percent.Value));
                subjectPoints.AddRange(blocks.Select(b => (month, b.SubjectId, b.SubjectName, b.Points!.Value, b.MaxPoints)));
            }
        }

        var marks = await (
                from m in db.SeasonalMarks.AsNoTracking()
                join subject in db.Subjects.AsNoTracking() on m.SubjectId equals subject.Id
                where m.StudentId == studentId && m.PeriodKind == SeasonalPeriodKind.Monthly
                      && m.Month != null && m.Score != null
                select new { m.Year, m.Month, m.Score, m.SubjectId, SubjectName = subject.Name })
            .ToListAsync(ct);
        var monthlyMarks = marks
            .Select(m => (Month: $"{m.Year:D4}-{m.Month:D2}", m.SubjectId, m.SubjectName, Score: m.Score!.Value))
            .ToList();

        var months = new List<PortalExamMonthDto>();
        decimal? lastTest = null, lastMark = null;
        // Fan bo'yicha oxirgi qiymat: o'zgarish shu fan bor bo'lgan oldingi oyga nisbatan.
        var lastSubjectTest = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var lastSubjectMark = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var month in tests.Select(t => t.Month).Concat(absent).Concat(pending)
                     .Concat(monthlyMarks.Select(m => m.Month)).Distinct().Order(StringComparer.Ordinal))
        {
            var mine = tests.Where(t => t.Month == month).Select(t => t.Percent).ToList();
            var testPercent = mine.Count == 0 ? (decimal?)null : ExamScoringService.RoundPoints(mine.Average());
            var myMarks = monthlyMarks.Where(m => m.Month == month).ToList();
            var markAverage = myMarks.Count == 0 ? (decimal?)null : ExamScoringService.RoundPoints(myMarks.Average(m => m.Score));

            var subjects = subjectPoints.Where(p => p.Month == month).Select(p => (p.SubjectId, p.Name))
                .Concat(myMarks.Select(m => (m.SubjectId, Name: m.SubjectName)))
                .Distinct()
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x =>
                {
                    var taken = subjectPoints.Where(p => p.Month == month && p.SubjectId == x.SubjectId).ToList();
                    var percent = taken.Count == 0 ? null : ExamScoringService.Percent(taken.Sum(p => p.Points), taken.Sum(p => p.Max));
                    var mark = myMarks.Where(m => m.SubjectId == x.SubjectId).Select(m => (decimal?)m.Score).FirstOrDefault();
                    var row = new PortalExamMonthSubjectDto(
                        x.SubjectId, x.Name, percent, mark,
                        percent is { } p2 && lastSubjectTest.TryGetValue(x.SubjectId, out var t0) ? p2 - t0 : null,
                        mark is { } m2 && lastSubjectMark.TryGetValue(x.SubjectId, out var m0) ? m2 - m0 : null);
                    if (percent is { } p3) lastSubjectTest[x.SubjectId] = p3;
                    if (mark is { } m3) lastSubjectMark[x.SubjectId] = m3;
                    return row;
                })
                .ToList();

            months.Add(new PortalExamMonthDto(
                month,
                testPercent, testPercent is { } tp && lastTest is { } lt ? tp - lt : null, mine.Count,
                absent.Count(a => a == month), pending.Count(p => p == month),
                markAverage, markAverage is { } ma && lastMark is { } lm ? ma - lm : null, myMarks.Count,
                subjects));

            lastTest = testPercent ?? lastTest;
            lastMark = markAverage ?? lastMark;
        }
        months.Reverse();

        return new PortalExamResultsDto(months, exams, showTests);
    }

    /// <summary>O'quvchi yo'nalish guruhida yoki 9-sinfdan yuqorida — blok test faqat ulardan olinadi.</summary>
    private static async Task<bool> TakesBlockTestsAsync(IAppDbContext db, string studentId, CancellationToken ct)
    {
        if ((await TrackMembership.ByStudentAsync(db, [studentId], ct: ct)).Count > 0) return true;
        return await (
                from s in db.Students.AsNoTracking()
                join c in db.Classes.AsNoTracking() on s.ClassName equals c.Name
                where s.Id == studentId && c.Grade >= FirstTrackGrade
                select c.Id)
            .AnyAsync(ct);
    }

    /// <summary>"2026-10-31" → "2026-10".</summary>
    private static string? MonthOf(string? examDate) => examDate is { Length: >= 7 } ? examDate[..7] : null;
}
