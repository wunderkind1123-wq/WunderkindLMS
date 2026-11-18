namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Bloklar — docs/modules/blocks.md, TZ 6.1–6.2.
// ===========================================================================

/// <summary>Blokka kiruvchi manba: sinf (<c>class</c>) yoki yo'nalish guruhi (<c>group</c>).</summary>
public record BlockSourceRef(string Kind, string Id);

/// <summary>Blok guruhi (aniq 2 ta). <see cref="TeacherId"/> faqat yaratishda o'qiladi —
/// keyin ustoz sanali almashtirish amali bilan o'zgaradi.</summary>
public record SaveBlockGroupRequest(
    string Name,
    string? TeacherId = null,
    string? BaseOwnerKind = null,
    string? BaseOwnerId = null,
    Guid? RoomId = null);

/// <summary>
/// Blokni saqlash. Fan va blok turi yaratilgandan keyin O'ZGARMAYDI (o'zgartirish —
/// boshqa blok ochish). <see cref="AcademicYear"/> berilmasa — joriy o'quv yili.
/// </summary>
public record SaveBlockRequest(
    string Name,
    string SubjectId,
    string BlockType,
    string DayPattern,
    IReadOnlyList<BlockSourceRef> Sources,
    IReadOnlyList<SaveBlockGroupRequest> Groups,
    string? AcademicYear = null);

public record BlockSourceDto(string Kind, string Id, string Name, int StudentCount, int UnassignedCount);

public record BlockGroupDto(
    Guid Id, Guid StudyGroupId, string Name, string Code, short SortOrder,
    string? BaseOwnerKind, string? BaseOwnerId, Guid? RoomId,
    string? TeacherId, string? TeacherName, DateOnly? TeacherSince, int MemberCount);

public record BlockDto(
    Guid Id, string Name, string SubjectId, string SubjectName, string AcademicYear,
    string BlockType, string DayPattern, bool IsActive,
    IReadOnlyList<BlockSourceDto> Sources, IReadOnlyList<BlockGroupDto> Groups,
    int StudentCount, int UnassignedCount);

/// <summary>Taqsimlash ekranidagi bitta o'quvchi: "Ali V. · Filologiya" (TZ 6.2).</summary>
public record BlockMemberDto(
    string StudentId, string FullName, string SourceKind, string SourceId, string SourceName,
    Guid? GroupId, Guid? MembershipId, DateOnly? JoinedOn);

public record BlockRosterGroupDto(Guid GroupId, string Name, IReadOnlyList<BlockMemberDto> Members);

/// <summary>Ikki ustun (Katta / Kichik) va "Taqsimlanmagan" ro'yxati.</summary>
public record BlockRosterDto(
    DateOnly Date, IReadOnlyList<BlockRosterGroupDto> Groups, IReadOnlyList<BlockMemberDto> Unassigned);

/// <summary>Taqsimlanmagan o'quvchilarni guruhga qo'shish. Sana berilmasa — bugun.</summary>
public record BlockAssignRequest(IReadOnlyList<string> StudentIds, Guid GroupId, DateOnly? Date = null);

/// <summary>Yil o'rtasida ko'chirish: sana (standart: bugun) va ixtiyoriy sabab (TZ 6.2).</summary>
public record BlockMoveRequest(
    IReadOnlyList<string> StudentIds, Guid GroupId, DateOnly? Date = null, string? Reason = null);

/// <summary>Blokdan chiqarish: shu sanadan boshlab o'quvchi blok guruhida emas.</summary>
public record BlockUnassignRequest(IReadOnlyList<string> StudentIds, DateOnly? Date = null, string? Reason = null);

/// <summary>Ustozni almashtirish: shu sanadan boshlab yangi ustoz (TZ 6.1).</summary>
public record BlockTeacherChangeRequest(string TeacherId, DateOnly? Date = null);

/// <summary>O'quvchining shu sanadagi blok guruhi (TZ 5.3). Topilmasa — null qaytadi.</summary>
public record BlockStudentGroupDto(Guid BlockId, Guid GroupId, string GroupName, string? TeacherId, string? TeacherName);

/// <summary>O'quvchi profilidagi "Guruhlar tarixi" qatori (TZ 6.2).</summary>
public record BlockHistoryItemDto(
    Guid BlockId, string BlockName, string SubjectName, Guid GroupId, string GroupName,
    DateOnly JoinedOn, DateOnly? LeftOn, string? JoinReason, string? LeaveReason, string? MovedBy);

/// <summary>"Ko'chirishlar" hisobotining qatori: qaysi guruhdan qaysi guruhga, qachon, kim.</summary>
public record BlockMoveItemDto(
    string StudentId, string FullName, Guid BlockId, string BlockName,
    string FromGroup, string ToGroup, DateOnly Date, string? Reason, string? MovedBy);

/// <summary>Faollashtirish natijasi: bo'sh <see cref="Errors"/> — blok faollashdi.</summary>
public record BlockActivationDto(bool IsActive, IReadOnlyList<string> Errors);
