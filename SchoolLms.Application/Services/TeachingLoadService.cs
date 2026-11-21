using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Yuklama, ustoz bandligi, dars xonalari va avtomatik jadval (mijoz, 2026-11-21: "dars jadvali bilan
//  bog'liq hamma narsani boshqarish" — 1- va 7-bandlar). Hisoblash yadrosi — TimetableSolver.
//
//  "Joriy jadval" — eganing joriy (yoki kelgusi) haftaga biriktirilgan shabloni
//  (<see cref="LiveScheduleEdit.TemplateRankAsync"/> ≥ 2). Generatsiyaga kirmagan egalarning
//  joriy jadvali va bloklar — o'zgarmas bandlik: generator ularning ustoz/xona/soatini band deb biladi.
//
//  Sinfning o'z xonasi — <see cref="SchoolClass.Room"/> matni reyestrdagi xona nomiga teng bo'lsa
//  (BlockScheduleService 5-qoidasi bilan bir xil). Ikki sinf bitta xonani ko'rsatgan bo'lsa — ular
//  uchun o'z xonasi hisobga olinmaydi (eski ma'lumotdagi tasodifiy takror generatorni to'xtatmasin).
// ===========================================================================

public static class TeachingLoadService
{
    private static readonly string[] DayNames = ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba"];

    // ------------------------------------------------------------------ rooms

    /// <summary>Faol xonalar va har eganing (sinfning) o'z xonasi.</summary>
    public static async Task<ScheduleRoomsDto> RoomsAsync(IAppDbContext db, CancellationToken ct)
    {
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(ct);
        var home = await HomeRoomsAsync(db, ct);
        return new ScheduleRoomsDto(
            [.. rooms.Select(r => new ScheduleRoomDto(r.Id, r.Name, r.Kind, r.Capacity))], home);
    }

    /// <summary>Sinf id → o'z xonasi (rooms.id). Takrorlangan xona va reyestrda yo'q nom — chiqarilmaydi.</summary>
    public static async Task<Dictionary<string, Guid>> HomeRoomsAsync(IAppDbContext db, CancellationToken ct)
    {
        var byName = (await db.Rooms.AsNoTracking().ToListAsync(ct))
            .GroupBy(r => r.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived && c.Room != null && c.Room != "")
            .Select(c => new { c.Id, c.Room }).ToListAsync(ct);
        return classes
            .Select(c => (c.Id, Room: byName.TryGetValue(c.Room!.Trim().ToLowerInvariant(), out var id) ? id : (Guid?)null))
            .Where(x => x.Room is not null)
            .GroupBy(x => x.Room!.Value).Where(g => g.Count() == 1)
            .ToDictionary(g => g.First().Id, g => g.Key, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------ loads

    public static async Task<List<TeachingLoadDto>> ListAsync(IAppDbContext db, CancellationToken ct) =>
        [.. (await db.TeachingLoads.AsNoTracking().ToListAsync(ct))
            .OrderBy(l => l.OwnerId).ThenBy(l => l.SubjectId).ThenBy(l => l.SubGroup)
            .Select(ToDto)];

    private static TeachingLoadDto ToDto(TeachingLoad l) =>
        new(l.Id, l.OwnerKind, l.OwnerId, l.SubjectId, l.SubGroup, l.TeacherId, l.HoursPerWeek, l.RoomId);

    /// <summary>Eganing yuklamasini to'liq almashtirish. Xato bo'lsa — matn, hech narsa saqlanmaydi.</summary>
    public static async Task<string?> SaveOwnerAsync(
        IAppDbContext db, LessonOwner owner, IReadOnlyList<TeachingLoadDto> rows, CancellationToken ct)
    {
        if (owner.IsBlock) return "Blok guruhining jadvali \"Bloklar\" bo'limidan boshqariladi";
        foreach (var r in rows)
        {
            if (r.HoursPerWeek is < 1 or > 20) return "Haftalik soat 1 dan 20 gacha bo'lishi kerak";
            if (r.SubGroup is < 0 or > 2) return "Guruh 0, 1 yoki 2 bo'lishi kerak";
            if (r.SubGroup != 0 && !owner.HasHalves) return "Bu guruhda darslar yarmiga bo'linmaydi";
            if (string.IsNullOrWhiteSpace(r.SubjectId) || string.IsNullOrWhiteSpace(r.TeacherId)) return "Fan va o'qituvchini tanlang";
        }
        if (rows.GroupBy(r => (r.SubjectId, r.SubGroup)).Any(g => g.Count() > 1))
            return "Bitta fan (va guruh) ikki marta yozilgan";
        if (rows.GroupBy(r => r.SubjectId).Any(g => g.Any(r => r.SubGroup == 0) && g.Any(r => r.SubGroup != 0)))
            return "Bir fan ham butun sinfga, ham yarmiga bo'lingan bo'lolmaydi";

        var subjectIds = rows.Select(r => r.SubjectId).Distinct().ToList();
        var teacherIds = rows.Select(r => r.TeacherId).Distinct().ToList();
        var roomIds = rows.Select(r => r.RoomId).OfType<Guid>().Distinct().ToList();
        if (await db.Subjects.CountAsync(s => subjectIds.Contains(s.Id), ct) != subjectIds.Count) return "Fan topilmadi";
        if (await db.Teachers.CountAsync(t => teacherIds.Contains(t.Id), ct) != teacherIds.Count) return "O'qituvchi topilmadi";
        if (await db.Rooms.CountAsync(r => roomIds.Contains(r.Id), ct) != roomIds.Count) return "Xona topilmadi";

        db.TeachingLoads.RemoveRange(await db.TeachingLoads.Where(l => l.OwnerId == owner.Id).ToListAsync(ct));
        var now = AppClock.Now;
        foreach (var r in rows)
            db.TeachingLoads.Add(new TeachingLoad
            {
                OwnerKind = owner.Kind, OwnerId = owner.Id, SubjectId = r.SubjectId, SubGroup = r.SubGroup,
                TeacherId = r.TeacherId, HoursPerWeek = r.HoursPerWeek, RoomId = r.RoomId, UpdatedAt = now,
            });
        return null;
    }

    /// <summary>
    /// Yuklamani jadvaldan to'ldirish: har ega × fan × yarmi — darslar soni (bir vaqtdagi takror bitta),
    /// eng ko'p uchragan ustoz va xona. Blok darslari olinmaydi. Saqlashni chaqiruvchi qiladi.
    /// </summary>
    public static async Task<FillLoadsResultDto> FillFromScheduleAsync(IAppDbContext db, FillLoadsRequest req, CancellationToken ct)
    {
        var owners = (await LessonRoster.PickerOwnersAsync(db, ct: ct)).ToList();
        var templates = await CurrentTemplatesAsync(db, owners.Select(o => o.Id).ToList(), req.TemplateName, ct);
        var existing = (await db.TeachingLoads.ToListAsync(ct)).ToLookup(l => l.OwnerId, StringComparer.Ordinal);
        int filled = 0, rows = 0, skipped = 0;
        var now = AppClock.Now;
        foreach (var o in owners)
        {
            if (!templates.TryGetValue(o.Id, out var tpl)) continue;
            if (existing[o.Id].Any())
            {
                if (!req.Overwrite) { skipped++; continue; }
                db.TeachingLoads.RemoveRange(existing[o.Id]);
            }
            var lessons = tpl.Lessons.Where(l => l.BlockId is null && !string.IsNullOrEmpty(l.SubjectId)).ToList();
            var groups = lessons.GroupBy(l => (l.SubjectId, l.SubGroup)).ToList();
            // Bir fan ham butun sinfga, ham yarmiga bo'lingan bo'lsa — yarmlari qoladi (generator buni qo'llamaydi).
            var splitSubjects = groups.Where(g => g.Key.SubGroup != 0).Select(g => g.Key.SubjectId).ToHashSet(StringComparer.Ordinal);
            foreach (var g in groups)
            {
                if (g.Key.SubGroup == 0 && splitSubjects.Contains(g.Key.SubjectId)) continue;
                var teacher = g.Where(l => !string.IsNullOrEmpty(l.TeacherId)).GroupBy(l => l.TeacherId)
                    .OrderByDescending(x => x.Count()).FirstOrDefault()?.Key;
                if (teacher is null) continue;
                var room = g.Where(l => l.RoomId is not null).GroupBy(l => l.RoomId).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key;
                db.TeachingLoads.Add(new TeachingLoad
                {
                    OwnerKind = o.Kind, OwnerId = o.Id, SubjectId = g.Key.SubjectId, SubGroup = g.Key.SubGroup,
                    TeacherId = teacher, HoursPerWeek = Math.Clamp(g.Select(l => (l.Day, l.Period)).Distinct().Count(), 1, 20),
                    RoomId = room, UpdatedAt = now,
                });
                rows++;
            }
            filled++;
        }
        return new FillLoadsResultDto(filled, rows, skipped);
    }

    /// <summary>Ega id → joriy (yoki berilgan nomdagi) shablon, darslari bilan.</summary>
    private static async Task<Dictionary<string, ScheduleTemplate>> CurrentTemplatesAsync(
        IAppDbContext db, List<string> ownerIds, string? name, CancellationToken ct)
    {
        var all = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
            .Where(t => ownerIds.Contains(t.ClassId) && !t.IsArchived).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(name))
            return all.Where(t => t.Name == name).GroupBy(t => t.ClassId)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        return all.Where(t => rank(t.Id) >= 2).GroupBy(t => t.ClassId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => rank(t.Id)).First(), StringComparer.Ordinal);
    }


    // ------------------------------------------------------------------ o'quv kunlari

    /// <summary>Sukut: 1–3-sinf — 5 kun (dushanba–juma), 4-sinfdan yuqori va guruhlar — 6 kun (mijoz, 2026-11-21).</summary>
    public static int DefaultDays(LessonOwnerItem o) => o.Kind == LessonOwnerKind.Class && o.Grade is >= 1 and <= 3 ? 5 : 6;

    private static async Task<Dictionary<string, int>> StudyDaysMapAsync(IAppDbContext db, CancellationToken ct) =>
        await db.OwnerStudyDays.AsNoTracking().ToDictionaryAsync(x => x.OwnerId, x => x.Days, StringComparer.Ordinal, ct);

    /// <summary>Har eganing o'quv kunlari (saqlangan yoki sukut).</summary>
    public static async Task<List<OwnerStudyDaysDto>> StudyDaysAsync(IAppDbContext db, CancellationToken ct)
    {
        var saved = await StudyDaysMapAsync(db, ct);
        return [.. (await LessonRoster.PickerOwnersAsync(db, ct: ct)).Select(o =>
            new OwnerStudyDaysDto(o.Id, saved.GetValueOrDefault(o.Id, DefaultDays(o)), !saved.ContainsKey(o.Id)))];
    }

    public static async Task<string?> SaveStudyDaysAsync(IAppDbContext db, LessonOwner owner, int days, CancellationToken ct)
    {
        if (days is not (5 or 6)) return "O'quv kunlari 5 yoki 6 bo'lishi kerak";
        var row = await db.OwnerStudyDays.FirstOrDefaultAsync(x => x.OwnerId == owner.Id, ct);
        if (row is null) db.OwnerStudyDays.Add(new OwnerStudyDays { OwnerId = owner.Id, OwnerKind = owner.Kind, Days = days });
        else row.Days = days;
        return null;
    }
    // ------------------------------------------------------------------ unavailability

    public static async Task<List<TeacherUnavailabilityDto>> UnavailabilityAsync(IAppDbContext db, CancellationToken ct) =>
        [.. (await db.TeacherUnavailability.AsNoTracking().ToListAsync(ct))
            .GroupBy(u => u.TeacherId)
            .Select(g => new TeacherUnavailabilityDto(g.Key,
                [.. g.OrderBy(u => u.Day).ThenBy(u => u.Period).Select(u => new UnavailabilityDto(u.Day, u.Period))]))];

    public static async Task<string?> SaveUnavailabilityAsync(
        IAppDbContext db, string teacherId, IReadOnlyList<UnavailabilityDto> slots, CancellationToken ct)
    {
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) return "O'qituvchi topilmadi";
        if (slots.Any(s => s.Day is < 0 or > 5 || s.Period is < 0 or > 12)) return "Kun yoki soat noto'g'ri";
        db.TeacherUnavailability.RemoveRange(await db.TeacherUnavailability.Where(u => u.TeacherId == teacherId).ToListAsync(ct));
        // Butun kun belgilangan bo'lsa, o'sha kunning alohida soatlari ortiqcha.
        var wholeDays = slots.Where(s => s.Period == 0).Select(s => s.Day).ToHashSet();
        foreach (var s in slots.Where(s => s.Period == 0 || !wholeDays.Contains(s.Day)).Distinct())
            db.TeacherUnavailability.Add(new TeacherUnavailability { TeacherId = teacherId, Day = s.Day, Period = s.Period });
        return null;
    }

    // ------------------------------------------------------------------ generate

    /// <summary>
    /// Yuklamadan yangi qoralama jadval: har egaga <see cref="GenerateScheduleRequest.Name"/> nomli shablon.
    /// Haftaga biriktirilmaydi — sinflarga ta'sir qilmaydi, "Nashr qilish" kerak. Saqlashni chaqiruvchi qiladi.
    /// </summary>
    public static async Task<(GenerateResultDto? Result, string? Error)> GenerateAsync(
        IAppDbContext db, GenerateScheduleRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Jadval nomini kiriting");
        if (name.Length > 100) return (null, "Nom 100 belgidan oshmasin");

        var loads = await db.TeachingLoads.AsNoTracking().ToListAsync(ct);
        var pickers = await LessonRoster.PickerOwnersAsync(db, ct: ct);
        var wanted = req.OwnerIds is { Count: > 0 } ? req.OwnerIds.ToHashSet(StringComparer.Ordinal) : null;
        var ownerItems = pickers.Where(o => loads.Any(l => l.OwnerId == o.Id) && (wanted is null || wanted.Contains(o.Id))).ToList();
        if (ownerItems.Count == 0) return (null, "Yuklama kiritilmagan — avval \"Yuklama\" jadvalini to'ldiring");
        var ownerIds = ownerItems.Select(o => o.Id).ToList();

        if (await db.ScheduleTemplates.AnyAsync(t => ownerIds.Contains(t.ClassId) && t.Name == name && !t.IsArchived, ct))
            return (null, $"\"{name}\" nomli jadval allaqachon bor — boshqa nom tanlang");

        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var bell = await BellScheduleService.OverviewAsync(db, ct);
        var bellByOwner = bell.Schedules.SelectMany(s => s.OwnerIds.Select(o => (o, s.Periods)))
            .GroupBy(x => x.o).ToDictionary(g => g.Key, g => g.First().Periods, StringComparer.Ordinal);
        List<SolverPeriod> PeriodsOf(string ownerId)
        {
            var list = bellByOwner.TryGetValue(ownerId, out var own) && own.Count > 0 ? own : bell.Default;
            return list.Count > 0
                ? [.. list.Where(p => p.Period is >= 1 and <= BellScheduleService.MaxPeriods).OrderBy(p => p.Period)
                    .Select(p => new SolverPeriod(p.Period, Minutes(p.StartTime), Minutes(p.EndTime)))]
                : [.. Enumerable.Range(1, 8).Select(p => new SolverPeriod(p, null, null))];
        }

        var homeRooms = await HomeRoomsAsync(db, ct);
        var studyDays = await StudyDaysMapAsync(db, ct);
        var owners = ownerItems.Select(o => new SolverOwner(o.Id, o.Name, PeriodsOf(o.Id),
            homeRooms.TryGetValue(o.Id, out var hr) ? hr : null,
            Enumerable.Range(0, studyDays.GetValueOrDefault(o.Id, DefaultDays(o))).ToList())).ToList();
        var index = owners.Select((o, i) => (o.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

        // Darslar: har ega × fan — yarmlari (1/2) bir vaqtda; soatlari teng bo'lmasa ortig'i alohida.
        var units = new List<SolverUnit>();
        var warnings = new List<string>();
        foreach (var g in loads.Where(l => index.ContainsKey(l.OwnerId)).GroupBy(l => (l.OwnerId, l.SubjectId)))
        {
            var o = index[g.Key.OwnerId];
            var whole = g.Where(l => l.SubGroup == 0).ToList();
            foreach (var l in whole)
                for (var h = 0; h < l.HoursPerWeek; h++)
                    units.Add(new SolverUnit(o, l.SubjectId, [new SolverPart(0, l.TeacherId, l.RoomId)]));
            var halves = g.Where(l => l.SubGroup != 0).OrderBy(l => l.SubGroup).ToList();
            if (halves.Count == 0) continue;
            var max = halves.Max(l => l.HoursPerWeek);
            for (var h = 0; h < max; h++)
                units.Add(new SolverUnit(o, g.Key.SubjectId,
                    [.. halves.Where(l => l.HoursPerWeek > h).Select(l => new SolverPart(l.SubGroup, l.TeacherId, l.RoomId))]));
        }

        foreach (var o in owners)
        {
            var need = units.Count(u => owners[u.Owner] == o);
            var cap = o.Periods.Count * o.Days!.Count;
            if (need > cap) warnings.Add($"{o.Name}: haftasiga {need} dars, lekin {cap} ta joy bor — hammasi sig'maydi");
        }

        // O'zgarmas bandlik: generatsiyaga kirmagan egalarning joriy jadvali (bloklar ham shu yerda).
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        var others = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => !ownerIds.Contains(t.ClassId) && !t.IsArchived).ToListAsync(ct))
            .Where(t => rank(t.Id) >= 2)
            .GroupBy(t => t.ClassId).Select(g => g.OrderByDescending(t => rank(t.Id)).First()).ToList();
        var scope = await LessonRoster.GroupScopeAsync(db, ct);
        var teacherBusy = new Dictionary<string, List<SolverBusy>>(StringComparer.Ordinal);
        var roomBusy = new Dictionary<Guid, List<SolverBusy>>();
        foreach (var t in others.Where(t => scope.Allows(t.OwnerKind, t.ClassId)))
        {
            var periods = PeriodsOf(t.ClassId).ToDictionary(p => p.Period);
            foreach (var l in t.Lessons)
            {
                var sp = periods.TryGetValue(l.Period, out var p) ? p : new SolverPeriod(l.Period, null, null);
                var busy = new SolverBusy(l.Day, l.Period, sp.Start, sp.End);
                if (!string.IsNullOrEmpty(l.TeacherId))
                {
                    if (!teacherBusy.TryGetValue(l.TeacherId, out var tl)) teacherBusy[l.TeacherId] = tl = [];
                    tl.Add(busy);
                }
                var room = l.RoomId ?? (l.SubGroup == 0 && homeRooms.TryGetValue(t.ClassId, out var h) ? h : null);
                if (room is { } r)
                {
                    if (!roomBusy.TryGetValue(r, out var rl)) roomBusy[r] = rl = [];
                    rl.Add(busy);
                }
            }
        }
        // Blok guruhlarining xonalari — blok soatlarida.
        var blockedOwners = new Dictionary<int, HashSet<(int, int)>>();
        foreach (var item in await new BlockScheduleService(db).OverlayAsync(ct))
            if (index.TryGetValue(item.OwnerId, out var oi))
            {
                if (!blockedOwners.TryGetValue(oi, out var set)) blockedOwners[oi] = set = [];
                set.Add((item.Day, item.Period));
            }

        var unavailable = new Dictionary<string, HashSet<(int, int)>>(StringComparer.Ordinal);
        if (req.RespectUnavailability)
            foreach (var u in await db.TeacherUnavailability.AsNoTracking().ToListAsync(ct))
            {
                if (!unavailable.TryGetValue(u.TeacherId, out var set)) unavailable[u.TeacherId] = set = [];
                set.Add((u.Day, u.Period));
            }

        var result = TimetableSolver.Solve(new SolverInput
        {
            Owners = owners, Units = units, TeacherBusy = teacherBusy, RoomBusy = roomBusy,
            OwnerBlocked = blockedOwners, Unavailable = unavailable, NoGaps = req.NoGaps,
            Seed = Math.Abs(name.GetHashCode()) % 100_000 + 1,
        });

        // Natija — har egaga qoralama shablon.
        var byOwner = result.Placed.GroupBy(p => units[p.Unit].Owner).ToDictionary(g => g.Key, g => g.ToList());
        for (var i = 0; i < owners.Count; i++)
        {
            var tpl = new ScheduleTemplate { ClassId = owners[i].Id, Name = name, OwnerKind = ownerItems[i].Kind };
            foreach (var p in byOwner.GetValueOrDefault(i) ?? [])
                foreach (var part in units[p.Unit].Parts)
                    tpl.Lessons.Add(new ScheduleLesson
                    {
                        TemplateId = tpl.Id, Day = p.Day, Period = p.Period, SubjectId = units[p.Unit].SubjectId,
                        TeacherId = part.TeacherId, SubGroup = part.SubGroup, RoomId = part.RoomId,
                    });
            db.ScheduleTemplates.Add(tpl);
        }

        var unplaced = result.Unplaced
            .GroupBy(u => (units[u].Owner, units[u].SubjectId))
            .Select(g => new GenerateUnplacedDto(
                owners[g.Key.Owner].Id, owners[g.Key.Owner].Name, subjects.GetValueOrDefault(g.Key.SubjectId, "?"),
                string.Join(", ", units[g.First()].Parts.Select(p => teacherNames.GetValueOrDefault(p.TeacherId, "?")).Distinct()),
                g.Count()))
            .OrderBy(x => x.OwnerName).ToList();

        return (new GenerateResultDto(name, owners.Count, result.Placed.Count, units.Count,
            result.OwnerGaps, result.TeacherGaps, result.SubjectRepeats, unplaced, warnings), null);
    }

    private static int? Minutes(string? hhmm) =>
        TimeOnly.TryParseExact(hhmm ?? "", ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.Hour * 60 + t.Minute : null;

    public static string DayName(int day) => day is >= 0 and < 6 ? DayNames[day] : $"{day}-kun";
}

public static class TimetableExport
{
    /// <summary>
    /// Jadvalni Excel'ga (2026-11-21): har kun — alohida varaq (qator — sinf, ustun — soat), oxirida
    /// "O'qituvchilar" varag'i (qator — ustoz, ustun — kun: "1: 5-A, 3: 6-B"). Katakda: fan — ustoz (xona).
    /// </summary>
    public static async Task<byte[]?> BuildAsync(IAppDbContext db, string name, CancellationToken ct)
    {
        var owners = await LessonRoster.PickerOwnersAsync(db, ct: ct);
        var ids = owners.Select(o => o.Id).ToList();
        var templates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => ids.Contains(t.ClassId) && t.Name == name && !t.IsArchived).ToListAsync(ct))
            .GroupBy(t => t.ClassId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (templates.Count == 0) return null;

        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teachers = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var rooms = await db.Rooms.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var times = await db.LessonTimes.AsNoTracking().ToDictionaryAsync(t => t.Period, t => $"{t.StartTime}–{t.EndTime}", ct);
        var rowsOwners = owners.Where(o => templates.ContainsKey(o.Id)).ToList();
        var maxPeriod = Math.Max(times.Keys.DefaultIfEmpty(0).Max(),
            templates.Values.SelectMany(t => t.Lessons).Select(l => l.Period).DefaultIfEmpty(0).Max());
        var days = templates.Values.SelectMany(t => t.Lessons).Any(l => l.Day == 5) ? 6 : 5;

        string Short(string full)
        {
            var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? $"{parts[0]} {parts[1][0]}." : full;
        }
        string Cell(IEnumerable<ScheduleLesson> lessons) => string.Join(" / ", lessons.OrderBy(l => l.SubGroup).Select(l =>
            (l.SubGroup > 0 ? $"{l.SubGroup}-gr: " : "") + subjects.GetValueOrDefault(l.SubjectId, "?")
            + (string.IsNullOrEmpty(l.TeacherId) ? "" : " — " + Short(teachers.GetValueOrDefault(l.TeacherId, "?")))
            + (l.RoomId is { } r && rooms.TryGetValue(r, out var rn) ? $" ({rn})" : "")));

        var headers = new List<string> { "Sinf" };
        for (var p = 1; p <= maxPeriod; p++) headers.Add(times.TryGetValue(p, out var t) ? $"{p}-soat ({t})" : $"{p}-soat");

        var sheets = new List<ExcelExport.SheetSpec>();
        for (var d = 0; d < days; d++)
        {
            var rows = rowsOwners.Select(o =>
            {
                var row = new List<string> { o.Name };
                for (var p = 1; p <= maxPeriod; p++)
                    row.Add(Cell(templates[o.Id].Lessons.Where(l => l.Day == d && l.Period == p)));
                return (IReadOnlyList<string>)row;
            }).ToList();
            sheets.Add(new ExcelExport.SheetSpec(TeachingLoadService.DayName(d), headers, rows));
        }

        var byTeacher = rowsOwners.SelectMany(o => templates[o.Id].Lessons
                .Where(l => !string.IsNullOrEmpty(l.TeacherId)).Select(l => (o.Name, l)))
            .GroupBy(x => x.l.TeacherId)
            .OrderBy(g => teachers.GetValueOrDefault(g.Key, "?"));
        var teacherHeaders = new List<string> { "O'qituvchi", "Haftasiga" };
        for (var d = 0; d < days; d++) teacherHeaders.Add(TeachingLoadService.DayName(d));
        var teacherRows = byTeacher.Select(g =>
        {
            var row = new List<string> { teachers.GetValueOrDefault(g.Key, "?"), g.Count().ToString(CultureInfo.InvariantCulture) };
            for (var d = 0; d < days; d++)
                row.Add(string.Join(", ", g.Where(x => x.l.Day == d).OrderBy(x => x.l.Period).Select(x => $"{x.l.Period}: {x.Name}")));
            return (IReadOnlyList<string>)row;
        }).ToList();
        sheets.Add(new ExcelExport.SheetSpec("O'qituvchilar", teacherHeaders, teacherRows));
        return ExcelExport.Build(sheets);
    }
}
