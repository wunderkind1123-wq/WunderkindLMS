using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Bloklar — docs/modules/blocks.md, TZ 6.1, 6.2, 7.
//
//  NIMA QILADI
//  -----------
//  Blokni yaratadi/tahrirlaydi (aniq 2 guruh, har birida ustoz), o'quvchilarni
//  taqsimlaydi, sana bilan ko'chiradi va chiqaradi, ustozni sana bilan
//  almashtiradi, faollashtirishdan oldin "hamma taqsimlanganmi" ni tekshiradi
//  va "o'quvchining shu sanadagi guruhi" ga javob beradi (TZ 5.3).
//
//  NIMA QILMAYDI (keyingi bosqichlar)
//  ----------------------------------
//  Dars jadvaliga (4-bosqich: blok katagi, 3–5 va 9-qoidalar), jurnal va
//  davomatga (5-bosqich) TEGMAYDI. Blok guruhlarining o'quv guruhi oddiy
//  guruh bo'lib turadi; uning darslari `group_lessons_enabled` ga qaramasdan
//  "tirik" bo'lishi — 4-bosqich ishi, chunki tirik dars faqat jadvalda paydo
//  bo'ladi.
//
//  SANALAR
//  -------
//  A'zolik va ustoz — SANALI, ikkala chegara ham KIRADI (LessonRoster.Query:
//  `joined_on <= d and (left_on is null or left_on >= d)`). Shuning uchun D
//  sanadan ko'chirish eski qatorni D-1 da yopadi va yangisini D dan ochadi:
//  bir kunda o'quvchi ikkita guruhda bo'lmaydi (1-qoida, "bir sanada").
//  D a'zolik boshlangan kunning o'zi bo'lsa — bu xatoni tuzatish: qator
//  joyida qayta yo'naltiriladi (o'sha davrda baho/davomat bo'lmasa).
//
//  Eski guruhga D dan keyin baho yoki davomat qo'yilgan bo'lsa, D dan
//  ko'chirish RAD etiladi: aks holda baho yangi guruh davriga tushib qolardi,
//  qatordagi `block_group_id` esa eski guruhni ko'rsatardi.
// ===========================================================================

/// <summary>
/// Blok, uning guruhlari, ustozlari va a'zoligi bilan bog'liq barcha qoidalar.
/// Metodlar xato matnini (o'zbekcha) qaytaradi; <c>null</c> — muvaffaqiyat.
/// </summary>
public sealed partial class BlockService(IAppDbContext db)
{
    /// <summary>7-qoida.</summary>
    public const string TwoGroupsMessage = "Blokda 2 ta guruh bo'lishi kerak";

    public const string NameRequiredMessage = "Blok nomini yozing (kamida 3 belgi)";
    public const string SubjectRequiredMessage = "Blok fanini tanlang";
    public const string SourcesRequiredMessage = "Blokka kamida bitta sinf yoki yo'nalish tanlang";
    public const string DuplicateNameMessage = "Shu o'quv yilida bunday nomli blok allaqachon bor";
    public const string ImmutableMessage =
        "Blokning fani va turini o'zgartirib bo'lmaydi — yangi blok oching";
    public const string NotInBlockMessage = "o'quvchi blokning manba sinflarida emas";

    /// <summary>9-qoida xabari (jadvalga qo'yishda ishlatiladi, 4-bosqich).</summary>
    public static string DayPatternMessage(string pattern) => pattern == BlockDayPattern.Odd
        ? "Bu blok faqat toq kunlarda ishlaydi"
        : "Bu blok faqat juft kunlarda ishlaydi";

    private const int MinNameLength = 3;

    [GeneratedRegex(@"^\d{4}-\d{4}$")]
    private static partial Regex AcademicYearPattern();

    /// <summary>Joriy o'quv yili: sentyabrdan avgustgacha — <c>"2026-2027"</c>.</summary>
    public static string AcademicYearOf(DateOnly day) => day.Month >= 9
        ? $"{day.Year}-{day.Year + 1}"
        : $"{day.Year - 1}-{day.Year}";

    public static bool IsAcademicYear(string? value) =>
        value is not null && AcademicYearPattern().IsMatch(value)
        && int.Parse(value[5..], CultureInfo.InvariantCulture) == int.Parse(value[..4], CultureInfo.InvariantCulture) + 1;

