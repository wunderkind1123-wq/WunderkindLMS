using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// A certificate's block-test equivalent (client, 2026-11-06: "o'quvchi olgan sertifikat necha
/// ballga tengligi … qaysi o'quvchi nechta sertifikatga egaligi, umumiy necha ball" —
/// curriculum-monitoring.md §3).
///
/// <para>
/// Points = the type's <see cref="CertificateType.ExamCredits"/> percent × the subject's ceiling in
/// the pupil's block: the track's 1st subject 93 (30 × 3.1), its 2nd 63 (30 × 2.1), any other
/// subject a mandatory one, 11 (10 × 1.1). The 1st/2nd subjects come from the pupil's current
/// track group (<see cref="StudyGroup.BlockSubject1Id"/>). A pupil without a track, a type
/// without a credit rule, or a result below every threshold has no points — the certificate
/// still counts.
/// </para>
///
/// <para>
/// A pupil's TOTAL takes the best certificate per subject — two IELTS do not add up, just as the
/// block test credits only the best one (<see cref="ExamCertificateCredits"/>).
/// </para>
///
/// <para>
/// A certificate in the track's 1st/2nd subject that is ALSO a mandatory block subject (ona tili,
/// matematika, tarix) covers both blocks (client, 2026-11-06: "asosiy fani uchun olingan sertifikat
/// majburiy fanga ham o'tadi"): its 93/63-based points plus the full mandatory 11 — all 10
/// questions counted as right, whatever the level (<see cref="IsMandatorySubject"/>).
/// </para>
/// </summary>
public static class CertificatePoints
{
    public const decimal FirstSubjectMax = 93m;
    public const decimal SecondSubjectMax = 63m;
    public const decimal MandatoryMax = 11m;

    public const string RoleFirst = "first";
    public const string RoleSecond = "second";
    public const string RoleMandatory = "mandatory";

    /// <param name="Points">Block points, <paramref name="MandatoryPoints"/> included.</param>
    /// <param name="MandatoryPoints">11 when the certificate also covers the mandatory block, else null.</param>
    public sealed record Info(decimal? Points, string? Role, string? SubjectId, decimal? MandatoryPoints = null);

    /// <summary>
    /// A mandatory block subject (ona tili, matematika, tarix), by name — the subject list has no
    /// such flag and holds variants ("Ona tili va adabiyot", "Matematika (majburiy)").
    /// "Mantiqiy matematika" and the like are not mandatory.
    /// </summary>
    public static bool IsMandatorySubject(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.StartsWith("ona tili") || n.StartsWith("matematika") || n.StartsWith("tarix")
            || n.Contains("o'zbekiston tarixi") || n.Contains("o‘zbekiston tarixi");
    }

    private static async Task<HashSet<string>> MandatorySubjectsAsync(IAppDbContext db, CancellationToken ct) =>
        [.. (await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .Where(s => IsMandatorySubject(s.Name)).Select(s => s.Id)];

    private sealed record TrackSubjects(Guid Id, string Name, string? First, string? Second);

    public static (decimal Max, string Role) CeilingFor(string subjectId, string? first, string? second) =>
        subjectId == first ? (FirstSubjectMax, RoleFirst)
        : subjectId == second ? (SecondSubjectMax, RoleSecond)
        : (MandatoryMax, RoleMandatory);

    private sealed record Row(
        Guid Id, string StudentId, string? SubjectId, string? Level, decimal? Score, string? ExamCredits,
        string TypeName, DateOnly IssuedOn, DateOnly? ExpiresOn);

    private static async Task<Dictionary<string, TrackSubjects>> TracksAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct)
    {
        var refs = await TrackMembership.ByStudentAsync(db, studentIds, null, ct);
        var ids = refs.Values.Select(r => r.Id).Distinct().ToList();
        var groups = await db.StudyGroups.AsNoTracking().Where(g => ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => new TrackSubjects(g.Id, g.Name, g.BlockSubject1Id, g.BlockSubject2Id), ct);
        return refs.ToDictionary(kv => kv.Key, kv => groups[kv.Value.Id], StringComparer.Ordinal);
    }

    private static async Task<ILookup<Guid, string>> SubjectsAsync(IAppDbContext db, List<Guid> certIds, CancellationToken ct) =>
        (await db.CertificateSubjects.AsNoTracking().Where(cs => certIds.Contains(cs.CertificateId)).ToListAsync(ct))
            .ToLookup(cs => cs.CertificateId, cs => cs.SubjectId);

