using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Ish reja — Excel import, "o'tildi" belgilari va hisobotlar (docs/modules/curriculum-monitoring.md §1).
///
/// <para>
/// <b>Ega (owner)</b> — rejani o'tadigan sinf yoki guruh. Daraja rejasining egalari — shu darajadagi
/// sinflar (va shu darajaning shu fandan ODDIY guruhlari), qaysiki dars jadvalida shu fan bor;
/// jadvalda hech biri bo'lmasa — darajaning barcha sinflari. Yo'nalish rejasining egasi — yo'nalishning
/// o'zi. "O'tildi" belgisi har ega uchun alohida.
/// </para>
/// </summary>
public static class CurriculumService
{
    /// <summary>Namuna Excel ustunlari. Import ustunlarni SARLAVHA nomi bo'yicha topadi.</summary>
    public static readonly string[] Columns =
        ["№", "Chorak", "Hafta", "Sana", "Bo'lim (modul)", "Mavzu", "Soat", "Maqsad / kutilgan natija", "Uy vazifasi"];

    /// <summary>"Sana" ustuni qo'shilishidan oldingi tartib — sarlavhasiz eski fayllar shu tartibda o'qiladi.</summary>
    public static readonly string[] LegacyColumns =
        ["№", "Chorak", "Hafta", "Bo'lim (modul)", "Mavzu", "Soat", "Maqsad / kutilgan natija", "Uy vazifasi"];

    // ------------------------------------------------------------------ import

    /// <summary>Fayldagi sana: yil yozilmagan bo'lishi mumkin (10.11) — u chorak sanalaridan topiladi.</summary>
    /// <param name="SoftYear">Yil Excel'ning sana katagidan keldi — Excel uni yozilgan paytdagi yil bilan
    /// to'ldirgan bo'lishi mumkin, shuning uchun o'quv yiliga tushmasa, yil yozilmagandek qaraladi.</param>
    public sealed record PlanDate(int Day, int Month, int? Year, bool SoftYear = false);

    /// <param name="Line">Fayldagi qator raqami (xabarlar uchun).</param>
    /// <param name="Date">Fayldagi "Sana" (xom); <see cref="ResolveDates"/> uni <paramref name="PlannedOn"/> ga aylantiradi.</param>
    public sealed record ParsedTopic(
        int Order, int? Quarter, int? Week, string? Section, string Title, int Hours, string? Goal, string? Homework,
        int Line = 0, PlanDate? Date = null, DateOnly? PlannedOn = null);

    private enum Col { Order, Quarter, Week, Date, Section, Title, Hours, Goal, Homework }

    /// <summary>Fayl turi bo'yicha o'qiydi: .xlsx (Excel) yoki .docx (Word jadvali).</summary>
    public static (List<ParsedTopic> Topics, List<string> Errors) Parse(Stream stream, string fileName)
    {
        List<string[]> rows;
        var word = fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
        try { rows = word ? WordTableImport.ReadRows(stream) : ExcelImport.ReadRows(stream, 30); }
        catch (Exception)
        {
            return ([], [word ? "Word fayl o'qilmadi — .docx formatida saqlab qayta yuklang." : "Fayl o'qilmadi — .xlsx (Excel) formatida yuklang."]);
        }
        // Word'da ustunlar tartibi har xil — sarlavhasiz jadvalni o'qib bo'lmaydi
        if (word && FindHeader(rows) < 0) return ([], ["Word faylda «Mavzu» ustunli jadval topilmadi"]);
        return ParseRows(rows);
    }

    /// <summary>Eski kirish nuqtasi — faqat .xlsx.</summary>
    public static (List<ParsedTopic> Topics, List<string> Errors) Parse(Stream stream) => Parse(stream, "reja.xlsx");

