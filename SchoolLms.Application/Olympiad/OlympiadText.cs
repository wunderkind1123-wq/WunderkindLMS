using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Olympiad;

/// <summary>Bot shabloni: kalit, qachon yuboriladi, qaysi tokenlar ishlaydi, standart matn.</summary>
public sealed record OlympiadTemplateDef(string Key, string Title, string Tokens, string Default);

/// <summary>
/// Matnlar: standart shablonlar, tokenlarni almashtirish, telefon va ism tekshiruvi, raqam formati.
/// Hammasi sof funksiya — testlar bazasiz yuradi (OlympiadRulesTests).
/// </summary>
public static partial class OlympiadText
{
    // ---------------- Menyu tugmalari (reply keyboard) ----------------
    public const string BtnRegister = "📝 Ro'yxatdan o'tish";
    public const string BtnMyRegs = "📋 Mening yozuvlarim";
    public const string BtnKids = "👶 Farzandlarim";
    public const string BtnAsk = "❓ Adminga savol";
    public const string BtnRules = "ℹ️ Nizom";
    public const string BtnBack = "⬅️ Orqaga";
    public const string BtnCancel = "❌ Bekor qilish";
    public const string BtnSendPhone = "📱 Raqamni yuborish";

    private const string RegTokens = "{bola}, {tadbir}, {sana}, {vaqt}, {manzil}, {raqam}";

    /// <summary>13-bo'limdagi barcha xabarlar. Matn — belgilarli (**qalin**, __kursiv__ …).</summary>
    public static readonly IReadOnlyList<OlympiadTemplateDef> Templates =
    [
        new("welcome", "/start — salomlashish", "{ism}",
            "Assalomu alaykum, **{ism}**! 👋\n\nBu — **Wunderkind Xalqaro Maktabi**ning bepul olimpiada boti.\nBu yerda farzandingizni olimpiadaga ro'yxatdan o'tkazasiz, eslatma, QR kod, natija va sertifikat olasiz.\n\nPastdagi menyudan tanlang 👇"),
        new("subscribe_required", "Obuna bo'lmagan", "",
            "Botdan foydalanish uchun quyidagi kanallarga obuna bo'ling, so'ng **✅ Tekshirish** tugmasini bosing."),
        new("subscribe_ok", "Obuna tasdiqlandi", "", "✅ Rahmat! Obuna tasdiqlandi."),
        new("no_events", "Ochiq tadbir yo'q", "",
            "Hozircha ro'yxat ochiq olimpiada yo'q. Yangi olimpiada e'lon qilinganda shu bot orqali xabar beramiz 🙂"),
        new("rules_prompt", "Nizom oldidan", "{tadbir}",
            "📜 **{tadbir}** — nizom bilan tanishib chiqing va pastdagi tugmani bosing."),
        new("registered", "Ro'yxatdan o'tdi", RegTokens,
            "🎉 **{bola}** ro'yxatdan o'tdi!\n\n🏆 {tadbir}\n📅 {sana}, {vaqt}\n📍 {manzil}\n🔢 Ishtirokchi raqami: **{raqam}**\n\nImtihon kuni pastdagi QR kodni kirishda ko'rsating."),
        new("waitlisted", "Kutish ro'yxatiga tushdi", "{bola}, {tadbir}, {navbat}",
            "⏳ Joylar hozircha band. **{bola}** — **{tadbir}** kutish ro'yxatiga qo'shildi.\nNavbatdagi o'rningiz: **{navbat}**.\n\nJoy bo'shashi bilan avtomatik ro'yxatga o'tkazamiz va xabar beramiz."),
        new("event_full", "Joy yo'q (ro'yxat yopiq)", "{tadbir}",
            "😔 Afsuski, **{tadbir}** uchun joylar tugadi."),
        new("promoted_from_waitlist", "Kutishdan o'tdi", RegTokens,
            "🎉 Joy bo'shadi! **{bola}** endi **{tadbir}** ro'yxatida.\n\n📅 {sana}, {vaqt}\n📍 {manzil}\n🔢 Ishtirokchi raqami: **{raqam}**\n\nImtihon kuni QR kodni kirishda ko'rsating."),
        new("cancelled", "Bekor qildi", "{bola}, {tadbir}", "❌ **{bola}** — **{tadbir}** ro'yxatidan chiqarildi."),
        new("reminder_default_1d", "Eslatma (1-standart, 1 kun oldin)", RegTokens,
            "⏰ Eslatma: ertaga **{tadbir}**!\n\n👤 {bola} ({raqam})\n📅 {sana}, {vaqt}\n📍 {manzil}\n\nQR kodni unutmang 🙂"),
        new("reminder_default_3h", "Eslatma (2-standart, 3 soat oldin)", RegTokens,
            "⏰ **{tadbir}** bugun soat **{vaqt}** da boshlanadi.\n\n👤 {bola} ({raqam})\n📍 {manzil}\n\nKirishda QR kodni ko'rsating."),
        new("result", "Natija", "{bola}, {tadbir}, {ball}, {max}, {foiz}, {orin}, {jami}",
            "📊 **{tadbir}** natijalari\n\n👤 {bola}\n✅ Ball: **{ball}** / {max} ({foiz}%)\n🏅 O'rin: **{orin}** ({jami} ishtirokchidan)\n\nQatnashganingiz uchun rahmat!"),
        new("ask_admin", "Adminga savol", "{admin_link}",
            "Savolingizni administratorga yozing — pastdagi tugmani bosing 👇"),
        new("registration_closed", "Ro'yxat muddati o'tgan", "{tadbir}",
            "⌛ **{tadbir}** uchun ro'yxatdan o'tish muddati tugagan."),
    ];