    /// <summary>The best (subject, points) of one certificate for a pupil's track.</summary>
    private static Info Evaluate(Row c, IEnumerable<string> subjects, TrackSubjects? track, HashSet<string> mandatory)
    {
        var all = subjects.Append(c.SubjectId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (track is null || all.Count == 0) return new Info(null, null, all.FirstOrDefault());
        if (CertificateService.ExamCreditPercent(c.ExamCredits, c.Level, c.Score) is not { } pct)
            return new Info(null, null, all[0]);
        Info? best = null;
        foreach (var subject in all)
        {
            var (max, role) = CeilingFor(subject, track.First, track.Second);
            var points = Math.Round(max * pct / 100m, 1, MidpointRounding.AwayFromZero);
            decimal? cover = role != RoleMandatory && mandatory.Contains(subject) ? MandatoryMax : null;
            points += cover ?? 0;
            if (best is null || points > best.Points) best = new Info(points, role, subject, cover);
        }
        return best!;
    }

    /// <summary>certificateId → points, for the certificates of the list screen.</summary>
    public static async Task<Dictionary<Guid, Info>> ForAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> certificateIds, CancellationToken ct = default)
    {
        if (certificateIds.Count == 0) return [];
        var rows = await RowsAsync(db, db.Certificates.AsNoTracking().Where(c => certificateIds.Contains(c.Id)), ct);
        var tracks = await TracksAsync(db, [.. rows.Select(r => r.StudentId).Distinct()], ct);
        var subjects = await SubjectsAsync(db, [.. rows.Select(r => r.Id)], ct);
        var mandatory = await MandatorySubjectsAsync(db, ct);
        return rows.ToDictionary(r => r.Id, r => Evaluate(r, subjects[r.Id], tracks.GetValueOrDefault(r.StudentId), mandatory));
    }

    private static Task<List<Row>> RowsAsync(IAppDbContext db, IQueryable<Certificate> q, CancellationToken ct) =>
        (from c in q
         join t in db.CertificateTypes.AsNoTracking() on c.TypeId equals t.Id
         select new Row(c.Id, c.StudentId, c.SubjectId, c.Level, c.Score, t.ExamCredits, t.Name, c.IssuedOn, c.ExpiresOn))
        .ToListAsync(ct);

    /// <summary>
    /// The rating: every pupil holding at least one (valid, unless <paramref name="includeExpired"/>)
    /// certificate — count and total points, highest total first.
    /// </summary>
    public static async Task<List<CertificateRatingRowDto>> RatingAsync(
        IAppDbContext db, string? className, Guid? trackId, string? studentId, bool includeExpired,
        CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var q = db.Certificates.AsNoTracking();
        if (!includeExpired) q = q.Where(c => c.ExpiresOn == null || c.ExpiresOn >= today);
        if (!string.IsNullOrWhiteSpace(studentId)) q = q.Where(c => c.StudentId == studentId);
        var rows = await RowsAsync(db, q, ct);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id, ct);
        var tracks = await TracksAsync(db, studentIds, ct);
        var subjects = await SubjectsAsync(db, [.. rows.Select(r => r.Id)], ct);
        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        HashSet<string> mandatory = [.. subjectNames.Where(kv => IsMandatorySubject(kv.Value)).Select(kv => kv.Key)];

        var result = new List<CertificateRatingRowDto>();
        foreach (var group in rows.GroupBy(r => r.StudentId))
        {
            if (!students.TryGetValue(group.Key, out var student)) continue;
            var track = tracks.GetValueOrDefault(group.Key);
            if (!string.IsNullOrWhiteSpace(className) && student.ClassName != className) continue;
            if (trackId is { } tid && track?.Id != tid) continue;

            var items = group
                .Select(c => (Cert: c, Info: Evaluate(c, subjects[c.Id], track, mandatory)))
                .OrderByDescending(x => x.Info.Points ?? -1).ThenByDescending(x => x.Cert.IssuedOn)
                .ToList();
            // best per subject — the one that counts
            var counted = items.Where(x => x.Info.Points is not null && x.Info.SubjectId is not null)
                .GroupBy(x => x.Info.SubjectId!).Select(g => g.First().Cert.Id).ToHashSet();
            var total = items.Where(x => counted.Contains(x.Cert.Id)).Sum(x => x.Info.Points ?? 0);

            result.Add(new CertificateRatingRowDto(
                student.Id, student.FullName, student.ClassName, track?.Name,
                items.Count, track is null ? null : total,
                [.. items.Select(x => new CertificateRatingItemDto(
                    x.Cert.Id, ExamCertificateCredits.LabelOf(x.Cert.TypeName, x.Cert.Level, x.Cert.Score),
                    x.Info.SubjectId is { } sid ? subjectNames.GetValueOrDefault(sid) : null,
                    x.Info.Points, x.Info.Role, counted.Contains(x.Cert.Id),
                    x.Cert.ExpiresOn is { } e && e < today, x.Info.MandatoryPoints))]));
        }
        return [.. result.OrderByDescending(r => r.TotalPoints ?? -1).ThenByDescending(r => r.CertificateCount).ThenBy(r => r.StudentName)];
    }
}