    /// <summary>
    /// Jadval qatorlari → mavzular. Ustunlar sarlavha qatoridan (unda "Mavzu" bor) nomi bo'yicha
    /// topiladi; sarlavhagacha bo'lgan qatorlar (hujjat nomi va h.k.) o'tkazib yuboriladi. Sarlavha
    /// yo'q bo'lsa — eski tartib (<see cref="LegacyColumns"/>). "II chorak" kabi ajratuvchi qator
    /// keyingi mavzularning chorağini belgilaydi. Xatolar qator raqami bilan qaytadi; bitta xato
    /// bo'lsa ham hech narsa saqlanmaydi.
    /// </summary>
    public static (List<ParsedTopic> Topics, List<string> Errors) ParseRows(IReadOnlyList<string[]> rows)
    {
        var topics = new List<ParsedTopic>();
        var errors = new List<string>();
        var seen = new HashSet<int>();
        var next = 1;
        var headerAt = FindHeader(rows);
        var map = headerAt >= 0 ? HeaderMap(rows[headerAt])! : LegacyMap();
        int? sectionQuarter = null;
        for (var i = Math.Max(headerAt, 0); i < rows.Count; i++)
        {
            var r = rows[i].Select(c => (c ?? "").Trim()).ToArray();
            var line = i + 1;
            if (r.All(string.IsNullOrEmpty)) continue;
            if (HeaderMap(r) is { } m) { map = m; continue; }
            var filled = r.Where(c => c.Length > 0).Distinct().ToList();
            if (filled.Count == 1 && QuarterHeading(filled[0], requireWord: true) is { } qh) { sectionQuarter = qh; continue; }
            // "Jami: 34 soat" yakuniy qatori ("Jamiyat" mavzusi emas)
            if (r.Any(c => System.Text.RegularExpressions.Regex.IsMatch(Norm(c), @"^jami\b"))) continue;

            string Get(Col c) => map.TryGetValue(c, out var idx) && idx < r.Length ? r[idx] : "";
            var title = Get(Col.Title);
            var orderText = Get(Col.Order).TrimEnd('.', ')');
            if (title.Length == 0)
            {
                // Word jadvalidagi bo'sh/oraliq qator — raqami ham yo'q bo'lsa, mavzu emas
                if (orderText.Length == 0) continue;
                errors.Add($"{line}-qator: «Mavzu» bo'sh"); continue;
            }
            if (title.Length > 500) { errors.Add($"{line}-qator: mavzu 500 belgidan uzun"); continue; }

            int order;
            if (orderText.Length == 0) order = next;
            else if (!TryInt(orderText, out order) || order < 1) { errors.Add($"{line}-qator: «№» butun son bo'lsin"); continue; }
            if (!seen.Add(order)) { errors.Add($"{line}-qator: {order}-raqam takrorlangan"); continue; }
            next = Math.Max(next, order + 1);

            int? quarter = sectionQuarter;
            var qText = Get(Col.Quarter);
            if (qText.Length > 0)
            {
                var qv = QuarterHeading(qText, requireWord: false) ?? (TryInt(qText, out var qi) ? qi : null);
                if (qv is not (>= 1 and <= 4)) { errors.Add($"{line}-qator: «Chorak» 1–4 bo'lsin"); continue; }
                quarter = qv;
            }
            int? week = null;
            var wText = Get(Col.Week);
            if (wText.Length > 0)
            {
                if (!TryInt(LeadingNumber(wText), out var w) || w is < 1 or > 20) { errors.Add($"{line}-qator: «Hafta» 1–20 bo'lsin (chorak ichidagi hafta)"); continue; }
                week = w;
            }
            var hours = 1;
            var hText = Get(Col.Hours);
            if (hText.Length > 0 && (!TryInt(LeadingNumber(hText), out hours) || hours is < 1 or > 50))
            { errors.Add($"{line}-qator: «Soat» 1–50 bo'lsin"); continue; }
            PlanDate? date = null;
            var dText = Get(Col.Date);
            if (dText.Length > 0 && (date = ParseDate(dText)) is null)
            { errors.Add($"{line}-qator: «Sana» noto'g'ri — {dText} (masalan: 10.11 yoki 10.11.2026)"); continue; }

            topics.Add(new ParsedTopic(order, quarter, week, Cut(Get(Col.Section), 300), title, hours,
                Cut(Get(Col.Goal), 1000), Cut(Get(Col.Homework), 500), line, date));
        }
        if (topics.Count == 0 && errors.Count == 0) errors.Add("Faylda birorta ham mavzu topilmadi");
        return ([.. topics.OrderBy(t => t.Order)], errors);
    }

    private static Dictionary<Col, int> LegacyMap() => new()
    {
        [Col.Order] = 0, [Col.Quarter] = 1, [Col.Week] = 2, [Col.Section] = 3, [Col.Title] = 4,
        [Col.Hours] = 5, [Col.Goal] = 6, [Col.Homework] = 7,
    };

    private static int FindHeader(IReadOnlyList<string[]> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            if (HeaderMap(rows[i]) is not null) return i;
        return -1;
    }

    /// <summary>
    /// Sarlavha qatori bo'lsa — ustun → indeks; aks holda null. "Mavzu" va yana kamida bitta
    /// tanish ustun shart, sof raqamli katak bo'lmasin (ma'lumot qatorida № / soat raqami bor).
    /// </summary>
    private static Dictionary<Col, int>? HeaderMap(string[] row)
    {
        var map = new Dictionary<Col, int>();
        for (var i = 0; i < row.Length; i++)
        {
            var n = Norm(row[i] ?? "");
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d+([.,]\d+)?$")) return null;
            if (n.Length == 0 || n.Length > 40) continue;
            Col? c =
                n is "№" or "no" or "n" or "#" or "t/r" or "t.r" or "tr" or "r/t" || n.StartsWith('№') ? Col.Order
                : n.Contains("chorak") ? Col.Quarter
                : n.Contains("hafta") ? Col.Week
                : n.Contains("sana") ? (n.Contains("o'tilgan") || n.Contains("otilgan") || n.Contains("haqiqiy") || n.Contains("amalda") ? null : Col.Date)
                : n.Contains("bo'lim") || n.Contains("bolim") || n.Contains("modul") || n == "bob" ? Col.Section
                : n.Contains("maqsad") || n.Contains("natija") ? Col.Goal
                : n.Contains("vazifa") ? Col.Homework
                : n.Contains("soat") ? Col.Hours
                : n.Contains("mavzu") ? Col.Title
                : null;
            if (c is { } col) map.TryAdd(col, i);
        }
        return map.ContainsKey(Col.Title) && map.Count >= 2 ? map : null;
    }

    private static string Norm(string s) =>
        string.Join(' ', s.ToLowerInvariant().Replace('‘', '\'').Replace('’', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('`', '\'')
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// "II chorak", "2-chorak" → 2. <paramref name="requireWord"/> = false (Chorak ustuni) — "2" va
    /// "II" ham bo'ladi; ajratuvchi qatorda esa "chorak" so'zi shart.
    /// </summary>
    private static int? QuarterHeading(string s, bool requireWord)
    {
        var n = Norm(s);
        var m = System.Text.RegularExpressions.Regex.Match(n, @"^(?:([1-4])|(iv|i{1,3}))\b\s*[-–.]?\s*(chorak)?");
        if (!m.Success) return null;
        if (!m.Groups[3].Success && (requireWord || n.Length > 3)) return null;
        return m.Groups[1].Success ? m.Groups[1].Value[0] - '0' : m.Groups[2].Value switch { "i" => 1, "ii" => 2, "iii" => 3, _ => 4 };
    }

    private static string LeadingNumber(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^\s*(\d+(?:[.,]\d+)?)");
        return m.Success ? m.Groups[1].Value.Replace(',', '.') : s;
    }

    /// <summary>
    /// "10.11", "1.09", "10.11.2026", "10/11/26", "2026-11-10" yoki Excel sana katagi (seriya raqami).
    /// Katakda bir nechta sana bo'lsa — birinchisi.
    /// </summary>
    public static PlanDate? ParseDate(string s)
    {
        s = s.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{5}(\.\d+)?$")
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            var d = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return new PlanDate(d.Day, d.Month, d.Year, SoftYear: true);
        }
        var iso = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
        if (iso.Success) return Valid(int.Parse(iso.Groups[3].Value), int.Parse(iso.Groups[2].Value), int.Parse(iso.Groups[1].Value));
        var m = System.Text.RegularExpressions.Regex.Match(s, @"(?<!\d)(\d{1,2})[./-](\d{1,2})(?:[./-](\d{4}|\d{2}))?(?!\d)");
        if (!m.Success) return null;
        int? year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
        if (year < 100) year += 2000;
        return Valid(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), year);

        static PlanDate? Valid(int day, int month, int? year) =>
            day is < 1 or > 31 || month is < 1 or > 12 || year is < 2000 or > 2100 ? null : new PlanDate(day, month, year);
    }

