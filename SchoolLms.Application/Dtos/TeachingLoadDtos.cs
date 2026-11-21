namespace SchoolLms.Application.Dtos;

// Yuklama, ustoz bandligi va avtomatik jadval tuzish (2026-11-21). Sxema: SchoolLms.Domain/TeachingLoads.cs.

/// <summary>Bitta yuklama qatori: ega × fan (× yarmi) — haftasiga necha dars, ustoz, xona.</summary>
public record TeachingLoadDto(
    Guid? Id, string OwnerKind, string OwnerId, string SubjectId, int SubGroup,
    string TeacherId, int HoursPerWeek, Guid? RoomId);

/// <summary>Bitta eganing yuklamasini to'liq almashtirish.</summary>
public record SaveOwnerLoadsRequest(List<TeachingLoadDto> Rows);

/// <summary>Jadvaldan yuklamani to'ldirish. <paramref name="TemplateName"/> null — har eganing joriy jadvali.</summary>
/// <param name="Overwrite">false — faqat yuklamasi yo'q egalar to'ldiriladi.</param>
public record FillLoadsRequest(string? TemplateName, bool Overwrite);

public record FillLoadsResultDto(int Owners, int Rows, int Skipped);

/// <summary>Ustozning ishlay olmaydigan vaqti; <paramref name="Period"/> 0 — butun kun.</summary>
public record UnavailabilityDto(int Day, int Period);

public record TeacherUnavailabilityDto(string TeacherId, List<UnavailabilityDto> Slots);

public record SaveUnavailabilityRequest(List<UnavailabilityDto> Slots);

/// <summary>
/// Avtomatik jadval: yuklamadan yangi QORALAMA jadval. Qoidalar ro'yxati kengayadi — yangi qoida
/// shu yerga yangi maydon (sukut bo'yicha bugungi xatti-harakat) bo'lib qo'shiladi.
/// </summary>
/// <param name="OwnerIds">null — yuklamasi bor hamma ega.</param>
public record GenerateScheduleRequest(
    string Name, bool NoGaps = true, bool RespectUnavailability = true,
    List<string>? OwnerIds = null);

public record GenerateUnplacedDto(string OwnerId, string OwnerName, string SubjectName, string TeacherNames, int Missing);

public record GenerateResultDto(
    string Name, int Owners, int Placed, int Total, int OwnerGaps, int TeacherGaps, int SubjectRepeats,
    List<GenerateUnplacedDto> Unplaced, List<string> Warnings);

/// <summary>Jadval muharriri uchun xonalar: faol xonalar va har eganing o'z xonasi.</summary>
public record ScheduleRoomDto(Guid Id, string Name, string Kind, int Capacity);

public record ScheduleRoomsDto(List<ScheduleRoomDto> Rooms, Dictionary<string, Guid> HomeRooms);

/// <summary>Eganing haftalik o'quv kunlari (5 yoki 6); <paramref name="IsDefault"/> — saqlanmagan, sukut qoidasi.</summary>
public record OwnerStudyDaysDto(string OwnerId, int Days, bool IsDefault);

public record SaveStudyDaysRequest(int Days);
