namespace SchoolLms.Domain;

// ===========================================================================
//  Ichki monitoring — docs/modules/curriculum-monitoring.md §2.
// ===========================================================================
//
//  Mijoz, 2026-11-06: "Yo'nalish guruhlardan blok testdan tashqari ichki monitoring
//  test olinadi … oddiy 30–100 test yoki milliy sertifikat imtihonlariga o'xshab.
//  1–8 sinf o'tilgan mavzular asosida. Faqat o'quv bo'limi tomonidan."
//
//  BLOK TESTDAN ALOHIDA
//  --------------------
//  Savollar o'z bankida (`monitoring_banks`), blok test banklariga aralashmaydi.
//  Natijani faqat o'quv bo'limi (`monitoring` ruxsati) ko'radi — ota-onaga ham,
//  o'qituvchiga ham chiqmaydi.
//
//  TESTNING SAVOLLARI — NUSXA
//  --------------------------
//  `MonitoringItem` bankdagi savolning NUSXASI (yoki bank savolisiz, qog'oz testning
//  faqat kaliti + mavzusi). Bank keyin tahrirlansa, o'tkazilgan test qayta baholanmaydi.
//
//  JAVOBLAR — BITTA SATR
//  ---------------------
//  `MonitoringParticipant.Answers` — har savolga bitta belgi: "A".."F" yoki "-"
//  (javobsiz). Qog'oz testda o'quv bo'limi "ABDC-A…" ni kiritadi, onlayn testda
//  o'quvchi o'zi belgilaydi. Tekshirish, Rasch va mavzu tahlili shu satrdan.
// ===========================================================================

public static class MonitoringDelivery
{
    public const string Paper = "paper";
    public const string Online = "online";
    public static bool IsValid(string? v) => v is Paper or Online;
}

public static class MonitoringScoring
{
    /// <summary>To'g'ri javoblar soni va foiz.</summary>
    public const string Simple = "simple";

    /// <summary>Milliy sertifikat uslubi: 0–75 ball va daraja (A+ … C).</summary>
    public const string National = "national";

    public static bool IsValid(string? v) => v is Simple or National;
}

public static class MonitoringNationalMethod
{
    /// <summary>Rasch (JMLE), T = 50 + 10·Z — mijoz tanlovi, 2026-11-06.</summary>
    public const string Rasch = "rasch";

    /// <summary>Savolga qo'lda qo'yilgan vazn: 75 × to'g'ri vaznlar / jami vazn.</summary>
    public const string Weights = "weights";

    public static bool IsValid(string? v) => v is Rasch or Weights;
}

public static class MonitoringStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Closed = "closed";
}

/// <summary>
/// Onlayn testdagi halollik hodisalari (2026-11-24). <see cref="Leaves"/> — test oynasidan chiqish
/// (boshqa tab/dastur, to'liq ekrandan chiqish): har biri sanaladi va
/// <see cref="MonitoringTest.MaxLeaves"/> dan oshsa test yakunlanadi. Qolganlari (nusxa olish,
/// qo'yish, chop etish, dasturchi oynasi) — faqat qayd, o'quv bo'limi ko'radi.
/// </summary>
public static class MonitoringEventKind
{
    public const string Hidden = "hidden";
    public const string Blur = "blur";
    public const string Fullscreen = "fullscreen";
    public const string Copy = "copy";
    public const string Paste = "paste";
    public const string Print = "print";
    public const string DevTools = "devtools";

    public static readonly string[] Leaves = [Hidden, Blur, Fullscreen];
    public static readonly string[] All = [Hidden, Blur, Fullscreen, Copy, Paste, Print, DevTools];
}

/// <summary>Test o'zi yakunlangan sabab (o'quvchi o'zi tugatsa — null).</summary>
public static class MonitoringFinishReason
{
    public const string Time = "time";
    public const string Leaves = "leaves";
}

public static class MonitoringParticipantStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Absent = "absent";
}