    private static string Short(DateOnly d) => d.ToString("dd.MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Fayldagi sanalarni aniq sanaga aylantiradi va tekshiradi. Sana bor mavzu: yil yozilmagan
    /// bo'lsa — choraklardan topiladi; chorak ko'rsatilgan bo'lsa sana shu chorak ichida bo'lishi
    /// SHART (aks holda xato), ko'rsatilmagan bo'lsa — sana tushgan chorak olinadi; hafta sanadan
    /// hisoblanadi. Bayram yoki yakshanbaga tushgan dars — ogohlantirish (saqlashga to'sqinlik
    /// qilmaydi). Sanasiz mavzu o'zgarmaydi (chorak + hafta bo'yicha hisoblanadi).
    /// </summary>
    public static (List<ParsedTopic> Topics, List<string> Errors, List<string> Warnings) ResolveDates(
        IReadOnlyList<ParsedTopic> topics,
        IReadOnlyList<(int Quarter, DateOnly Start, DateOnly End)> quarters,
        IReadOnlyDictionary<DateOnly, string> holidays)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var result = new List<ParsedTopic>();
        var yStart = quarters.Count == 0 ? (DateOnly?)null : quarters.Min(q => q.Start);
        var yEnd = quarters.Count == 0 ? (DateOnly?)null : quarters.Max(q => q.End);

        foreach (var t in topics.OrderBy(t => t.Line).ThenBy(t => t.Order))
        {
            var given = t.Quarter is { } gq ? quarters.FirstOrDefault(x => x.Quarter == gq) : default;
            if (t.Date is not { } pd)
            {
                if (given != default && t.Week is { } wk)
                {
                    var count = ScheduleMath.GetQuarterWeeks(Iso(given.Start), Iso(given.End)).Count;
                    if (wk > count)
                        warnings.Add($"{t.Line}-qator: {given.Quarter}-chorakda {count} hafta bor — {wk}-hafta chorak oxiriga suriladi. Aniq «Sana» yozing.");
                }
                result.Add(t);
                continue;
            }

            DateOnly date;
            if (yStart is null || yEnd is null)
            {
                if (pd.Year is null || !TryDate(pd.Year.Value, pd, out date))
                {
                    errors.Add(pd.Year is null
                        ? $"{t.Line}-qator: sanada yil yo'q, chorak sanalari esa kiritilmagan — to'liq yozing (10.11.2026)"
                        : $"{t.Line}-qator: «Sana» noto'g'ri — bunday kun yo'q ({pd.Day:00}.{pd.Month:00}.{pd.Year})");
                    continue;
                }
            }
            else
            {
                var ys = yStart.Value;
                var ye = yEnd.Value;
                // academic-year guess for a missing year: Sep–Dec → the start year, Jan–Aug → the end year
                var guessYear = pd.Year ?? (pd.Month >= ys.Month ? ys.Year : ye.Year);
                var years = pd.Year is not null && !pd.SoftYear
                    ? new[] { pd.Year.Value }
                    : new[] { guessYear, ys.Year, ye.Year }.Distinct().ToArray();
                var candidates = new List<DateOnly>();
                foreach (var yr in years)
                    if (TryDate(yr, pd, out var cand)) candidates.Add(cand);
                if (candidates.Count == 0)
                {
                    errors.Add($"{t.Line}-qator: «Sana» noto'g'ri — bunday kun yo'q ({pd.Day:00}.{pd.Month:00})");
                    continue;
                }
                var target = given;
                date = candidates.Where(d => target != default && d >= target.Start && d <= target.End)
                    .Concat(candidates.Where(d => quarters.Any(q => d >= q.Start && d <= q.End)))
                    .Concat(candidates.Where(d => d >= ys && d <= ye))
                    .Append(candidates[0])
                    .First();
            }

            var quarter = t.Quarter;
            var week = t.Week;
            if (quarters.Count > 0)
            {
                if (given != default)
                {
                    if (date < given.Start || date > given.End)
                    {
                        errors.Add($"{t.Line}-qator: {Short(date)} sanasi {given.Quarter}-chorakdan ({Short(given.Start)}–{Short(given.End)}) tashqarida");
                        continue;
                    }
                }
                else
                {
                    var inQ = quarters.FirstOrDefault(q => date >= q.Start && date <= q.End);
                    if (inQ == default)
                    {
                        errors.Add($"{t.Line}-qator: {Short(date)} sanasi hech bir chorakka tushmaydi ("
                            + string.Join(", ", quarters.OrderBy(q => q.Quarter).Select(q => $"{q.Quarter}-chorak {Short(q.Start)}–{Short(q.End)}")) + ")");
                        continue;
                    }
                    given = inQ;
                }
                if (given != default)
                {
                    quarter = given.Quarter;
                    week = WeekOf(given.Start, given.End, date);
                }
            }

            if (holidays.TryGetValue(date, out var holiday))
                warnings.Add($"{t.Line}-qator: {Short(date)} — dam olish kuni{(string.IsNullOrWhiteSpace(holiday) ? "" : $" ({holiday})")}");
            else if (date.DayOfWeek == DayOfWeek.Sunday)
                warnings.Add($"{t.Line}-qator: {Short(date)} — yakshanba");

            result.Add(t with { Quarter = quarter, Week = week, PlannedOn = date });
        }
        return ([.. result.OrderBy(t => t.Order)], errors, warnings);

        static bool TryDate(int year, PlanDate p, out DateOnly d)
        {
            d = default;
            if (p.Day > DateTime.DaysInMonth(year, p.Month)) return false;
            d = new DateOnly(year, p.Month, p.Day);
            return true;
        }
    }