    private static string Dmy(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /* -------------------------------------------------------------------
     *  1. Manbalar: sinf yoki yo'nalish
     * ---------------------------------------------------------------- */

    /// <summary>Blokning manbasi. Sinfda <see cref="ClassName"/> — ro'yxat kaliti
    /// (<c>students.class_name</c>); yo'nalishda <see cref="TrackId"/> — a'zolik guruhi.</summary>
    public sealed record Source(string Kind, string Id, string Name, string? ClassName, Guid? TrackId);

    private async Task<Source?> ResolveSourceAsync(BlockSourceRef r, CancellationToken ct)
    {
        if (r.Kind == LessonOwnerKind.Class)
        {
            var cls = await db.Classes.AsNoTracking()
                .Where(c => c.Id == r.Id && !c.IsArchived)
                .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
            return cls is null ? null : new Source(LessonOwnerKind.Class, cls.Id, cls.Name, cls.Name, null);
        }
        if (r.Kind != LessonOwnerKind.Group || !Guid.TryParse(r.Id, out var gid)) return null;
        var track = await db.StudyGroups.AsNoTracking()
            .Where(g => g.Id == gid && g.IsTrack && !g.IsArchived)
            .Select(g => new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        return track is null
            ? null
            : new Source(LessonOwnerKind.Group, track.Id.ToString(), track.Name, null, track.Id);
    }

    /// <summary>Blokning manbalari — nomlari bilan (arxivlangan bo'lsa ham).</summary>
    public async Task<List<Source>> SourcesAsync(Guid blockId, CancellationToken ct = default)
    {
        var refs = await db.BlockClasses.AsNoTracking()
            .Where(c => c.BlockId == blockId).ToListAsync(ct);
        var classIds = refs.Where(r => r.OwnerKind == LessonOwnerKind.Class).Select(r => r.OwnerId).ToList();
        var trackIds = refs.Where(r => r.OwnerKind == LessonOwnerKind.Group)
            .Select(r => Guid.TryParse(r.OwnerId, out var g) ? g : Guid.Empty).ToList();
        var classes = await db.Classes.AsNoTracking().Where(c => classIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var tracks = await db.StudyGroups.AsNoTracking().Where(g => trackIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var result = new List<Source>();
        foreach (var r in refs)
        {
            if (r.OwnerKind == LessonOwnerKind.Class && classes.TryGetValue(r.OwnerId, out var cn))
                result.Add(new Source(r.OwnerKind, r.OwnerId, cn, cn, null));
            else if (Guid.TryParse(r.OwnerId, out var g) && tracks.TryGetValue(g, out var tn))
                result.Add(new Source(r.OwnerKind, r.OwnerId, tn, null, g));
        }
        return [.. result.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Manbalarning SHU SANADAGI o'quvchilari (arxivlanmaganlar): sinf — <c>class_name</c>,
    /// yo'nalish — o'sha sanada ochiq a'zolik. O'quvchi ikki manbada bo'lsa — birinchisida.
    /// </summary>
    public async Task<List<(Student Student, Source Source)>> SourceStudentsAsync(
        IReadOnlyList<Source> sources, DateOnly on, CancellationToken ct = default)
    {
        var result = new List<(Student, Source)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var src in sources)
        {
            var q = db.Students.AsNoTracking().Where(s => !s.IsArchived);
            q = src.TrackId is { } tid
                ? q.Where(s => db.StudyGroupMembers.Any(m => m.GroupId == tid && m.StudentId == s.Id
                    && m.JoinedOn <= on && (m.LeftOn == null || m.LeftOn >= on)))
                : q.Where(s => s.ClassName == src.ClassName);
            foreach (var s in await q.OrderBy(s => s.FullName).ToListAsync(ct))
                if (seen.Add(s.Id)) result.Add((s, src));
        }
        return result;
    }

    /// <summary>Blok o'quv guruhlarini "boqadigan" sinflar: sinf manbalari va
    /// yo'nalishlarni boqadigan sinflar.</summary>
    private async Task<List<string>> FeedingClassIdsAsync(IReadOnlyList<Source> sources, CancellationToken ct)
    {
        var ids = sources.Where(s => s.Kind == LessonOwnerKind.Class).Select(s => s.Id).ToList();
        var trackIds = sources.Where(s => s.TrackId is not null).Select(s => s.TrackId!.Value).ToList();
        if (trackIds.Count > 0)
            ids.AddRange(await db.StudyGroupClasses.AsNoTracking()
                .Where(c => trackIds.Contains(c.GroupId)).Select(c => c.ClassId).ToListAsync(ct));
        return [.. ids.Distinct(StringComparer.Ordinal)];
    }

    /* -------------------------------------------------------------------
     *  2. Blokning o'zi
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Saqlash so'rovini tekshiradi (6- va 7-qoidalar shu yerda). <paramref name="existing"/>
    /// — tahrirlanayotgan blok (yaratishda null).
    /// </summary>
    public async Task<string?> ValidateAsync(
        SaveBlockRequest req, Block? existing, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < MinNameLength) return NameRequiredMessage;
        if (string.IsNullOrWhiteSpace(req.SubjectId)) return SubjectRequiredMessage;
        if (!await db.Subjects.AnyAsync(s => s.Id == req.SubjectId, ct)) return "Fan topilmadi";
        if (!BlockType.All.Contains(req.BlockType)) return "Blok turi noto'g'ri (level yoki base_class)";
        if (!BlockDayPattern.All.Contains(req.DayPattern)) return "Kun naqshi noto'g'ri (odd, even yoki custom)";

        if (existing is not null
            && (existing.SubjectId != req.SubjectId || existing.BlockType != req.BlockType))
            return ImmutableMessage;

        var year = existing?.AcademicYear ?? req.AcademicYear ?? AcademicYearOf(AppClock.Today);
        if (!IsAcademicYear(year)) return "O'quv yili noto'g'ri (masalan 2026-2027)";

        var excludeId = existing?.Id ?? Guid.Empty;
        if (await db.Blocks.AnyAsync(b => b.Id != excludeId && b.AcademicYear == year
                && b.Name.ToLower() == name.ToLower(), ct))
            return DuplicateNameMessage;

        // Manbalar: kamida bitta, takrorsiz, har biri mavjud va arxivlanmagan.
        var refs = (req.Sources ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .DistinctBy(s => (s.Kind, s.Id)).ToList();
        if (refs.Count == 0) return SourcesRequiredMessage;
        var sources = new List<Source>();
        foreach (var r in refs)
        {
            var src = await ResolveSourceAsync(r, ct);
            if (src is null) return "Tanlangan sinf yoki yo'nalishlardan biri topilmadi";
            sources.Add(src);
        }

        // Bitta manba bitta fandan bitta blokda: aks holda o'quvchi ikkita ingliz tili
        // blokiga tushib qolardi ("bitta fandan bitta guruh" ham buni ushlamasdi —
        // taqsimlanmagan bo'lib qolardi).
        foreach (var src in sources)
        {
            var other = await db.BlockClasses
                .Where(c => c.OwnerKind == src.Kind && c.OwnerId == src.Id && c.BlockId != excludeId)
                .Join(db.Blocks, c => c.BlockId, b => b.Id, (c, b) => b)
                .Where(b => b.SubjectId == req.SubjectId && b.AcademicYear == year)
                .Select(b => b.Name).FirstOrDefaultAsync(ct);
            if (other is not null) return $"{src.Name} shu fan bo'yicha allaqachon «{other}» blokida";
        }

        // Manbadan chiqarish: undan taqsimlangan o'quvchilar bo'lsa — rad.
        if (existing is not null)
        {
            var current = await SourcesAsync(existing.Id, ct);
            var removed = current.Where(c => !sources.Any(s => s.Kind == c.Kind && s.Id == c.Id)).ToList();
            if (removed.Count > 0)
            {
                var sgIds = await StudyGroupIdsAsync(existing.Id, ct);
                foreach (var r in removed)
                    if (await db.StudyGroupMembers.AnyAsync(m => sgIds.Contains(m.GroupId)
                            && m.LeftOn == null && m.SourceClassId == r.Id, ct))
                        return $"{r.Name}dan taqsimlangan o'quvchilar bor — avval ularni blokdan chiqaring";
            }
        }

        // 7-qoida: aniq 2 ta guruh.
        var groups = req.Groups ?? [];
        if (groups.Count != 2) return TwoGroupsMessage;
        if (groups.Any(g => string.IsNullOrWhiteSpace(g.Name))) return "Guruh nomini yozing";
        if (string.Equals(groups[0].Name.Trim(), groups[1].Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return "Ikki guruhning nomi bir xil bo'lmasin";

        // 6-qoida: har guruhda ustoz (yaratishda; keyin ustoz sanali amal bilan almashadi).
        if (existing is null)
        {
            foreach (var g in groups)
            {
                if (string.IsNullOrWhiteSpace(g.TeacherId)) return $"{g.Name.Trim()} guruhga ustoz biriktirilmagan";
                if (!await db.Teachers.AnyAsync(t => t.Id == g.TeacherId, ct)) return "Tanlangan ustoz topilmadi";
            }
        }

        if (req.BlockType == BlockType.BaseClass)
        {
            foreach (var g in groups)
            {
                if (g.BaseOwnerKind is null || g.BaseOwnerId is null)
                    return $"{g.Name.Trim()}: manba sinf bo'yicha blokda guruhning sinfini tanlang";
                if (!sources.Any(s => s.Kind == g.BaseOwnerKind && s.Id == g.BaseOwnerId))
                    return $"{g.Name.Trim()}: guruh sinfi blokning manbalaridan biri bo'lishi kerak";
            }
            if (groups[0].BaseOwnerId == groups[1].BaseOwnerId && groups[0].BaseOwnerKind == groups[1].BaseOwnerKind)
                return "Ikki guruhning sinfi bir xil bo'lmasin";
        }
        else if (groups.Any(g => g.BaseOwnerId is not null))
        {
            return "Daraja bo'yicha blokda guruhga sinf biriktirilmaydi";
        }

        foreach (var g in groups.Where(g => g.RoomId is not null))
            if (!await db.Rooms.AnyAsync(r => r.Id == g.RoomId, ct)) return "Tanlangan xona topilmadi";

        // O'quv guruhi nomi (`ux_study_groups_name_active` — fan ichida unikal).
        var ownSg = existing is null ? [] : await StudyGroupIdsAsync(existing.Id, ct);
        foreach (var g in groups)
        {
            var sgName = StudyGroupName(name, g.Name);
            if (await db.StudyGroups.AnyAsync(s => s.SubjectId == req.SubjectId && !s.IsArchived
                    && !ownSg.Contains(s.Id) && s.Name.ToLower() == sgName.ToLower(), ct))
                return $"«{sgName}» nomli guruh shu fanda allaqachon bor — blok yoki guruh nomini o'zgartiring";
        }
        return null;
    }

    /// <summary>Blok guruhining o'quv guruhi nomi: "Fil+Ijt Ingliz · Katta".</summary>
    public static string StudyGroupName(string blockName, string groupName) =>
        $"{blockName.Trim()} · {groupName.Trim()}";

    private Task<List<Guid>> StudyGroupIdsAsync(Guid blockId, CancellationToken ct) =>
        db.BlockGroups.Where(g => g.BlockId == blockId).Select(g => g.StudyGroupId).ToListAsync(ct);

    /// <summary>
    /// Yangi blok: manbalar, 2 guruh (har biri o'z o'quv guruhi bilan) va ustozlar.
    /// Blok FAOL EMAS. SaveChanges CHAQIRILMAYDI. So'rov <see cref="ValidateAsync"/> dan o'tgan.
    /// </summary>
    public async Task<Block> CreateAsync(SaveBlockRequest req, string userId, CancellationToken ct = default)
    {
        var block = new Block
        {
            Name = req.Name.Trim(),
            SubjectId = req.SubjectId,
            AcademicYear = req.AcademicYear ?? AcademicYearOf(AppClock.Today),
            BlockType = req.BlockType,
            DayPattern = req.DayPattern,
            IsActive = false,
            CreatedBy = userId,
            CreatedAt = AppClock.NowInstant,
        };
        db.Blocks.Add(block);

        var sources = new List<Source>();
        foreach (var r in req.Sources.DistinctBy(s => (s.Kind, s.Id)))
        {
            var src = (await ResolveSourceAsync(r, ct))!;
            sources.Add(src);
            db.BlockClasses.Add(new BlockClass { BlockId = block.Id, OwnerKind = src.Kind, OwnerId = src.Id });
        }
        var feeding = await FeedingClassIdsAsync(sources, ct);

        for (var i = 0; i < 2; i++)
        {
            var g = req.Groups[i];
            var sg = new StudyGroup
            {
                Name = StudyGroupName(block.Name, g.Name),
                SubjectId = block.SubjectId,
                CreatedBy = userId,
                CreatedAt = AppClock.NowInstant,
            };
            db.StudyGroups.Add(sg);
            foreach (var cid in feeding)
                db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = sg.Id, ClassId = cid });
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = sg.Id, TeacherId = g.TeacherId! });

            var bg = new BlockGroup
            {
                BlockId = block.Id,
                StudyGroupId = sg.Id,
                Name = g.Name.Trim(),
                Code = block.BlockType == BlockType.BaseClass
                    ? BlockGroupCode.Custom
                    : i == 0 ? BlockGroupCode.Katta : BlockGroupCode.Kichik,
                BaseOwnerKind = g.BaseOwnerKind,
                BaseOwnerId = g.BaseOwnerId,
                RoomId = g.RoomId,
                SortOrder = (short)(i + 1),
            };
            db.BlockGroups.Add(bg);
            db.BlockGroupTeachers.Add(new BlockGroupTeacher
            {
                BlockGroupId = bg.Id,
                TeacherId = g.TeacherId!,
                ValidFrom = AppClock.Today,
                CreatedBy = userId,
                CreatedAt = AppClock.NowInstant,
            });
        }
        return block;
    }

    /// <summary>
    /// Nom, kun naqshi, manbalar, guruh nomlari va xonalarini yangilaydi. Ustoz bu yerda
    /// O'ZGARMAYDI (<see cref="ChangeTeacherAsync"/>). SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task UpdateAsync(Block block, SaveBlockRequest req, CancellationToken ct = default)
    {
        block.Name = req.Name.Trim();
        block.DayPattern = req.DayPattern;

        var wanted = new List<Source>();
        foreach (var r in req.Sources.DistinctBy(s => (s.Kind, s.Id)))
            wanted.Add((await ResolveSourceAsync(r, ct))!);
        var current = await db.BlockClasses.Where(c => c.BlockId == block.Id).ToListAsync(ct);
        db.BlockClasses.RemoveRange(current.Where(c => !wanted.Any(w => w.Kind == c.OwnerKind && w.Id == c.OwnerId)));
        foreach (var w in wanted.Where(w => !current.Any(c => c.OwnerKind == w.Kind && c.OwnerId == w.Id)))
            db.BlockClasses.Add(new BlockClass { BlockId = block.Id, OwnerKind = w.Kind, OwnerId = w.Id });
        var feeding = await FeedingClassIdsAsync(wanted, ct);

        var groups = await db.BlockGroups.Where(g => g.BlockId == block.Id)
            .OrderBy(g => g.SortOrder).ToListAsync(ct);
        for (var i = 0; i < groups.Count && i < req.Groups.Count; i++)
        {
            var bg = groups[i];
            var g = req.Groups[i];
            bg.Name = g.Name.Trim();
            bg.RoomId = g.RoomId;
            bg.BaseOwnerKind = g.BaseOwnerKind;
            bg.BaseOwnerId = g.BaseOwnerId;

            var sg = await db.StudyGroups.FirstAsync(s => s.Id == bg.StudyGroupId, ct);
            sg.Name = StudyGroupName(block.Name, bg.Name);
            var have = await db.StudyGroupClasses.Where(c => c.GroupId == sg.Id).ToListAsync(ct);
            db.StudyGroupClasses.RemoveRange(have.Where(c => !feeding.Contains(c.ClassId, StringComparer.Ordinal)));
            foreach (var cid in feeding.Where(cid => !have.Any(c => c.ClassId == cid)))
                db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = sg.Id, ClassId = cid });
        }
    }

    /// <summary>
    /// Blokni o'chirish — faqat FAOL BO'LMAGAN, jadvalga qo'yilmagan va hech qanday baho/davomat
    /// yozilmagan blok (aks holda tarix yo'qolardi; bunday blokni faolsizlantirish kerak).
    /// SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task<string?> DeleteAsync(Block block, CancellationToken ct = default)
    {
        if (block.IsActive) return "Faol blokni o'chirib bo'lmaydi — avval faolsizlantiring";
        var groups = await db.BlockGroups.Where(g => g.BlockId == block.Id).ToListAsync(ct);
        var bgIds = groups.Select(g => g.Id).ToList();
        var sgIds = groups.Select(g => g.StudyGroupId).ToList();
        var sgIdStrings = sgIds.Select(i => i.ToString()).ToList();

        var used = await db.ScheduleTemplates.SelectMany(t => t.Lessons).AnyAsync(l => l.BlockId == block.Id, ct)
                   || await db.JournalEntries.AnyAsync(j => (j.BlockGroupId != null && bgIds.Contains(j.BlockGroupId.Value))
                       || (j.OwnerKind == LessonOwnerKind.Group && sgIdStrings.Contains(j.ClassId)), ct)
                   || await db.LessonNotes.AnyAsync(n => n.BlockGroupId != null && bgIds.Contains(n.BlockGroupId.Value), ct)
                   || await db.QuarterGrades.AnyAsync(q => q.BlockGroupId != null && bgIds.Contains(q.BlockGroupId.Value), ct)
                   || await db.DisciplinePoints.AnyAsync(p => p.BlockGroupId != null && bgIds.Contains(p.BlockGroupId.Value), ct)
                   || await db.InterimTests.AnyAsync(t => t.BlockGroupId != null && bgIds.Contains(t.BlockGroupId.Value), ct);
        if (used) return "Blok jadvalda yoki unga baho/davomat yozilgan — o'chirib bo'lmaydi, faolsizlantiring";

        db.StudyGroupMembers.RemoveRange(await db.StudyGroupMembers.Where(m => sgIds.Contains(m.GroupId)).ToListAsync(ct));
        db.StudyGroupTeachers.RemoveRange(await db.StudyGroupTeachers.Where(t => sgIds.Contains(t.GroupId)).ToListAsync(ct));
        db.StudyGroupClasses.RemoveRange(await db.StudyGroupClasses.Where(c => sgIds.Contains(c.GroupId)).ToListAsync(ct));
        db.BlockGroupTeachers.RemoveRange(await db.BlockGroupTeachers.Where(t => bgIds.Contains(t.BlockGroupId)).ToListAsync(ct));
        db.BlockGroups.RemoveRange(groups);
        db.BlockClasses.RemoveRange(await db.BlockClasses.Where(c => c.BlockId == block.Id).ToListAsync(ct));
        db.StudyGroups.RemoveRange(await db.StudyGroups.Where(s => sgIds.Contains(s.Id)).ToListAsync(ct));
        db.Blocks.Remove(block);
        return null;
    }

    /* -------------------------------------------------------------------
     *  3. Ustoz — sanali (TZ 6.1)
     * ---------------------------------------------------------------- */

    /// <summary>Guruhning shu sanadagi ustozi.</summary>
    public Task<BlockGroupTeacher?> TeacherOnAsync(Guid blockGroupId, DateOnly on, CancellationToken ct = default) =>
        db.BlockGroupTeachers.AsNoTracking()
            .Where(t => t.BlockGroupId == blockGroupId && t.ValidFrom <= on && (t.ValidTo == null || t.ValidTo >= on))
            .OrderByDescending(t => t.ValidFrom).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Ustozni <paramref name="from"/> sanadan almashtiradi: eski qator D-1 da yopiladi,
    /// yangisi D dan ochiladi — o'sha kungacha qo'yilgan baholar eski ustozda qoladi.
    /// D eski ustoz boshlagan kunning o'zi bo'lsa (va guruhda o'sha kundan baho/davomat
    /// yo'q) — xatoni tuzatish: qator joyida yangilanadi. SaveChanges'ni o'zi chaqiradi
    /// (bitta joriy ustoz indeksi bir lahzada ikki ochiq qatorni ko'rmasligi kerak);
    /// chaqiruvchi tranzaksiya ochadi.
    /// </summary>
    public async Task<string?> ChangeTeacherAsync(
        BlockGroup group, string teacherId, DateOnly from, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacherId)) return "Yangi ustozni tanlang";
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) return "Tanlangan ustoz topilmadi";

        var current = await db.BlockGroupTeachers
            .FirstOrDefaultAsync(t => t.BlockGroupId == group.Id && t.ValidTo == null, ct);
        if (current is not null)
        {
            if (current.TeacherId == teacherId) return "Bu ustoz allaqachon shu guruhda";
            if (from < current.ValidFrom)
                return $"Almashtirish sanasi joriy ustoz boshlagan kundan ({Dmy(current.ValidFrom)}) oldin bo'lishi mumkin emas";
            if (await GroupHasDataFromAsync(group.Id, from, ct))
                return $"{Dmy(from)} dan keyin guruhda baho yoki davomat qo'yilgan — almashtirish sanasini keyinroq tanlang";

            if (from == current.ValidFrom)
            {
                current.TeacherId = teacherId;
                current.CreatedBy = userId;
                current.CreatedAt = AppClock.NowInstant;
                await db.SaveChangesAsync(ct);
                await SyncStudyGroupTeachersAsync(group, ct);
                return null;
            }
            current.ValidTo = from.AddDays(-1);
            await db.SaveChangesAsync(ct);
        }

        db.BlockGroupTeachers.Add(new BlockGroupTeacher
        {
            BlockGroupId = group.Id,
            TeacherId = teacherId,
            ValidFrom = from,
            CreatedBy = userId,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync(ct);
        await SyncStudyGroupTeachersAsync(group, ct);
        return null;
    }

    /// <summary>
    /// O'quv guruhining o'qituvchilar ro'yxati (<c>study_group_teachers</c>) — bugun yoki
    /// kelajakda dars beradigan ustozlar. Sanaga aniq javobni <see cref="TeacherOnAsync"/> beradi.
    /// </summary>
    private async Task SyncStudyGroupTeachersAsync(BlockGroup group, CancellationToken ct)
    {
        var today = AppClock.Today;
        var wanted = await db.BlockGroupTeachers
            .Where(t => t.BlockGroupId == group.Id && (t.ValidTo == null || t.ValidTo >= today))
            .Select(t => t.TeacherId).Distinct().ToListAsync(ct);
        var have = await db.StudyGroupTeachers.Where(t => t.GroupId == group.StudyGroupId).ToListAsync(ct);
        db.StudyGroupTeachers.RemoveRange(have.Where(h => !wanted.Contains(h.TeacherId)));
        foreach (var id in wanted.Where(id => !have.Any(h => h.TeacherId == id)))
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.StudyGroupId, TeacherId = id });
        await db.SaveChangesAsync(ct);
    }

    private Task<bool> GroupHasDataFromAsync(Guid blockGroupId, DateOnly from, CancellationToken ct)
    {
        var iso = Iso(from);
        return db.JournalEntries.AnyAsync(j => j.BlockGroupId == blockGroupId && string.Compare(j.Date, iso) >= 0, ct);
    }

    private Task<bool> StudentHasDataFromAsync(Guid blockGroupId, string studentId, DateOnly from, CancellationToken ct)
    {
        var iso = Iso(from);
        return db.JournalEntries.AnyAsync(j => j.BlockGroupId == blockGroupId && j.StudentId == studentId
                                               && string.Compare(j.Date, iso) >= 0, ct);
    }

    /* -------------------------------------------------------------------
     *  4. A'zolik: taqsimlash, ko'chirish, chiqarish (TZ 6.2, 1- va 8-qoidalar)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// O'quvchining shu sanadagi blok guruhi (TZ 5.3). Topilmasa — null.
    /// </summary>
    public async Task<BlockGroup?> GroupOnAsync(Guid blockId, string studentId, DateOnly on, CancellationToken ct = default)
    {
        var sgId = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId && m.JoinedOn <= on && (m.LeftOn == null || m.LeftOn >= on))
            .Join(db.BlockGroups.Where(g => g.BlockId == blockId), m => m.GroupId, g => g.StudyGroupId, (m, g) => g.Id)
            .FirstOrDefaultAsync(ct);
        return sgId == Guid.Empty
            ? null
            : await db.BlockGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == sgId, ct);
    }

    /// <summary>Blokning guruhlari tartib bo'yicha.</summary>
    public Task<List<BlockGroup>> GroupsAsync(Guid blockId, CancellationToken ct = default) =>
        db.BlockGroups.Where(g => g.BlockId == blockId).OrderBy(g => g.SortOrder).ToListAsync(ct);

    /// <summary>8-qoida: manba sinf bo'yicha blokda manba sinf o'quvchisi faqat o'z guruhida.</summary>
    private static string? BaseClassError(Block block, List<BlockGroup> groups, Source source, BlockGroup target)
    {
        if (block.BlockType != BlockType.BaseClass) return null;
        var own = groups.FirstOrDefault(g => g.BaseOwnerKind == source.Kind && g.BaseOwnerId == source.Id);
        return own is not null && own.Id != target.Id
            ? $"{source.Name} o'quvchisini {target.Name}ga o'tkazib bo'lmaydi"
            : null;
    }

    private sealed record Ctx(
        List<BlockGroup> Groups, List<Guid> StudyGroupIds,
        Dictionary<string, (Student Student, Source Source)> Pupils);

    private async Task<Ctx> ContextAsync(Block block, DateOnly on, CancellationToken ct)
    {
        var groups = await GroupsAsync(block.Id, ct);
        var pupils = (await SourceStudentsAsync(await SourcesAsync(block.Id, ct), on, ct))
            .ToDictionary(p => p.Student.Id, StringComparer.Ordinal);
        return new Ctx(groups, [.. groups.Select(g => g.StudyGroupId)], pupils);
    }

    /// <summary>
    /// TAQSIMLANMAGAN o'quvchilarni guruhga qo'shadi (D sanadan). Boshqa guruhda turgan
    /// o'quvchi — 1-qoida xatosi ("allaqachon Katta guruhida"): uni ko'chirish amali
    /// bilan o'tkazish kerak. Shu guruhda turgani jim o'tkaziladi. SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task<string?> AssignAsync(
        Block block, IReadOnlyList<string> studentIds, Guid groupId, DateOnly on, string userId,
        CancellationToken ct = default)
    {
        var c = await ContextAsync(block, on, ct);
        var target = c.Groups.FirstOrDefault(g => g.Id == groupId);
        if (target is null) return "Guruh bu blokka tegishli emas";

        foreach (var id in Distinct(studentIds))
        {
            var error = await CheckJoinAsync(block, c, id, target, on, ct);
            if (error == "") continue;
            if (error is not null) return error;
            var (student, source) = c.Pupils[id];
            db.StudyGroupMembers.Add(NewMember(block, target, student.Id, source, on, null, userId));
        }
        return null;
    }

    /// <summary>
    /// Qo'shishdan oldingi tekshiruv. <c>null</c> — qo'shsa bo'ladi, <c>""</c> — allaqachon
    /// shu guruhda (o'tkazib yuboriladi), boshqa matn — xato.
    /// </summary>
    private async Task<string?> CheckJoinAsync(
        Block block, Ctx c, string studentId, BlockGroup target, DateOnly on, CancellationToken ct)
    {
        if (!c.Pupils.TryGetValue(studentId, out var p))
        {
            var name = await db.Students.Where(s => s.Id == studentId).Select(s => s.FullName).FirstOrDefaultAsync(ct);
            return name is null ? "Tanlangan o'quvchilardan biri topilmadi" : $"{name}: {NotInBlockMessage}";
        }

        var rows = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId && m.SubjectId == block.SubjectId
                        && (m.LeftOn == null || m.LeftOn >= on))
            .ToListAsync(ct);
        var open = rows.FirstOrDefault(m => m.LeftOn == null);
        if (open is not null && open.GroupId == target.StudyGroupId) return "";
        if (rows.Count > 0)
        {
            var other = rows.OrderBy(m => m.LeftOn is null).First();
            var inBlock = c.Groups.FirstOrDefault(g => g.StudyGroupId == other.GroupId);
            var groupName = inBlock?.Name
                ?? await db.StudyGroups.Where(g => g.Id == other.GroupId).Select(g => g.Name).FirstAsync(ct);
            return inBlock is not null
                ? $"{p.Student.FullName} allaqachon «{groupName}» guruhida"
                : $"{p.Student.FullName} shu fan bo'yicha boshqa guruhda («{groupName}»)";
        }

        return BaseClassError(block, c.Groups, p.Source, target);
    }

    private static StudyGroupMember NewMember(
        Block block, BlockGroup group, string studentId, Source source, DateOnly on, string? reason, string userId) => new()
    {
        GroupId = group.StudyGroupId,
        SubjectId = block.SubjectId,
        StudentId = studentId,
        JoinedOn = on,
        SourceClassId = source.Id,
        JoinReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        CreatedBy = userId,
        CreatedAt = AppClock.NowInstant,
    };

    /// <summary>
    /// Manba sinf bo'yicha blokda: guruhi bor manba sinf o'quvchilarini o'z guruhiga avtomatik
    /// qo'shadi (TZ 6.1). Allaqachon taqsimlanganlar tegilmaydi. Nechta qo'shilgani qaytadi.
    /// SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task<int> AutoAssignAsync(Block block, DateOnly on, string userId, CancellationToken ct = default)
    {
        if (block.BlockType != BlockType.BaseClass) return 0;
        var c = await ContextAsync(block, on, ct);
        var added = 0;
        foreach (var (id, p) in c.Pupils)
        {
            var target = c.Groups.FirstOrDefault(g => g.BaseOwnerKind == p.Source.Kind && g.BaseOwnerId == p.Source.Id);
            if (target is null) continue;
            if (await CheckJoinAsync(block, c, id, target, on, ct) is not null) continue;
            db.StudyGroupMembers.Add(NewMember(block, target, id, p.Source, on, "Manba sinf bo'yicha avtomatik", userId));
            added++;
        }
        return added;
    }

    /// <summary>
    /// Yil o'rtasida ko'chirish (TZ 6.2): eski a'zolik D-1 da yopiladi, yangisi D dan ochiladi.
    /// Faqat SHU blokka ta'sir qiladi. SaveChanges'ni o'zi chaqiradi (ikki bosqich — qisman
    /// unikal indekslar bir lahzada ikki ochiq qatorni ko'rmasligi kerak); chaqiruvchi
    /// tranzaksiya ochadi.
    /// </summary>
    public async Task<string?> MoveAsync(
        Block block, IReadOnlyList<string> studentIds, Guid groupId, DateOnly on, string? reason, string userId,
        CancellationToken ct = default)
    {
        var c = await ContextAsync(block, on, ct);
        var target = c.Groups.FirstOrDefault(g => g.Id == groupId);
        if (target is null) return "Guruh bu blokka tegishli emas";

        var toOpen = new List<StudyGroupMember>();
        foreach (var id in Distinct(studentIds))
        {
            var open = await db.StudyGroupMembers.FirstOrDefaultAsync(
                m => m.StudentId == id && m.LeftOn == null && c.StudyGroupIds.Contains(m.GroupId), ct);
            var name = c.Pupils.TryGetValue(id, out var p)
                ? p.Student.FullName
                : await db.Students.Where(s => s.Id == id).Select(s => s.FullName).FirstOrDefaultAsync(ct);
            if (name is null) return "Tanlangan o'quvchilardan biri topilmadi";
            if (open is null) return $"{name} hali taqsimlanmagan — avval guruhga qo'shing";
            if (open.GroupId == target.StudyGroupId) continue;
            if (p.Student is null) return $"{name}: {Dmy(on)} sanada {NotInBlockMessage}";

            var from = c.Groups.First(g => g.StudyGroupId == open.GroupId);
            var rule8 = BaseClassError(block, c.Groups, p.Source, target);
            if (rule8 is not null) return rule8;
            if (on < open.JoinedOn)
                return $"{name}: ko'chirish sanasi guruhga qo'shilgan kundan ({Dmy(open.JoinedOn)}) oldin bo'lishi mumkin emas";
            if (await StudentHasDataFromAsync(from.Id, id, on, ct))
                return $"{name}: {Dmy(on)} dan keyin «{from.Name}» guruhida baho yoki davomat qo'yilgan — sanani keyinroq tanlang";

            var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (on == open.JoinedOn)
            {
                // Xatoni tuzatish: a'zolik hali bir kun ham ishlamagan — joyida qayta yo'naltiriladi.
                open.GroupId = target.StudyGroupId;
                open.SourceClassId = p.Source.Id;
                open.JoinReason = why;
                open.CreatedBy = userId;
                continue;
            }
            open.LeftOn = on.AddDays(-1);
            open.LeaveReason = why ?? $"{from.Name} → {target.Name}";
            toOpen.Add(NewMember(block, target, id, p.Source, on, why, userId));
        }

        await db.SaveChangesAsync(ct);
        db.StudyGroupMembers.AddRange(toOpen);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Blokdan chiqarish: D sanadan boshlab o'quvchi blok guruhida emas (a'zolik D-1 da
    /// yopiladi). D a'zolik boshlangan kunning o'zi bo'lsa — a'zolik bir kun ham ishlamagan,
    /// qator o'chiriladi. SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task<string?> UnassignAsync(
        Block block, IReadOnlyList<string> studentIds, DateOnly on, string? reason, CancellationToken ct = default)
    {
        var groups = await GroupsAsync(block.Id, ct);
        var sgIds = groups.Select(g => g.StudyGroupId).ToList();
        foreach (var id in Distinct(studentIds))
        {
            var open = await db.StudyGroupMembers.FirstOrDefaultAsync(
                m => m.StudentId == id && m.LeftOn == null && sgIds.Contains(m.GroupId), ct);
            if (open is null) continue;
            var name = await db.Students.Where(s => s.Id == id).Select(s => s.FullName).FirstAsync(ct);
            var group = groups.First(g => g.StudyGroupId == open.GroupId);
            if (on < open.JoinedOn)
                return $"{name}: sana guruhga qo'shilgan kundan ({Dmy(open.JoinedOn)}) oldin bo'lishi mumkin emas";
            if (await StudentHasDataFromAsync(group.Id, id, on, ct))
                return $"{name}: {Dmy(on)} dan keyin «{group.Name}» guruhida baho yoki davomat qo'yilgan — sanani keyinroq tanlang";

            if (on == open.JoinedOn)
            {
                db.StudyGroupMembers.Remove(open);
                continue;
            }
            open.LeftOn = on.AddDays(-1);
            open.LeaveReason = string.IsNullOrWhiteSpace(reason) ? "Blokdan chiqarildi" : reason.Trim();
        }
        return null;
    }

    /* -------------------------------------------------------------------
     *  5. Ro'yxat va faollashtirish (2-qoida)
     * ---------------------------------------------------------------- */

    /// <summary>Taqsimlash ekrani: ikki guruh ustuni va "Taqsimlanmagan" (shu sanada).</summary>
    public async Task<BlockRosterDto> RosterAsync(Block block, DateOnly on, CancellationToken ct = default)
    {
        var c = await ContextAsync(block, on, ct);
        var members = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => c.StudyGroupIds.Contains(m.GroupId) && m.JoinedOn <= on && (m.LeftOn == null || m.LeftOn >= on))
            .ToListAsync(ct);

        // Manbadan chiqib ketgan, lekin hali guruhda turgan o'quvchi ham ko'rinsin (masalan
        // boshqa sinfga o'tgan) — aks holda uni blokdan chiqarib bo'lmasdi.
        var extraIds = members.Select(m => m.StudentId).Where(id => !c.Pupils.ContainsKey(id)).ToList();
        var extras = await db.Students.AsNoTracking().Where(s => extraIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var sourceNames = (await SourcesAsync(block.Id, ct)).ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);

        BlockMemberDto Dto(string id, StudyGroupMember? m)
        {
            if (c.Pupils.TryGetValue(id, out var p))
                return new BlockMemberDto(id, p.Student.FullName, p.Source.Kind, p.Source.Id, p.Source.Name,
                    m is null ? null : c.Groups.First(g => g.StudyGroupId == m.GroupId).Id, m?.Id, m?.JoinedOn);
            var src = m?.SourceClassId ?? "";
            return new BlockMemberDto(id, extras.GetValueOrDefault(id, ""), "", src, sourceNames.GetValueOrDefault(src, ""),
                m is null ? null : c.Groups.First(g => g.StudyGroupId == m.GroupId).Id, m?.Id, m?.JoinedOn);
        }

        var byStudent = members.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var groups = c.Groups.Select(g => new BlockRosterGroupDto(g.Id, g.Name,
            [.. members.Where(m => m.GroupId == g.StudyGroupId).Select(m => Dto(m.StudentId, m))
                .OrderBy(d => d.FullName, StringComparer.Ordinal)])).ToList();
        var unassigned = c.Pupils.Keys.Where(id => !byStudent.ContainsKey(id)).Select(id => Dto(id, null))
            .OrderBy(d => d.SourceName, StringComparer.Ordinal).ThenBy(d => d.FullName, StringComparer.Ordinal).ToList();
        return new BlockRosterDto(on, groups, unassigned);
    }

    /// <summary>
    /// Faollashtirish to'siqlari (TZ 7: 2- va 6-qoidalar): har guruhning ustozi bor va
    /// manbalarning barcha o'quvchilari biror guruhga taqsimlangan. Bo'sh — faollashtirsa bo'ladi.
    /// Kunlik ogohlantirish ham shu ro'yxatni ishlatadi.
    /// </summary>
    public async Task<List<string>> ActivationErrorsAsync(Block block, DateOnly on, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var g in await GroupsAsync(block.Id, ct))
            if (await TeacherOnAsync(g.Id, on, ct) is null)
                errors.Add($"{g.Name} guruhga ustoz biriktirilmagan");

        var roster = await RosterAsync(block, on, ct);
        errors.AddRange(roster.Unassigned
            .GroupBy(u => u.SourceName)
            .Select(g => $"{g.Key}dan {g.Count()} o'quvchi taqsimlanmagan"));
        return errors;
    }

    private static List<string> Distinct(IReadOnlyList<string>? ids) =>
        ids is null
            ? []
            : [.. ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal)];
}
