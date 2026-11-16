namespace SchoolLms.Domain;

/* =====================================================================================
 *  OLIMPIADA — tashqaridan keladigan bolalar uchun bepul olimpiada (mock) imtihonlari.
 *  Alohida Telegram bot orqali ro'yxatdan o'tish, QR bilan kirish, natija va sertifikat,
 *  rassilka, qiziqqanlarni Lidlarga o'tkazish. Spetsifikatsiya: docs/modules/olympiad.md
 *  (mijoz TZ'si, 2026-11-16). Jadvallar `olympiad_` prefiksli — mavjud jadvallarga TEGMAYDI
 *  (yagona istisno: `leads.source` ga `olympiad` qiymati qo'shilgan).
 * ===================================================================================== */

/// <summary>Ruxsat kalitlari (Boshqaruv → Rollar oynasida).</summary>
public static class OlympiadPerms
{
    /// <summary>To'liq boshqaruv: tadbirlar, ishtirokchilar, natijalar, rassilka, sozlamalar.
    /// <c>olympiad:view</c> — faqat ko'rish.</summary>
    public const string Manage = "olympiad";

    /// <summary>Faqat imtihon kuni kirishni belgilash (QR skaner va qidiruv).</summary>
    public const string Checkin = "olympiadCheckin";
}

public static class OlympiadEventStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Finished = "finished";
    public const string Archived = "archived";
    public static readonly IReadOnlyList<string> All = [Draft, Published, Finished, Archived];
}

public static class OlympiadOnFull
{
    public const string Waitlist = "waitlist";
    public const string Close = "close";
}

public static class OlympiadRegStatus
{
    public const string Registered = "registered";
    public const string Waitlist = "waitlist";
    public const string Cancelled = "cancelled";
    public const string Attended = "attended";
    public const string NoShow = "no_show";

    /// <summary>Joy egallab turgan holatlar (limit hisobiga kiradi).</summary>
    public static readonly IReadOnlyList<string> HoldingSeat = [Registered, Attended, NoShow];
}

public static class OlympiadRulesDisplay
{
    public const string Text = "text";
    public const string Pdf = "pdf";
    public const string Both = "both";
}

/// <summary>Bo'limning yagona sozlamalar qatori (<see cref="Id"/> = 1).</summary>
public class OlympiadSettings
{
    public int Id { get; set; } = 1;

