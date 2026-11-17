namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Arxivlash sabablari katalogi va ommaviy arxivlash — §2.2.
//
//  IKKI USTUN, IKKI VAZIFA. `students.archive_reason` (erkin matn) QOLADI va
//  MAJBURIY: katalog qatori "nega ketishdi" ni GURUHLAYDI, matn esa TAFSILOT
//  yozadi. Shuning uchun quyidagi so'rovlarning hammasida `Reason` (matn) bor
//  va u bo'sh bo'lishi mumkin emas, `ArchiveReasonId` esa ixtiyoriy.
// ===========================================================================

/// <summary>Katalogdagi bitta arxivlash sababi. <c>GroupId</c> — qaysi guruhda (null = guruhsiz).</summary>
public record StudentArchiveReasonDto(Guid Id, string Name, bool IsActive, int Position, int UsedBy, Guid? GroupId = null);

/// <summary>
/// Katalog qatorini yaratish/tahrirlash. <c>IsActive</c> va <c>Position</c> berilmasa o'zgarmaydi;
/// <c>GroupId</c> esa HAR DOIM yoziladi (null = guruhdan chiqarish) — sozlamalar oynasi uni doim yuboradi.
/// </summary>
public record SaveArchiveReasonRequest(string Name, bool? IsActive = null, int? Position = null, Guid? GroupId = null);

/// <summary>Sabab guruhi — arxiv doskasidagi bitta ustun.</summary>
/// <param name="ReasonCount">Guruhdagi sabablar soni (o'chirish faqat 0 bo'lsa).</param>
/// <param name="StudentCount">Hozir arxivda turgan, shu guruh sababi bilan ketgan o'quvchilar.</param>
public record StudentArchiveReasonGroupDto(
    Guid Id, string Name, string Color, int Position, bool IsActive, int ReasonCount, int StudentCount);

/// <summary>Guruhni yaratish/tahrirlash. Berilmagan maydon o'zgarmaydi (yaratishda — sukut).</summary>
public record SaveArchiveReasonGroupRequest(string Name, string? Color = null, int? Position = null, bool? IsActive = null);

/// <summary>Arxiv doskasidagi bitta o'quvchi kartochkasi.</summary>
/// <param name="ClassName">Ketgan paytdagi sinfi (arxivlash sinfni bo'shatadi — ketish yozuvidan olinadi).</param>
/// <param name="LeadId">"Lidlarga qaytarish" bilan yaratilgan, hali ochiq lid (bo'lsa).</param>
public record ArchiveBoardStudentDto(
    string Id, string FullName, string? ClassName, string? ArchivedAt,
    Guid? ReasonId, string? ReasonName, string? Note, string? ParentPhone, string? LeadId);

/// <summary>Doskaning bitta ustuni. <c>Id = null</c> — tizim ustuni "Sababi aniqlanmagan".</summary>
public record ArchiveBoardColumnDto(Guid? Id, string Name, string Color, IReadOnlyList<ArchiveBoardStudentDto> Students);

public record ArchiveBoardDto(IReadOnlyList<ArchiveBoardColumnDto> Columns, int Total);

/// <summary>Arxivdagi o'quvchining ketish sababini o'zgartirish (doskada sudrab o'tkazish).</summary>
/// <param name="Note">Berilsa izoh ham almashadi; null — izoh o'zgarmaydi.</param>
public record SetArchiveReasonRequest(Guid ReasonId, string? Note = null);

/// <summary>Arxivdagi o'quvchini lidlarga qaytarish.</summary>
/// <param name="Stage">Qaysi ustunga (lid bosqichi id'si); null — birinchi ustun.</param>
/// <param name="Note">Lidga qo'shimcha izoh (ixtiyoriy).</param>
public record ArchiveToLeadRequest(string? Stage = null, string? Note = null);

/// <summary>
/// Bir nechta o'quvchini bitta sabab bilan arxivlash (§2.2 — bitiruv tabiatan
/// ommaviy amal).
/// </summary>
/// <param name="StudentIds">Arxivlanadigan o'quvchilar.</param>
/// <param name="Reason">Erkin matn — MAJBURIY (katalog uni almashtirmaydi).</param>
/// <param name="ArchiveReasonId">Katalog qatori — MAJBURIY (2026-11-13; null bo'lsa 400).</param>
/// <param name="Force">
/// Qarzdorlik to'sig'ini chetlab o'tish. FAQAT superadmin uchun (§9 Q4); boshqa
/// rolda bu bayroq e'tiborga olinmaydi.
/// </param>
public record BulkArchiveRequest(
    IReadOnlyList<string> StudentIds,
    string Reason,
    Guid? ArchiveReasonId = null,
    bool Force = false);

/// <summary>Qarzi borligi uchun arxivlanmagan bitta o'quvchi.</summary>
public record ArchiveBlockedStudentDto(string StudentId, string FullName, string ClassName, decimal Debt);

/// <summary>
/// Ommaviy arxivlash natijasi. Qarzdorlar SAKRAB O'TILMAYDI — butun amal rad
/// etiladi (<c>Archived = 0</c>) va ro'yxat qaytadi: "o'n besh bolaning uchtasi
/// qoldi" degan yarim natija administrator uchun eng yomon holat.
/// </summary>
public record BulkArchiveResultDto(
    int Archived,
    IReadOnlyList<ArchiveBlockedStudentDto> Blocked,
    bool CanOverride,
    string? Message = null);

/// <summary>
/// Archiving a class (student-flow.md, 2026-10-15) — optional body: the pupils' leave reason from
/// the catalog and a note. Both empty — the note is "Sinf arxivlandi (…)" as before.
/// </summary>
public record ArchiveClassRequest(Guid? ArchiveReasonId = null, string? Reason = null);
