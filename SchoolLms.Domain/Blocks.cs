namespace SchoolLms.Domain;

// ===========================================================================
//  Bloklar — birlashgan va daraja guruhlari (docs/TZ_Bloklar_Daraja_guruhlari.docx,
//  docs/modules/blocks.md).
// ===========================================================================
//
//  MUAMMO
//  ------
//  Ingliz tili va matematikada bir nechta sinf (yoki yo'nalish) bir soatda
//  birlashadi va ikkita guruhga bo'linadi: "Katta" / "Kichik" (daraja bo'yicha)
//  yoki "Iqt-1 guruhi" / "Iqt-2 guruhi" (manba sinf bo'yicha). Har guruhning
//  o'z ustozi, davomati va bahosi bor. Bugungi `students.sub_group` bitta sinf
//  ichidagi, barcha fanlar uchun UMUMIY bo'linish — bunga yetmaydi.
//
//  MODEL
//  -----
//  `blocks`               — blok: fan, o'quv yili, turi, kun naqshi.
//  `block_classes`        — blokka kiruvchi manba sinflar YOKI yo'nalishlar.
//  `block_groups`         — blok ichidagi aniq 2 ta guruh. Har biri bitta oddiy
//                           `study_groups` qatoriga bog'lanadi: jurnal, davomat,
//                           dars ro'yxati va konflikt tekshiruvi o'sha mavjud
//                           "guruh darsi" mexanizmidan foydalanadi (mijoz roziligi,
//                           2026-11-18).
//  `block_group_teachers` — guruh ustozi, SANALI (almashtirilsa eski baholar eski
//                           ustozda qoladi).
//  A'zolik — mavjud `study_group_members` (joined_on / left_on = TZ'dagi
//  valid_from / valid_to), unga `source_class_id` va `join_reason` qo'shildi.
//
//  Davomat va baho (`journal_entries`), mavzu (`lesson_notes`), chorak bahosi,
//  intizomiy ball va oraliq test natijalari `block_group_id` va
//  `source_class_id` ni oladi — ikkalasi ham NULLABLE, oddiy darslarda bo'sh.
//
//  `source_class_id` — sinf id'si YOKI yo'nalish guruhi id'si (Filologiya,
//  Iqtisodiyot-1 ... sinf emas, yo'nalish). Ikkalasi ham uuid matn — to'qnashmaydi.
// ===========================================================================

/// <summary>Blok turi (<c>blocks.block_type</c>).</summary>
public static class BlockType
{
    /// <summary>Daraja bo'yicha: Katta / Kichik. Hamma o'quvchini admin taqsimlaydi.</summary>
    public const string Level = "level";

    /// <summary>
    /// Manba sinf bo'yicha (Aniq+Iqt matematika): har guruh bitta manba sinfga
    /// bog'langan (<see cref="BlockGroup.BaseOwnerId"/>), o'sha sinf o'quvchilari
    /// avtomatik shu guruhga tushadi; qolgan sinf o'quvchilarini admin taqsimlaydi.
    /// </summary>
    public const string BaseClass = "base_class";

    public static readonly string[] All = [Level, BaseClass];
}

/// <summary>Blok qaysi kunlarda ishlaydi (<c>blocks.day_pattern</c>).</summary>
public static class BlockDayPattern
{
    /// <summary>Toq kunlar: Dushanba, Chorshanba, Juma (ingliz tili).</summary>
    public const string Odd = "odd";

    /// <summary>Juft kunlar: Seshanba, Payshanba, Shanba (matematika).</summary>
    public const string Even = "even";

    /// <summary>Cheklovsiz — istisno holatlar uchun admin ataylab tanlaydi.</summary>
    public const string Custom = "custom";

    public static readonly string[] All = [Odd, Even, Custom];

    /// <summary>
    /// Shu naqshdagi blok <paramref name="day"/> kuniga (0 = Dushanba ... 5 = Shanba,
    /// <see cref="ScheduleLesson.Day"/>) qo'yila oladimi. Toq kunlar — 0, 2, 4.
    /// </summary>
    public static bool Allows(string pattern, int day) => pattern switch
    {
        Odd => day is 0 or 2 or 4,
        Even => day is 1 or 3 or 5,
        _ => true,
    };
}