/// <summary>Monitoring savollar banki — fan va daraja (yoki yo'nalish) bo'yicha.</summary>
public class MonitoringBank
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Sinf darajasi — ish reja mavzularini tanlash uchun. Yo'nalish bankida null.</summary>
    public int? Grade { get; set; }

    /// <summary>Yo'nalish guruhi — yo'nalish ish rejasi mavzulari uchun.</summary>
    public Guid? GroupId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<MonitoringQuestion> Questions { get; set; } = new();
}

/// <summary>Bankdagi bitta savol (2–6 variant, bittasi to'g'ri).</summary>
public class MonitoringQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BankId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public List<string> Options { get; set; } = new();

    /// <summary>Variant rasmlari — <see cref="Options"/> bilan bir xil uzunlikda ("" — rasmsiz) yoki bo'sh.</summary>
    public List<string> OptionImages { get; set; } = new();

    /// <summary>0 → A, 1 → B …</summary>
    public int CorrectIndex { get; set; }

    /// <summary>Ish rejadagi mavzu (curriculum_topics.id) — mavzu tahlili uchun.</summary>
    public Guid? TopicId { get; set; }

    /// <summary>Qo'lda vazn (milliy uslub, "vazn" usuli). Sukut — 1.</summary>
    public decimal Weight { get; set; } = 1m;
}

