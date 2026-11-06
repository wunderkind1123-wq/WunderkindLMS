namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Ichki monitoring — docs/modules/curriculum-monitoring.md §2.
// ===========================================================================

// ---------------------------------------------------------------- banks

public record MonitoringBankDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string? GroupName,
    int QuestionCount, Guid? PlanId);

public record MonitoringBankPayload(string Title, string SubjectId, int? Grade, Guid? GroupId);

public record MonitoringQuestionDto(
    Guid Id, int Order, string Text, string? ImageUrl, List<string> Options, int CorrectIndex,
    Guid? TopicId, string? TopicTitle, decimal Weight);

public record MonitoringQuestionPayload(
    string Text, string? ImageUrl, List<string> Options, int CorrectIndex, Guid? TopicId, decimal? Weight);

public record MonitoringTopicRefDto(Guid Id, int Order, int? Quarter, string Title);

public record MonitoringBankDetailDto(MonitoringBankDto Bank, List<MonitoringQuestionDto> Questions, List<MonitoringTopicRefDto> Topics);

public record MonitoringImportResultDto(int Added, List<string> Errors);

// ---------------------------------------------------------------- tests

public record MonitoringOwnerRef(string Kind, string Id);

/// <param name="QuestionIds">From the bank (snapshot, in this order). Empty → <paramref name="Key"/>.</param>
/// <param name="Key">Paper test without a bank: correct letters, one per question ("ABDC…").</param>
/// <param name="OptionCount">Paper key: options per question (2–6, default 4).</param>
public record MonitoringTestPayload(
    string Title, string SubjectId, int? Grade, Guid? GroupId,
    string Delivery, string Scoring, string? NationalMethod, string TestDate,
    DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int? TimeLimitMin,
    List<MonitoringOwnerRef> Owners, List<Guid>? QuestionIds, string? Key, int? OptionCount);

public record MonitoringTestListItemDto(
    Guid Id, string Title, string SubjectName, int? Grade, string? GroupName, string Delivery, string Scoring,
    string? NationalMethod, string TestDate, string Status, int ItemCount, int ParticipantCount, int DoneCount,
    decimal? AveragePercent, decimal? AveragePoints, List<string> Owners);

public record MonitoringItemDto(
    Guid Id, int Order, string? Text, string? ImageUrl, List<string> Options, int OptionCount, int CorrectIndex,
    Guid? TopicId, string? TopicTitle, decimal Weight);

/// <summary>Paper key edit — one row per item.</summary>
public record MonitoringItemKeyPayload(int Order, int CorrectIndex, int? OptionCount, Guid? TopicId, decimal? Weight);

public record MonitoringParticipantDto(
    Guid Id, string StudentId, string StudentName, string OwnerKind, string OwnerId, string OwnerName,
    string Status, string? Answers, int Correct, decimal Percent, decimal? Points, string? Level,
    string? AccessCode, bool DeviceBound, string? StartedAt, string? FinishedAt);

public record MonitoringAnswersPayload(Guid ParticipantId, string? Answers, bool Absent);

public record MonitoringTopicStatDto(Guid? TopicId, string Title, int ItemCount, decimal CorrectRate, List<MonitoringOwnerRateDto> ByOwner);

public record MonitoringOwnerRateDto(string OwnerId, string OwnerName, decimal CorrectRate);

public record MonitoringItemStatDto(int Order, string? TopicTitle, string Correct, decimal CorrectRate, Dictionary<string, int> Choices);

public record MonitoringOwnerSummaryDto(string OwnerId, string OwnerName, int Participants, int Done, decimal? AveragePercent, decimal? AveragePoints);

public record MonitoringTestDetailDto(
    Guid Id, string Title, string SubjectId, string SubjectName, int? Grade, Guid? GroupId, string? GroupName,
    string Delivery, string Scoring, string? NationalMethod, string TestDate,
    DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int? TimeLimitMin, string Status,
    List<MonitoringItemDto> Items, List<MonitoringParticipantDto> Participants,
    List<MonitoringOwnerSummaryDto> Owners, List<MonitoringTopicStatDto> Topics, List<MonitoringItemStatDto> ItemStats,
    Dictionary<string, int> Levels, bool SmallSampleWarning, List<MonitoringTopicRefDto>? PlanTopics = null);

// ---------------------------------------------------------------- public (online, by code)

public record MonitoringStartRequest(string Code, string Device);

public record MonitoringAnswerRequest(string Code, string Device, int Order, string? Choice);

public record MonitoringSessionItemDto(int Order, string? Text, string? ImageUrl, List<string> Options, int OptionCount);

public record MonitoringSessionDto(
    string StudentName, string Title, string SubjectName, DateTimeOffset Deadline, string Answers,
    List<MonitoringSessionItemDto> Items, bool Finished);
