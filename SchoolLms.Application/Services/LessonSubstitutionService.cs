using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hr;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'rindosh ustoz (client, 2026-10-07; LessonSubstitution.cs). The admin picks a date and the
//  teacher who does not teach, sees that teacher's lessons on that date (from the weekly timetable)
//  and names a free teacher for each. Everything else reads the rows:
//    * PortalSchedule  — the substitute's week shows the lesson, the absent teacher's shows who covers;
//    * journal access  — the substitute may fill that class+subject on that date (CoversAsync);
//    * pay             — MonthDeltaAsync: −1 lesson for the absent teacher (unless the whole day is
//                        already "absent"), +1 for the substitute at their own rate.
// ===========================================================================

public record SubstitutionLessonDto(
    int Period, string? StartTime, string? EndTime, string OwnerKind, string OwnerId, string OwnerName,
    string SubjectId, string SubjectName, int SubGroup,
    Guid? SubstitutionId, string? SubstituteTeacherId, string? SubstituteTeacherName, Guid? ExternalSubstituteId = null);

/// <summary>A teacher the admin can choose; <see cref="BusyPeriods"/> — periods they already teach that day.</summary>
public record SubstitutionTeacherOptionDto(string Id, string FullName, List<int> BusyPeriods);

public record SubstitutionDayDto(
    string Date, string TeacherId, string TeacherName, List<SubstitutionLessonDto> Lessons, List<SubstitutionTeacherOptionDto> Teachers);

/// <summary>One lesson and who covers it: an internal teacher or a tashqi o'rindosh — exactly one.</summary>
public record SubstitutionCreateItem(
    int Period, string OwnerId, string SubjectId, int SubGroup, string? SubstituteTeacherId, Guid? ExternalSubstituteId = null);

public record SubstitutionCreateRequest(string Date, string AbsentTeacherId, List<SubstitutionCreateItem> Items, string? Reason);

/// <summary><see cref="SubstituteTeacherName"/> is the tashqi o'rindosh's name when <see cref="ExternalSubstituteId"/> is set.</summary>
public record SubstitutionDto(
    Guid Id, string Date, int Period, string OwnerKind, string OwnerId, string OwnerName, string SubjectId, string SubjectName,
    int SubGroup, string AbsentTeacherId, string AbsentTeacherName, string? SubstituteTeacherId, string SubstituteTeacherName,
    string? Reason, string CreatedByName, DateTimeOffset CreatedAt, Guid? ExternalSubstituteId = null);

public record ExternalSubstituteDto(
    Guid Id, string FullName, string Phone, string? SubjectId, string? SubjectName);

/// <summary>A tashqi o'rindosh is paid in cash only (client, 2026-11-11) — no payment details.</summary>
public record ExternalSubstituteRequest(string? FullName, string? Phone, string? SubjectId);

/// <summary>Found by phone: <see cref="Existed"/> — the card was already there and nothing was created.</summary>
public record ExternalSubstituteSaveResult(ExternalSubstituteDto Substitute, bool Existed);

/// <summary>
/// One person in a month (client, 2026-11-11: o'rindoshlik oylikka bog'liq): <see cref="Covered"/> —
/// lessons taught in someone's place, <see cref="Missed"/> — own lessons someone else taught.
/// </summary>
public record SubstitutionMonthPersonDto(
    string Kind, string Id, string FullName, int Covered, int Missed, ExternalSubstituteDto? External);

public record SubstitutionMonthDto(string Month, List<SubstitutionMonthPersonDto> People, List<SubstitutionDto> Rows);

public static class SubstitutionPersonKind
{
    public const string Teacher = "teacher";
    public const string External = "external";
}

