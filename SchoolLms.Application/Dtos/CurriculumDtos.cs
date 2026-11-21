namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Ish reja — docs/modules/curriculum-monitoring.md §1. Dates on the wire are
//  "yyyy-MM-dd" text, like the rest of the project.
// ===========================================================================

public record CurriculumPlanListItemDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string? GroupName,
    int TopicCount, int TotalHours, string? FileUrl, string? FileName, DateTimeOffset UpdatedAt,
    List<string> Owners, decimal AveragePercent, string? TeacherId = null, string? TeacherName = null);

/// <param name="Expected">Bugungacha o'tilishi kerak bo'lgan mavzular; null — reja sanalari yo'q.</param>
/// <param name="Delta">O'tilgan − kutilgan: musbat — oldinda, manfiy — orqada (hisobot bilan bir xil).</param>
public record CurriculumOwnerDto(
    string Kind, string Id, string Name,
    int? Expected = null, int? Delta = null, string? ForecastFinish = null, bool LateWarning = false);

public record CurriculumMarkDto(string OwnerKind, string OwnerId, string PassedOn);

/// <param name="PlannedOn">When the plan expects it: the exact date from the file (or set by hand), else
/// estimated from quarter/week; null when neither is available.</param>
/// <param name="PlannedExact">PlannedOn is an exact lesson date, not an estimate.</param>
public record CurriculumTopicDto(
    Guid Id, int Order, int? Quarter, int? Week, string? Section, string Title, int Hours, string? Goal, string? Homework,
    string? PlannedOn, bool PlannedExact, List<CurriculumMarkDto> Marks);

/// <summary>One topic's exact date, set by hand; null clears it (back to the quarter/week estimate).</summary>
public record CurriculumTopicDateRequest(string? PlannedOn);

public record CurriculumPlanDetailDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId,
    string? FileUrl, string? FileName, DateTimeOffset UpdatedAt,
    List<CurriculumOwnerDto> Owners, List<CurriculumTopicDto> Topics, string? TeacherId = null, string? TeacherName = null);

public record CurriculumMarkRequest(Guid TopicId, string OwnerKind, string OwnerId, bool Passed, string? PassedOn = null);

/// <summary>
/// Import result: the parsed topics (dry run) or the saved plan id; row errors block saving,
/// warnings (a lesson on a holiday or a Sunday) do not.
/// </summary>
public record CurriculumImportResultDto(
    Guid? PlanId, int TopicCount, int TotalHours, List<string> Errors, List<CurriculumPreviewTopicDto> Preview, List<string> Warnings,
    List<CurriculumFitDto>? Fit = null);

public record CurriculumPreviewTopicDto(int Order, int? Quarter, int? Week, string? Section, string Title, int Hours, string? PlannedOn);

/// <param name="Expected">Topics the plan expects passed by today; null without plan dates.</param>
/// <param name="Delta">Passed − expected: positive = ahead, negative = behind (same as the report).</param>
/// <param name="ForecastFinish">Finish date at the current pace; null — nothing passed yet.</param>
/// <param name="LateWarning">The forecast is past the last quarter's end.</param>
public record TeacherCurriculumDto(
    Guid PlanId, string Title, string SubjectName, string OwnerKind, string OwnerId, string OwnerName,
    int TopicCount, int PassedCount, Guid? NextTopicId, string? NextTopicTitle,
    int? Expected, int? Delta, string? ForecastFinish, bool LateWarning);

public record CurriculumQuarterDto(int Quarter, int Planned, int Passed);

/// <param name="Expected">Topics the plan expects passed by today; null without quarter dates.</param>
/// <param name="Delta">Passed − expected: positive = ahead, negative = behind.</param>
/// <param name="ForecastFinish">Finish date at the current pace (hours per week); null — nothing passed yet.</param>
/// <param name="LateWarning">The forecast is past the last quarter's end.</param>
public record CurriculumReportRowDto(
    Guid PlanId, string PlanTitle, string SubjectName, string OwnerKind, string OwnerId, string OwnerName,
    int TopicCount, int PassedCount, int TotalHours, int PassedHours, decimal Percent,
    int? Expected, int? Delta, string? ForecastFinish, bool LateWarning, List<CurriculumQuarterDto> Quarters);

public record CurriculumMissingDto(string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string? GroupName, List<string> Owners);