    /// <summary><see cref="ResolveDates"/> — maktabning choraklari va bayram kunlari bilan.</summary>
    public static async Task<(List<ParsedTopic> Topics, List<string> Errors, List<string> Warnings)> ResolveDatesAsync(
        IAppDbContext db, IReadOnlyList<ParsedTopic> topics, CancellationToken ct)
    {
        var cal = await CalendarAsync(db, ct);
        var holidays = await HolidaysAsync(db, ct);
        return ResolveDates(topics, cal.Quarters, holidays);
    }

    private static async Task<Dictionary<DateOnly, string>> HolidaysAsync(IAppDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<DateOnly, string>();
        foreach (var h in await db.Holidays.AsNoTracking().Select(h => new { h.Date, h.Name }).ToListAsync(ct))
            if (DateOnly.TryParseExact(h.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                result.TryAdd(d, h.Name);
        return result;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Sana chorakning nechanchi haftasiga tushadi (<see cref="ScheduleMath.GetQuarterWeeks"/> raqamlashi bilan).</summary>
    private static int WeekOf(DateOnly start, DateOnly end, DateOnly date)
    {
        var iso = Iso(date);
        return ScheduleMath.GetQuarterWeeks(Iso(start), Iso(end))
            .LastOrDefault(w => string.CompareOrdinal(w.StartISO, iso) <= 0)?.Week ?? 1;
    }

    private static string? Cut(string s, int max) => s.Length == 0 ? null : s.Length > max ? s[..max] : s;

    private static bool TryInt(string s, out int value)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        // Excel may store "3" as 3.0
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d))
        { value = (int)d; return true; }
        return false;
    }

    /// <summary>Namuna fayl — "Ish reja" varag'i (to'ldiriladi) va "Ko'rsatma" varag'i.</summary>
    public static byte[] Template()
    {
        var sample = new List<IReadOnlyList<string>>
        {
            new[] { "1", "1", "1", "02.09", "Natural sonlar", "Natural sonlar va ularni yozish", "1", "O'quvchi ko'p xonali sonni o'qiy va yoza oladi", "§1, 3-5-misollar" },
            new[] { "2", "1", "1", "04.09", "Natural sonlar", "Natural sonlarni taqqoslash", "1", "Sonlarni <, >, = belgilari bilan taqqoslaydi", "§2, 7-misol" },
            new[] { "3", "1", "2", "09.09", "Natural sonlar", "Qo'shish va uning xossalari", "2", "Qo'shishning o'rin almashtirish va guruhlash xossalarini qo'llaydi", "§3, 12-15-misollar" },
            new[] { "4", "1", "2", "", "Natural sonlar", "Nazorat ishi №1", "1", "Bo'lim bo'yicha bilimni tekshirish", "" },
        };
        var guide = new List<IReadOnlyList<string>>
        {
            new[] { "№", "Mavzuning tartib raqami (1, 2, 3 …). Qayta yuklaganda shu raqam bo'yicha «o'tildi» belgilari saqlanadi." },
            new[] { "Chorak", "1–4. Hisobotdagi «chorak bo'yicha reja» va «rejadan orqada/oldinda» shundan hisoblanadi. Sana yozilsa, bo'sh qoldirsa ham bo'ladi — sanadan topiladi." },
            new[] { "Hafta", "Chorak ichidagi hafta raqami (1 dan). Sana yozilsa — sanadan avtomatik hisoblanadi. Ikkalasi ham bo'sh qolsa — mavzu chorak bo'ylab teng taqsimlanadi." },
            new[] { "Sana", "Ixtiyoriy. Darsning aniq sanasi: 10.11, 1.09 yoki 10.11.2026 (yil yozilmasa — chorak sanalaridan aniqlanadi). Sana chorakdan tashqarida bo'lsa fayl qabul qilinmaydi; bayram kuni yoki yakshanba bo'lsa — ogohlantirish chiqadi. Excel sanani o'zgartirib yubormasligi uchun ustunni «Matn» (Text) formatida qoldiring." },
            new[] { "Bo'lim (modul)", "Ixtiyoriy. Bir nechta mavzuni birlashtiruvchi bo'lim nomi." },
            new[] { "Mavzu", "MAJBURIY. Darsda o'tiladigan mavzu — o'qituvchi darsda ishlatadigan to'liq nomi bilan." },
            new[] { "Soat", "Shu mavzuga ajratilgan dars soni (sukut — 1). Tugash muddati prognozi soat bo'yicha hisoblanadi." },
            new[] { "Maqsad / kutilgan natija", "Mavzu oxirida o'quvchi nimani bila olishi kerak. Monitoring testi savollari shu mavzuga bog'lanadi." },
            new[] { "Uy vazifasi", "Ixtiyoriy." },
            new[] { "", "" },
            new[] { "Qoidalar", "Birinchi qator — sarlavha (ustunlar nomi bo'yicha topiladi, tartibi muhim emas). Har mavzu alohida qatorda. Bitta fan va bitta sinf darajasi (yoki yo'nalish) uchun bitta fayl." },
            new[] { "", "Word (.docx) ish rejani ham yuklash mumkin: jadvalda «Mavzu» ustuni bo'lsin; «№», «Soat», «Sana», «Chorak» ustunlari bo'lsa ular ham olinadi." },
            new[] { "", "Reja butun o'quv yili uchun to'liq bo'lsin: tizim o'tilgan va qolgan mavzularni, tugash sanasini shundan hisoblaydi." },
        };
        return ExcelExport.Build(
        [
            new ExcelExport.SheetSpec("Ish reja", Columns, sample),
            new ExcelExport.SheetSpec("Ko'rsatma", ["Ustun", "Izoh"], guide),
        ]);
    }