public static class LessonSubstitutionService
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static int Dow(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    /// <summary>(quarter, week, day) of a date, or null when it is in no quarter or on a Sunday.</summary>
    public static async Task<(int Quarter, int Week, int Day)?> PlaceAsync(IAppDbContext db, DateOnly date, CancellationToken ct = default) =>
        Place(await db.Quarters.AsNoTracking().ToListAsync(ct), date);

    /// <summary><see cref="PlaceAsync"/> over quarters already loaded — for callers that place many dates.</summary>
    public static (int Quarter, int Week, int Day)? Place(IReadOnlyList<QuarterPeriod> quarters, DateOnly date)
    {
        if (Dow(date) > 5) return null;
        var iso = Iso(date);
        var quarter = quarters
            .FirstOrDefault(q => string.CompareOrdinal(iso, q.StartDate) >= 0 && string.CompareOrdinal(iso, q.EndDate) <= 0);
        if (quarter is null) return null;
        var week = PortalSchedule.WeekOf(ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate), iso);
        return (quarter.Quarter, week, Dow(date));
    }

    /// <summary>The timetable's lessons on a date — one teacher's, or everybody's (null).</summary>
    public static async Task<List<OwnedLesson>> LessonsOnAsync(IAppDbContext db, string? teacherId, DateOnly date, CancellationToken ct = default)
    {
        if (await PlaceAsync(db, date, ct) is not { } place) return [];
        return [.. (await TeacherLessons.ForWeekAsync(db, teacherId, place.Quarter, place.Week, ct)).Where(l => l.Day == place.Day)];
    }

    // =====================================================================
    //  Admin
    // =====================================================================

    /// <summary>One teacher's lessons on a date with their current substitutes, and who is free when.</summary>
    public static async Task<SubstitutionDayDto?> DayAsync(IAppDbContext db, string teacherId, DateOnly date, CancellationToken ct = default)
    {
        var teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        if (teacher is null) return null;
        var all = await LessonsOnAsync(db, null, date, ct);
        var own = all.Where(l => l.TeacherId == teacherId).OrderBy(l => l.Period).ToList();
        var standing = await db.LessonSubstitutions.AsNoTracking()
            .Where(s => s.Date == date && s.CancelledAt == null).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var bells = await BellTimes.LoadAsync(db, ct);
        var teachers = await db.Teachers.AsNoTracking().Where(t => !t.IsArchived).OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName }).ToListAsync(ct);
        var names = teachers.ToDictionary(t => t.Id, t => t.FullName);
        var externals = await ExternalNamesAsync(db, standing, ct);

        var lessons = own.Select(l =>
        {
            var sub = standing.FirstOrDefault(s => s.Period == l.Period && s.OwnerId == l.Owner.Id && s.SubjectId == l.SubjectId && s.SubGroup == l.SubGroup);
            var lt = bells.Time(l.Owner.Id, l.Period);
            return new SubstitutionLessonDto(
                l.Period, lt?.StartTime, lt?.EndTime, l.Owner.Kind, l.Owner.Id, l.Owner.Name,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""), l.SubGroup,
                sub?.Id, sub?.SubstituteTeacherId, sub is null ? null : SubstituteName(sub, names, externals),
                sub?.ExternalSubstituteId);
        }).ToList();

        var options = teachers.Where(t => t.Id != teacherId).Select(t => new SubstitutionTeacherOptionDto(
            t.Id, t.FullName,
            [.. all.Where(l => l.TeacherId == t.Id).Select(l => l.Period)
                .Concat(standing.Where(s => s.SubstituteTeacherId == t.Id).Select(s => s.Period))
                .Distinct().Order()])).ToList();

        return new SubstitutionDayDto(Iso(date), teacher.Id, teacher.FullName, lessons, options);
    }

    /// <summary>
    /// Names substitutes for one teacher's lessons on a date. A lesson that already has a substitute
    /// is re-assigned (the old row is cancelled). Error text or the created rows — the caller saves,
    /// inside a transaction (the old rows are already saved as cancelled here).
    /// </summary>
    public static async Task<(string? Error, List<LessonSubstitution> Created)> CreateAsync(
        IAppDbContext db, SubstitutionCreateRequest req, string actorId, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(req.Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return ("Sana noto'g'ri (yyyy-MM-dd)", []);
        if (req.Items is not { Count: > 0 }) return ("Kamida bitta darsga o'rindosh tanlang", []);
        if (await PlaceAsync(db, date, ct) is null) return ("Bu sana o'quv choragida emas yoki yakshanba", []);
        if (!await db.Teachers.AnyAsync(t => t.Id == req.AbsentTeacherId, ct)) return ("O'qituvchi topilmadi", []);

        var all = await LessonsOnAsync(db, null, date, ct);
        var standing = await db.LessonSubstitutions.Where(s => s.Date == date && s.CancelledAt == null).ToListAsync(ct);
        var substitutes = req.Items.Select(i => i.SubstituteTeacherId).OfType<string>().Distinct().ToList();
        var active = await db.Teachers.AsNoTracking().Where(t => substitutes.Contains(t.Id) && !t.IsArchived)
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var externalIds = req.Items.Select(i => i.ExternalSubstituteId).OfType<Guid>().Distinct().ToList();
        var externals = await db.ExternalSubstitutes.AsNoTracking().Where(x => externalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var now = AppClock.NowInstant;
        var created = new List<LessonSubstitution>();

        foreach (var item in req.Items)
        {
            var lesson = all.FirstOrDefault(l => l.TeacherId == req.AbsentTeacherId && l.Period == item.Period
                && l.Owner.Id == item.OwnerId && l.SubjectId == item.SubjectId && l.SubGroup == item.SubGroup);
            if (lesson is null) return ($"{item.Period}-dars bu ustozning shu kungi jadvalida yo'q", []);
            bool SameLesson(LessonSubstitution s) => s.OwnerId == item.OwnerId && s.SubjectId == item.SubjectId && s.SubGroup == item.SubGroup;
            string subName;
            bool busy;
            if (item.ExternalSubstituteId is { } externalId)
            {
                // Tashqi o'rindosh: not in the timetable — busy only with another lesson they cover.
                if (!string.IsNullOrEmpty(item.SubstituteTeacherId)) return ("O'rindosh ichki yoki tashqi ustoz — bittasi tanlanadi", []);
                if (!externals.TryGetValue(externalId, out var externalName)) return ("Tashqi o'rindosh topilmadi", []);
                subName = externalName;
                busy = standing.Any(s => s.ExternalSubstituteId == externalId && s.Period == item.Period && !SameLesson(s))
                    || created.Any(c => c.ExternalSubstituteId == externalId && c.Period == item.Period);
            }
            else
            {
                if (string.IsNullOrEmpty(item.SubstituteTeacherId)) return ("O'rindosh ustozni tanlang", []);
                if (item.SubstituteTeacherId == req.AbsentTeacherId) return ("O'rindosh boshqa ustoz bo'lishi kerak", []);
                if (!active.TryGetValue(item.SubstituteTeacherId, out var teacherName)) return ("O'rindosh ustoz topilmadi", []);
                subName = teacherName;
                busy = all.Any(l => l.TeacherId == item.SubstituteTeacherId && l.Period == item.Period)
                    || standing.Any(s => s.SubstituteTeacherId == item.SubstituteTeacherId && s.Period == item.Period && !SameLesson(s))
                    || created.Any(c => c.SubstituteTeacherId == item.SubstituteTeacherId && c.Period == item.Period);
            }
            if (busy) return ($"{subName} {item.Period}-darsda band", []);

            foreach (var old in standing.Where(s => s.Period == item.Period && s.OwnerId == item.OwnerId
                                                    && s.SubjectId == item.SubjectId && s.SubGroup == item.SubGroup))
            {
                old.CancelledAt = now;
                old.CancelledBy = actorId;
            }
            created.Add(new LessonSubstitution
            {
                Date = date, Period = item.Period, OwnerKind = lesson.Owner.Kind, OwnerId = lesson.Owner.Id,
                SubjectId = lesson.SubjectId, SubGroup = lesson.SubGroup,
                AbsentTeacherId = req.AbsentTeacherId,
                SubstituteTeacherId = item.ExternalSubstituteId is null ? item.SubstituteTeacherId : null,
                ExternalSubstituteId = item.ExternalSubstituteId,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()[..Math.Min(req.Reason.Trim().Length, 300)],
                CreatedBy = actorId, CreatedAt = now,
            });
        }
        // The old rows step aside first — the unique index allows one standing row per lesson.
        await db.SaveChangesAsync(ct);
        db.LessonSubstitutions.AddRange(created);
        return (null, created);
    }

    public static async Task<LessonSubstitution?> CancelAsync(IAppDbContext db, Guid id, string actorId, CancellationToken ct = default)
    {
        var row = await db.LessonSubstitutions.FirstOrDefaultAsync(s => s.Id == id && s.CancelledAt == null, ct);
        if (row is null) return null;
        row.CancelledAt = AppClock.NowInstant;
        row.CancelledBy = actorId;
        return row;
    }

    public static async Task<List<SubstitutionDto>> ListAsync(
        IAppDbContext db, DateOnly from, DateOnly to, string? teacherId, CancellationToken ct = default)
    {
        var q = db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null && s.Date >= from && s.Date <= to);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(s => s.AbsentTeacherId == teacherId || s.SubstituteTeacherId == teacherId);
        var rows = await q.OrderByDescending(s => s.Date).ThenBy(s => s.Period).Take(500).ToListAsync(ct);
        return await ToDtosAsync(db, rows, ct);
    }

    /// <summary>
    /// A month of substitutions (client, 2026-11-11: o'rindosh almashinuvi oylikka bog'liq — necha marta
    /// bo'lgani oy kesimida): every standing row of the month and, per person, how many lessons they
    /// covered and how many of their own lessons somebody else taught.
    /// </summary>
    public static async Task<SubstitutionMonthDto> MonthAsync(IAppDbContext db, int year, int month, CancellationToken ct = default)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = await db.LessonSubstitutions.AsNoTracking()
            .Where(s => s.CancelledAt == null && s.Date >= from && s.Date <= to)
            .OrderByDescending(s => s.Date).ThenBy(s => s.Period).ToListAsync(ct);
        var dtos = await ToDtosAsync(db, rows, ct);

        var externalIds = rows.Select(r => r.ExternalSubstituteId).OfType<Guid>().Distinct().ToList();
        var externals = (await ExternalDtosAsync(db, db.ExternalSubstitutes.Where(x => externalIds.Contains(x.Id)), ct))
            .ToDictionary(x => x.Id);
        var people = new Dictionary<(string Kind, string Id), (string Name, int Covered, int Missed)>();
        void Add(string kind, string id, string name, int covered, int missed)
        {
            var p = people.GetValueOrDefault((kind, id), (Name: name, Covered: 0, Missed: 0));
            people[(kind, id)] = (p.Name, p.Covered + covered, p.Missed + missed);
        }
        foreach (var d in dtos)
        {
            Add(SubstitutionPersonKind.Teacher, d.AbsentTeacherId, d.AbsentTeacherName, 0, 1);
            if (d.ExternalSubstituteId is { } ext)
                Add(SubstitutionPersonKind.External, ext.ToString(), d.SubstituteTeacherName, 1, 0);
            else if (d.SubstituteTeacherId is { } sub)
                Add(SubstitutionPersonKind.Teacher, sub, d.SubstituteTeacherName, 1, 0);
        }
        var list = people
            .Select(p => new SubstitutionMonthPersonDto(p.Key.Kind, p.Key.Id, p.Value.Name, p.Value.Covered, p.Value.Missed,
                p.Key.Kind == SubstitutionPersonKind.External ? externals.GetValueOrDefault(Guid.Parse(p.Key.Id)) : null))
            .OrderByDescending(p => p.Covered).ThenByDescending(p => p.Missed).ThenBy(p => p.FullName, StringComparer.Ordinal)
            .ToList();
        return new SubstitutionMonthDto(from.ToString("yyyy-MM", CultureInfo.InvariantCulture), list, dtos);
    }

    public static async Task<List<SubstitutionDto>> ToDtosAsync(IAppDbContext db, List<LessonSubstitution> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return [];
        var teacherIds = rows.Select(r => r.AbsentTeacherId).Concat(rows.Select(r => r.SubstituteTeacherId).OfType<string>())
            .Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var externals = await ExternalNamesAsync(db, rows, ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return [.. rows.Select(r => new SubstitutionDto(
            r.Id, Iso(r.Date), r.Period, r.OwnerKind, r.OwnerId, owners.TryGetValue(r.OwnerId, out var o) ? o.Name : "",
            r.SubjectId, subjects.GetValueOrDefault(r.SubjectId, ""), r.SubGroup,
            r.AbsentTeacherId, teachers.GetValueOrDefault(r.AbsentTeacherId, ""),
            r.SubstituteTeacherId, SubstituteName(r, teachers, externals),
            r.Reason, users.GetValueOrDefault(r.CreatedBy, ""), r.CreatedAt, r.ExternalSubstituteId))];
    }

    /// <summary>Who covers the row — the internal teacher's or the tashqi o'rindosh's name.</summary>
    private static string SubstituteName(
        LessonSubstitution r, IReadOnlyDictionary<string, string> teachers, IReadOnlyDictionary<Guid, string> externals) =>
        r.ExternalSubstituteId is { } ext ? externals.GetValueOrDefault(ext, "")
        : r.SubstituteTeacherId is { } id ? teachers.GetValueOrDefault(id, "")
        : "";

    private static async Task<Dictionary<Guid, string>> ExternalNamesAsync(
        IAppDbContext db, IEnumerable<LessonSubstitution> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.ExternalSubstituteId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.ExternalSubstitutes.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    // =====================================================================
    //  Tashqi o'rindosh (client, 2026-11-11) — a card without a login
    // =====================================================================

    private static async Task<List<ExternalSubstituteDto>> ExternalDtosAsync(
        IAppDbContext db, IQueryable<ExternalSubstitute> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return [.. rows.Select(x => new ExternalSubstituteDto(
            x.Id, x.FullName, x.Phone, x.SubjectId, x.SubjectId is null ? null : subjects.GetValueOrDefault(x.SubjectId)))];
    }

    /// <summary>The cards whose name or phone digits contain <paramref name="search"/>; all of them when it is empty.</summary>
    public static async Task<List<ExternalSubstituteDto>> ExternalListAsync(IAppDbContext db, string? search, CancellationToken ct = default)
    {
        var q = db.ExternalSubstitutes.AsQueryable();
        var text = search?.Trim() ?? "";
        if (text.Length > 0)
        {
            var digits = new string(text.Where(char.IsDigit).ToArray());
            q = digits.Length >= 3
                ? q.Where(x => x.FullName.ToLower().Contains(text.ToLower()) || x.Phone.Contains(digits))
                : q.Where(x => x.FullName.ToLower().Contains(text.ToLower()));
        }
        return await ExternalDtosAsync(db, q.Take(200), ct);
    }

    /// <summary>
    /// Saves a new card — or, when the phone is already someone's card, returns that one untouched
    /// (the client: "telefon raqam bo'yicha takrorlanish tekshiriladi … qaytadan kiritilmaydi").
    /// The caller saves.
    /// </summary>
    public static async Task<(string? Error, ExternalSubstituteSaveResult? Result)> ExternalCreateAsync(
        IAppDbContext db, ExternalSubstituteRequest req, string actorId, CancellationToken ct = default)
    {
        var phone = HrValues.NormalizePhone(req.Phone);
        if (phone is null) return ("Telefon raqami noto'g'ri (+998 XX XXX XX XX)", null);
        var existing = await db.ExternalSubstitutes.AsNoTracking().Where(x => x.Phone == phone).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty)
            return (null, new ExternalSubstituteSaveResult(
                (await ExternalDtosAsync(db, db.ExternalSubstitutes.Where(x => x.Id == existing), ct))[0], true));

        var (error, row) = await ValidateExternalAsync(db, req, ct);
        if (error is not null) return (error, null);
        row!.Phone = phone;
        row.CreatedBy = actorId;
        row.CreatedAt = AppClock.NowInstant;
        db.ExternalSubstitutes.Add(row);
        var subject = row.SubjectId is null ? null
            : await db.Subjects.AsNoTracking().Where(s => s.Id == row.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return (null, new ExternalSubstituteSaveResult(
            new ExternalSubstituteDto(row.Id, row.FullName, row.Phone, row.SubjectId, subject), false));
    }

    /// <summary>Name and subject checked; the phone is the caller's.</summary>
    private static async Task<(string? Error, ExternalSubstitute? Row)> ValidateExternalAsync(
        IAppDbContext db, ExternalSubstituteRequest req, CancellationToken ct)
    {
        var name = (req.FullName ?? "").Trim();
        if (name.Length < 3) return ("F.I.Sh. kiriting", null);
        if (name.Length > 200) return ("F.I.Sh. juda uzun", null);
        var subjectId = string.IsNullOrWhiteSpace(req.SubjectId) ? null : req.SubjectId.Trim();
        if (subjectId is not null && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) return ("Fan topilmadi", null);
        return (null, new ExternalSubstitute { FullName = name, SubjectId = subjectId });
    }

    // =====================================================================
    //  Reads for the journal, the schedule and pay
    // =====================================================================

    /// <summary>
    /// May this teacher, as a substitute, open (date null — any standing substitution in the last
    /// 30 days or ahead) or write (that date) the class+subject journal?
    /// </summary>
    public static Task<bool> CoversAsync(IAppDbContext db, string teacherId, string ownerId, string subjectId, string? date, CancellationToken ct = default)
    {
        var q = db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null && s.SubstituteTeacherId == teacherId
                                                                  && s.OwnerId == ownerId && s.SubjectId == subjectId);
        if (date is not null)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return Task.FromResult(false);
            return q.AnyAsync(s => s.Date == d, ct);
        }
        var since = AppClock.Today.AddDays(-30);
        return q.AnyAsync(s => s.Date >= since, ct);
    }

    /// <summary>A standing substitution in this class (any subject) in the last 30 days or ahead — the roster read.</summary>
    public static Task<bool> CoversClassAsync(IAppDbContext db, string teacherId, string ownerId, CancellationToken ct = default)
    {
        var since = AppClock.Today.AddDays(-30);
        return db.LessonSubstitutions.AsNoTracking().AnyAsync(s => s.CancelledAt == null && s.SubstituteTeacherId == teacherId
                                                                    && s.OwnerId == ownerId && s.Date >= since, ct);
    }

    /// <summary>
    /// Lessons each teacher gains (as substitute) minus loses (as the absent one) per month "yyyy-MM".
    /// A lesson lost on a day already marked absent is not counted twice.
    /// </summary>
    public static async Task<Dictionary<(string TeacherId, string Month), int>> MonthDeltaAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null)
            .Select(s => new { s.Date, s.AbsentTeacherId, s.SubstituteTeacherId }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var absentDays = (await db.TeacherAttendances.AsNoTracking().Where(a => a.Status == "absent")
                .Select(a => new { a.TeacherId, a.Date }).ToListAsync(ct))
            .Select(a => (a.TeacherId, a.Date)).ToHashSet();
        var delta = new Dictionary<(string, string), int>();
        foreach (var r in rows)
        {
            var month = r.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            // A tashqi o'rindosh is not on the payroll — only the absent teacher's side counts here.
            if (r.SubstituteTeacherId is { } sub)
                delta[(sub, month)] = delta.GetValueOrDefault((sub, month)) + 1;
            if (!absentDays.Contains((r.AbsentTeacherId, Iso(r.Date))))
                delta[(r.AbsentTeacherId, month)] = delta.GetValueOrDefault((r.AbsentTeacherId, month)) - 1;
        }
        return delta;
    }

    /// <summary>Monday of a timetable week (quarter, week); null when unknown.</summary>
    public static async Task<DateOnly?> WeekMondayAsync(IAppDbContext db, int quarter, int week, CancellationToken ct = default)
    {
        var q = await db.Quarters.AsNoTracking().FirstOrDefaultAsync(x => x.Quarter == quarter, ct);
        if (q is null) return null;
        var range = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate).FirstOrDefault(w => w.Week == week);
        if (range is null || !DateOnly.TryParseExact(range.StartISO, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return null;
        return start.AddDays(-Dow(start));
    }
}

