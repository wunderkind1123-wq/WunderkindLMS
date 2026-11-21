namespace SchoolLms.Domain;

// ===========================================================================
//  Yuklama va avtomatik jadval tuzish (mijoz, 2026-11-21: "dars jadvali bilan bog'liq hamma
//  narsani boshqarish" — 7-band).
// ===========================================================================
//
//  `teaching_loads`          — har ega (sinf yoki guruh) × fan: haftasiga necha dars, kim o'tadi,
//                              qaysi xonada (maxsus xona: sport zali, kompyuter xonasi). Yo'nalish
//                              guruhining katta/kichik bo'linishi — SubGroup 1 va 2 ga alohida qator
//                              (har biriga o'z ustozi); generator ularni BIR soatga qo'yadi.
//  `teacher_unavailability`  — ustoz ishlay olmaydigan kun (Period = 0) yoki aniq soat.
//
//  Ikkalasi faqat jadval TUZISH uchun manba: jurnal, davomat, maosh ularni o'qimaydi — ular
//  doim shablonlardagi darslarga qaraydi. Generator natijasi oddiy QORALAMA shablon bo'ladi.
// ===========================================================================

/// <summary>Ega × fan bo'yicha haftalik yuklama — avtomatik jadval tuzishning manbai.</summary>
public class TeachingLoad
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="LessonOwnerKind"/>: <c>class</c> | <c>group</c>.</summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;

    /// <summary>Sinf id'si yoki o'quv guruhi id'si (schedule_templates.class_id bilan bir xil).</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;

    /// <summary>0 = butun sinf; 1/2 = yo'nalish guruhining katta/kichik yarmi.</summary>
    public int SubGroup { get; set; }

    public string TeacherId { get; set; } = string.Empty;

    /// <summary>Haftasiga dars soni (1–20).</summary>
    public int HoursPerWeek { get; set; }

    /// <summary>Maxsus xona (rooms.id). null — sinfning o'z xonasi.</summary>
    public Guid? RoomId { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Eganing haftalik o'quv kunlari (mijoz, 2026-11-21: "1–3-sinflar 5 kun, 4–8-sinflar va yo'nalishlar
/// 6 kun o'qiydi"). Qator yo'q bo'lsa — sukut: 1–3-sinf 5 kun, qolganlari 6 kun
/// (<c>TeachingLoadService.DefaultDays</c>).
/// </summary>
public class OwnerStudyDays
{
    /// <summary>Sinf yoki guruh id'si.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string OwnerKind { get; set; } = LessonOwnerKind.Class;

    /// <summary>5 — dushanba–juma, 6 — dushanba–shanba.</summary>
    public int Days { get; set; } = 6;
}

/// <summary>Ustoz ishlay olmaydigan vaqt: butun kun (<see cref="Period"/> = 0) yoki bitta soat.</summary>
public class TeacherUnavailability
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TeacherId { get; set; } = string.Empty;

    /// <summary>0 = Dushanba ... 5 = Shanba.</summary>
    public int Day { get; set; }

    /// <summary>0 = butun kun; 1..10 — aniq dars soati.</summary>
    public int Period { get; set; }
}
