namespace SchoolLms.Domain;

// ===========================================================================
//  Ish reja (o'quv reja) — docs/modules/curriculum-monitoring.md §1.
// ===========================================================================
//
//  Mijoz, 2026-11-06: "O'quvchilarning ish rejasi — to'liq, ustoz aynan darsda
//  ishlatadigan holatda … qaysi mavzu o'tildi yo endi o'tiladi bilish uchun zarur."
//
//  REJA KIMGA TEGISHLI
//  -------------------
//  Fan + sinf darajasi (barcha 5-sinflar uchun bitta "5-sinf Matematika") YOKI fan +
//  yo'nalish guruhi (9–11-sinf o'quvchilari yo'nalishda bitta darsda o'qiydi, shuning
//  uchun daraja yo'q). Bittasi to'ldiriladi: `Grade` yoki `GroupId`.
//
//  "O'TILDI" BELGISI HAR SINF / GURUH UCHUN ALOHIDA
//  ------------------------------------------------
//  Reja bitta, lekin 5-A va 5-B o'z tezligida yuradi: `CurriculumProgress` mavzu ×
//  ega (sinf yoki guruh). Belgini o'qituvchi ham, o'quv bo'limi ham qo'yadi.
//
//  QAYTA YUKLASH BELGILARNI SAQLAYDI
//  ---------------------------------
//  Yangi Excel mavzularni TARTIB RAQAMI bo'yicha yangilaydi: shu raqamli mavzu bor
//  bo'lsa — matni almashadi (belgisi qoladi), yangisi qo'shiladi, ortiqchasi o'chadi.
// ===========================================================================

/// <summary>Bitta fanning ish rejasi — sinf darajasi yoki yo'nalish guruhi uchun.</summary>
public class CurriculumPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>subjects.id.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Sinf darajasi (1–11). Yo'nalish rejasida null.</summary>
    public int? Grade { get; set; }

    /// <summary>Yo'nalish guruhi (study_groups.id, <c>is_track</c>). Daraja rejasida null.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Ko'rsatiladigan nom ("5-sinf Matematika").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>O'qituvchining asl hujjati (Word/PDF/Excel) — `/uploads/...`.</summary>
    public string? FileUrl { get; set; }

    public string? FileName { get; set; }

    /// <summary>Kim yuklagan (users.id).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<CurriculumTopic> Topics { get; set; } = new();
}

/// <summary>Rejadagi bitta mavzu — Excel'ning bitta qatori.</summary>
public class CurriculumTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlanId { get; set; }

    /// <summary>Tartib raqami (Excel'dagi "№") — 1 dan.</summary>
    public int Order { get; set; }

    /// <summary>Chorak (1–4). null = ko'rsatilmagan.</summary>
    public int? Quarter { get; set; }

    /// <summary>Chorak ichidagi hafta (1 dan). null = ko'rsatilmagan.</summary>
    public int? Week { get; set; }

    /// <summary>
    /// Ish rejadagi ANIQ dars sanasi ("Sana" ustuni yoki qo'lda tahrir). Berilsa — reja sanasi
    /// shu; chorak va hafta undan hisoblanadi. null — reja sanasi chorak/haftadan taxmin qilinadi.
    /// </summary>
    public DateOnly? PlannedOn { get; set; }

    /// <summary>Bo'lim / modul nomi.</summary>
    public string? Section { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Soat (dars) soni — kamida 1.</summary>
    public int Hours { get; set; } = 1;

    /// <summary>Maqsad / kutilgan natija.</summary>
    public string? Goal { get; set; }

    public string? Homework { get; set; }
}

/// <summary>"O'tildi" belgisi — bitta mavzu, bitta sinf yoki guruh.</summary>
public class CurriculumProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TopicId { get; set; }

    /// <summary><see cref="LessonOwnerKind"/>: <c>class</c> | <c>group</c>.</summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;

    /// <summary>classes.id yoki study_groups.id (matn ko'rinishida).</summary>
    public string OwnerId { get; set; } = string.Empty;

    public DateOnly PassedOn { get; set; }

    /// <summary>Kim belgilagan (users.id).</summary>
    public string MarkedBy { get; set; } = string.Empty;

    public DateTimeOffset MarkedAt { get; set; }
}