/// <summary>Bot messages to the substitute: named for lessons, or told the substitution is off.</summary>
public sealed class SubstitutionNotifier(IAppDbContext db, TelegramService telegram, ILogger<SubstitutionNotifier> logger)
{
    private static readonly string[] Days = ["dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba", "yakshanba"];

    public async Task<int> AssignedAsync(IReadOnlyList<SubstitutionDto> rows, CancellationToken ct = default) =>
        await SendAsync(rows, "🔁 Sizga o'rindosh dars biriktirildi", true, ct);

    public async Task<int> CancelledAsync(IReadOnlyList<SubstitutionDto> rows, CancellationToken ct = default) =>
        await SendAsync(rows, "❎ O'rindosh dars bekor qilindi", false, ct);

    private async Task<int> SendAsync(IReadOnlyList<SubstitutionDto> rows, string title, bool withHint, CancellationToken ct)
    {
        if (!telegram.IsConfigured || rows.Count == 0) return 0;
        var sent = 0;
        try
        {
            var bells = await BellTimes.LoadAsync(db, ct);
            // A tashqi o'rindosh has no bot account — only internal teachers hear about it.
            foreach (var group in rows.Where(r => r.SubstituteTeacherId is not null).GroupBy(r => r.SubstituteTeacherId!))
            {
                var lines = new List<string> { title, "" };
                foreach (var r in group.OrderBy(r => r.Date).ThenBy(r => r.Period))
                {
                    var d = DateOnly.ParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var lt = bells.Time(r.OwnerId, r.Period);
                    var time = lt is not { } t ? "" : $" ({t.StartTime}–{t.EndTime})";
                    lines.Add($"• {d:dd.MM.yyyy} ({Days[((int)d.DayOfWeek + 6) % 7]}), {r.Period}-dars{time}: {r.OwnerName} · {r.SubjectName}");
                    lines.Add($"  Asosiy ustoz: {r.AbsentTeacherName}");
                }
                if (withHint)
                {
                    lines.Add("");
                    lines.Add($"Jurnalni to'ldirish: «{TelegramPanelKeys.ButtonText}» → Bugun.");
                }
                var text = string.Join('\n', lines);
                foreach (var chat in await PenaltyNotifier.RecipientsAsync(db, EmployeeKind.Teacher, group.Key, ct))
                    if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "O'rindosh dars xabari yuborilmadi");
        }
        return sent;
    }
}
