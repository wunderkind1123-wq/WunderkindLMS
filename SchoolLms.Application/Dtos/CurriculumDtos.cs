namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Ish reja — docs/modules/curriculum-monitoring.md §1. Dates on the wire are
//  "yyyy-MM-dd" text, like the rest of the project.
// ===========================================================================

public record CurriculumPlanListItemDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string? GroupName,
    int TopicCount, int TotalHours, string? FileUrl, string? FileName, DateTimeOffset UpdatedAt,
    List<string> Owners, decimal AveragePercent);

public record CurriculumOwnerDto(string Kind, string Id, string Name);

public record CurriculumMarkDto(string OwnerKind, string OwnerId, string PassedOn);

/// <param name="PlannedOn">When the plan expects it (from quarter/week); null when quarters are not set.</param>
public record CurriculumTopicDto(
    Guid Id, int Order, int? Quarter, int? Week, string? Section, string Title, int Hours, string? Goal, string? Homework,
    string? PlannedOn, List<CurriculumMarkDto> Marks);

public record CurriculumPlanDetailDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId,
    string? FileUrl, string? FileName, DateTimeOffset UpdatedAt,
    List<CurriculumOwnerDto> Owners, List<CurriculumTopicDto> Topics);

public record CurriculumMarkRequest(Guid TopicId, string OwnerKind, string OwnerId, bool Passed, string? PassedOn = null);

/// <summary>Import result: the parsed topics (dry run) or the saved plan id; row errors block saving.</summary>
public record CurriculumImportResultDto(Guid? PlanId, int TopicCount, int TotalHours, List<string> Errors, List<CurriculumPreviewTopicDto> Preview);

public record CurriculumPreviewTopicDto(int Order, int? Quarter, int? Week, string? Section, string Title, int Hours);

public record TeacherCurriculumDto(
    Guid PlanId, string Title, string SubjectName, string OwnerKind, string OwnerId, string OwnerName,
    int TopicCount, int PassedCount, Guid? NextTopicId, string? NextTopicTitle);

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