/// <summary>Blok guruhi kodi (<c>block_groups.code</c>).</summary>
public static class BlockGroupCode
{
    public const string Katta = "KATTA";
    public const string Kichik = "KICHIK";

    /// <summary>Manba sinf bo'yicha blokdagi guruh ("Iqt-1 guruhi").</summary>
    public const string Custom = "custom";

    public static readonly string[] All = [Katta, Kichik, Custom];
}

/// <summary>
/// Blok — bitta fan bo'yicha bir nechta manba sinfni bir vaqtda birlashtiradi
/// va aniq 2 ta guruhga bo'ladi.
/// </summary>
public class Block
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rinadigan nom: "Fil+Ijt Ingliz".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>subjects.id — blok fani.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>
    /// O'quv yili, <c>"2026-2027"</c> ko'rinishida. Loyihada o'quv yili jadvali
    /// yo'q (TZ'dagi <c>academic_year_id</c> o'rniga).
    /// </summary>
    public string AcademicYear { get; set; } = string.Empty;

    /// <summary><see cref="BlockType"/> qiymati.</summary>
    public string BlockType { get; set; } = Domain.BlockType.Level;

    /// <summary><see cref="BlockDayPattern"/> qiymati.</summary>
    public string DayPattern { get; set; } = BlockDayPattern.Custom;

    /// <summary>
    /// Blok faolmi. Yangi blok FAOL EMAS — admin o'quvchilarni taqsimlab bo'lgach
    /// faollashtiradi (TZ 7-bo'lim, 2-qoida shu paytda tekshiriladi).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Kim yaratgan (users.id).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Blokka kiruvchi manba sinf yoki yo'nalish guruhi.</summary>
public class BlockClass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlockId { get; set; }

    /// <summary><see cref="LessonOwnerKind"/>: <c>class</c> (classes.id) yoki
    /// <c>group</c> (yo'nalish — study_groups.id).</summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;

    /// <summary>Sinf yoki yo'nalish id'si (matn). Ikki jadvalga qaragani uchun FK yo'q —
    /// butunlik xizmatda tekshiriladi.</summary>
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>
/// Blok ichidagi guruh. Har blokda aniq 2 ta (<see cref="SortOrder"/> 1 va 2 —
/// baza bundan ortig'ini qabul qilmaydi).
/// </summary>
public class BlockGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlockId { get; set; }

    /// <summary>
    /// Shu guruhning darslari, jurnali va a'zoligi yuradigan oddiy o'quv guruhi
    /// (study_groups.id, fani — blok fani). Bitta o'quv guruhi bitta blok guruhiga.
    /// </summary>
    public Guid StudyGroupId { get; set; }

    /// <summary>Ko'rinadigan nom: "Katta", "Kichik", "Iqt-1 guruhi".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="BlockGroupCode"/> qiymati.</summary>
    public string Code { get; set; } = BlockGroupCode.Katta;

    /// <summary>Manba sinf bo'yicha blokda: guruhga avtomatik tushadigan sinf turi
    /// (<see cref="LessonOwnerKind"/>). Daraja blokida null.</summary>
    public string? BaseOwnerKind { get; set; }

    /// <summary>Manba sinf bo'yicha blokda: shu sinf/yo'nalish id'si. Daraja blokida null.</summary>
    public string? BaseOwnerId { get; set; }

    /// <summary>Xona (rooms.id). Ixtiyoriy.</summary>
    public Guid? RoomId { get; set; }

    /// <summary>1 yoki 2 — guruh tartibi (1 = Katta).</summary>
    public short SortOrder { get; set; }
}

/// <summary>
/// Blok guruhining ustozi — SANALI. Ustoz almashtirilsa eski qator yopiladi
/// (<see cref="ValidTo"/>) va yangisi ochiladi: o'sha kungacha qo'yilgan baholar
/// eski ustozga tegishli bo'lib qoladi (TZ 6.1).
/// </summary>
public class BlockGroupTeacher
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlockGroupId { get; set; }

    /// <summary>teachers.id.</summary>
    public string TeacherId { get; set; } = string.Empty;

    public DateOnly ValidFrom { get; set; }

    /// <summary>null = hozirgi ustoz.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Kim biriktirgan (users.id).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
