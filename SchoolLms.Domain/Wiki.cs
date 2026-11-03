namespace SchoolLms.Domain;

/* =====================================================================================
 *  BILIMLAR BAZASI (wiki) — xodimlarning SHAXSIY eslatmalari (insaytlar, ish rejasi, qaydlar).
 *  HR → «Bilimlar bazasi» (`/admin/bilimlar`): «Eslatmalarim» va (rahbariyatda) «Xodimlar».
 *  WunderkindLC CRM'idan ko'chirilgan modul (mijoz, 2026-11-03). Qoidalar: docs/modules/wiki.md.
 *
 *  ⚠️ FAQAT MATN: rasm/video/audio/fayl bloklari YO'Q (klient sxemasida ham, serverda ham —
 *  `WikiRules.ParseContent` boshqa blok turini rad etadi). Shuning uchun `/uploads` ga hech narsa tushmaydi.
 * ===================================================================================== */

/// <summary>
/// Bitta ESLATMA (sahifa). Egasining eslatmalari daraxt bo'lib turadi (<see cref="ParentId"/>).
/// Matn BlockNote JSON ko'rinishida (<see cref="ContentJson"/>), qidiruv uchun esa oddiy matn
/// nusxasi (<see cref="ContentText"/>) — ikkalasi har saqlashda BIRGA yoziladi.
/// </summary>
public class WikiPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>
    /// EGASI (`AppUser.Id`). Eslatmani egasi yozadi; rahbariyat ham (xodimga sahifa qo'shadi, tahrirlaydi, qulflaydi).
    /// Kim o'qiy olishi — `WikiViewer.CanSeeNotesOf` (o'zi · rahbariyat). Mijoz tanlovi (2026-11-03): menejerlar
    /// bo'ysunuvchilar eslatmalarini KO'RMAYDI — faqat rahbariyat.
    /// </summary>
    public string OwnerUserId { get; set; } = string.Empty;
    /// <summary>Ota eslatma (shu egada). "" = yuqori daraja.</summary>
    public string ParentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>Bitta emoji — daraxtda sarlavha oldida. Bo'sh bo'lishi mumkin.</summary>
    public string Icon { get; set; } = string.Empty;
    /// <summary>BlockNote bloklari (JSON massiv). "" = bo'sh sahifa.</summary>
    public string ContentJson { get; set; } = string.Empty;
    /// <summary>Bloklardan olingan oddiy matn — FAQAT qidiruv uchun (ekranda ko'rsatilmaydi).</summary>
    public string ContentText { get; set; } = string.Empty;
    /// <summary>Bir ota ichidagi tartib (kichigi tepada).</summary>
    public int Order { get; set; }

    /// <summary>
    /// BO'LIM — chap panelda «Mening eslatmalarim» ostidagi sarlavha (papka): matni yo'q, faqat ichida sahifalar turadi.
    /// Har doim YUQORI darajada (<see cref="ParentId"/> = ""). Qulflansa — ichidagi hamma sahifa egasiga faqat o'qish.
    /// </summary>
    public bool IsSection { get; set; }

    /// <summary>Har saqlashda oshadi. Klient eski versiya ustidan yozmoqchi bo'lsa — 409
    /// (ikki kishi bir sahifani bir vaqtda tahrirlasa, biri ikkinchisinikini jimgina o'chirmasin).</summary>
    public int Version { get; set; } = 1;

    /// <summary>QULF (faqat rahbariyat qo'yadi): shu eslatma VA ichidagilari EGASINING O'ZIGA ham faqat o'qish uchun.</summary>
    public bool Locked { get; set; }
    public string LockedBy { get; set; } = string.Empty;
    public string LockedAt { get; set; } = string.Empty;

    /// <summary>Vaqtlar — <see cref="AppClock.Iso"/> (Toshkent devor soati, "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}