/// <summary>Bitta monitoring testi.</summary>
public class MonitoringTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Sinf darajasi yoki yo'nalish — mavzular qaysi ish rejadan olinishi.</summary>
    public int? Grade { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary><see cref="MonitoringDelivery"/>.</summary>
    public string Delivery { get; set; } = MonitoringDelivery.Paper;

    /// <summary><see cref="MonitoringScoring"/>.</summary>
    public string Scoring { get; set; } = MonitoringScoring.Simple;

    /// <summary><see cref="MonitoringNationalMethod"/> — faqat milliy uslubda.</summary>
    public string? NationalMethod { get; set; }

    /// <summary>O'tkazilgan (yoki o'tkaziladigan) kun.</summary>
    public DateOnly TestDate { get; set; }

    /// <summary>Onlayn: oyna boshi / oxiri va bir o'quvchiga vaqt.</summary>
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public int? TimeLimitMin { get; set; }

    /// <summary>
    /// Onlayn: har o'quvchiga savollar va variantlar o'z tartibida (yonidagidan ko'chirib bo'lmasin).
    /// Tartib o'quvchi yozuvidan hisoblanadi — saqlanmaydi, qayta kirganda o'zgarmaydi.
    /// </summary>
    public bool Shuffle { get; set; }

    /// <summary>
    /// Onlayn: test oynasidan necha marta chiqishga ruxsat. Oshsa test yakunlanadi.
    /// null — cheklanmagan, faqat qayd qilinadi.
    /// </summary>
    public int? MaxLeaves { get; set; }

    /// <summary>
    /// Oylik test (2026-11-24): natija shu oyning Mavsumiy bahosi sifatida yoziladi
    /// (<see cref="SeasonalMark"/>, monthly, ball = foiz) va ota-onaning "Oylik o'sish"
    /// kartasiga chiqadi. Ikkalasi ham bor yoki ikkalasi ham null — null bo'lsa natija
    /// avvalgidek faqat o'quv bo'limida qoladi.
    /// </summary>
    public int? SeasonalYear { get; set; }
    public int? SeasonalMonth { get; set; }

    /// <summary><see cref="MonitoringStatus"/>.</summary>
    public string Status { get; set; } = MonitoringStatus.Draft;

    /// <summary>Onlayn: xona PIN'i (4 raqam, ochilganda beriladi) — doimiy kod bilan kirish uchun.</summary>
    public string? RoomPin { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public List<MonitoringItem> Items { get; set; } = new();
}

/// <summary>Testning bitta savoli — bank savolining nusxasi yoki faqat kalit.</summary>
public class MonitoringItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestId { get; set; }
    public int Order { get; set; }

    /// <summary>Qaysi bank savolidan olingan (tarix uchun). Faqat kalitli savolda null.</summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Onlayn testda ko'rsatiladi. Qog'oz testda bo'sh bo'lishi mumkin.</summary>
    public string? Text { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> Options { get; set; } = new();

    /// <summary>Variant rasmlari (bank savolidan nusxa) — <see cref="MonitoringQuestion.OptionImages"/>.</summary>
    public List<string> OptionImages { get; set; } = new();

    /// <summary>Variantlar soni (qog'oz testda matnsiz ham bo'ladi): 2–6.</summary>
    public int OptionCount { get; set; } = 4;

    public int CorrectIndex { get; set; }
    public Guid? TopicId { get; set; }
    public decimal Weight { get; set; } = 1m;
}

/// <summary>
/// Test sessiyasi (2026-11-24): bir o'tirishda bir nechta fan testi ketma-ket — mijoz:
/// "o'quvchilar bir kirishda bir nechta fandan test ishlashadi va bir fandan testni tugatgan
/// o'quvchi keyingi fandan test ishlashni boshlaydi". Har fan — oddiy <see cref="MonitoringTest"/>
/// (o'z vaqti, natijasi, Mavsumiy bahosi); sessiya faqat tartib va bitta kod beradi.
/// </summary>
public class MonitoringSitting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateOnly TestDate { get; set; }

    /// <summary>Fanlar tartibi — o'quvchi shu tartibda ishlaydi. Bir test faqat bitta sessiyada.</summary>
    public List<Guid> TestIds { get; set; } = new();

    /// <summary>Xona PIN'i (4 raqam) — doimiy kod bilan kirganda shu sessiyani tanlaydi.</summary>
    public string? RoomPin { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Doimiy shaxsiy kod (2026-11-25, curriculum-monitoring.md §2.9) — mijoz: "har oy test olinsa
/// har safar kod berish kerakmi?". O'quvchining yil bo'yi o'zgarmaydigan kodi; testga u bilan
/// kirish uchun nazoratchi doskaga yozgan xona PIN'i (<see cref="MonitoringSitting.RoomPin"/>,
/// <see cref="MonitoringTest.RoomPin"/>) ham kerak — kod kimligini, PIN qaysi testligini aytadi.
/// Kod yo'qolsa o'quv bo'limi yangisini beradi, eskisi ishlamay qoladi.
/// </summary>
public class MonitoringStudentCode
{
    public string StudentId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Sessiyadagi o'quvchining yagona kodi va qurilma qulfi (butun sessiya bitta kompyuterda).</summary>
public class MonitoringSittingCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SittingId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? DeviceToken { get; set; }
}

/// <summary>Testga kiritilgan o'quvchi va uning javoblari, natijasi.</summary>
public class MonitoringParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestId { get; set; }
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Test paytidagi sinf yoki guruh (<see cref="LessonOwnerKind"/>).</summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Har savolga bitta belgi: "A".."F" yoki "-". null — hali kiritilmagan.</summary>
    public string? Answers { get; set; }

    /// <summary><see cref="MonitoringParticipantStatus"/>.</summary>
    public string Status { get; set; } = MonitoringParticipantStatus.Pending;

    /// <summary>Onlayn: bir martalik shaxsiy kod (8 belgi).</summary>
    public string? AccessCode { get; set; }

    /// <summary>Onlayn: testni boshlagan qurilma siri — boshqa qurilmadan davom ettirib bo'lmaydi.</summary>
    public string? DeviceToken { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Onlayn: test oynasidan chiqishlar soni (<see cref="MonitoringEventKind.Leaves"/>).</summary>
    public int Leaves { get; set; }

    /// <summary>Onlayn: hodisalar jurnali, "2026-11-24T09:15:02Z blur" — 200 tagacha.</summary>
    public List<string> Events { get; set; } = new();

    /// <summary><see cref="MonitoringFinishReason"/> yoki null.</summary>
    public string? FinishReason { get; set; }

    /// <summary>Onlayn: brauzerdan oxirgi xabar (javob, hodisa yoki 30 soniyalik "ping") — jonli kuzatuv uchun.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public int Correct { get; set; }
    public decimal Percent { get; set; }

    /// <summary>Milliy uslub: 0–75 ball.</summary>
    public decimal? Points { get; set; }

    /// <summary>Milliy uslub: A+ / A / B+ / B / C+ / C yoki null.</summary>
    public string? Level { get; set; }
}