public record CurriculumReportDto(
    string Today, string? YearEnd, bool QuartersSet, List<CurriculumReportRowDto> Rows, List<CurriculumMissingDto> Missing);

// ---------------------------------------------------------------------------
//  Rejaning jadvalga mosligi (mijoz, 2026-11-17): rejadagi soatlar ↔ jadvaldagi darslar.
// ---------------------------------------------------------------------------

/// <param name="PlanHours">Rejada shu chorakka tushgan soatlar.</param>
/// <param name="Lessons">Jadval bo'yicha shu chorakdagi darslar (yakshanba va bayramlarsiz).</param>
/// <param name="EstimatedWeeks">Jadval biriktirilmagan haftalar — ular asosiy jadvaldan taxmin qilindi.</param>
public record CurriculumFitQuarterDto(int Quarter, int PlanHours, int Lessons, int EstimatedWeeks);

/// <summary>Aniq reja sanasi, lekin o'sha kuni dars yo'q (bayram, yakshanba yoki jadvalda dars yo'q).</summary>
public record CurriculumFitDateDto(int Order, string Title, string Date, string Reason);

/// <summary>Bitta ega (sinf/guruh) bo'yicha reja ↔ jadval. Ustoz rejasida faqat shu ustozning darslari sanaladi.</summary>
/// <param name="WeeklyLessons">Asosiy jadvalda haftasiga necha dars.</param>
/// <param name="UnplacedHours">Hech bir chorakka tushmagan mavzular soati (choraklar kiritilmagan bo'lsa — hammasi).</param>
public record CurriculumFitDto(
    string OwnerKind, string OwnerId, string OwnerName, int WeeklyLessons,
    int PlanHours, int Lessons, int UnplacedHours,
    List<CurriculumFitQuarterDto> Quarters, List<CurriculumFitDateDto> DateIssues);

/// <summary>Yuklash formasidagi ustoz tanlovi: ustoz jadvalda dars beradigan (fan, daraja/yo'nalish) juftliklari.</summary>
public record CurriculumTeacherOptionDto(string Id, string Name, List<CurriculumTeacherKeyDto> Keys);

public record CurriculumTeacherKeyDto(string SubjectId, int? Grade, Guid? GroupId);

// ---------------------------------------------------------------------------
//  Ustoz rejasini o'zi tahrirlaydi (mijoz, 2026-11-18): mavzu qo'shish/o'zgartirish/o'chirish va
//  "dars bo'lmadi" — keyingi mavzularni keyingi darslarga surish.
// ---------------------------------------------------------------------------

/// <summary>
/// Mavzuni saqlash. <paramref name="PlannedOn"/> ("yyyy-MM-dd") majburiy: chorak va hafta undan
/// hisoblanadi (choraklar kiritilmagan bo'lsa — <paramref name="Quarter"/>/<paramref name="Week"/> majburiy).
/// <paramref name="AfterOrder"/> — faqat yangi mavzu: shu raqamdan keyin qo'yiladi (0 — boshiga; null — oxiriga).
/// </summary>
public record CurriculumTopicSaveRequest(
    string Title, string? Section, string? PlannedOn, int? Quarter, int? Week, int Hours,
    string? Goal, string? Homework, int? AfterOrder = null);

/// <summary>"Dars bo'lmadi": shu mavzudan boshlab hammasi bir darsga suriladi.</summary>
public record CurriculumShiftRequest(Guid FromTopicId);

/// <summary>Surish natijasi: nechta mavzu surildi va oxirgisining yangi sanasi.</summary>
public record CurriculumShiftResultDto(int Moved, string LastDate);

/// <summary>Ustozning yuklash formasi: u jadvalda dars beradigan (fan, daraja/yo'nalish) juftliklari.</summary>
/// <param name="PlanId">Shu juftlik bo'yicha ustozning o'z rejasi bor bo'lsa — qayta yuklash.</param>
public record TeacherCurriculumKeyDto(string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string Label, Guid? PlanId);

/// <summary>Jadval muharriri uchun: ega × fan bo'yicha chorakdagi reja soati va haftasiga kerakli dars.</summary>
public record CurriculumNormDto(string OwnerKind, string OwnerId, string SubjectId, int PlanHours, double PerWeek, string PlanTitle);

/// <param name="Weeks">Chorakdagi o'quv haftalari (o'quv kunlari / 6).</param>
public record CurriculumNormsDto(int Quarter, double Weeks, List<CurriculumNormDto> Norms);
