using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  BLOK TEST NATIJASINI O'QITUVCHI KIRITADI — mijoz, 2026-11-03
//  docs/modules/track-groups-as-classes.md
// ===========================================================================
//
//  QOIDA
//  -----
//  O'qituvchi dars jadvalida o'zi o'tadigan (ega, fan) juftlari bo'yicha ishlaydi: tarix
//  o'qituvchisi beshta yo'nalishga kirsa — beshta yo'nalishning tarix bloklarini ko'radi. Ega
//  odatda yo'nalish guruhi (9–11-sinflar), lekin blok testga sinf ham qo'shilishi mumkin, u holda
//  shu sinfda dars beradigan o'qituvchi kiritadi. Alohida ruxsat kaliti YO'Q: jadvalda dars
//  biriktirilishi bilan ro'yxat o'zi paydo bo'ladi, dars olib tashlansa — yo'qoladi.
//
//  QATOR VA USTUN
//  --------------
//  Ustun — imtihonning o'qituvchi faniga tegishli bloklari (bir fan ikki blokda bo'lsa — ikkalasi,
//  ballari har xil). Qator — shu egadagi ishtirokchilar. Boshqa fanning bali ham, o'quvchining
//  jami bali ham o'qituvchiga berilmaydi.
//
//  YOZISH
//  ------
//  Yozish yo'li bitta — ExamService.SaveEntryAsync; bu yerda faqat qamrov tekshiriladi.
//
//  "KELMADI" — ADMINNIKI
//  ---------------------
//  "Kelmadi" o'quvchining BUTUN imtihondagi ballarini o'chiradi (§8.4), jumladan boshqa
//  o'qituvchilar kiritganini ham. Shuning uchun o'qituvchi uni belgilay olmaydi va admin belgilagan
//  qatorga ball yoza olmaydi.
//
//  QORALAMA KO'RINMAYDI
//  --------------------
//  Birinchi ball bloklarni muzlatadi (SectionsHaveResultsMessage). Admin hali tuzayotgan
//  qoralamani o'qituvchi muzlatib qo'ymasligi uchun faqat e'lon qilingan va yopilgan imtihonlar.
// ===========================================================================

public static class TeacherBlockExamService
{
    /// <summary>Ro'yxatga olinadigan eng yangi imtihonlar soni.</summary>
    public const int MaxExams = 50;

    public const string NotYoursMessage = "Bu imtihon yoki fan sizga biriktirilmagan";
    public const string PupilNotYoursMessage = "Ro'yxatda sizning yo'nalishingizda bo'lmagan o'quvchi bor — sahifani yangilang";
    public const string SectionNotYoursMessage = "Faqat o'z faningiz bloklariga natija kiritasiz";
    public const string AbsentAdminOnlyMessage = "\"Kelmadi\"ni administrator belgilaydi";
    public const string AbsentLockedMessage = "O'quvchi \"Kelmadi\" deb belgilangan — natija kiritish uchun administratorga murojaat qiling";

    /// <summary>O'qituvchining bitta egasi va u yerdagi fanlari.</summary>
    private sealed record Owner(string Id, string Kind, string Name, bool IsTrack, HashSet<string> SubjectIds)
    {
        public Guid? GroupId => Kind == LessonOwnerKind.Group && Guid.TryParse(Id, out var id) ? id : null;
    }

    private sealed record Member(Guid GroupId, string StudentId, DateOnly JoinedOn, DateOnly? LeftOn);

    /// <summary><c>GET /api/teacher/block-exams</c> — yangi imtihon birinchi, keyin ega nomi.</summary>
    public static async Task<List<ExamTeacherRowDto>> ListAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var owners = await OwnersAsync(db, teacherId, ct);
        if (owners.Count == 0) return [];