    // --- Bot ---
    /// <summary>Alohida bot tokeni (maktab botidan boshqa). API hech qachon to'liq qaytarmaydi.</summary>
    public string BotToken { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;

    // --- Ro'yxatdan o'tish ---
    public int GradeFrom { get; set; } = 1;
    public int GradeTo { get; set; } = 11;
    public string DefaultOnFull { get; set; } = OlympiadOnFull.Waitlist;

    // --- Umumiy nizom ---
    /// <summary>Belgilarli matn (TelegramHtml.FromMarkup).</summary>
    public string RulesText { get; set; } = string.Empty;
    public string RulesFileUrl { get; set; } = string.Empty;
    public string RulesFileName { get; set; } = string.Empty;
    public string RulesDisplay { get; set; } = OlympiadRulesDisplay.Text;
    /// <summary>Har saqlashda oshadi. Avval tasdiqlaganlardan qayta so'ralmaydi.</summary>
    public int RulesVersion { get; set; } = 1;

    // --- Standart eslatmalar (har yangi tadbirga qo'shiladi) ---
    public bool Reminder1Enabled { get; set; } = true;
    public int Reminder1Minutes { get; set; } = 1440;
    public bool Reminder2Enabled { get; set; } = true;
    public int Reminder2Minutes { get; set; } = 180;

    // --- Natija xabari qismlarining standart qiymatlari ---
    public bool ResultShowScore { get; set; } = true;
    public bool ResultShowRank { get; set; } = true;
    public bool ResultShowCertificate { get; set; } = true;
    public bool ResultShowInvite { get; set; } = true;
    public string InviteText { get; set; } = "🏫 Maktabimizga qabul";
    public string InviteUrl { get; set; } = string.Empty;
    /// <summary>pdf | png</summary>
    public string CertificateFormat { get; set; } = "pdf";

    // --- Aloqa ---
    /// <summary>"Adminga savol" — https://t.me/username</summary>
    public string AdminLink { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Majburiy obuna kanali. Bot kanalda admin bo'lishi shart.</summary>
public class OlympiadChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    /// <summary><c>@username</c> yoki <c>-100…</c> chat id.</summary>
    public string ChatRef { get; set; } = string.Empty;
    public string InviteLink { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Oxirgi tekshiruv xatosi ("bot admin emas"); bo'sh — hammasi joyida.</summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>Botga yozgan Telegram akkaunt (ota-ona yoki bolaning o'zi).</summary>
public class OlympiadBotUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Akkaunt egasi kontakt yuborgan bo'lsa.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary><c>/start src_&lt;kod&gt;</c> — birinchi kelgan manba.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Foydalanuvchi botni bloklagan (403). Qayta /start bossa olib tashlanadi.</summary>
    public bool IsBlocked { get; set; }
    /// <summary>Suhbat holati (FSM) — JSON. Server qayta ishga tushsa ham anketa davom etadi.</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>Obuna tekshiruvi keshi: shu vaqtgacha qayta tekshirilmaydi.</summary>
    public DateTime? SubscribedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Bola. Bitta akkauntda bir nechta bola bo'lishi mumkin.</summary>
public class OlympiadParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Kim qo'shgan; CRM'da qo'lda qo'shilgan bo'lsa <c>null</c>.</summary>
    public Guid? BotUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary><c>+998XXXXXXXXX</c></summary>
    public string Phone { get; set; } = string.Empty;
    public int Grade { get; set; }
    public string School { get; set; } = string.Empty;
    /// <summary>Ota-ona "Farzandlarim"dan o'chirgan — ro'yxatlarda ko'rinmaydi, tarix saqlanadi.</summary>
    public bool IsHidden { get; set; }
    public string? LeadId { get; set; }
    public DateTime? LeadConvertedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Tadbir — har bir olimpiada alohida.</summary>
public class OlympiadEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Qisqa tartib raqami: ishtirokchi raqami <c>M{Code}-0001</c> va deep link <c>ev_{Code}</c>.</summary>
    public int Code { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Belgilarli matn.</summary>
    public string Description { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    /// <summary>= Date + StartTime (Toshkent devor soati). Saqlashda hisoblanadi.</summary>
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public string LocationMapUrl { get; set; } = string.Empty;
    /// <summary><c>null</c> — limitsiz.</summary>
    public int? Capacity { get; set; }
    public string OnFull { get; set; } = OlympiadOnFull.Waitlist;
    public DateTime? RegistrationOpensAt { get; set; }
    public DateTime? RegistrationClosesAt { get; set; }
    public string Status { get; set; } = OlympiadEventStatus.Draft;
    public decimal MaxScore { get; set; } = 100;

    public bool ShowScore { get; set; } = true;
    public bool ShowRank { get; set; } = true;
    public bool ShowCertificate { get; set; } = true;
    public bool ShowInvite { get; set; } = true;

    /// <summary>Tadbirning o'z nizomi; bo'sh (matn ham, fayl ham) — umumiy nizom ishlatiladi.</summary>
    public string RulesText { get; set; } = string.Empty;
    public string RulesFileUrl { get; set; } = string.Empty;
    public string RulesFileName { get; set; } = string.Empty;
    public string RulesDisplay { get; set; } = OlympiadRulesDisplay.Text;
    public int RulesVersion { get; set; } = 1;

    /// <summary>Joy egallagan yozuvlar soni — atomar UPDATE bilan o'zgaradi (poyga holati yo'q).</summary>
    public int SeatsTaken { get; set; }
    /// <summary>Oxirgi berilgan ishtirokchi raqami. Bekor qilinganning raqami qayta berilmaydi.</summary>
    public int LastNumber { get; set; }
    /// <summary>Kutish ro'yxati navbat hisoblagichi.</summary>
    public int LastWaitlistSeq { get; set; }

    /// <summary>"Yakunlash" bosilgan vaqt — belgilanmaganlar <c>no_show</c> bo'lgan.</summary>
    public DateTime? CheckinClosedAt { get; set; }
    public DateTime? ResultsSentAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Bolaning tadbirga yozilishi. <c>(EventId, ParticipantId)</c> — noyob.</summary>
public class OlympiadRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid ParticipantId { get; set; }
    /// <summary>Ishtirokchi raqami (tadbir ichida). Kutish ro'yxatida 0.</summary>
    public int Number { get; set; }
    public string Status { get; set; } = OlympiadRegStatus.Registered;
    /// <summary>Kutish ro'yxatidagi tartib (kichigi oldinda).</summary>
    public int WaitlistSeq { get; set; }
    /// <summary>QR ichidagi yagona narsa — 128 bit tasodifiy, base64url.</summary>
    public string QrToken { get; set; } = string.Empty;
    public DateTime? RulesAcceptedAt { get; set; }
    public int RulesVersion { get; set; }

    public DateTime? CheckedInAt { get; set; }
    public string CheckedInBy { get; set; } = string.Empty;
    /// <summary>qr | manual</summary>
    public string CheckinMethod { get; set; } = string.Empty;

    // --- Natija ---
    public decimal? Score { get; set; }
    /// <summary>excel | manual</summary>
    public string ScoreSource { get; set; } = string.Empty;
    public DateTime? ScoreUpdatedAt { get; set; }
    public string ScoreUpdatedBy { get; set; } = string.Empty;
    /// <summary>Oxirgi yuborilgan ball — "Tuzatilganni qayta yuborish" faqat farq qilganlarga boradi.</summary>
    public decimal? ResultSentScore { get; set; }
    public DateTime? ResultSentAt { get; set; }

    /// <summary>bot | crm</summary>
    public string CreatedVia { get; set; } = "bot";
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public static class OlympiadReminderKind
{
    public const string Standard1 = "standard1";
    public const string Standard2 = "standard2";
    public const string Custom = "custom";
}

public static class OlympiadTarget
{
    public const string Registered = "registered";
    public const string Waitlist = "waitlist";
    public const string All = "all";
}

/// <summary>Tadbirga bog'liq eslatma.</summary>
public class OlympiadReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string Kind { get; set; } = OlympiadReminderKind.Custom;
    /// <summary>Tadbirgacha necha daqiqa oldin. Berilsa — tadbir vaqti o'zgarsa o'zi siljiydi.</summary>
    public int? OffsetMinutes { get; set; }
    /// <summary>Aniq vaqt (OffsetMinutes bo'lmasa).</summary>
    public DateTime? SendAt { get; set; }
    /// <summary>Belgilarli matn. Standart eslatmada bo'sh — shablon ishlatiladi.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>JSON: [{kind,url,name}]</summary>
    public string AttachmentsJson { get; set; } = "[]";
    public string Target { get; set; } = OlympiadTarget.Registered;
    /// <summary>scheduled | sent | cancelled</summary>
    public string Status { get; set; } = "scheduled";
    public DateTime? SentAt { get; set; }
    public int SentCount { get; set; }
}

public static class OlympiadBroadcastStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

/// <summary>Rassilka.</summary>
public class OlympiadBroadcast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string AttachmentsJson { get; set; } = "[]";
    /// <summary>JSON: [{text,url}]</summary>
    public string ButtonsJson { get; set; } = "[]";
    /// <summary>JSON filtr — OlympiadAudience.</summary>
    public string AudienceJson { get; set; } = "{}";
    public DateTime? ScheduledAt { get; set; }
    public string Status { get; set; } = OlympiadBroadcastStatus.Draft;
    public int Total { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Blocked { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public static class OlympiadDeliveryKind
{
    public const string Broadcast = "broadcast";
    public const string Reminder = "reminder";
    public const string Result = "result";
}

public static class OlympiadDeliveryStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Yuborish navbati — rassilka, eslatma va natija. Noyob kalit
/// <c>(Kind, RefId, BotUserId, RegistrationId, Tag)</c>: bir xabar bir odamga faqat bir marta boradi.
/// </summary>
public class OlympiadDelivery
{
    public long Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    /// <summary>Rassilka / eslatma / tadbir id'si.</summary>
    public Guid RefId { get; set; }
    public Guid BotUserId { get; set; }
    /// <summary>Eslatma va natijada — qaysi yozuv uchun; rassilkada <see cref="Guid.Empty"/>.</summary>
    public Guid RegistrationId { get; set; }
    /// <summary>Natijada — yuborilgan ball (tuzatilgan natija yangi qator bo'ladi).</summary>
    public string Tag { get; set; } = string.Empty;
    public string Status { get; set; } = OlympiadDeliveryStatus.Pending;
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

/// <summary>Botning standart xabari (kalit bo'yicha). Bazada yo'q kalit — koddagi standart matn.</summary>
public class OlympiadTemplate
{
    public string Key { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string AttachmentsJson { get; set; } = "[]";
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Yuklangan fayl → Telegram <c>file_id</c> keshi (qayta yuklanmasin).</summary>
public class OlympiadMediaCache
{
    public string Path { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
