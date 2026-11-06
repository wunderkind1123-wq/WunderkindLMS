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

    /// <summary><see cref="MonitoringStatus"/>.</summary>
    public string Status { get; set; } = MonitoringStatus.Draft;

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

    /// <summary>Variantlar soni (qog'oz testda matnsiz ham bo'ladi): 2–6.</summary>
    public int OptionCount { get; set; } = 4;

    public int CorrectIndex { get; set; }
    public Guid? TopicId { get; set; }
    public decimal Weight { get; set; } = 1m;
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

    public int Correct { get; set; }
    public decimal Percent { get; set; }

    /// <summary>Milliy uslub: 0–75 ball.</summary>
    public decimal? Points { get; set; }

    /// <summary>Milliy uslub: A+ / A / B+ / B / C+ / C yoki null.</summary>
    public string? Level { get; set; }
}