        var subjectIds = owners.SelectMany(o => o.SubjectIds).Distinct().ToList();
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.Kind == ExamKind.Block && e.Delivery == ExamDelivery.Manual
                && (e.Status == ExamStatus.Published || e.Status == ExamStatus.Closed)
                && db.ExamSections.Any(s => s.ExamId == e.Id && subjectIds.Contains(s.SubjectId)))
            .OrderByDescending(e => e.ExamDate).ThenByDescending(e => e.CreatedAt)
            .Take(MaxExams)
            .ToListAsync(ct);
        if (exams.Count == 0) return [];

        var examIds = exams.Select(e => e.Id).ToList();
        var sections = (await db.ExamSections.AsNoTracking()
                .Where(s => examIds.Contains(s.ExamId) && subjectIds.Contains(s.SubjectId))
                .ToListAsync(ct))
            .ToLookup(s => s.ExamId);
        var participants = (await db.ExamParticipants.AsNoTracking()
                .Where(p => examIds.Contains(p.ExamId) && p.StudentId != null && p.Status != ExamParticipantStatus.Cancelled)
                .ToListAsync(ct))
            .ToLookup(p => p.ExamId);
        var filled = (await (from score in db.ExamSectionScores.AsNoTracking()
                             join p in db.ExamParticipants.AsNoTracking() on score.ParticipantId equals p.Id
                             where examIds.Contains(p.ExamId)
                             select new { score.ParticipantId, score.SectionId })
                .ToListAsync(ct))
            .Select(x => (x.ParticipantId, x.SectionId))
            .ToHashSet();
        var members = await MembersAsync(db, owners, ct);
        var subjectNames = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var typeIds = exams.Select(e => e.ExamTypeId).OfType<string>().Distinct().ToList();
        var typeNames = await db.ExamTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var rows = new List<ExamTeacherRowDto>();
        foreach (var exam in exams)
        {
            var credits = await ExamCertificateCredits.ForAsync(db, exam, sections[exam.Id].ToList(),
                participants[exam.Id].Select(p => (p.Id, p.StudentId)).ToList(), ct);

            foreach (var owner in owners.OrderBy(o => o.Name, StringComparer.Ordinal))
            {
                var mine = sections[exam.Id].Where(s => owner.SubjectIds.Contains(s.SubjectId)).ToList();
                if (mine.Count == 0) continue;
                var pupils = Roster(participants[exam.Id], exam, owner, members);
                if (pupils.Count == 0) continue;

                var done = pupils.Count(p => p.Status == ExamParticipantStatus.Absent
                    || mine.All(s => filled.Contains((p.Id, s.Id))
                        || credits.GetValueOrDefault(p.Id)?.ContainsKey(s.Id) == true));
                var subjects = mine.Select(s => s.SubjectId).Distinct()
                    .Select(id => new ExamTeacherSubjectDto(id, subjectNames.GetValueOrDefault(id, id)))
                    .ToList();
                rows.Add(new ExamTeacherRowDto(
                    exam.Id, exam.Title, exam.ExamDate,
                    exam.ExamTypeId is { } typeId ? typeNames.GetValueOrDefault(typeId) : null,
                    owner.Id, owner.Name, owner.IsTrack, subjects, pupils.Count, done));
            }
        }
        return rows;
    }

    /// <summary>
    /// <c>GET /api/teacher/block-exams/{id}/entry?ownerId=</c> — admin jadvalining o'zi, o'qituvchi
    /// bloklari va shu ega o'quvchilari bilan cheklangan.
    /// </summary>
    public static async Task<ExamOutcome<ExamEntryTableDto>> EntryAsync(
        IAppDbContext db, string teacherId, string examId, string? ownerId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(db, teacherId, examId, ownerId, ct);
        if (scope.Error is { } error) return error;
        var (sectionIds, participants) = scope.Value;

        var full = await ExamService.GetEntryTableAsync(db, examId, ct);
        if (full.Error is { } tableError) return tableError;

        var pupilIds = participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var table = full.Value!;
        return table with
        {
            Columns = table.Columns.Where(c => sectionIds.Contains(c.SectionId)).ToList(),
            Rows = table.Rows.Where(r => pupilIds.Contains(r.ParticipantId)).Select(r => r with
            {
                Scores = Only(r.Scores, sectionIds)!,
                Correct = Only(r.Correct, sectionIds),
                Credits = Only(r.Credits, sectionIds),
                TotalPoints = null,
                MaxPoints = null,
                Percent = null,
            }).ToList(),
        };
    }

    /// <summary>
    /// <c>POST /api/teacher/block-exams/{id}/entry</c>. Qamrovdan tashqaridagi birorta katak bo'lsa,
    /// hech narsa yozilmaydi.
    /// </summary>
    public static async Task<ExamOutcome<ExamEntrySaveResultDto>> SaveAsync(
        IAppDbContext db, ExamActor actor, string teacherId, string examId, ExamTeacherSaveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await ScopeAsync(db, teacherId, examId, request.OwnerId, ct);
        if (scope.Error is { } error) return error;
        var (sectionIds, participants) = scope.Value;

        var byId = participants.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var row in request.Rows ?? [])
        {
            if (row.Absent) return ExamError.Forbidden(AbsentAdminOnlyMessage);
            if (ExamService.Clean(row.ParticipantId) is not { } id || !byId.TryGetValue(id, out var p))
                return ExamError.Forbidden(PupilNotYoursMessage);
            if (p.Status == ExamParticipantStatus.Absent)
                return ExamError.Conflict("participant_absent", AbsentLockedMessage);
            if ((row.Scores ?? []).Any(c => ExamService.Clean(c.SectionId) is not { } s || !sectionIds.Contains(s)))
                return ExamError.Forbidden(SectionNotYoursMessage);
        }

        return await ExamService.SaveEntryAsync(db, actor, examId, request.Rows, ct);
    }

    // =====================================================================

    /// <summary>Imtihon + ega + o'qituvchi → ruxsat etilgan bloklar va o'quvchilar, yoki rad javobi.</summary>
    private static async Task<ExamOutcome<(HashSet<string> SectionIds, List<ExamParticipant> Participants)>> ScopeAsync(
        IAppDbContext db, string teacherId, string examId, string? ownerId, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null || exam.Kind != ExamKind.Block || exam.Delivery != ExamDelivery.Manual
            || exam.Status is not (ExamStatus.Published or ExamStatus.Closed))
            return ExamError.NotFound("exam_not_found", ExamService.ExamNotFoundMessage);

        var id = ExamService.Clean(ownerId);
        var owner = (await OwnersAsync(db, teacherId, ct)).FirstOrDefault(o => o.Id == id);
        if (owner is null) return ExamError.Forbidden(NotYoursMessage);

        var sectionIds = (await db.ExamSections.AsNoTracking()
                .Where(s => s.ExamId == examId && owner.SubjectIds.Contains(s.SubjectId))
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        if (sectionIds.Count == 0) return ExamError.Forbidden(NotYoursMessage);

        var participants = await db.ExamParticipants.AsNoTracking()
            .Where(p => p.ExamId == examId && p.StudentId != null && p.Status != ExamParticipantStatus.Cancelled)
            .ToListAsync(ct);
        var members = await MembersAsync(db, [owner], ct);
        return (sectionIds, Roster(participants, exam, owner, members));
    }

    /// <summary>
    /// O'qituvchining jadvaldagi egalari (faqat tirik va arxivlanmagan) va u yerdagi fanlari. Juftlarni
    /// jurnal qoidasi beradi (<see cref="TeacherOwnerAccess.PairsAsync"/>).
    /// </summary>
    private static async Task<List<Owner>> OwnersAsync(IAppDbContext db, string teacherId, CancellationToken ct)
    {
        var pairs = await TeacherOwnerAccess.PairsAsync(db, teacherId, ct);
        if (pairs.Count == 0) return [];

        var classIds = pairs.Where(p => p.OwnerKind == LessonOwnerKind.Class).Select(p => p.OwnerId).Distinct().ToList();
        var groupIds = pairs.Where(p => p.OwnerKind == LessonOwnerKind.Group)
            .Select(p => Guid.TryParse(p.OwnerId, out var g) ? g : (Guid?)null).OfType<Guid>().Distinct().ToList();

        var classes = await db.Classes.AsNoTracking()
            .Where(c => classIds.Contains(c.Id) && !c.IsArchived)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id) && !g.IsArchived)
            .ToDictionaryAsync(g => g.Id.ToString(), g => (g.Name, g.IsTrack), ct);

        return pairs.GroupBy(p => (p.OwnerId, p.OwnerKind))
            .Select(g =>
            {
                var (ownerId, kind) = g.Key;
                var subjects = g.Select(p => p.SubjectId).ToHashSet(StringComparer.Ordinal);
                if (kind == LessonOwnerKind.Class)
                    return classes.TryGetValue(ownerId, out var name) ? new Owner(ownerId, kind, name, false, subjects) : null;
                return groups.TryGetValue(ownerId, out var group) ? new Owner(ownerId, kind, group.Name, group.IsTrack, subjects) : null;
            })
            .OfType<Owner>()
            .ToList();
    }

    private static async Task<ILookup<Guid, Member>> MembersAsync(
        IAppDbContext db, IReadOnlyCollection<Owner> owners, CancellationToken ct)
    {
        var groupIds = owners.Select(o => o.GroupId).OfType<Guid>().Distinct().ToList();
        if (groupIds.Count == 0) return Array.Empty<Member>().ToLookup(m => m.GroupId);
        return (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => groupIds.Contains(m.GroupId))
                .Select(m => new Member(m.GroupId, m.StudentId, m.JoinedOn, m.LeftOn))
                .ToListAsync(ct))
            .ToLookup(m => m.GroupId);
    }

    /// <summary>
    /// Ishtirokchilardan shu egadagilari. Sinf — sinf nusxasi (§5.7). Guruh — imtihon kuni a'zo bo'lgan
    /// yoki hozir a'zo: boshqa yo'nalishga o'tgan o'quvchining qog'ozini test kunidagi o'qituvchi tekshiradi,
    /// imtihon sanasidan keyin qo'shilgani ham ro'yxatdan tushib qolmaydi.
    /// </summary>
    private static List<ExamParticipant> Roster(
        IEnumerable<ExamParticipant> participants, Exam exam, Owner owner, ILookup<Guid, Member> members)
    {
        if (owner.GroupId is not { } groupId)
            return participants.Where(p => p.ClassId == owner.Id).ToList();

        DateOnly? day = DateOnly.TryParseExact(exam.ExamDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
        var pupils = members[groupId]
            .Where(m => m.LeftOn == null || (day is { } on && m.JoinedOn <= on && m.LeftOn > on))
            .Select(m => m.StudentId)
            .ToHashSet(StringComparer.Ordinal);
        return participants.Where(p => p.StudentId is { } id && pupils.Contains(id)).ToList();
    }

    private static IReadOnlyDictionary<string, T>? Only<T>(IReadOnlyDictionary<string, T>? cells, HashSet<string> sectionIds) =>
        cells?.Where(kv => sectionIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
