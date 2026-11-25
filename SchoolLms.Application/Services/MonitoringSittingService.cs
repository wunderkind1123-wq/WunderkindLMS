using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Test sessiyasi (docs/modules/curriculum-monitoring.md §2.5): bir o'tirishda bir nechta fan
/// testi ketma-ket, o'quvchiga bitta kod. Testlarning o'zi o'zgarmaydi — har biri o'z vaqti,
/// natijasi va Mavsumiy bahosi bilan; kodni test sahifasi <see cref="MonitoringService"/> da
/// navbatdagi fanga yo'naltiradi.
/// </summary>
public static class MonitoringSittingService
{
    public const int MaxTests = 10;

    public static async Task<MonitoringSitting> CreateAsync(IAppDbContext db, MonitoringSittingPayload p, string userId, CancellationToken ct)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length is < 2 or > 200) throw new MonitoringException("Sessiya nomi 2–200 belgi bo'lsin");
        if (!DateOnly.TryParseExact(p.TestDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new MonitoringException("Sana noto'g'ri");
        var ids = (p.TestIds ?? []).Distinct().ToList();
        if (ids.Count is < 2 or > MaxTests) throw new MonitoringException($"Sessiyaga 2–{MaxTests} ta test tanlang");

        var tests = await db.MonitoringTests.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        if (tests.Count != ids.Count) throw new MonitoringException("Test topilmadi", 404);
        if (tests.Any(t => t.Delivery != MonitoringDelivery.Online)) throw new MonitoringException("Sessiyaga faqat onlayn testlar kiradi");
        if (tests.Any(t => t.Status == MonitoringStatus.Closed)) throw new MonitoringException("Yopilgan test sessiyaga qo'shilmaydi");
        var busy = (await db.MonitoringSittings.AsNoTracking().Select(s => s.TestIds).ToListAsync(ct)).SelectMany(x => x).ToHashSet();
        if (tests.FirstOrDefault(t => busy.Contains(t.Id)) is { } used)
            throw new MonitoringException($"«{used.Title}» boshqa sessiyada bor", 409);

        var sitting = new MonitoringSitting
        {
            Title = title, TestDate = date, TestIds = ids, CreatedBy = userId, CreatedAt = DateTimeOffset.UtcNow,
            RoomPin = await MonitoringService.NewPinAsync(db, ct),
        };
        // every pupil of any of the tests gets one code; a pupil missing from a subject skips it
        var students = await db.MonitoringParticipants.AsNoTracking().Where(x => ids.Contains(x.TestId))
            .Select(x => x.StudentId).Distinct().ToListAsync(ct);
        var taken = await MonitoringService.TakenCodesAsync(db, ct);
        db.MonitoringSittings.Add(sitting);
        foreach (var s in students)
        {
            string code;
            do code = MonitoringService.NewCode(); while (!taken.Add(code));
            db.MonitoringSittingCodes.Add(new MonitoringSittingCode { SittingId = sitting.Id, StudentId = s, Code = code });
        }
        await db.SaveChangesAsync(ct);
        return sitting;
    }

    private static async Task<List<MonitoringSittingTestDto>> TestsAsync(IAppDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var tests = await db.MonitoringTests.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var subjectIds = tests.Select(t => t.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var items = await db.MonitoringItems.AsNoTracking().Where(i => ids.Contains(i.TestId))
            .GroupBy(i => i.TestId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var people = await db.MonitoringParticipants.AsNoTracking().Where(x => ids.Contains(x.TestId))
            .GroupBy(x => x.TestId)
            .Select(g => new { g.Key, N = g.Count(), Done = g.Count(x => x.Status == MonitoringParticipantStatus.Done) })
            .ToDictionaryAsync(x => x.Key, ct);
        return [.. ids.Select(id => tests.FirstOrDefault(t => t.Id == id)).OfType<MonitoringTest>().Select(t => new MonitoringSittingTestDto(
            t.Id, t.Title, subjects.GetValueOrDefault(t.SubjectId) ?? "", t.Status, items.GetValueOrDefault(t.Id), t.TimeLimitMin,
            people.GetValueOrDefault(t.Id)?.N ?? 0, people.GetValueOrDefault(t.Id)?.Done ?? 0))];
    }

    public static async Task<List<MonitoringSittingListItemDto>> ListAsync(IAppDbContext db, CancellationToken ct)
    {
        var sittings = await db.MonitoringSittings.AsNoTracking().OrderByDescending(s => s.TestDate).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);
        var result = new List<MonitoringSittingListItemDto>(sittings.Count);
        foreach (var s in sittings)
        {
            var detail = await DetailAsync(db, s, ct);
            result.Add(new MonitoringSittingListItemDto(s.Id, s.Title, detail.TestDate, detail.Tests, detail.Rows.Count,
                detail.Rows.Count(r => r.Steps.All(x => x is "" or MonitoringParticipantStatus.Done or MonitoringParticipantStatus.Absent))));
        }
        return result;
    }

    public static async Task<MonitoringSittingDetailDto> DetailAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        await DetailAsync(db, await db.MonitoringSittings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new MonitoringException("Sessiya topilmadi", 404), ct);

    private static async Task<MonitoringSittingDetailDto> DetailAsync(IAppDbContext db, MonitoringSitting s, CancellationToken ct)
    {
        var ids = s.TestIds;
        var tests = await TestsAsync(db, ids, ct);
        var codes = await db.MonitoringSittingCodes.AsNoTracking().Where(c => c.SittingId == s.Id).ToListAsync(ct);
        var parts = await db.MonitoringParticipants.AsNoTracking().Where(x => ids.Contains(x.TestId)).ToListAsync(ct);
        var studentIds = codes.Select(c => c.StudentId).ToList();
        var names = await db.Students.AsNoTracking().Where(x => studentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var owners = await MonitoringService.OwnerNamesAsync(db, ct);
        var rows = codes.Select(c =>
        {
            var mine = parts.Where(x => x.StudentId == c.StudentId).ToList();
            var owner = mine.FirstOrDefault(x => x.OwnerKind == LessonOwnerKind.Class) ?? mine.FirstOrDefault();
            return new MonitoringSittingRowDto(c.Id, c.StudentId, names.GetValueOrDefault(c.StudentId) ?? "—",
                owner is null ? "—" : owners.GetValueOrDefault(owner.OwnerId) ?? "—", c.Code, c.DeviceToken != null,
                [.. tests.Select(t => mine.FirstOrDefault(x => x.TestId == t.Id)?.Status ?? "")], mine.Sum(x => x.Leaves));
        }).OrderBy(r => r.OwnerName).ThenBy(r => r.StudentName).ToList();
        return new MonitoringSittingDetailDto(s.Id, s.Title, s.TestDate.ToString("yyyy-MM-dd"), tests, rows, s.RoomPin);
    }

    /// <summary>Sessiya o'chadi — testlar va natijalar qoladi (ularni alohida boshqarish mumkin).</summary>
    public static async Task DeleteAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.MonitoringSittings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new MonitoringException("Sessiya topilmadi", 404);
        db.MonitoringSittings.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kompyuter almashsa: sessiya qulfi va hozir ishlanayotgan fan qulfi ochiladi.</summary>
    public static async Task ResetDeviceAsync(IAppDbContext db, Guid codeId, CancellationToken ct)
    {
        var c = await db.MonitoringSittingCodes.FirstOrDefaultAsync(x => x.Id == codeId, ct) ?? throw new MonitoringException("Kod topilmadi", 404);
        var ids = (await db.MonitoringSittings.AsNoTracking().FirstAsync(s => s.Id == c.SittingId, ct)).TestIds;
        c.DeviceToken = null;
        foreach (var p in await db.MonitoringParticipants.Where(x => x.StudentId == c.StudentId && ids.Contains(x.TestId)
                     && x.Status == MonitoringParticipantStatus.InProgress).ToListAsync(ct))
            p.DeviceToken = null;
        await db.SaveChangesAsync(ct);
    }

    // ================================================================== tez yaratish

    /// <summary>
    /// Bitta formadan: har fan uchun bankdan onlayn test (bir xil sinflar, oyna, aralashtirish, chiqish
    /// chegarasi, oylik test belgisi), xohlansa darhol ochiladi, keyin sessiya. Hammasi bitta
    /// tranzaksiyada — biror fan xato bo'lsa hech narsa yaratilmaydi.
    /// </summary>
    public static async Task<MonitoringSitting> QuickCreateAsync(IAppDbContext db, MonitoringQuickSittingPayload p, string userId, CancellationToken ct)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length is < 2 or > 150) throw new MonitoringException("Sessiya nomi 2–150 belgi bo'lsin");
        var subjects = p.Subjects ?? [];
        if (subjects.Count is < 2 or > MaxTests) throw new MonitoringException($"Sessiyaga 2–{MaxTests} ta fan tanlang");
        if (subjects.Select(x => x.BankId).Distinct().Count() != subjects.Count) throw new MonitoringException("Bitta bank ikki marta tanlangan");

        await using var tx = await db.BeginTransactionAsync(ct);
        var ids = new List<Guid>(subjects.Count);
        foreach (var row in subjects)
        {
            var bank = await db.MonitoringBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == row.BankId, ct)
                ?? throw new MonitoringException("Bank topilmadi", 404);
            var subjectName = await db.Subjects.AsNoTracking().Where(x => x.Id == bank.SubjectId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? bank.Title;
            var questions = await db.MonitoringQuestions.AsNoTracking().Where(q => q.BankId == bank.Id)
                .OrderBy(q => q.Order).Select(q => q.Id).ToListAsync(ct);
            if (questions.Count == 0) throw new MonitoringException($"«{bank.Title}» bankida savol yo'q");
            if (row.QuestionCount is { } n)
            {
                if (n < 1 || n > questions.Count) throw new MonitoringException($"«{bank.Title}»: 1–{questions.Count} ta savol tanlash mumkin");
                // a random set, kept in the bank's order (each pupil still gets an own order when shuffled)
                var chosen = questions.OrderBy(_ => Random.Shared.Next()).Take(n).ToHashSet();
                questions = [.. questions.Where(chosen.Contains)];
            }
            try
            {
                var test = await MonitoringService.CreateTestAsync(db, new MonitoringTestPayload(
                    $"{title} — {subjectName}", bank.SubjectId, bank.Grade, bank.GroupId,
                    MonitoringDelivery.Online, MonitoringScoring.Simple, null, p.TestDate,
                    p.OpensAt, p.ClosesAt, row.TimeLimitMin, p.Owners, questions, null, null,
                    p.SeasonalYear, p.SeasonalMonth, p.Shuffle, p.MaxLeaves), userId, ct);
                if (p.Publish) await MonitoringService.SetStatusAsync(db, test.Id, MonitoringStatus.Published, ct);
                ids.Add(test.Id);
            }
            catch (MonitoringException ex)
            {
                throw new MonitoringException($"«{subjectName}»: {ex.Message}", ex.Status);
            }
        }
        var sitting = await CreateAsync(db, new MonitoringSittingPayload(title, p.TestDate, ids), userId, ct);
        await tx.CommitAsync(ct);
        return sitting;
    }

    // ================================================================== jonli kuzatuv

    public static async Task<MonitoringLiveDto> SittingLiveAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.MonitoringSittings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new MonitoringException("Sessiya topilmadi", 404);
        return await LiveAsync(db, s.Title, s.TestIds, ct) with { RoomPin = s.RoomPin };
    }

    /// <summary>Sessiyaning xona PIN'ini almashtirish.</summary>
    public static async Task<string> RotatePinAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.MonitoringSittings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new MonitoringException("Sessiya topilmadi", 404);
        s.RoomPin = await MonitoringService.NewPinAsync(db, ct);
        await db.SaveChangesAsync(ct);
        return s.RoomPin;
    }

    public static async Task<MonitoringLiveDto> TestLiveAsync(IAppDbContext db, Guid testId, CancellationToken ct)
    {
        var t = await db.MonitoringTests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == testId, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        return await LiveAsync(db, t.Title, [t.Id], ct) with { RoomPin = t.RoomPin };
    }

    /// <summary>Har o'quvchi: qaysi fanda, nechta javob, chiqishlar, oxirgi faollik, qolgan vaqt.</summary>
    private static async Task<MonitoringLiveDto> LiveAsync(IAppDbContext db, string title, List<Guid> ids, CancellationToken ct)
    {
        var tests = await db.MonitoringTests.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var ordered = ids.Select(id => tests.FirstOrDefault(t => t.Id == id)).OfType<MonitoringTest>().ToList();
        var subjectIds = ordered.Select(t => t.SubjectId).Distinct().ToList();
        var subjectNames = await db.Subjects.AsNoTracking().Where(x => subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var totals = await db.MonitoringItems.AsNoTracking().Where(i => ids.Contains(i.TestId))
            .GroupBy(i => i.TestId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var parts = await db.MonitoringParticipants.AsNoTracking().Where(x => ids.Contains(x.TestId)).ToListAsync(ct);
        var studentIds = parts.Select(x => x.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking().Where(x => studentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var owners = await MonitoringService.OwnerNamesAsync(db, ct);

        var rows = new List<MonitoringLiveRowDto>(studentIds.Count);
        foreach (var group in parts.GroupBy(x => x.StudentId))
        {
            var steps = ordered.Select(t => (T: t, P: group.FirstOrDefault(x => x.TestId == t.Id))).Where(x => x.P is not null).ToList();
            static bool Over(MonitoringParticipant p) => p.Status is MonitoringParticipantStatus.Done or MonitoringParticipantStatus.Absent;
            var at = steps.FindIndex(x => !Over(x.P!));
            var cur = at < 0 ? steps[^1] : steps[at];
            string status;
            if (at < 0) status = steps.All(x => x.P!.Status == MonitoringParticipantStatus.Absent) ? "absent" : "done";
            else if (cur.P!.Status == MonitoringParticipantStatus.InProgress) status = "in_progress";
            else status = steps.Any(x => Over(x.P!)) ? "between" : "pending";
            var p = cur.P!;
            var owner = group.FirstOrDefault(x => x.OwnerKind == LessonOwnerKind.Class) ?? group.First();
            rows.Add(new MonitoringLiveRowDto(
                group.Key, names.GetValueOrDefault(group.Key) ?? "—", owners.GetValueOrDefault(owner.OwnerId) ?? "—", status,
                subjectNames.GetValueOrDefault(cur.T.SubjectId), (at < 0 ? steps.Count - 1 : at) + 1, steps.Count,
                (p.Answers ?? "").Count(c => c != '-'), totals.GetValueOrDefault(cur.T.Id),
                group.Sum(x => x.Leaves), group.Any(x => x.FinishReason == MonitoringFinishReason.Leaves),
                group.Max(x => x.LastSeenAt),
                p.Status == MonitoringParticipantStatus.InProgress ? MonitoringService.DeadlineOf(cur.T, p) : null));
        }
        return new MonitoringLiveDto(title, DateTimeOffset.UtcNow,
            [.. ordered.Select(t => subjectNames.GetValueOrDefault(t.SubjectId) ?? t.Title)],
            [.. rows.OrderBy(r => r.OwnerName).ThenBy(r => r.StudentName)]);
    }

    // ================================================================== tayyorlik tekshiruvi

    public static async Task<List<MonitoringCheckDto>> SittingReadinessAsync(IAppDbContext db, Guid id, Func<string, bool> uploadExists, CancellationToken ct)
    {
        var s = await db.MonitoringSittings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new MonitoringException("Sessiya topilmadi", 404);
        return await ReadinessAsync(db, s.TestIds, uploadExists, ct);
    }

    public static async Task<List<MonitoringCheckDto>> TestReadinessAsync(IAppDbContext db, Guid id, Func<string, bool> uploadExists, CancellationToken ct)
    {
        if (!await db.MonitoringTests.AnyAsync(t => t.Id == id, ct)) throw new MonitoringException("Test topilmadi", 404);
        return await ReadinessAsync(db, [id], uploadExists, ct);
    }

    /// <summary>Variant joyiga bog'liq iboralar — aralashtirilgan testda ma'nosi buziladi.</summary>
    private static readonly System.Text.RegularExpressions.Regex PositionPhrase = new(
        @"\b(barcha(si)?\s+javob|hammasi\s+to|yuqoridagi|hech\s+(biri|qaysi)|[a-f]\s+va\s+[a-f]\b|all of the above|none of the above|both [a-f] and)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Test kuni oldidan tekshiruv (§2.8): o'quvchi kelib qiynalmasin deb, ma'lum xatolarni oldindan
    /// ko'rsatadi. Hech narsa o'zgartirmaydi.
    /// </summary>
    private static async Task<List<MonitoringCheckDto>> ReadinessAsync(IAppDbContext db, List<Guid> ids, Func<string, bool> uploadExists, CancellationToken ct)
    {
        const string Error = "error", Warning = "warning", Ok = "ok";
        var now = DateTimeOffset.UtcNow;
        var tz = TimeSpan.FromHours(5);
        var list = new List<MonitoringCheckDto>();
        var loaded = await db.MonitoringTests.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var tests = ids.Select(id => loaded.FirstOrDefault(t => t.Id == id)).OfType<MonitoringTest>().ToList();
        var subjectIds = tests.Select(t => t.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(x => subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var items = (await db.MonitoringItems.AsNoTracking().Where(i => ids.Contains(i.TestId)).ToListAsync(ct)).ToLookup(i => i.TestId);
        var parts = (await db.MonitoringParticipants.AsNoTracking().Where(p => ids.Contains(p.TestId)).ToListAsync(ct)).ToLookup(p => p.TestId);
        var many = tests.Count > 1;

        static string Orders(IEnumerable<int> orders) => string.Join(", ", orders.Take(12)) + (orders.Count() > 12 ? "…" : "");
        static bool IsUpload(string? url) => url is { Length: > 9 } && url.StartsWith("/uploads/", StringComparison.Ordinal);

        foreach (var t in tests)
        {
            var who = many ? $"«{subjects.GetValueOrDefault(t.SubjectId) ?? t.Title}»: " : "";
            var its = items[t.Id].OrderBy(i => i.Order).ToList();
            var ps = parts[t.Id].ToList();

            if (t.Delivery != MonitoringDelivery.Online) list.Add(new(Error, who + "qog'oz test — o'quvchi uni kompyuterda ishlay olmaydi"));
            if (t.Status == MonitoringStatus.Draft) list.Add(new(Error, who + "test hali ochilmagan — «Ochish va kodlar» bosilmagan"));
            if (t.Status == MonitoringStatus.Closed) list.Add(new(Error, who + "test yopilgan"));
            if (t.ClosesAt is { } close && close <= now)
                list.Add(new(Error, who + $"yopilish vaqti o'tib ketgan ({close.ToOffset(tz):dd.MM HH:mm})"));
            else if (t.ClosesAt is { } c2 && t.TimeLimitMin is { } lim)
            {
                var from = t.OpensAt is { } op && op > now ? op : now;
                var span = (int)(c2 - from).TotalMinutes;
                if (span < lim)
                    list.Add(new(Warning, who + $"testning ochiq qolgan vaqti ({span} daq) test vaqtidan ({lim} daq) qisqa — kech boshlaganlar to'liq vaqt ololmaydi"));
                if (t.OpensAt is { } op2 && op2 > now)
                    list.Add(new(Ok, who + $"{op2.ToOffset(tz):dd.MM HH:mm} da ochiladi"));
            }
            if (ps.Count == 0) list.Add(new(Error, who + "testda o'quvchi yo'q"));
            else if (t.Status == MonitoringStatus.Published && ps.Any(p => p.AccessCode is null))
                list.Add(new(Error, who + $"{ps.Count(p => p.AccessCode is null)} o'quvchida kod yo'q — testni qayta oching"));
            if (its.Count == 0) { list.Add(new(Error, who + "testda savol yo'q")); continue; }

            var noText = its.Where(i => string.IsNullOrWhiteSpace(i.Text) && i.ImageUrl is null).Select(i => i.Order).ToList();
            if (noText.Count > 0) list.Add(new(Error, who + $"savol matni yo'q: {Orders(noText)}-savol"));
            var emptyOption = its.Where(i => Enumerable.Range(0, i.OptionCount).Any(k =>
                string.IsNullOrWhiteSpace(i.Options.ElementAtOrDefault(k)) && string.IsNullOrEmpty(i.OptionImages.ElementAtOrDefault(k)))).Select(i => i.Order).ToList();
            if (emptyOption.Count > 0) list.Add(new(Error, who + $"bo'sh variant bor: {Orders(emptyOption)}-savol"));
            var lostImage = its.Where(i => (IsUpload(i.ImageUrl) && !uploadExists(i.ImageUrl![9..]))
                || i.OptionImages.Any(u => IsUpload(u) && !uploadExists(u[9..]))).Select(i => i.Order).ToList();
            if (lostImage.Count > 0) list.Add(new(Error, who + $"rasm serverda topilmadi: {Orders(lostImage)}-savol — o'quvchi bo'sh rasm ko'radi"));
            if (t.Shuffle)
            {
                var positional = its.Where(i => i.Options.Any(o => PositionPhrase.IsMatch(o ?? ""))).Select(i => i.Order).ToList();
                if (positional.Count > 0)
                    list.Add(new(Warning, who + $"«barcha javoblar to'g'ri», «A va B» kabi variant bor ({Orders(positional)}-savol) — aralashtirilganda ma'nosi buziladi; aralashtirishni o'chiring yoki variantni o'zgartiring"));
            }
            var dup = its.Where(i => !string.IsNullOrWhiteSpace(i.Text)).GroupBy(i => i.Text!.Trim().ToLowerInvariant())
                .Where(g => g.Count() > 1).Select(g => string.Join(" va ", g.Select(i => i.Order))).ToList();
            if (dup.Count > 0) list.Add(new(Warning, who + $"bir xil savollar: {string.Join("; ", dup.Take(5))}"));
            if (t.MaxLeaves == 0) list.Add(new(Warning, who + "oynadan birinchi chiqishdayoq test to'xtaydi — tasodifiy bosish ham hisoblanadi"));
        }

        if (tests.Count > 1)
        {
            var total = tests.Sum(t => t.TimeLimitMin ?? 0);
            var start = tests.Select(t => t.OpensAt ?? now).Append(now).Max();
            var end = tests.Where(t => t.ClosesAt is not null).Select(t => t.ClosesAt!.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (end != DateTimeOffset.MaxValue && (end - start).TotalMinutes < total)
                list.Add(new(Warning, $"fanlar vaqtlari yig'indisi {total} daq, umumiy ochiq vaqt {(int)Math.Max(0, (end - start).TotalMinutes)} daq — oxirgi fanlarga vaqt yetmasligi mumkin"));
            var firstOpen = tests[0].OpensAt ?? now;
            foreach (var t in tests.Skip(1).Where(t => t.OpensAt is { } o && o > firstOpen.AddMinutes(1) && o > now))
                list.Add(new(Warning, $"«{subjects.GetValueOrDefault(t.SubjectId) ?? t.Title}» birinchi fandan keyinroq ochiladi ({t.OpensAt!.Value.ToOffset(tz):HH:mm}) — tez ishlaganlar kutib qoladi"));
            var everyone = parts.SelectMany(g => g).Select(p => p.StudentId).Distinct().Count();
            var partial = parts.SelectMany(g => g).GroupBy(p => p.StudentId).Count(g => g.Count() < tests.Count);
            if (partial > 0) list.Add(new(Ok, $"{partial} o'quvchi hamma fanda emas (masalan boshqa yo'nalish) — ular o'z fanlarini ishlaydi"));
            list.Add(new(Ok, $"{everyone} o'quvchi, {tests.Count} fan, jami {tests.Sum(t => items[t.Id].Count())} savol, {total} daqiqa"));
        }
        else if (tests.Count == 1)
            list.Add(new(Ok, $"{parts[tests[0].Id].Count()} o'quvchi, {items[tests[0].Id].Count()} savol, {tests[0].TimeLimitMin} daqiqa"));

        if (!list.Any(c => c.Level is Error or Warning)) list.Insert(0, new(Ok, "Hammasi tayyor — ma'lum muammo topilmadi"));
        return [.. list.OrderBy(c => c.Level switch { Error => 0, Warning => 1, _ => 2 })];
    }

    /// <summary>Sessiyadagi test o'chirilmaydi — avval sessiyani o'chirish kerak.</summary>
    public static async Task EnsureNotInSittingAsync(IAppDbContext db, Guid testId, CancellationToken ct)
    {
        var all = await db.MonitoringSittings.AsNoTracking().ToListAsync(ct);
        if (all.FirstOrDefault(s => s.TestIds.Contains(testId)) is { } s)
            throw new MonitoringException($"Bu test «{s.Title}» sessiyasida — avval sessiyani o'chiring", 409);
    }
}