    public static OlympiadTemplateDef? Def(string key) => Templates.FirstOrDefault(t => t.Key == key);

    /// <summary>Bazadagi (tahrirlangan) yoki standart matn.</summary>
    public static async Task<string> TemplateAsync(IAppDbContext db, string key, CancellationToken ct)
    {
        var row = await db.OlympiadTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);
        return row is { Text.Length: > 0 } ? row.Text : Def(key)?.Default ?? "";
    }

    /// <summary>
    /// Shablonni Telegram HTML'ga aylantiradi. <paramref name="values"/> — token → qiymat (oddiy matn,
    /// ekranlanadi). <paramref name="dropTokens"/> — o'chirilgan qismlar: shu token turgan qator
    /// BUTUNLAY olib tashlanadi (bo'sh qator qolmaydi).
    /// </summary>
    public static string Render(string markup, IReadOnlyDictionary<string, string?> values, IEnumerable<string>? dropTokens = null)
    {
        var drop = dropTokens?.ToHashSet() ?? [];
        var lines = (markup ?? "").Replace("\r\n", "\n").Split('\n')
            .Where(line => !drop.Any(t => line.Contains("{" + t + "}", StringComparison.Ordinal)))
            .ToList();
        // Ketma-ket ikki bo'sh qatorni bittaga, boshi va oxiridagi bo'shlarni olib tashlaymiz.
        var compact = new List<string>();
        foreach (var l in lines)
        {
            if (l.Trim().Length == 0 && (compact.Count == 0 || compact[^1].Trim().Length == 0)) continue;
            compact.Add(l);
        }
        while (compact.Count > 0 && compact[^1].Trim().Length == 0) compact.RemoveAt(compact.Count - 1);

        var html = TelegramHtml.FromMarkup(string.Join("\n", compact));
        foreach (var (k, v) in values)
            html = html.Replace("{" + k + "}", TelegramHtml.Escape(v ?? ""), StringComparison.Ordinal);
        return html;
    }

    /// <summary>Tadbir va bola uchun umumiy tokenlar.</summary>
    public static Dictionary<string, string?> RegValues(OlympiadEvent ev, OlympiadParticipant kid, OlympiadRegistration reg) => new()
    {
        ["bola"] = kid.FullName,
        ["tadbir"] = ev.Title,
        ["sana"] = FormatDate(ev.Date),
        ["vaqt"] = $"{ev.StartTime:HH\\:mm}–{ev.EndTime:HH\\:mm}",
        ["manzil"] = ev.Location,
        ["raqam"] = Number(ev.Code, reg.Number),
        ["sinf"] = kid.Grade.ToString(CultureInfo.InvariantCulture),
        ["maktab"] = kid.School,
    };

    private static readonly string[] Months =
        ["yanvar", "fevral", "mart", "aprel", "may", "iyun", "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr"];

    public static string FormatDate(DateOnly d) => $"{d.Day}-{Months[d.Month - 1]}, {d.Year}";

    /// <summary>Ishtirokchi raqami: <c>M{kod}-{0001}</c>.</summary>
    public static string Number(int eventCode, int number) =>
        number <= 0 ? "—" : $"M{eventCode}-{number:0000}";

    /// <summary>Raqamni qidiruv/import uchun o'qiydi: "M3-0012", "m3-12", "12" (tadbir ichida) → 12.</summary>
    public static int? ParseNumber(string? text, int eventCode)
    {
        var t = (text ?? "").Trim().ToUpperInvariant();
        if (t.Length == 0) return null;
        var m = NumberRegex().Match(t);
        if (m.Success)
        {
            if (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) != eventCode) return null;
            return int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    [GeneratedRegex(@"^M(\d+)-(\d+)$")]
    private static partial Regex NumberRegex();

    // ---------------- Tekshiruvlar ----------------

    /// <summary>
    /// Telefonni <c>+998XXXXXXXXX</c> ga keltiradi. Qabul qiladi: +998 90 123 45 67, 998901234567,
    /// 901234567, (90) 123-45-67. Noto'g'ri bo'lsa — null.
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        var digits = new string((input ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 9) digits = "998" + digits;
        if (digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal)) return "+" + digits;
        return null;
    }

    /// <summary>Taqqoslash uchun oxirgi 9 raqam.</summary>
    public static string PhoneKey(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length >= 9 ? d[^9..] : d;
    }

    /// <summary>F.I.Sh: 3–100 belgi, kamida 2 so'z. Xato bo'lsa — matni, aks holda null.</summary>
    public static string? ValidateName(string? name)
    {
        var n = CleanSpaces(name);
        if (n.Length < 3 || n.Length > 100) return "Ism 3 tadan 100 tagacha belgi bo'lishi kerak.";
        if (n.Split(' ').Length < 2) return "Familiya va ismni to'liq yozing (kamida 2 so'z).";
        if (n.Any(char.IsDigit)) return "Ismda raqam bo'lmasligi kerak.";
        return null;
    }

    public static string? ValidateSchool(string? school)
    {
        var s = CleanSpaces(school);
        return s.Length is < 2 or > 100 ? "Maktab nomi 2 tadan 100 tagacha belgi bo'lishi kerak." : null;
    }

    public static string CleanSpaces(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>QR token — 128 bit tasodifiy, base64url (22 belgi). Taxmin qilib bo'lmaydi.</summary>
    public static string NewQrToken()
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // ---------------- Natija ----------------

    public static decimal Percent(decimal score, decimal max) =>
        max <= 0 ? 0 : Math.Round(score / max * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// "Musobaqa" o'rni: ball kamayish tartibida, teng ballga bir xil o'rin (1, 2, 2, 4).
    /// </summary>
    public static Dictionary<TKey, int> Ranks<TKey>(IEnumerable<(TKey Key, decimal Score)> scores) where TKey : notnull
    {
        var ordered = scores.OrderByDescending(s => s.Score).ToList();
        var ranks = new Dictionary<TKey, int>();
        for (var i = 0; i < ordered.Count; i++)
            ranks[ordered[i].Key] = i > 0 && ordered[i].Score == ordered[i - 1].Score
                ? ranks[ordered[i - 1].Key]
                : i + 1;
        return ranks;
    }

    public static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
