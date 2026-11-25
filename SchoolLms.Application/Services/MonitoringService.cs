using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>A readable refusal: 400 (default), 404 or 409 at the HTTP edge.</summary>
public sealed class MonitoringException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// Ichki monitoring (docs/modules/curriculum-monitoring.md §2): separate question banks, tests on
/// paper (answers entered) or online (personal code), simple or national-certificate scoring, and
/// per-topic analysis. Results are for o'quv bo'limi only — except an "oylik test"
/// (<see cref="MonitoringTest.SeasonalMonth"/> set, 2026-11-24): there each finished pupil's percent
/// is also written as that month's Mavsumiy baho, which the parent sees ("Oylik o'sish").
/// </summary>
public static class MonitoringService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 8;

    // ================================================================== banks

    private static async Task<CurriculumPlan?> PlanForAsync(IAppDbContext db, string subjectId, int? grade, Guid? groupId, CancellationToken ct) =>
        await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics)
            .FirstOrDefaultAsync(p => p.SubjectId == subjectId && (groupId != null ? p.GroupId == groupId : p.Grade == grade), ct);

    public static async Task<List<MonitoringBankDto>> BanksAsync(IAppDbContext db, CancellationToken ct)
    {
        var banks = await db.MonitoringBanks.AsNoTracking()
            .Select(b => new { b, Count = db.MonitoringQuestions.Count(q => q.BankId == b.Id) }).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var groups = await db.StudyGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var plans = await db.CurriculumPlans.AsNoTracking().Select(p => new { p.Id, p.SubjectId, p.Grade, p.GroupId }).ToListAsync(ct);
        return [.. banks
            .Select(x => new MonitoringBankDto(x.b.Id, x.b.Title, x.b.SubjectId, subjects.GetValueOrDefault(x.b.SubjectId) ?? "—",
                x.b.Grade, x.b.GroupId, x.b.GroupId is { } g ? groups.GetValueOrDefault(g) : null, x.Count,
                plans.FirstOrDefault(p => p.SubjectId == x.b.SubjectId && (x.b.GroupId != null ? p.GroupId == x.b.GroupId : p.Grade == x.b.Grade))?.Id))
            .OrderBy(b => b.GroupId is null ? 0 : 1).ThenBy(b => b.Grade).ThenBy(b => b.SubjectName).ThenBy(b => b.Title)];
    }

    private static async Task ValidateScopeAsync(IAppDbContext db, string subjectId, int? grade, Guid? groupId, CancellationToken ct)
    {
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw new MonitoringException("Fanni tanlang");
        if ((grade is null) == (groupId is null)) throw new MonitoringException("Sinf darajasini yoki yo'nalishni tanlang");
        if (grade is < 0 or > 11) throw new MonitoringException("Sinf darajasi 0–11");
        if (groupId is { } gid && !await db.StudyGroups.AnyAsync(g => g.Id == gid && g.IsTrack, ct))
            throw new MonitoringException("Yo'nalish guruhi topilmadi");
    }

    public static async Task<MonitoringBank> SaveBankAsync(IAppDbContext db, Guid? id, MonitoringBankPayload p, string userId, CancellationToken ct)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length is < 2 or > 200) throw new MonitoringException("Nomi 2–200 belgi bo'lsin");
        await ValidateScopeAsync(db, p.SubjectId, p.Grade, p.GroupId, ct);
        MonitoringBank bank;
        if (id is { } existing)
        {
            bank = await db.MonitoringBanks.FirstOrDefaultAsync(b => b.Id == existing, ct) ?? throw new MonitoringException("Bank topilmadi", 404);
            var moved = bank.SubjectId != p.SubjectId || bank.Grade != p.Grade || bank.GroupId != p.GroupId;
            if (moved)
                // topics belong to the old plan — unlink them
                await db.MonitoringQuestions.Where(q => q.BankId == bank.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.TopicId, (Guid?)null), ct);
        }
        else
        {
            bank = new MonitoringBank { CreatedBy = userId, CreatedAt = DateTimeOffset.UtcNow };
            db.MonitoringBanks.Add(bank);
        }
        bank.Title = title;
        bank.SubjectId = p.SubjectId;
        bank.Grade = p.Grade;
        bank.GroupId = p.GroupId;
        await db.SaveChangesAsync(ct);
        return bank;
    }

    public static async Task<MonitoringBankDetailDto> BankDetailAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var bank = (await BanksAsync(db, ct)).FirstOrDefault(b => b.Id == id) ?? throw new MonitoringException("Bank topilmadi", 404);
        var plan = await PlanForAsync(db, bank.SubjectId, bank.Grade, bank.GroupId, ct);
        var topics = plan?.Topics.OrderBy(t => t.Order).ToList() ?? [];
        var titles = topics.ToDictionary(t => t.Id, t => $"{t.Order}. {t.Title}");
        var questions = await db.MonitoringQuestions.AsNoTracking().Where(q => q.BankId == id).OrderBy(q => q.Order).ToListAsync(ct);
        return new MonitoringBankDetailDto(bank,
            [.. questions.Select(q => new MonitoringQuestionDto(q.Id, q.Order, q.Text, q.ImageUrl, q.Options, q.CorrectIndex,
                q.TopicId, q.TopicId is { } t ? titles.GetValueOrDefault(t) : null, q.Weight, q.OptionImages))],
            [.. topics.Select(t => new MonitoringTopicRefDto(t.Id, t.Order, t.Quarter, t.Title))]);
    }

    private static async Task<HashSet<Guid>> BankTopicIdsAsync(IAppDbContext db, MonitoringBank bank, CancellationToken ct) =>
        (await PlanForAsync(db, bank.SubjectId, bank.Grade, bank.GroupId, ct))?.Topics.Select(t => t.Id).ToHashSet() ?? [];

    /// <param name="images">Variant rasmlari (<see cref="OptionImagesOf"/>): rasmli variantning matni bo'sh bo'lishi mumkin.</param>
    private static void ValidateQuestion(string text, List<string> options, int correct, decimal weight, List<string>? images = null)
    {
        if (text.Length is < 1 or > 4000) throw new MonitoringException("Savol matni 1–4000 belgi bo'lsin");
        if (options.Count is < 2 or > 6) throw new MonitoringException("Variantlar soni 2–6 bo'lsin");
        if (options.Where((o, i) => o.Length > 1000 || (o.Length == 0 && (images is not { Count: > 0 } || images[i].Length == 0))).Any())
            throw new MonitoringException("Bo'sh yoki juda uzun variant bor");
        if (correct < 0 || correct >= options.Count) throw new MonitoringException("To'g'ri javobni belgilang");
        if (weight is <= 0 or > 100) throw new MonitoringException("Vazn 0 dan katta, 100 dan kichik bo'lsin");
    }

    /// <summary>
    /// Ko'rib chiqilgan importni bankka yozadi (bankning oxiriga, kelgan tartibda). Bitta savol
    /// xato bo'lsa — hech biri yozilmaydi va qaysi ekani aytiladi.
    /// </summary>
    public static async Task<int> AddQuestionsAsync(IAppDbContext db, Guid bankId, List<MonitoringQuestionPayload> rows, CancellationToken ct)
    {
        var bank = await db.MonitoringBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankId, ct) ?? throw new MonitoringException("Bank topilmadi", 404);
        if (rows is not { Count: > 0 }) throw new MonitoringException("Qo'shiladigan savol yo'q");
        if (rows.Count > 500) throw new MonitoringException("Bir martada 500 tadan ko'p savol qo'shib bo'lmaydi");
        var topicIds = await BankTopicIdsAsync(db, bank, ct);
        var next = (await db.MonitoringQuestions.Where(x => x.BankId == bankId).MaxAsync(x => (int?)x.Order, ct) ?? 0) + 1;
        var added = new List<MonitoringQuestion>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var p = rows[i];
            var text = (p.Text ?? "").Trim();
            var options = (p.Options ?? []).Select(o => (o ?? "").Trim()).ToList();
            var weight = p.Weight ?? 1m;
            List<string> images;
            try
            {
                images = OptionImagesOf(p.OptionImages, options.Count);
                ValidateQuestion(text, options, p.CorrectIndex, weight, images);
                if (p.TopicId is { } topic && !topicIds.Contains(topic))
                    throw new MonitoringException("mavzu shu fan va sinfning ish rejasidan bo'lsin");
                if (p.ImageUrl is { Length: > 0 } img && !IsUploadUrl(img))
                    throw new MonitoringException("rasm manzili noto'g'ri");
            }
            catch (MonitoringException ex) { throw new MonitoringException($"{i + 1}-savol: {ex.Message}"); }
            added.Add(new MonitoringQuestion
            {
                BankId = bankId, Order = next++, Text = text, ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl) ? null : p.ImageUrl,
                Options = options, OptionImages = images, CorrectIndex = p.CorrectIndex, TopicId = p.TopicId, Weight = weight,
            });
        }
        db.MonitoringQuestions.AddRange(added);
        await db.SaveChangesAsync(ct);
        return added.Count;
    }

    /// <summary>
    /// Variant rasmlari: har variantga "" yoki /uploads manzili. Hech birida rasm bo'lmasa — bo'sh ro'yxat
    /// (oddiy savol hech narsa saqlamaydi).
    /// </summary>
    private static List<string> OptionImagesOf(List<string>? images, int optionCount)
    {
        var list = (images ?? []).Select(x => (x ?? "").Trim()).ToList();
        if (list.All(x => x.Length == 0)) return [];
        if (list.Count != optionCount) throw new MonitoringException("Variant rasmlari soni variantlar soniga mos emas");
        if (list.Any(x => x.Length > 0 && !IsUploadUrl(x))) throw new MonitoringException("Variant rasmi manzili noto'g'ri");
        return list;
    }

    /// <summary>Faqat o'zimiz yozgan fayl: <c>/uploads/&lt;nom&gt;.&lt;kengaytma&gt;</c>, papka va ".." siz.</summary>
    private static bool IsUploadUrl(string url) =>
        url.StartsWith("/uploads/", StringComparison.Ordinal) && url.Length < 300
        && url[9..].All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_') && !url.Contains("..");

    public static async Task<Guid> SaveQuestionAsync(IAppDbContext db, Guid bankId, Guid? id, MonitoringQuestionPayload p, CancellationToken ct)
    {
        var bank = await db.MonitoringBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankId, ct) ?? throw new MonitoringException("Bank topilmadi", 404);
        var options = (p.Options ?? []).Select(o => (o ?? "").Trim()).ToList();
        var weight = p.Weight ?? 1m;
        var images = OptionImagesOf(p.OptionImages, options.Count);
        ValidateQuestion((p.Text ?? "").Trim(), options, p.CorrectIndex, weight, images);
        if (p.TopicId is { } topic && !(await BankTopicIdsAsync(db, bank, ct)).Contains(topic))
            throw new MonitoringException("Mavzu shu fan va sinfning ish rejasidan bo'lsin");
        if (p.ImageUrl is { Length: > 0 } img && !IsUploadUrl(img))
            throw new MonitoringException("Rasm manzili noto'g'ri");

        MonitoringQuestion q;
        if (id is { } qid)
            q = await db.MonitoringQuestions.FirstOrDefaultAsync(x => x.Id == qid && x.BankId == bankId, ct) ?? throw new MonitoringException("Savol topilmadi", 404);
        else
        {
            var next = (await db.MonitoringQuestions.Where(x => x.BankId == bankId).MaxAsync(x => (int?)x.Order, ct) ?? 0) + 1;
            q = new MonitoringQuestion { BankId = bankId, Order = next };
            db.MonitoringQuestions.Add(q);
        }
        q.Text = p.Text!.Trim();
        q.ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl) ? null : p.ImageUrl;
        q.Options = options;
        q.OptionImages = images;
        q.CorrectIndex = p.CorrectIndex;
        q.TopicId = p.TopicId;
        q.Weight = weight;
        await db.SaveChangesAsync(ct);
        return q.Id;
    }

    /// <summary>Excel ustunlari: savollar importi va namuna fayli.</summary>
    public static readonly string[] QuestionColumns = ["Savol", "A", "B", "C", "D", "E", "F", "Javob", "Mavzu №", "Vazn"];

    public static byte[] QuestionTemplate() => ExcelExport.Build(
    [
        new ExcelExport.SheetSpec("Savollar", QuestionColumns,
        [
            ["24 + 18 = ?", "32", "42", "44", "52", "", "", "B", "3", "1"],
            ["Eng kichik ikki xonali son qaysi?", "1", "9", "10", "11", "", "", "C", "1", "1"],
        ]),
        new ExcelExport.SheetSpec("Ko'rsatma", ["Ustun", "Izoh"],
        [
            ["Savol", "Savol matni (majburiy)."],
            ["A–F", "Variantlar: kamida 2 ta (A va B), ko'pi bilan 6 ta. Bo'sh ustunlar o'tkaziladi."],
            ["Javob", "To'g'ri variant harfi: A, B, C …"],
            ["Mavzu №", "Ish rejadagi mavzuning tartib raqami (ixtiyoriy). Mavzu bo'yicha tahlil shundan chiqadi."],
            ["Vazn", "Milliy uslub, \"qo'lda vazn\" usuli uchun savol bali (sukut — 1)."],
        ]),
    ]);

    public static async Task<MonitoringImportResultDto> ImportQuestionsAsync(IAppDbContext db, Guid bankId, Stream stream, CancellationToken ct)
    {
        var bank = await db.MonitoringBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankId, ct) ?? throw new MonitoringException("Bank topilmadi", 404);
        List<string[]> rows;
        try { rows = ExcelImport.ReadRows(stream, QuestionColumns.Length); }
        catch (Exception) { throw new MonitoringException("Fayl o'qilmadi — .xlsx (Excel) formatida yuklang."); }
        var plan = await PlanForAsync(db, bank.SubjectId, bank.Grade, bank.GroupId, ct);
        var topicByOrder = plan?.Topics.ToDictionary(t => t.Order, t => t.Id) ?? [];
        var next = (await db.MonitoringQuestions.Where(x => x.BankId == bankId).MaxAsync(x => (int?)x.Order, ct) ?? 0) + 1;

        var errors = new List<string>();
        var added = new List<MonitoringQuestion>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i].Select(c => (c ?? "").Trim()).ToArray();
            var line = i + 1;
            if (r.All(string.IsNullOrEmpty)) continue;
            if (string.Equals(r[0], "Savol", StringComparison.OrdinalIgnoreCase)) continue;
            var options = r[1..7].TakeWhile(o => o.Length > 0).ToList();
            var answer = r[7].ToUpperInvariant();
            var correct = answer.Length == 1 ? answer[0] - 'A' : -1;
            var weight = 1m;
            if (r[9].Length > 0 && !decimal.TryParse(r[9].Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out weight))
            { errors.Add($"{line}-qator: «Vazn» son bo'lsin"); continue; }
            Guid? topic = null;
            if (r[8].Length > 0)
            {
                if (!int.TryParse(r[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) && !(decimal.TryParse(r[8], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && (order = (int)d) == d))
                { errors.Add($"{line}-qator: «Mavzu №» son bo'lsin"); continue; }
                if (!topicByOrder.TryGetValue(order, out var tid)) { errors.Add($"{line}-qator: ish rejada {order}-mavzu yo'q"); continue; }
                topic = tid;
            }
            try { ValidateQuestion(r[0], options, correct, weight); }
            catch (MonitoringException ex) { errors.Add($"{line}-qator: {ex.Message}"); continue; }
            added.Add(new MonitoringQuestion
            {
                BankId = bankId, Order = next++, Text = r[0], Options = options, CorrectIndex = correct, TopicId = topic, Weight = weight,
            });
        }
        if (errors.Count > 0) return new MonitoringImportResultDto(0, errors);
        if (added.Count == 0) return new MonitoringImportResultDto(0, ["Faylda savol topilmadi"]);
        db.MonitoringQuestions.AddRange(added);
        await db.SaveChangesAsync(ct);
        return new MonitoringImportResultDto(added.Count, []);
    }

    // ================================================================== tests

    internal static async Task<Dictionary<string, string>> OwnerNamesAsync(IAppDbContext db, CancellationToken ct)
    {
        var names = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        foreach (var g in await db.StudyGroups.AsNoTracking().Select(g => new { g.Id, g.Name }).ToListAsync(ct))
            names[g.Id.ToString()] = g.Name;
        return names;
    }

    public static async Task<MonitoringTest> CreateTestAsync(IAppDbContext db, MonitoringTestPayload p, string userId, CancellationToken ct)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length is < 2 or > 200) throw new MonitoringException("Test nomi 2–200 belgi bo'lsin");
        await ValidateScopeAsync(db, p.SubjectId, p.Grade, p.GroupId, ct);
        if (!MonitoringDelivery.IsValid(p.Delivery)) throw new MonitoringException("O'tkazish usulini tanlang");
        if (!MonitoringScoring.IsValid(p.Scoring)) throw new MonitoringException("Baholash usulini tanlang");
        var method = p.Scoring == MonitoringScoring.National ? p.NationalMethod : null;
        if (p.Scoring == MonitoringScoring.National && !MonitoringNationalMethod.IsValid(method))
            throw new MonitoringException("Milliy uslub uchun usulni tanlang: Rasch yoki qo'lda vazn");
        if (!DateOnly.TryParseExact(p.TestDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new MonitoringException("Sana noto'g'ri");
        if (p.Delivery == MonitoringDelivery.Online)
        {
            if (p.OpensAt is null || p.ClosesAt is null || p.ClosesAt <= p.OpensAt) throw new MonitoringException("Onlayn test uchun ochilish va yopilish vaqtini kiriting");
            if (p.TimeLimitMin is not (>= 5 and <= 300)) throw new MonitoringException("Vaqt 5–300 daqiqa bo'lsin");
        }
        if (p.Delivery == MonitoringDelivery.Online && p.MaxLeaves is not (null or (>= 0 and <= 20)))
            throw new MonitoringException("Oynadan chiqishlar soni 0–20 bo'lsin (bo'sh — cheklanmagan)");
        if ((p.SeasonalYear is null) != (p.SeasonalMonth is null))
            throw new MonitoringException("Oylik test uchun yil va oyni birga tanlang");
        if (p.SeasonalYear is { } sy && (sy is < 2000 or > 2100 || p.SeasonalMonth is not (>= 1 and <= 12)))
            throw new MonitoringException("Oylik test oyi noto'g'ri");

        var test = new MonitoringTest
        {
            Title = title, SubjectId = p.SubjectId, Grade = p.Grade, GroupId = p.GroupId, Delivery = p.Delivery,
            Scoring = p.Scoring, NationalMethod = method, TestDate = date,
            OpensAt = p.Delivery == MonitoringDelivery.Online ? p.OpensAt!.Value.ToUniversalTime() : null,
            ClosesAt = p.Delivery == MonitoringDelivery.Online ? p.ClosesAt!.Value.ToUniversalTime() : null,
            TimeLimitMin = p.Delivery == MonitoringDelivery.Online ? p.TimeLimitMin : null,
            SeasonalYear = p.SeasonalYear, SeasonalMonth = p.SeasonalMonth,
            Shuffle = p.Delivery == MonitoringDelivery.Online && p.Shuffle,
            MaxLeaves = p.Delivery == MonitoringDelivery.Online ? p.MaxLeaves : null,
            Status = MonitoringStatus.Draft, CreatedBy = userId, CreatedAt = DateTimeOffset.UtcNow,
        };

        // items
        var items = new List<MonitoringItem>();
        if (p.QuestionIds is { Count: > 0 } ids)
        {
            var questions = await db.MonitoringQuestions.AsNoTracking().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
            var bankIds = questions.Values.Select(q => q.BankId).Distinct().ToList();
            var banks = await db.MonitoringBanks.AsNoTracking().Where(b => bankIds.Contains(b.Id)).ToListAsync(ct);
            if (banks.Any(b => b.SubjectId != p.SubjectId)) throw new MonitoringException("Savollar boshqa fan bankidan");
            var order = 1;
            foreach (var qid in ids.Distinct())
            {
                if (!questions.TryGetValue(qid, out var q)) throw new MonitoringException("Savol topilmadi");
                items.Add(new MonitoringItem
                {
                    TestId = test.Id, Order = order++, QuestionId = q.Id, Text = q.Text, ImageUrl = q.ImageUrl, Options = [.. q.Options], OptionImages = [.. q.OptionImages],
                    OptionCount = q.Options.Count, CorrectIndex = q.CorrectIndex, TopicId = q.TopicId, Weight = q.Weight,
                });
            }
        }
        else
        {
            if (p.Delivery == MonitoringDelivery.Online) throw new MonitoringException("Onlayn test savollar bankidan tuziladi — savollarni tanlang");
            var key = (p.Key ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            var count = p.OptionCount ?? 4;
            if (count is < 2 or > 6) throw new MonitoringException("Variantlar soni 2–6 bo'lsin");
            if (key.Length is < 1 or > 300) throw new MonitoringException("Javoblar kalitini kiriting (masalan ABDC…), 300 tagacha savol");
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i] - 'A';
                if (c < 0 || c >= count) throw new MonitoringException($"Kalitning {i + 1}-belgisi noto'g'ri: «{key[i]}»");
                items.Add(new MonitoringItem { TestId = test.Id, Order = i + 1, OptionCount = count, CorrectIndex = c });
            }
        }
        if (items.Count == 0) throw new MonitoringException("Testda savol yo'q");
        test.Items = items;

        // participants — a snapshot of the chosen classes and groups
        if (p.Owners is not { Count: > 0 }) throw new MonitoringException("Kamida bitta sinf yoki guruhni tanlang");
        var participants = new Dictionary<string, MonitoringParticipant>(StringComparer.Ordinal);
        foreach (var o in p.Owners.DistinctBy(o => (o.Kind, o.Id)))
        {
            var owner = await LessonRoster.OwnerAsync(db, o.Id, ct);
            if (owner is null || owner.Kind != o.Kind) throw new MonitoringException("Sinf yoki guruh topilmadi");
            foreach (var s in await LessonRoster.Query(db, owner).Select(s => s.Id).ToListAsync(ct))
                participants.TryAdd(s, new MonitoringParticipant { TestId = test.Id, StudentId = s, OwnerKind = owner.Kind, OwnerId = owner.Id });
        }
        if (participants.Count == 0) throw new MonitoringException("Tanlangan sinf/guruhlarda o'quvchi yo'q");

        db.MonitoringTests.Add(test);
        db.MonitoringParticipants.AddRange(participants.Values);
        await db.SaveChangesAsync(ct);
        return test;
    }

    /// <summary>Paper key / topics / weights edit — the test is rescored.</summary>
    private const string TestLockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    /// <summary>
    /// Bir testni yakunlash / qayta baholash navbat bilan (2026-11-25, §2.8). Har yakunlash butun
    /// testni qayta baholaydi va Mavsumiy bahoni yozadi: 30 o'quvchi birdan tugatsa, ikki parallel
    /// yozuv bir o'quvchining bahosini ikki marta qo'shmoqchi bo'lib unique indeksga urilardi —
    /// o'quvchi xato olardi va yakuni saqlanmasdi. Tranzaksiya ochadi (ochiq bo'lsa o'shani ishlatadi
    /// va <c>null</c> qaytaradi), qulfni oladi va qulfdan oldin o'qilgan yozuvlarni qayta o'qiydi.
    /// </summary>
    private static async Task<IDbContextTransaction?> LockTestAsync(IAppDbContext db, Guid testId, CancellationToken ct, params object[] reload)
    {
        if (db is not DbContext ef) return null;
        var tx = ef.Database.CurrentTransaction is null ? await db.BeginTransactionAsync(ct) : null;
        await ef.Database.ExecuteSqlRawAsync(TestLockSql, ["monitoring-test:" + testId], ct);
        foreach (var entity in reload) await ef.Entry(entity).ReloadAsync(ct);
        return tx;
    }

    private static async Task CommitAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    public static async Task UpdateItemsAsync(IAppDbContext db, Guid testId, List<MonitoringItemKeyPayload> rows, CancellationToken ct)
    {
        await using var tx = await LockTestAsync(db, testId, ct);
        var test = await db.MonitoringTests.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == testId, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        var plan = await PlanForAsync(db, test.SubjectId, test.Grade, test.GroupId, ct);
        var topicIds = plan?.Topics.Select(t => t.Id).ToHashSet() ?? [];
        foreach (var r in rows)
        {
            var item = test.Items.FirstOrDefault(i => i.Order == r.Order) ?? throw new MonitoringException($"{r.Order}-savol topilmadi");
            if (item.QuestionId is null && r.OptionCount is { } oc)
            {
                if (oc is < 2 or > 6) throw new MonitoringException("Variantlar soni 2–6 bo'lsin");
                item.OptionCount = oc;
            }
            if (r.CorrectIndex < 0 || r.CorrectIndex >= item.OptionCount) throw new MonitoringException($"{r.Order}-savol: to'g'ri javob noto'g'ri");
            if (r.TopicId is { } t && !topicIds.Contains(t)) throw new MonitoringException($"{r.Order}-savol: mavzu ish rejadan bo'lsin");
            if (r.Weight is <= 0 or > 100) throw new MonitoringException($"{r.Order}-savol: vazn noto'g'ri");
            item.CorrectIndex = r.CorrectIndex;
            item.TopicId = r.TopicId;
            if (r.Weight is { } w) item.Weight = w;
        }
        await RescoreAsync(db, test, ct);
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
    }

    private static async Task RescoreAsync(IAppDbContext db, MonitoringTest test, CancellationToken ct)
    {
        var items = test.Items.Count > 0 ? test.Items.OrderBy(i => i.Order).ToList()
            : await db.MonitoringItems.Where(i => i.TestId == test.Id).OrderBy(i => i.Order).ToListAsync(ct);
        var participants = await db.MonitoringParticipants.Where(p => p.TestId == test.Id).ToListAsync(ct);
        MonitoringScore.Apply(test, items, participants);
        await SyncSeasonalAsync(db, test, participants, ct);
    }

    public static async Task SetStatusAsync(IAppDbContext db, Guid testId, string status, CancellationToken ct)
    {
        await using var tx = await LockTestAsync(db, testId, ct);
        var test = await db.MonitoringTests.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == testId, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        var participants = await db.MonitoringParticipants.Where(p => p.TestId == testId).ToListAsync(ct);
        if (status == MonitoringStatus.Published)
        {
            if (test.Status == MonitoringStatus.Closed) throw new MonitoringException("Yopilgan testni qayta ochib bo'lmaydi", 409);
            if (test.Delivery == MonitoringDelivery.Online)
            {
                test.RoomPin ??= await NewPinAsync(db, ct);
                var taken = await TakenCodesAsync(db, ct);
                foreach (var p in participants.Where(p => p.AccessCode is null))
                {
                    string code;
                    do code = NewCode(); while (!taken.Add(code));
                    p.AccessCode = code;
                }
            }
        }
        else if (status == MonitoringStatus.Closed)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var p in participants.Where(p => p.Status == MonitoringParticipantStatus.InProgress))
            {
                p.Status = MonitoringParticipantStatus.Done;
                p.FinishedAt = now;
            }
            if (test.Delivery == MonitoringDelivery.Online)
                foreach (var p in participants.Where(p => p.Status == MonitoringParticipantStatus.Pending))
                    p.Status = MonitoringParticipantStatus.Absent;
            MonitoringScore.Apply(test, [.. test.Items.OrderBy(i => i.Order)], participants);
            await SyncSeasonalAsync(db, test, participants, ct);
        }
        else throw new MonitoringException("Holat noto'g'ri");
        test.Status = status;
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
    }

    /// <summary>Band kodlar — bitta test kodlari va sessiya kodlari bir xil bo'lmasin.</summary>
    internal static async Task<HashSet<string>> TakenCodesAsync(IAppDbContext db, CancellationToken ct)
    {
        var taken = (await db.MonitoringParticipants.Where(p => p.AccessCode != null).Select(p => p.AccessCode!).ToListAsync(ct)).ToHashSet();
        taken.UnionWith(await db.MonitoringSittingCodes.Select(c => c.Code).ToListAsync(ct));
        taken.UnionWith(await db.MonitoringStudentCodes.Select(c => c.Code).ToListAsync(ct));
        return taken;
    }

    /// <summary>
    /// Yangi xona PIN'i — 4 raqam, hozir ochiq testlar va so'nggi 60 kundagi sessiyalarnikidan farqli
    /// (doimiy kod + PIN aynan bitta test yoki sessiyani topsin).
    /// </summary>
    internal static async Task<string> NewPinAsync(IAppDbContext db, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-60);
        var used = (await db.MonitoringSittings.Where(s => s.RoomPin != null && s.CreatedAt > since).Select(s => s.RoomPin!).ToListAsync(ct))
            .Concat(await db.MonitoringTests.Where(t => t.RoomPin != null && t.Status != MonitoringStatus.Closed).Select(t => t.RoomPin!).ToListAsync(ct))
            .ToHashSet();
        for (var i = 0; i < 500; i++)
        {
            var pin = RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture);
            if (used.Add(pin)) return pin;
        }
        throw new MonitoringException("Bo'sh PIN topilmadi — qayta urinib ko'ring", 409);
    }

    /// <summary>Testning xona PIN'ini almashtirish (masalan, PIN boshqa xonaga ma'lum bo'lib qolsa).</summary>
    public static async Task<string> RotateTestPinAsync(IAppDbContext db, Guid testId, CancellationToken ct)
    {
        var t = await db.MonitoringTests.FirstOrDefaultAsync(x => x.Id == testId, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        if (t.Delivery != MonitoringDelivery.Online) throw new MonitoringException("PIN faqat onlayn testda", 409);
        t.RoomPin = await NewPinAsync(db, ct);
        await db.SaveChangesAsync(ct);
        return t.RoomPin;
    }

    internal static string NewCode() =>
        string.Concat(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));

    /// <summary>Paper answers: "ABDC-…" per pupil, or absent. The whole test is rescored (Rasch depends on everyone).</summary>
    public static async Task SaveAnswersAsync(IAppDbContext db, Guid testId, List<MonitoringAnswersPayload> rows, CancellationToken ct)
    {
        await using var tx = await LockTestAsync(db, testId, ct);
        var test = await db.MonitoringTests.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == testId, ct) ?? throw new MonitoringException("Test topilmadi", 404);
        if (test.Delivery != MonitoringDelivery.Paper) throw new MonitoringException("Onlayn testda javoblarni o'quvchi o'zi kiritadi", 409);
        var items = test.Items.OrderBy(i => i.Order).ToList();
        var participants = await db.MonitoringParticipants.Where(p => p.TestId == testId).ToListAsync(ct);
        foreach (var r in rows)
        {
            var p = participants.FirstOrDefault(x => x.Id == r.ParticipantId) ?? throw new MonitoringException("O'quvchi topilmadi");
            if (r.Absent)
            {
                p.Status = MonitoringParticipantStatus.Absent;
                p.Answers = null;
                continue;
            }
            if (string.IsNullOrWhiteSpace(r.Answers))
            {
                p.Status = MonitoringParticipantStatus.Pending;
                p.Answers = null;
                continue;
            }
            p.Answers = MonitoringScore.Normalize(r.Answers, items)
                ?? throw new MonitoringException($"Javoblar savollar sonidan ({items.Count}) ko'p");
            p.Status = MonitoringParticipantStatus.Done;
            p.FinishedAt ??= DateTimeOffset.UtcNow;
        }
        MonitoringScore.Apply(test, items, participants);
        await SyncSeasonalAsync(db, test, participants, ct);
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
    }

    public static async Task ResetDeviceAsync(IAppDbContext db, Guid participantId, CancellationToken ct)
    {
        var p = await db.MonitoringParticipants.FirstOrDefaultAsync(x => x.Id == participantId, ct) ?? throw new MonitoringException("O'quvchi topilmadi", 404);
        p.DeviceToken = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Oynadan chiqish chegarasi bilan yakunlangan testni davom ettirishga ruxsat (o'quv bo'limi
    /// sababini aniqlagandan keyin). Chiqishlar hisobi noldan boshlanadi; jurnal saqlanadi.
    /// </summary>
    public static async Task ReopenAsync(IAppDbContext db, Guid participantId, CancellationToken ct)
    {
        var p = await db.MonitoringParticipants.FirstOrDefaultAsync(x => x.Id == participantId, ct) ?? throw new MonitoringException("O'quvchi topilmadi", 404);
        await using var tx = await LockTestAsync(db, p.TestId, ct, p);
        var t = await db.MonitoringTests.FirstAsync(x => x.Id == p.TestId, ct);
        if (p.Status != MonitoringParticipantStatus.Done || p.FinishReason != MonitoringFinishReason.Leaves)
            throw new MonitoringException("Faqat oynadan chiqish sababli yakunlangan testni davom ettirish mumkin", 409);
        if (t.Status != MonitoringStatus.Published) throw new MonitoringException("Test ochiq emas", 409);
        if (DeadlineOf(t, p) <= DateTimeOffset.UtcNow) throw new MonitoringException("Bu o'quvchining vaqti tugagan", 409);
        p.Status = MonitoringParticipantStatus.InProgress;
        p.FinishedAt = null;
        p.FinishReason = null;
        p.Leaves = 0;
        AppendEvent(p, "reopened");
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
    }

    // ================================================================== read models

    public static async Task<List<MonitoringTestListItemDto>> TestsAsync(IAppDbContext db, CancellationToken ct)
    {
        await FinishExpiredAsync(db, null, ct);
        var tests = await db.MonitoringTests.AsNoTracking().OrderByDescending(t => t.TestDate).ThenByDescending(t => t.CreatedAt).ToListAsync(ct);
        var ids = tests.Select(t => t.Id).ToList();
        var parts = (await db.MonitoringParticipants.AsNoTracking().Where(p => ids.Contains(p.TestId))
            .Select(p => new { p.TestId, p.Status, p.Percent, p.Points, p.OwnerId }).ToListAsync(ct)).ToLookup(p => p.TestId);
        var itemCounts = await db.MonitoringItems.AsNoTracking().Where(i => ids.Contains(i.TestId))
            .GroupBy(i => i.TestId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var names = await OwnerNamesAsync(db, ct);
        return [.. tests.Select(t =>
        {
            var ps = parts[t.Id].ToList();
            var done = ps.Where(p => p.Status == MonitoringParticipantStatus.Done).ToList();
            return new MonitoringTestListItemDto(
                t.Id, t.Title, subjects.GetValueOrDefault(t.SubjectId) ?? "—", t.Grade,
                t.GroupId is { } g ? names.GetValueOrDefault(g.ToString()) : null,
                t.Delivery, t.Scoring, t.NationalMethod, t.TestDate.ToString("yyyy-MM-dd"), t.Status,
                itemCounts.GetValueOrDefault(t.Id), ps.Count, done.Count,
                done.Count == 0 ? null : Math.Round(done.Average(p => p.Percent), 1),
                done.Count == 0 || t.Scoring != MonitoringScoring.National ? null : Math.Round(done.Average(p => p.Points ?? 0), 1),
                [.. ps.Select(p => p.OwnerId).Distinct().Select(o => names.GetValueOrDefault(o) ?? "—").Order()],
                t.SeasonalYear, t.SeasonalMonth);
        })];
    }

    public static async Task<MonitoringTestDetailDto> DetailAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        await FinishExpiredAsync(db, id, ct);
        var test = await db.MonitoringTests.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new MonitoringException("Test topilmadi", 404);
        var items = test.Items.OrderBy(i => i.Order).ToList();
        var participants = await (from p in db.MonitoringParticipants.AsNoTracking()
                                  join s in db.Students.AsNoTracking() on p.StudentId equals s.Id
                                  where p.TestId == id
                                  select new { P = p, s.FullName }).ToListAsync(ct);
        var names = await OwnerNamesAsync(db, ct);
        var subjectName = await db.Subjects.AsNoTracking().Where(s => s.Id == test.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var topicIds = items.Where(i => i.TopicId != null).Select(i => i.TopicId!.Value).Distinct().ToList();
        var topics = await db.CurriculumTopics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => $"{t.Order}. {t.Title}", ct);

        var done = participants.Where(x => x.P.Status == MonitoringParticipantStatus.Done).ToList();
        var marks = done.ToDictionary(x => x.P.Id, x => MonitoringScore.Marks(x.P.Answers, items));
        decimal Rate(IEnumerable<(Guid Pid, int Index)> cells)
        {
            var list = cells.ToList();
            return list.Count == 0 ? 0 : Math.Round(list.Count(c => marks[c.Pid][c.Index]) * 100m / list.Count, 1);
        }

        var ownerIds = participants.Select(x => x.P.OwnerId).Distinct().OrderBy(o => names.GetValueOrDefault(o)).ToList();
        var topicStats = items.Select((it, idx) => (it, idx)).GroupBy(x => x.it.TopicId)
            .Select(g => new MonitoringTopicStatDto(
                g.Key, g.Key is { } t ? topics.GetValueOrDefault(t) ?? "—" : "Mavzu biriktirilmagan", g.Count(),
                Rate(done.SelectMany(d => g.Select(x => (d.P.Id, x.idx)))),
                [.. ownerIds.Select(o => new MonitoringOwnerRateDto(o, names.GetValueOrDefault(o) ?? "—",
                    Rate(done.Where(d => d.P.OwnerId == o).SelectMany(d => g.Select(x => (d.P.Id, x.idx))))))]))
            .OrderBy(s => s.CorrectRate).ToList();

        var itemStats = items.Select((it, idx) =>
        {
            var choices = new Dictionary<string, int>();
            foreach (var d in done)
            {
                var c = d.P.Answers is { } a && idx < a.Length ? a[idx].ToString() : "-";
                choices[c] = choices.GetValueOrDefault(c) + 1;
            }
            return new MonitoringItemStatDto(it.Order, it.TopicId is { } t ? topics.GetValueOrDefault(t) : null,
                MonitoringScore.Letter(it.CorrectIndex).ToString(), Rate(done.Select(d => (d.P.Id, idx))), choices);
        }).ToList();

        var owners = ownerIds.Select(o =>
        {
            var ps = participants.Where(x => x.P.OwnerId == o).ToList();
            var dn = ps.Where(x => x.P.Status == MonitoringParticipantStatus.Done).ToList();
            return new MonitoringOwnerSummaryDto(o, names.GetValueOrDefault(o) ?? "—", ps.Count, dn.Count,
                dn.Count == 0 ? null : Math.Round(dn.Average(x => x.P.Percent), 1),
                dn.Count == 0 || test.Scoring != MonitoringScoring.National ? null : Math.Round(dn.Average(x => x.P.Points ?? 0), 1));
        }).ToList();

        var levels = test.Scoring == MonitoringScoring.National
            ? MonitoringScore.Bands.Select(b => b.Level).Append("—")
                .ToDictionary(l => l, l => done.Count(d => (d.P.Level ?? "—") == l))
            : [];

        return new MonitoringTestDetailDto(
            test.Id, test.Title, test.SubjectId, subjectName, test.Grade, test.GroupId,
            test.GroupId is { } g ? names.GetValueOrDefault(g.ToString()) : null,
            test.Delivery, test.Scoring, test.NationalMethod, test.TestDate.ToString("yyyy-MM-dd"),
            test.OpensAt, test.ClosesAt, test.TimeLimitMin, test.Status,
            [.. items.Select(i => new MonitoringItemDto(i.Id, i.Order, i.Text, i.ImageUrl, i.Options, i.OptionCount, i.CorrectIndex,
                i.TopicId, i.TopicId is { } t ? topics.GetValueOrDefault(t) : null, i.Weight, i.OptionImages))],
            [.. participants.OrderBy(x => names.GetValueOrDefault(x.P.OwnerId)).ThenBy(x => x.FullName).Select(x => new MonitoringParticipantDto(
                x.P.Id, x.P.StudentId, x.FullName, x.P.OwnerKind, x.P.OwnerId, names.GetValueOrDefault(x.P.OwnerId) ?? "—",
                x.P.Status, x.P.Answers, x.P.Correct, x.P.Percent, x.P.Points, x.P.Level, x.P.AccessCode, x.P.DeviceToken != null,
                x.P.StartedAt?.ToString("o"), x.P.FinishedAt?.ToString("o"), x.P.Leaves, x.P.Events, x.P.FinishReason))],
            owners, topicStats, itemStats, levels,
            test.Scoring == MonitoringScoring.National && test.NationalMethod == MonitoringNationalMethod.Rasch && done.Count < 30,
            [.. (await PlanForAsync(db, test.SubjectId, test.Grade, test.GroupId, ct))?.Topics.OrderBy(t => t.Order)
                .Select(t => new MonitoringTopicRefDto(t.Id, t.Order, t.Quarter, t.Title)) ?? []],
            test.SeasonalYear, test.SeasonalMonth, test.Shuffle, test.MaxLeaves, test.RoomPin);
    }

    public static byte[] ResultsXlsx(MonitoringTestDetailDto d)
    {
        var national = d.Scoring == MonitoringScoring.National;
        var headers = new List<string> { "O'quvchi", "Sinf / guruh", "Holat", "To'g'ri", "Foiz" };
        if (national) headers.AddRange(["Ball (0–75)", "Daraja"]);
        headers.Add("Javoblar");
        headers.Add("Oynadan chiqish");
        var rows = d.Participants.Select(p =>
        {
            var row = new List<string> { p.StudentName, p.OwnerName, StatusLabel(p.Status), p.Correct.ToString(), p.Percent.ToString("0.#", CultureInfo.InvariantCulture) };
            if (national) row.AddRange([p.Points?.ToString("0.#", CultureInfo.InvariantCulture) ?? "", p.Level ?? ""]);
            row.Add(p.Answers ?? "");
            row.Add(p.FinishReason == MonitoringFinishReason.Leaves ? $"{p.Leaves} (test to'xtatildi)" : p.Leaves.ToString());
            return (IReadOnlyList<string>)row;
        }).ToList();
        var topicRows = d.Topics.Select(t => (IReadOnlyList<string>)
            [t.Title, t.ItemCount.ToString(), t.CorrectRate.ToString("0.#", CultureInfo.InvariantCulture),
             .. t.ByOwner.Select(o => o.CorrectRate.ToString("0.#", CultureInfo.InvariantCulture))]).ToList();
        var topicHeaders = new List<string> { "Mavzu", "Savollar", "To'g'ri, %" };
        topicHeaders.AddRange(d.Owners.Select(o => o.OwnerName + ", %"));
        return ExcelExport.Build([new ExcelExport.SheetSpec("Natijalar", headers, rows), new ExcelExport.SheetSpec("Mavzular", topicHeaders, topicRows)]);
    }

    public static string StatusLabel(string status) => status switch
    {
        MonitoringParticipantStatus.Done => "Topshirdi",
        MonitoringParticipantStatus.InProgress => "Yechmoqda",
        MonitoringParticipantStatus.Absent => "Kelmadi",
        _ => "Kutilmoqda",
    };

    // ================================================================== online (public, by code)

    internal static DateTimeOffset DeadlineOf(MonitoringTest t, MonitoringParticipant p)
    {
        var limit = (p.StartedAt ?? DateTimeOffset.UtcNow).AddMinutes(t.TimeLimitMin ?? 60);
        return t.ClosesAt is { } c && c < limit ? c : limit;
    }

    /// <summary>Online attempts past their deadline are finished (lazy — no background job).</summary>
    public static async Task FinishExpiredAsync(IAppDbContext db, Guid? testId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var q = from p in db.MonitoringParticipants.AsNoTracking()
                join t in db.MonitoringTests.AsNoTracking() on p.TestId equals t.Id
                where t.Delivery == MonitoringDelivery.Online && p.Status == MonitoringParticipantStatus.InProgress
                select new { P = p, T = t };
        if (testId is { } id) q = q.Where(x => x.T.Id == id);
        var open = await q.ToListAsync(ct);
        var expiredTests = open.Where(x => DeadlineOf(x.T, x.P) <= now).Select(x => x.T.Id).Distinct().Order().ToList();
        // one test at a time, under its lock, re-reading who is still in progress
        foreach (var tid in expiredTests)
        {
            await using var tx = await LockTestAsync(db, tid, ct);
            var test = await db.MonitoringTests.FirstAsync(t => t.Id == tid, ct);
            var participants = await db.MonitoringParticipants.Where(p => p.TestId == tid).ToListAsync(ct);
            var expired = participants.Where(p => p.Status == MonitoringParticipantStatus.InProgress && DeadlineOf(test, p) <= now).ToList();
            if (expired.Count == 0) continue;
            foreach (var p in expired)
            {
                p.Status = MonitoringParticipantStatus.Done;
                p.FinishedAt = DeadlineOf(test, p);
                p.FinishReason = MonitoringFinishReason.Time;
            }
            var items = await db.MonitoringItems.Where(i => i.TestId == tid).OrderBy(i => i.Order).ToListAsync(ct);
            MonitoringScore.Apply(test, items, participants);
            await SyncSeasonalAsync(db, test, participants, ct);
            await db.SaveChangesAsync(ct);
            await CommitAsync(tx, ct);
        }
    }

    /// <summary>
    /// Kod qayerga olib boradi. Bitta test kodi — o'sha test. Sessiya kodi — so'ralgan fan
    /// (<paramref name="testId"/>) yoki navbatdagisi (birinchi tugallanmagan); <see cref="CodeTarget.P"/>
    /// null — hammasi topshirilgan.
    /// </summary>
    private sealed record CodeTarget(
        MonitoringParticipant? P, MonitoringTest? T, MonitoringSittingCode? Code, MonitoringSitting? Sitting,
        List<(MonitoringTest T, MonitoringParticipant P)> Steps);

    private static async Task<CodeTarget> ResolveAsync(IAppDbContext db, string? code, Guid? testId, CancellationToken ct, string? pin = null)
    {
        var clean = (code ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (clean.Length != CodeLength) throw new MonitoringException("Kod noto'g'ri", 404);
        var single = await db.MonitoringParticipants.FirstOrDefaultAsync(x => x.AccessCode == clean, ct);
        if (single is not null)
            return new CodeTarget(single, await db.MonitoringTests.FirstAsync(x => x.Id == single.TestId, ct), null, null, []);

        var sc = await db.MonitoringSittingCodes.FirstOrDefaultAsync(x => x.Code == clean, ct);
        if (sc is null)
        {
            // a permanent personal code (§2.9): the code says who, the room PIN on the board says which test
            var student = await db.MonitoringStudentCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Code == clean, ct)
                ?? throw new MonitoringException("Kod noto'g'ri", 404);
            var roomPin = (pin ?? "").Trim();
            if (roomPin.Length == 0) throw new MonitoringException("Doskadagi 4 xonali xona PIN'ini kiriting");
            var sittingIds = await db.MonitoringSittings.AsNoTracking().Where(x => x.RoomPin == roomPin)
                .OrderByDescending(x => x.CreatedAt).Select(x => x.Id).ToListAsync(ct);
            if (sittingIds.Count > 0)
            {
                var own = await db.MonitoringSittingCodes.Where(x => sittingIds.Contains(x.SittingId) && x.StudentId == student.StudentId).ToListAsync(ct);
                sc = sittingIds.Select(id => own.FirstOrDefault(x => x.SittingId == id)).FirstOrDefault(x => x is not null);
            }
            if (sc is null)
            {
                // a single online test (one in a sitting is reached through the sitting's PIN only)
                var one = await (from x in db.MonitoringParticipants
                                 join t in db.MonitoringTests on x.TestId equals t.Id
                                 where t.RoomPin == roomPin && t.Delivery == MonitoringDelivery.Online && x.StudentId == student.StudentId
                                       && !db.MonitoringSittings.Any(st => st.TestIds.Contains(t.Id))
                                 orderby t.CreatedAt descending
                                 select x).FirstOrDefaultAsync(ct)
                    ?? throw new MonitoringException("PIN noto'g'ri yoki bu test sizga tegishli emas", 404);
                return new CodeTarget(one, await db.MonitoringTests.FirstAsync(t => t.Id == one.TestId, ct), null, null, []);
            }
        }
        var sitting = await db.MonitoringSittings.FirstAsync(x => x.Id == sc.SittingId, ct);
        var ids = sitting.TestIds;
        var tests = await db.MonitoringTests.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var parts = await db.MonitoringParticipants.Where(x => x.StudentId == sc.StudentId && ids.Contains(x.TestId)).ToListAsync(ct);
        var steps = new List<(MonitoringTest T, MonitoringParticipant P)>();
        foreach (var id in ids)
            if (tests.FirstOrDefault(t => t.Id == id) is { } t && parts.FirstOrDefault(x => x.TestId == id) is { } p)
                steps.Add((t, p));
        var next = steps.FindIndex(x => x.P.Status is MonitoringParticipantStatus.Pending or MonitoringParticipantStatus.InProgress);
        var pick = next;
        if (testId is { } wanted)
        {
            pick = steps.FindIndex(x => x.T.Id == wanted);
            if (pick < 0) throw new MonitoringException("Bu fan sizning sessiyangizda yo'q", 404);
            // the sitting's order: a later subject does not open before the current one is finished
            if (steps[pick].P.Status == MonitoringParticipantStatus.Pending && pick != next)
                throw new MonitoringException("Avval navbatdagi fanni tugating", 409);
        }
        return pick < 0
            ? new CodeTarget(null, null, sc, sitting, steps)
            : new CodeTarget(steps[pick].P, steps[pick].T, sc, sitting, steps);
    }

    private static async Task<(MonitoringParticipant P, MonitoringTest T)> ByCodeAsync(IAppDbContext db, string? code, Guid? testId, CancellationToken ct, string? pin = null)
    {
        var r = await ResolveAsync(db, code, testId, ct, pin);
        if (r.P is null) throw new MonitoringException("Siz sessiyadagi barcha fanlardan test topshirib bo'lgansiz", 409);
        return (r.P, r.T!);
    }

    /// <summary>Sessiya bitta kompyuterda: birinchi fan boshlangan qurilmadan boshqasi kira olmaydi.</summary>
    private static void EnsureSittingDevice(MonitoringSittingCode? sc, string device)
    {
        if (sc?.DeviceToken is { } bound && bound != device)
            throw new MonitoringException("Sessiya boshqa qurilmada boshlangan. O'quv bo'limiga murojaat qiling.", 409);
    }

    private static async Task<List<MonitoringSittingStepDto>> StepsAsync(IAppDbContext db, CodeTarget r, CancellationToken ct)
    {
        var subjectIds = r.Steps.Select(x => x.T.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return [.. r.Steps.Select(x => new MonitoringSittingStepDto(x.T.Id, x.T.Title, subjects.GetValueOrDefault(x.T.SubjectId) ?? "",
            x.P.Status switch
            {
                MonitoringParticipantStatus.Done => "done",
                MonitoringParticipantStatus.Absent => "absent",
                _ when x.T.Id == r.T?.Id => "current",
                _ => "waiting",
            }, x.P.Status == MonitoringParticipantStatus.Done ? x.P.Percent : null))];
    }

    private static async Task<MonitoringSessionDto> SessionAsync(IAppDbContext db, MonitoringParticipant p, MonitoringTest t, CancellationToken ct, bool inSitting = false)
    {
        var items = await db.MonitoringItems.AsNoTracking().Where(i => i.TestId == t.Id).OrderBy(i => i.Order).ToListAsync(ct);
        var name = await db.Students.AsNoTracking().Where(s => s.Id == p.StudentId).Select(s => s.FullName).FirstAsync(ct);
        var subject = await db.Subjects.AsNoTracking().Where(s => s.Id == t.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var shown = t.Shuffle ? new StableRandom(p.Id, 0).Shuffle(items) : items;
        return new MonitoringSessionDto(name, t.Title, subject, DeadlineOf(t, p), p.Answers ?? new string('-', items.Count),
            [.. shown.Select(i =>
            {
                // display position → original option; the pupil answers with the original letter
                var idx = Enumerable.Range(0, i.OptionCount).ToList();
                if (t.Shuffle) idx = new StableRandom(p.Id, i.Order).Shuffle(idx);
                return new MonitoringSessionItemDto(i.Order, i.Text, i.ImageUrl,
                    [.. idx.Select(k => i.Options.ElementAtOrDefault(k) ?? "")], i.OptionCount,
                    i.OptionImages.Count > 0 ? [.. idx.Select(k => i.OptionImages.ElementAtOrDefault(k) ?? "")] : null,
                    [.. idx.Select(k => MonitoringScore.Letter(k).ToString())]);
            })],
            p.Status == MonitoringParticipantStatus.Done, p.Leaves, t.MaxLeaves, p.FinishReason, t.Id, inSitting,
            // the page counts down against this, not the lab PC's own clock (often minutes off)
            DateTimeOffset.UtcNow,
            // the pupil sees their own total once finished — never which questions were wrong
            p.Status == MonitoringParticipantStatus.Done ? p.Correct : null,
            p.Status == MonitoringParticipantStatus.Done ? p.Percent : null);
    }

    /// <summary>
    /// O'quvchining barqaror tasodifiy tartibi (splitmix64, urug' — ishtirokchi Id va savol raqami):
    /// saqlanmaydi, qayta kirganda ham, server qayta ishga tushsa ham bir xil.
    /// </summary>
    private sealed class StableRandom
    {
        private ulong state;

        public StableRandom(Guid seed, int salt)
        {
            var b = seed.ToByteArray();
            state = BitConverter.ToUInt64(b, 0) ^ BitConverter.ToUInt64(b, 8) ^ ((ulong)(uint)salt * 0xD1B54A32D192ED03UL);
        }

        private int Next(int max)
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)max);
        }

        public List<T> Shuffle<T>(IEnumerable<T> source)
        {
            var list = source.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }

    private const int MaxEvents = 200;

    private static void AppendEvent(MonitoringParticipant p, string kind)
    {
        if (p.Events.Count >= MaxEvents) return;
        p.Events = [.. p.Events, $"{DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)} {kind}"];
    }

    /// <summary>
    /// Brauzer yuborgan halollik hodisasi. Oynadan chiqish sanaladi (bitta chiqishda brauzer "blur" va
    /// "hidden" ni ketma-ket yuboradi — 2 soniya ichidagisi bitta hisoblanadi); chegaradan oshsa test
    /// yakunlanadi va natija hozirgi javoblar bilan hisoblanadi.
    /// </summary>
    public static async Task<MonitoringEventResultDto> RecordEventAsync(IAppDbContext db, MonitoringEventRequest req, CancellationToken ct)
    {
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
        if (!MonitoringEventKind.All.Contains(kind)) throw new MonitoringException("Hodisa turi noto'g'ri");
        var (p, t) = await ByCodeAsync(db, req.Code, req.TestId, ct, req.Pin);
        EnsureDevice(p, req.Device);
        await using var tx = await LockTestAsync(db, t.Id, ct, p);
        if (p.Status != MonitoringParticipantStatus.InProgress) return new MonitoringEventResultDto(p.Leaves, t.MaxLeaves, true);

        var now = DateTimeOffset.UtcNow;
        p.LastSeenAt = now;
        var finished = false;
        if (MonitoringEventKind.Leaves.Contains(kind))
        {
            var last = p.Events.LastOrDefault(e => MonitoringEventKind.Leaves.Any(k => e.EndsWith(" " + k, StringComparison.Ordinal)));
            var repeat = last is not null
                && DateTimeOffset.TryParse(last.Split(' ')[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                && now - at < TimeSpan.FromSeconds(2);
            AppendEvent(p, kind);
            if (!repeat) p.Leaves++;
            if (t.MaxLeaves is { } max && p.Leaves > max)
            {
                await FinishOneAsync(db, p, t, ct, MonitoringFinishReason.Leaves);
                finished = true;
            }
        }
        else AppendEvent(p, kind);
        if (!finished && DeadlineOf(t, p) <= now)
        {
            await FinishOneAsync(db, p, t, ct, MonitoringFinishReason.Time);
            finished = true;
        }
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new MonitoringEventResultDto(p.Leaves, t.MaxLeaves, finished);
    }

    private static void EnsureDevice(MonitoringParticipant p, string? device)
    {
        if (p.DeviceToken is null || p.DeviceToken != device)
            throw new MonitoringException("Test boshqa qurilmada boshlangan. O'quv bo'limiga murojaat qiling.", 409);
    }

    /// <summary>
    /// Kod tekshiruvi — <see cref="StartAsync"/> bilan bir xil to'siqlar, lekin hech narsa yozilmaydi.
    /// O'quvchi o'z ismi, sinfi va testni ko'rib, keyin boshlaydi.
    /// </summary>
    public static async Task<MonitoringPeekDto> PeekAsync(IAppDbContext db, MonitoringStartRequest req, CancellationToken ct)
    {
        var r = await ResolveAsync(db, req.Code, req.TestId, ct, req.Pin);
        EnsureSittingDevice(r.Code, (req.Device ?? "").Trim());
        var steps = r.Sitting is null ? null : await StepsAsync(db, r, ct);
        if (r.P is null)
        {
            var pupil = await db.Students.AsNoTracking().Where(s => s.Id == r.Code!.StudentId).Select(s => s.FullName).FirstAsync(ct);
            var ownerName = r.Steps.Count > 0 ? (await LessonRoster.OwnerAsync(db, r.Steps[0].P.OwnerId, ct))?.Name ?? "" : "";
            return new MonitoringPeekDto(pupil, ownerName, r.Sitting!.Title, "", 0, 0, false, null, null, r.Sitting.Title, steps, AllDone: true);
        }
        try
        {
            return await PeekOneAsync(db, r.P, r.T!, req, ct) with { SittingTitle = r.Sitting?.Title, Steps = steps };
        }
        catch (MonitoringException ex) when (r.Sitting is not null)
        {
            throw new MonitoringException($"«{r.T!.Title}»: {ex.Message}", ex.Status);
        }
    }

    private static async Task<MonitoringPeekDto> PeekOneAsync(IAppDbContext db, MonitoringParticipant p, MonitoringTest t, MonitoringStartRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (t.Status != MonitoringStatus.Published || t.Delivery != MonitoringDelivery.Online) throw new MonitoringException("Test ochiq emas", 409);
        if (p.Status == MonitoringParticipantStatus.Done) throw new MonitoringException("Siz bu testni topshirib bo'lgansiz", 409);
        if (p.Status == MonitoringParticipantStatus.Absent) throw new MonitoringException("Test yopilgan", 409);
        if (t.OpensAt is { } o && now < o) throw new MonitoringException($"Test {o.ToOffset(TimeSpan.FromHours(5)):dd.MM HH:mm} da ochiladi", 409);
        if (p.Status == MonitoringParticipantStatus.Pending && t.ClosesAt is { } c && now >= c) throw new MonitoringException("Test vaqti tugagan", 409);
        if (p.Status == MonitoringParticipantStatus.InProgress && p.DeviceToken is { } bound && bound != (req.Device ?? "").Trim())
            throw new MonitoringException("Test boshqa qurilmada boshlangan. O'quv bo'limiga murojaat qiling.", 409);

        var name = await db.Students.AsNoTracking().Where(s => s.Id == p.StudentId).Select(s => s.FullName).FirstAsync(ct);
        var owner = (await LessonRoster.OwnerAsync(db, p.OwnerId, ct))?.Name ?? "";
        var subject = await db.Subjects.AsNoTracking().Where(s => s.Id == t.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var count = await db.MonitoringItems.CountAsync(i => i.TestId == t.Id, ct);
        // not started: the deadline it would get if started now (DeadlineOf counts from now then)
        var minutes = (int)Math.Max(0, Math.Floor((DeadlineOf(t, p) - now).TotalMinutes));
        return new MonitoringPeekDto(name, owner, t.Title, subject, count, minutes,
            p.Status == MonitoringParticipantStatus.InProgress, t.MaxLeaves, t.Id);
    }

    public static async Task<MonitoringSessionDto> StartAsync(IAppDbContext db, MonitoringStartRequest req, CancellationToken ct)
    {
        var device = (req.Device ?? "").Trim();
        if (device.Length is < 16 or > 64) throw new MonitoringException("Qurilma aniqlanmadi — sahifani yangilang");
        var r = await ResolveAsync(db, req.Code, req.TestId, ct, req.Pin);
        if (r.P is null) throw new MonitoringException("Siz sessiyadagi barcha fanlardan test topshirib bo'lgansiz", 409);
        EnsureSittingDevice(r.Code, device);
        var (p, t) = (r.P, r.T!);
        // a double click on "Boshlash" or a timer finishing at the same moment — one at a time
        await using var tx = await LockTestAsync(db, t.Id, ct, p);
        if (r.Code is { } sc) sc.DeviceToken ??= device;
        var now = DateTimeOffset.UtcNow;
        if (t.Status != MonitoringStatus.Published || t.Delivery != MonitoringDelivery.Online) throw new MonitoringException("Test ochiq emas", 409);
        if (p.Status == MonitoringParticipantStatus.Done) throw new MonitoringException("Siz bu testni topshirib bo'lgansiz", 409);
        if (p.Status == MonitoringParticipantStatus.Absent) throw new MonitoringException("Test yopilgan", 409);
        if (t.OpensAt is { } o && now < o) throw new MonitoringException($"Test {o.ToOffset(TimeSpan.FromHours(5)):dd.MM HH:mm} da ochiladi", 409);
        if (p.Status == MonitoringParticipantStatus.Pending)
        {
            if (t.ClosesAt is { } c && now >= c) throw new MonitoringException("Test vaqti tugagan", 409);
            p.Status = MonitoringParticipantStatus.InProgress;
            p.StartedAt = now;
            p.LastSeenAt = now;
            p.DeviceToken = device;
            p.Answers = new string('-', await db.MonitoringItems.CountAsync(i => i.TestId == t.Id, ct));
        }
        else
        {
            // in progress: the same device resumes; after an admin reset a new device binds
            p.DeviceToken ??= device;
            EnsureDevice(p, device);
            p.LastSeenAt = now;
            if (DeadlineOf(t, p) <= now) await FinishOneAsync(db, p, t, ct, MonitoringFinishReason.Time);
        }
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return await SessionAsync(db, p, t, ct, r.Sitting is not null);
    }

    public static async Task AnswerAsync(IAppDbContext db, MonitoringAnswerRequest req, CancellationToken ct)
    {
        var (p, t) = await ByCodeAsync(db, req.Code, req.TestId, ct, req.Pin);
        EnsureDevice(p, req.Device);
        await using var tx = await LockTestAsync(db, t.Id, ct, p);
        if (p.Status != MonitoringParticipantStatus.InProgress) throw new MonitoringException("Test yakunlangan", 409);
        if (DeadlineOf(t, p) <= DateTimeOffset.UtcNow)
        {
            await FinishOneAsync(db, p, t, ct, MonitoringFinishReason.Time);
            await db.SaveChangesAsync(ct);
            await CommitAsync(tx, ct);
            throw new MonitoringException("Vaqt tugadi — javoblaringiz saqlandi", 409);
        }
        var item = await db.MonitoringItems.AsNoTracking().FirstOrDefaultAsync(i => i.TestId == t.Id && i.Order == req.Order, ct)
            ?? throw new MonitoringException("Savol topilmadi");
        var choice = (req.Choice ?? "-").Trim().ToUpperInvariant();
        var c = choice.Length == 1 && choice[0] >= 'A' && choice[0] < MonitoringScore.Letter(item.OptionCount) ? choice[0] : '-';
        var chars = (p.Answers ?? "").PadRight(req.Order, '-').ToCharArray();
        chars[req.Order - 1] = c;
        p.Answers = new string(chars);
        p.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
    }

    /// <summary>
    /// Brauzer 30 soniyada bir "tirikman" deydi: jonli kuzatuvda o'quvchi oflayn ko'rinmasin. Vaqt
    /// tugagan bo'lsa yakunlaydi; o'quv bo'limi testni yopgan bo'lsa ham javob <c>Finished</c>.
    /// </summary>
    public static async Task<MonitoringEventResultDto> PingAsync(IAppDbContext db, MonitoringStartRequest req, CancellationToken ct)
    {
        var (p, t) = await ByCodeAsync(db, req.Code, req.TestId, ct, req.Pin);
        EnsureDevice(p, req.Device);
        if (p.Status != MonitoringParticipantStatus.InProgress) return new MonitoringEventResultDto(p.Leaves, t.MaxLeaves, true);
        var now = DateTimeOffset.UtcNow;
        if (DeadlineOf(t, p) > now)
        {
            // the common case: only "last seen" — one column, no rescoring, no lock needed
            p.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
            return new MonitoringEventResultDto(p.Leaves, t.MaxLeaves, false);
        }
        await using var tx = await LockTestAsync(db, t.Id, ct, p);
        var finished = p.Status != MonitoringParticipantStatus.InProgress;
        if (!finished)
        {
            p.LastSeenAt = now;
            await FinishOneAsync(db, p, t, ct, MonitoringFinishReason.Time);
            finished = true;
        }
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new MonitoringEventResultDto(p.Leaves, t.MaxLeaves, finished);
    }

    public static async Task<MonitoringSessionDto> FinishAsync(IAppDbContext db, MonitoringStartRequest req, CancellationToken ct)
    {
        var r = await ResolveAsync(db, req.Code, req.TestId, ct, req.Pin);
        if (r.P is null) throw new MonitoringException("Siz sessiyadagi barcha fanlardan test topshirib bo'lgansiz", 409);
        var (p, t) = (r.P, r.T!);
        EnsureDevice(p, req.Device);
        await using var tx = await LockTestAsync(db, t.Id, ct, p);
        // the page also calls finish when its timer runs out — record that as time, not as the pupil's choice
        if (p.Status == MonitoringParticipantStatus.InProgress)
            await FinishOneAsync(db, p, t, ct, DeadlineOf(t, p) <= DateTimeOffset.UtcNow ? MonitoringFinishReason.Time : null);
        await db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return await SessionAsync(db, p, t, ct, r.Sitting is not null);
    }

    private static async Task FinishOneAsync(IAppDbContext db, MonitoringParticipant p, MonitoringTest t, CancellationToken ct, string? reason = null)
    {
        p.Status = MonitoringParticipantStatus.Done;
        p.FinishReason = reason;
        p.FinishedAt = DateTimeOffset.UtcNow < DeadlineOf(t, p) ? DateTimeOffset.UtcNow : DeadlineOf(t, p);
        var items = await db.MonitoringItems.Where(i => i.TestId == t.Id).OrderBy(i => i.Order).ToListAsync(ct);
        var participants = await db.MonitoringParticipants.Where(x => x.TestId == t.Id).ToListAsync(ct);
        MonitoringScore.Apply(t, items, participants);
        await SyncSeasonalAsync(db, t, participants, ct);
    }

    // ================================================================== oylik test → Mavsumiy baho

    /// <summary>
    /// Oylik test: every <c>done</c> pupil's percent becomes their Mavsumiy baho (monthly, the test's
    /// subject and month) — created, or updated when it differs, so a key fix or a rescore follows
    /// through. Called after every <see cref="MonitoringScore.Apply"/>; does not save.
    ///
    /// <para>
    /// A mark already typed by a teacher for the same pupil, subject and month is overwritten — the
    /// test is the month's assessment. The comment is left alone. A pupil who did not finish
    /// (absent, still pending) gets no mark and an existing one is not touched.
    /// </para>
    /// </summary>
    private static async Task SyncSeasonalAsync(
        IAppDbContext db, MonitoringTest t, List<MonitoringParticipant> participants, CancellationToken ct)
    {
        if (t.SeasonalYear is not { } year || t.SeasonalMonth is not { } month) return;
        var done = participants.Where(p => p.Status == MonitoringParticipantStatus.Done).ToList();
        if (done.Count == 0) return;

        var key = SeasonalMark.BuildPeriodKey(SeasonalPeriodKind.Monthly, year, month, null);
        var ids = done.Select(p => p.StudentId).Distinct().ToList();
        var marks = await db.SeasonalMarks
            .Where(m => ids.Contains(m.StudentId) && m.SubjectId == t.SubjectId && m.PeriodKey == key)
            .ToDictionaryAsync(m => m.StudentId, ct);
        // Added earlier in this same unit of work (FinishExpired grades several tests) — PeriodKey
        // is generated by the database, so match those on the columns instead.
        foreach (var m in db.SeasonalMarks.Local.Where(m => ids.Contains(m.StudentId) && m.SubjectId == t.SubjectId
                     && m.PeriodKind == SeasonalPeriodKind.Monthly && m.Year == year && m.Month == month))
            marks[m.StudentId] = m;

        var pupils = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.FullName, s.ClassName }, ct);
        var classNames = pupils.Values.Select(v => v.ClassName).Distinct().ToList();
        var classByName = (await db.Classes.AsNoTracking().Where(c => classNames.Contains(c.Name))
                .Select(c => new { c.Id, c.Name, c.IsArchived }).ToListAsync(ct))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.IsArchived).ThenBy(c => c.Id).First().Id);
        var subject = await db.Subjects.AsNoTracking().Where(s => s.Id == t.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var period = SeasonalLabels.Period(SeasonalPeriodKind.Monthly, year, month, null);
        var now = AppClock.Now;

        foreach (var p in done)
        {
            var score = Math.Clamp(Math.Round(p.Percent, 2, MidpointRounding.AwayFromZero), 0m, 100m);
            var pupil = pupils.GetValueOrDefault(p.StudentId);
            var summary = $"Mavsumiy baho: {pupil?.FullName ?? "—"} · {subject} · {period} (onlayn test «{t.Title}»)";
            if (marks.TryGetValue(p.StudentId, out var mark))
            {
                if (mark.Score == score) continue;
                var before = new { mark.Score };
                mark.Score = score;
                mark.UpdatedAt = now;
                db.AuditLogs.Add(AuditService.Entry(SeasonalMarkService.AuditEntity, mark.Id, "update", summary, null,
                    "Onlayn test", before: before, after: new { mark.Score }, studentId: p.StudentId));
                continue;
            }

            // Class snapshot: the class the pupil sat in, else their own class (a track group).
            var classId = p.OwnerKind == LessonOwnerKind.Class
                ? p.OwnerId
                : pupil is null ? null : classByName.GetValueOrDefault(pupil.ClassName);
            if (classId is null) continue;
            mark = new SeasonalMark
            {
                StudentId = p.StudentId, ClassId = classId, SubjectId = t.SubjectId,
                PeriodKind = SeasonalPeriodKind.Monthly, Year = year, Month = month,
                Score = score, CreatedAt = now, UpdatedAt = now,
            };
            db.SeasonalMarks.Add(mark);
            marks[p.StudentId] = mark;
            db.AuditLogs.Add(AuditService.Entry(SeasonalMarkService.AuditEntity, mark.Id, "create", summary, null,
                "Onlayn test", after: new { mark.Score }, studentId: p.StudentId));
        }
    }
}