    /// <summary>
    /// Rejaning mavzularini yangilaydi: shu tartib raqamli mavzu bor bo'lsa — matni almashadi
    /// (belgisi qoladi), yangisi qo'shiladi, faylda yo'g'i o'chadi (belgisi bilan).
    /// </summary>
    public static async Task ReplaceTopicsAsync(IAppDbContext db, CurriculumPlan plan, IReadOnlyList<ParsedTopic> parsed, CancellationToken ct)
    {
        var existing = await db.CurriculumTopics.Where(t => t.PlanId == plan.Id).ToListAsync(ct);
        var byOrder = existing.ToDictionary(t => t.Order);
        foreach (var p in parsed)
        {
            if (!byOrder.Remove(p.Order, out var t))
            {
                t = new CurriculumTopic { PlanId = plan.Id, Order = p.Order };
                db.CurriculumTopics.Add(t);
            }
            t.Quarter = p.Quarter;
            t.Week = p.Week;
            t.Section = p.Section;
            t.Title = p.Title;
            t.Hours = p.Hours;
            t.Goal = p.Goal;
            t.Homework = p.Homework;
            t.PlannedOn = p.PlannedOn;
        }
        foreach (var gone in byOrder.Values) db.CurriculumTopics.Remove(gone);
    }

    /// <summary>
    /// Bitta mavzuning aniq sanasini qo'lda o'rnatish (null — tozalash, reja sanasi yana
    /// chorak/haftadan hisoblanadi). Choraklar kiritilgan bo'lsa sana birortasiga tushishi shart;
    /// chorak va hafta sanadan yangilanadi. Xato matni yoki null qaytaradi.
    /// </summary>
    public static async Task<string?> SetTopicDateAsync(IAppDbContext db, Guid topicId, string? value, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null) return "Mavzu topilmadi";
        if (string.IsNullOrWhiteSpace(value))
        {
            topic.PlannedOn = null;
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return "Sana noto'g'ri";
        var cal = await CalendarAsync(db, ct);
        if (cal.Quarters.Count > 0)
        {
            var q = cal.Quarters.FirstOrDefault(x => date >= x.Start && date <= x.End);
            if (q == default)
                return $"{Short(date)} hech bir chorakka tushmaydi ("
                    + string.Join(", ", cal.Quarters.Select(x => $"{x.Quarter}-chorak {Short(x.Start)}–{Short(x.End)}")) + ")";
            topic.Quarter = q.Quarter;
            topic.Week = WeekOf(q.Start, q.End, date);
        }
        topic.PlannedOn = date;
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ------------------------------------------------------------------ owners

    /// <summary>Darsi bor (ega, fan) juftliklari — arxivlanmagan jadvallardan.</summary>
    public static async Task<HashSet<(string OwnerKind, string OwnerId, string SubjectId)>> TaughtPairsAsync(
        IAppDbContext db, CancellationToken ct)
    {
        var scope = await LessonRoster.GroupScopeAsync(db, ct);
        var rows = await (from t in db.ScheduleTemplates.AsNoTracking()
                          where !t.IsArchived
                          from l in t.Lessons
                          select new { t.OwnerKind, t.ClassId, l.SubjectId }).Distinct().ToListAsync(ct);
        return [.. rows.Where(r => scope.Allows(r.OwnerKind, r.ClassId)).Select(r => (r.OwnerKind, r.ClassId, r.SubjectId))];
    }

    public sealed record Owner(string Kind, string Id, string Name, int Grade);

    private sealed record World(
        List<SchoolClass> Classes,
        List<StudyGroup> Groups,
        Dictionary<Guid, int> GroupGrade,
        HashSet<(string OwnerKind, string OwnerId, string SubjectId)> Taught);

    private static async Task<World> WorldAsync(IAppDbContext db, CancellationToken ct)
    {
        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var groups = await db.StudyGroups.AsNoTracking().Where(g => !g.IsArchived).OrderBy(g => g.Name).ToListAsync(ct);
        var gradeOf = classes.ToDictionary(c => c.Id, c => c.Grade);
        var groupGrade = (await db.StudyGroupClasses.AsNoTracking().ToListAsync(ct))
            .Where(x => gradeOf.ContainsKey(x.ClassId))
            .GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.Min(x => gradeOf[x.ClassId]));
        return new World(classes, groups, groupGrade, await TaughtPairsAsync(db, ct));
    }

    /// <summary>Rejani o'tadigan sinf/guruhlar (sinfiy tartibda).</summary>
    private static List<Owner> OwnersOf(CurriculumPlan plan, World w)
    {
        if (plan.GroupId is { } gid)
        {
            var g = w.Groups.FirstOrDefault(x => x.Id == gid);
            return g is null ? [] : [new Owner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, w.GroupGrade.GetValueOrDefault(g.Id))];
        }
        var grade = plan.Grade ?? -1;
        var gradeClasses = w.Classes.Where(c => c.Grade == grade).ToList();
        var taughtClasses = gradeClasses.Where(c => w.Taught.Contains((LessonOwnerKind.Class, c.Id, plan.SubjectId))).ToList();
        var groups = w.Groups
            .Where(g => !g.IsTrack && g.SubjectId == plan.SubjectId && w.GroupGrade.GetValueOrDefault(g.Id, -2) == grade
                        && w.Taught.Contains((LessonOwnerKind.Group, g.Id.ToString(), plan.SubjectId)))
            .ToList();
        var owners = (taughtClasses.Count + groups.Count > 0 ? taughtClasses : gradeClasses)
            .Select(c => new Owner(LessonOwnerKind.Class, c.Id, c.Name, c.Grade)).ToList();
        owners.AddRange(groups.Select(g => new Owner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, grade)));
        return owners;
    }

    /// <summary>Ega va fan qaysi rejaga tushadi: (fan, daraja) yoki (fan, yo'nalish).</summary>
    private static (string SubjectId, int? Grade, Guid? GroupId)? KeyOf(string ownerKind, string ownerId, string subjectId, World w)
    {
        if (ownerKind == LessonOwnerKind.Class)
        {
            var c = w.Classes.FirstOrDefault(x => x.Id == ownerId);
            return c is null ? null : (subjectId, c.Grade, null);
        }
        if (!Guid.TryParse(ownerId, out var gid)) return null;
        var g = w.Groups.FirstOrDefault(x => x.Id == gid);
        if (g is null) return null;
        if (g.IsTrack) return (subjectId, null, g.Id);
        return w.GroupGrade.TryGetValue(g.Id, out var grade) ? (subjectId, grade, null) : null;
    }

    // ------------------------------------------------------------------ calendar

    private sealed record Calendar(List<(int Quarter, DateOnly Start, DateOnly End)> Quarters)
    {
        public DateOnly? YearStart => Quarters.Count == 0 ? null : Quarters.Min(q => q.Start);
        public DateOnly? YearEnd => Quarters.Count == 0 ? null : Quarters.Max(q => q.End);
    }

    private static async Task<Calendar> CalendarAsync(IAppDbContext db, CancellationToken ct)
    {
        var list = new List<(int, DateOnly, DateOnly)>();
        foreach (var q in await db.Quarters.AsNoTracking().OrderBy(q => q.Quarter).ToListAsync(ct))
            if (DateOnly.TryParse(q.StartDate, CultureInfo.InvariantCulture, out var s)
                && DateOnly.TryParse(q.EndDate, CultureInfo.InvariantCulture, out var e) && e >= s)
                list.Add((q.Quarter, s, e));
        return new Calendar(list);
    }

    /// <summary>
    /// Mavzu rejaga ko'ra qachon o'tilishi kerak: aniq sana (<see cref="CurriculumTopic.PlannedOn"/>)
    /// bo'lsa — o'sha; aks holda chorak + hafta berilsa — o'sha haftaning boshi; faqat chorak —
    /// mavzular chorak bo'ylab teng taqsimlanadi; hech biri — butun yil bo'ylab. Aniq sanasiz mavzu
    /// uchun choraklar kiritilmagan bo'lsa — sana yo'q.
    /// </summary>
    private static Dictionary<Guid, DateOnly> PlannedDates(IReadOnlyList<CurriculumTopic> topics, Calendar cal)
    {
        var result = new Dictionary<Guid, DateOnly>();
        foreach (var t in topics)
            if (t.PlannedOn is { } exact) result[t.Id] = exact;
        topics = [.. topics.Where(t => t.PlannedOn is null)];
        if (cal.YearStart is not { } yStart || cal.YearEnd is not { } yEnd) return result;

        static DateOnly Spread(DateOnly start, DateOnly end, int index, int count) =>
            start.AddDays((int)Math.Floor((end.DayNumber - start.DayNumber) * (index + 0.5) / Math.Max(count, 1)));

        foreach (var group in topics.Where(t => t.Quarter is not null).GroupBy(t => t.Quarter!.Value))
        {
            var q = cal.Quarters.FirstOrDefault(x => x.Quarter == group.Key);
            if (q == default) continue;
            var weeks = ScheduleMath.GetQuarterWeeks(q.Start.ToString("yyyy-MM-dd"), q.End.ToString("yyyy-MM-dd"));
            var noWeek = group.Where(t => t.Week is null).OrderBy(t => t.Order).ToList();
            foreach (var t in group.Where(t => t.Week is not null))
            {
                var w = weeks.FirstOrDefault(x => x.Week == t.Week) ?? weeks.LastOrDefault();
                result[t.Id] = w is null ? q.Start : DateOnly.ParseExact(w.StartISO, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            for (var i = 0; i < noWeek.Count; i++) result[noWeek[i].Id] = Spread(q.Start, q.End, i, noWeek.Count);
        }
        var free = topics.Where(t => t.Quarter is null).OrderBy(t => t.Order).ToList();
        for (var i = 0; i < free.Count; i++) result[free[i].Id] = Spread(yStart, yEnd, i, free.Count);
        return result;
    }

    // ------------------------------------------------------------------ read models

    public static async Task<List<CurriculumPlanListItemDto>> ListAsync(IAppDbContext db, CancellationToken ct)
    {
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var w = await WorldAsync(db, ct);
        var topicIds = plans.SelectMany(p => p.Topics).Select(t => t.Id).ToList();
        var progress = await db.CurriculumProgress.AsNoTracking().Where(p => topicIds.Contains(p.TopicId))
            .Select(p => new { p.TopicId, p.OwnerKind, p.OwnerId }).ToListAsync(ct);
        var passedBy = progress.GroupBy(p => p.TopicId).ToDictionary(g => g.Key, g => g.Select(x => (x.OwnerKind, x.OwnerId)).ToHashSet());

        return [.. plans
            .Select(p =>
            {
                var owners = OwnersOf(p, w);
                var total = p.Topics.Count;
                var avg = owners.Count == 0 || total == 0 ? 0m
                    : Math.Round(owners.Average(o => (decimal)p.Topics.Count(t => passedBy.GetValueOrDefault(t.Id)?.Contains((o.Kind, o.Id)) == true)) * 100m / total, 1);
                return new CurriculumPlanListItemDto(
                    p.Id, p.Title, p.SubjectId, subjects.GetValueOrDefault(p.SubjectId) ?? "—", p.Grade, p.GroupId,
                    p.GroupId is { } g ? w.Groups.FirstOrDefault(x => x.Id == g)?.Name : null,
                    total, p.Topics.Sum(t => t.Hours), p.FileUrl, p.FileName, p.UpdatedAt,
                    [.. owners.Select(o => o.Name)], avg);
            })
            .OrderBy(p => p.GroupId is null ? 0 : 1).ThenBy(p => p.Grade).ThenBy(p => p.GroupName).ThenBy(p => p.SubjectName)];
    }

    /// <summary>
    /// Bitta reja: mavzular va har ega bo'yicha belgilar. <paramref name="onlyOwners"/> berilsa
    /// (o'qituvchi) — faqat o'sha egalar.
    /// </summary>
    public static async Task<CurriculumPlanDetailDto?> DetailAsync(
        IAppDbContext db, Guid id, IReadOnlySet<(string Kind, string Id)>? onlyOwners, CancellationToken ct)
    {
        var plan = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return null;
        var w = await WorldAsync(db, ct);
        var owners = OwnersOf(plan, w).Where(o => onlyOwners is null || onlyOwners.Contains((o.Kind, o.Id))).ToList();
        var topics = plan.Topics.OrderBy(t => t.Order).ToList();
        var topicIds = topics.Select(t => t.Id).ToList();
        var marks = await db.CurriculumProgress.AsNoTracking().Where(p => topicIds.Contains(p.TopicId)).ToListAsync(ct);
        var cal = await CalendarAsync(db, ct);
        var planned = PlannedDates(topics, cal);
        var subjectName = await db.Subjects.AsNoTracking().Where(s => s.Id == plan.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";

        return new CurriculumPlanDetailDto(
            plan.Id, plan.Title, plan.SubjectId, subjectName, plan.Grade, plan.GroupId, plan.FileUrl, plan.FileName, plan.UpdatedAt,
            [.. owners.Select(o => new CurriculumOwnerDto(o.Kind, o.Id, o.Name))],
            [.. topics.Select(t => new CurriculumTopicDto(
                t.Id, t.Order, t.Quarter, t.Week, t.Section, t.Title, t.Hours, t.Goal, t.Homework,
                planned.TryGetValue(t.Id, out var d) ? d.ToString("yyyy-MM-dd") : null, t.PlannedOn is not null,
                [.. marks.Where(m => m.TopicId == t.Id && owners.Any(o => o.Kind == m.OwnerKind && o.Id == m.OwnerId))
                    .Select(m => new CurriculumMarkDto(m.OwnerKind, m.OwnerId, m.PassedOn.ToString("yyyy-MM-dd")))]))]);
    }

    /// <summary>Belgi qo'yish / olib tashlash. Ega rejaning egalaridan biri bo'lishi shart.</summary>
    public static async Task<string?> MarkAsync(
        IAppDbContext db, CurriculumMarkRequest req, string userId, CancellationToken ct)
    {
        var topic = await db.CurriculumTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TopicId, ct);
        if (topic is null) return "Mavzu topilmadi";
        var plan = await db.CurriculumPlans.AsNoTracking().FirstAsync(p => p.Id == topic.PlanId, ct);
        var w = await WorldAsync(db, ct);
        if (!OwnersOf(plan, w).Any(o => o.Kind == req.OwnerKind && o.Id == req.OwnerId))
            return "Bu sinf yoki guruh shu ish rejani o'tmaydi";

        var row = await db.CurriculumProgress.FirstOrDefaultAsync(
            p => p.TopicId == req.TopicId && p.OwnerKind == req.OwnerKind && p.OwnerId == req.OwnerId, ct);
        if (!req.Passed)
        {
            if (row is not null) db.CurriculumProgress.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }
        var day = AppClock.Today;
        if (!string.IsNullOrWhiteSpace(req.PassedOn))
        {
            if (!DateOnly.TryParseExact(req.PassedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                return "Sana noto'g'ri";
            if (day > AppClock.Today) return "Kelajakdagi sanani belgilab bo'lmaydi";
        }
        if (row is null)
        {
            row = new CurriculumProgress { TopicId = req.TopicId, OwnerKind = req.OwnerKind, OwnerId = req.OwnerId };
            db.CurriculumProgress.Add(row);
        }
        row.PassedOn = day;
        row.MarkedBy = userId;
        row.MarkedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>O'qituvchining rejalari: u dars beradigan (ega, fan) juftliklariga tushganlari.</summary>
    public static async Task<List<TeacherCurriculumDto>> ForTeacherAsync(IAppDbContext db, string teacherId, CancellationToken ct)
    {
        var pairs = await TeacherOwnerAccess.PairsAsync(db, teacherId, ct);
        var w = await WorldAsync(db, ct);
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var result = new List<TeacherCurriculumDto>();
        foreach (var plan in plans)
        {
            var mine = OwnersOf(plan, w).Where(o => pairs.Contains((o.Id, o.Kind, plan.SubjectId))).ToList();
            if (mine.Count == 0) continue;
            var ids = plan.Topics.Select(t => t.Id).ToList();
            var marks = await db.CurriculumProgress.AsNoTracking().Where(p => ids.Contains(p.TopicId)).ToListAsync(ct);
            foreach (var o in mine)
            {
                var passed = marks.Where(m => m.OwnerKind == o.Kind && m.OwnerId == o.Id).Select(m => m.TopicId).ToHashSet();
                var next = plan.Topics.OrderBy(t => t.Order).FirstOrDefault(t => !passed.Contains(t.Id));
                result.Add(new TeacherCurriculumDto(plan.Id, plan.Title, subjects.GetValueOrDefault(plan.SubjectId) ?? "—",
                    o.Kind, o.Id, o.Name, plan.Topics.Count, passed.Count, next?.Id, next?.Title));
            }
        }
        return [.. result.OrderBy(r => r.OwnerName).ThenBy(r => r.SubjectName)];
    }

    /// <summary>
    /// Hisobot: har reja × ega — o'tilgan/qolgan, rejadan orqada/oldinda, tugash prognozi, chorak
    /// bo'yicha; va jadvalda darsi bor, lekin ish rejasi yo'q (fan, daraja/yo'nalish) juftliklari.
    /// </summary>
    public static async Task<CurriculumReportDto> ReportAsync(IAppDbContext db, CancellationToken ct)
    {
        var today = AppClock.Today;
        var plans = await db.CurriculumPlans.AsNoTracking().Include(p => p.Topics).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var w = await WorldAsync(db, ct);
        var cal = await CalendarAsync(db, ct);
        var allIds = plans.SelectMany(p => p.Topics).Select(t => t.Id).ToList();
        var marks = await db.CurriculumProgress.AsNoTracking().Where(p => allIds.Contains(p.TopicId)).ToListAsync(ct);
        var marksByOwner = marks.ToLookup(m => (m.OwnerKind, m.OwnerId));

        var rows = new List<CurriculumReportRowDto>();
        foreach (var plan in plans)
        {
            var topics = plan.Topics.OrderBy(t => t.Order).ToList();
            var planned = PlannedDates(topics, cal);
            var totalHours = topics.Sum(t => t.Hours);
            var topicSet = topics.ToDictionary(t => t.Id);
            foreach (var o in OwnersOf(plan, w))
            {
                var mine = marksByOwner[(o.Kind, o.Id)].Where(m => topicSet.ContainsKey(m.TopicId)).ToList();
                var passedIds = mine.Select(m => m.TopicId).ToHashSet();
                var passedHours = topics.Where(t => passedIds.Contains(t.Id)).Sum(t => t.Hours);
                int? expected = planned.Count == 0 ? null : topics.Count(t => planned.TryGetValue(t.Id, out var d) && d <= today);

                // pace: hours per week since the year (or the first mark) began
                DateOnly? finish = null;
                var since = cal.YearStart is { } ys && ys <= today ? ys : mine.Count > 0 ? mine.Min(m => m.PassedOn) : (DateOnly?)null;
                if (since is { } s && passedHours > 0)
                {
                    var weeks = Math.Max(1m, (today.DayNumber - s.DayNumber) / 7m);
                    var pace = passedHours / weeks;
                    var left = totalHours - passedHours;
                    finish = left <= 0 ? mine.Max(m => m.PassedOn) : today.AddDays((int)Math.Ceiling(left / pace * 7m));
                }

                var quarters = cal.Quarters.Select(q => new CurriculumQuarterDto(
                    q.Quarter,
                    topics.Count(t => planned.TryGetValue(t.Id, out var d) ? d >= q.Start && d <= q.End : t.Quarter == q.Quarter),
                    mine.Count(m => m.PassedOn >= q.Start && m.PassedOn <= q.End))).ToList();

                rows.Add(new CurriculumReportRowDto(
                    plan.Id, plan.Title, subjects.GetValueOrDefault(plan.SubjectId) ?? "—", o.Kind, o.Id, o.Name,
                    topics.Count, passedIds.Count, totalHours, passedHours,
                    topics.Count == 0 ? 0 : Math.Round(passedIds.Count * 100m / topics.Count, 1),
                    expected, expected is { } e ? passedIds.Count - e : null,
                    finish?.ToString("yyyy-MM-dd"),
                    finish is { } f && cal.YearEnd is { } end && f > end,
                    quarters));
            }
        }

        // taught (subject, grade/track) without a plan
        var planKeys = plans.Select(p => (p.SubjectId, p.Grade, p.GroupId)).ToHashSet();
        var missing = new Dictionary<(string, int?, Guid?), List<string>>();
        foreach (var (kind, ownerId, subjectId) in w.Taught)
        {
            if (KeyOf(kind, ownerId, subjectId, w) is not { } key || planKeys.Contains(key)) continue;
            var name = kind == LessonOwnerKind.Class
                ? w.Classes.FirstOrDefault(c => c.Id == ownerId)?.Name
                : w.Groups.FirstOrDefault(g => g.Id.ToString() == ownerId)?.Name;
            if (!missing.TryGetValue(key, out var list)) missing[key] = list = [];
            if (name is not null && !list.Contains(name)) list.Add(name);
        }
        var missingDtos = missing
            .Select(kv => new CurriculumMissingDto(
                kv.Key.Item1, subjects.GetValueOrDefault(kv.Key.Item1) ?? "—", kv.Key.Item2, kv.Key.Item3,
                kv.Key.Item3 is { } g ? w.Groups.FirstOrDefault(x => x.Id == g)?.Name : null,
                [.. kv.Value.Order()]))
            .OrderBy(m => m.GroupId is null ? 0 : 1).ThenBy(m => m.Grade).ThenBy(m => m.GroupName).ThenBy(m => m.SubjectName)
            .ToList();

        return new CurriculumReportDto(
            today.ToString("yyyy-MM-dd"), cal.YearEnd?.ToString("yyyy-MM-dd"), cal.Quarters.Count > 0,
            [.. rows.OrderBy(r => r.OwnerName).ThenBy(r => r.SubjectName)], missingDtos);
    }
}
