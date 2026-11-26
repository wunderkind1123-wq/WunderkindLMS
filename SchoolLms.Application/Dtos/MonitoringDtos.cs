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
    Guid? TopicId, string? TopicTitle, decimal Weight, List<string>? OptionImages = null);

/// <param name="OptionImages">Har variantga rasm manzili yoki "" (variant matni rasm bo'lsa bo'sh bo'lishi mumkin).</param>
public record MonitoringQuestionPayload(
    string Text, string? ImageUrl, List<string> Options, int CorrectIndex, Guid? TopicId, decimal? Weight,
    List<string>? OptionImages = null);

public record MonitoringTopicRefDto(Guid Id, int Order, int? Quarter, string Title);

public record MonitoringBankDetailDto(MonitoringBankDto Bank, List<MonitoringQuestionDto> Questions, List<MonitoringTopicRefDto> Topics);

public record MonitoringImportResultDto(int Added, List<string> Errors);

/// <summary>Word / matn importining oldindan ko'rinishi — hali saqlanmagan savollar va tushib qolganlar sababi.</summary>
public record MonitoringImportPreviewDto(List<MonitoringQuestionPayload> Questions, List<string> Problems);

/// <summary>Pasted text for the import preview (a file comes as multipart instead).</summary>
public record MonitoringImportTextRequest(string? Text);

// ---------------------------------------------------------------- tests

public record MonitoringOwnerRef(string Kind, string Id);

/// <param name="QuestionIds">From the bank (snapshot, in this order). Empty → <paramref name="Key"/>.</param>
/// <param name="Key">Paper test without a bank: correct letters, one per question ("ABDC…").</param>
/// <param name="OptionCount">Paper key: options per question (2–6, default 4).</param>
/// <param name="SeasonalYear">Oylik test: natija shu yil/oyning Mavsumiy bahosi bo'ladi. Oy bilan birga yoki hech biri.</param>
public record MonitoringTestPayload(
    string Title, string SubjectId, int? Grade, Guid? GroupId,
    string Delivery, string Scoring, string? NationalMethod, string TestDate,
    DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int? TimeLimitMin,
    List<MonitoringOwnerRef> Owners, List<Guid>? QuestionIds, string? Key, int? OptionCount,
    int? SeasonalYear = null, int? SeasonalMonth = null, bool Shuffle = false, int? MaxLeaves = null);

public record MonitoringTestListItemDto(
    Guid Id, string Title, string SubjectName, int? Grade, string? GroupName, string Delivery, string Scoring,
    string? NationalMethod, string TestDate, string Status, int ItemCount, int ParticipantCount, int DoneCount,
    decimal? AveragePercent, decimal? AveragePoints, List<string> Owners,
    int? SeasonalYear = null, int? SeasonalMonth = null);

public record MonitoringItemDto(
    Guid Id, int Order, string? Text, string? ImageUrl, List<string> Options, int OptionCount, int CorrectIndex,
    Guid? TopicId, string? TopicTitle, decimal Weight, List<string>? OptionImages = null);

/// <summary>Paper key edit — one row per item.</summary>
public record MonitoringItemKeyPayload(int Order, int CorrectIndex, int? OptionCount, Guid? TopicId, decimal? Weight);

public record MonitoringParticipantDto(
    Guid Id, string StudentId, string StudentName, string OwnerKind, string OwnerId, string OwnerName,
    string Status, string? Answers, int Correct, decimal Percent, decimal? Points, string? Level,
    string? AccessCode, bool DeviceBound, string? StartedAt, string? FinishedAt,
    int Leaves = 0, List<string>? Events = null, string? FinishReason = null);

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
    Dictionary<string, int> Levels, bool SmallSampleWarning, List<MonitoringTopicRefDto>? PlanTopics = null,
    int? SeasonalYear = null, int? SeasonalMonth = null, bool Shuffle = false, int? MaxLeaves = null,
    string? RoomPin = null);

// ---------------------------------------------------------------- public (online, by code)

/// <param name="TestId">Sessiya kodida — qaysi fan testi (bo'lmasa navbatdagisi). Bitta test kodida e'tiborsiz.</param>
/// <param name="Pin">Doimiy kod bilan kirganda — doskadagi xona PIN'i (bir martalik kodda kerak emas).</param>
public record MonitoringStartRequest(string Code, string Device, Guid? TestId = null, string? Pin = null);

public record MonitoringAnswerRequest(string Code, string Device, int Order, string? Choice, Guid? TestId = null, string? Pin = null);

/// <summary>
/// O'quvchiga ko'rinadigan savol. Aralashtirilgan testda variantlar o'quvchining o'z tartibida:
/// <paramref name="Keys"/>[k] — ekrandagi k-variantning asl harfi (javob shu harf bilan yuboriladi).
/// </summary>
public record MonitoringSessionItemDto(
    int Order, string? Text, string? ImageUrl, List<string> Options, int OptionCount,
    List<string>? OptionImages = null, List<string>? Keys = null);

public record MonitoringSessionDto(
    string StudentName, string Title, string SubjectName, DateTimeOffset Deadline, string Answers,
    List<MonitoringSessionItemDto> Items, bool Finished,
    int Leaves = 0, int? MaxLeaves = null, string? FinishReason = null,
    Guid? TestId = null, bool InSitting = false, DateTimeOffset? ServerNow = null,
    int? ResultCorrect = null, decimal? ResultPercent = null);

/// <summary>
/// Boshlashdan oldingi tasdiq: kod kimniki va qaysi test (o'quvchi "Bu men" deb boshlaydi).
/// Hech narsa o'zgarmaydi — qurilma bog'lanmaydi, vaqt boshlanmaydi.
/// </summary>
/// <param name="Steps">Sessiya kodida — fanlar tartibi va holati; <paramref name="AllDone"/> — hammasi topshirilgan.</param>
public record MonitoringPeekDto(
    string StudentName, string OwnerName, string Title, string SubjectName, int ItemCount, int TimeLimitMin,
    bool InProgress, int? MaxLeaves,
    Guid? TestId = null, string? SittingTitle = null, List<MonitoringSittingStepDto>? Steps = null, bool AllDone = false);

/// <param name="Status">done / current / waiting / absent.</param>
/// <param name="Percent">Topshirilgan fanning foizi (o'quvchiga ko'rsatiladi; savollar bo'yicha tafsilot — yo'q).</param>
public record MonitoringSittingStepDto(Guid TestId, string Title, string SubjectName, string Status, decimal? Percent = null);

// ---------------------------------------------------------------- live (jonli kuzatuv)

/// <param name="Status">pending / in_progress / between (fan tugadi, keyingisi boshlanmagan) / done / absent.</param>
/// <param name="Step">Hozirgi (yoki oxirgi) fan raqami, 1 dan.</param>
public record MonitoringLiveRowDto(
    string StudentId, string StudentName, string OwnerName, string Status, string? Subject, int Step, int Steps,
    int Answered, int Total, int Leaves, bool Stopped, DateTimeOffset? LastSeenAt, DateTimeOffset? Deadline);

public record MonitoringLiveDto(string Title, DateTimeOffset Now, List<string> Subjects, List<MonitoringLiveRowDto> Rows, string? RoomPin = null);

// ---------------------------------------------------------------- tayyorlik tekshiruvi

/// <param name="Level">error — test shu holda o'tkazilsa o'quvchilar qiynaladi; warning — tekshirib ko'ring; ok — hammasi joyida.</param>
public record MonitoringCheckDto(string Level, string Message);

// ---------------------------------------------------------------- sittings (o'quv bo'limi)

/// <param name="QuestionCount">Bankdan tasodifiy shuncha savol (hammaga bir xil to'plam); null — bankning hammasi.</param>
public record MonitoringQuickSubject(Guid BankId, int? QuestionCount, int TimeLimitMin);

/// <summary>Sessiyani bitta formada: har fan uchun bankdan test yaratiladi (bir xil sinflar, oyna, himoya), keyin sessiya.</summary>
public record MonitoringQuickSittingPayload(
    string Title, string TestDate, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, List<MonitoringOwnerRef> Owners,
    List<MonitoringQuickSubject> Subjects, bool Shuffle, int? MaxLeaves, int? SeasonalYear, int? SeasonalMonth, bool Publish);

public record MonitoringSittingPayload(string Title, string TestDate, List<Guid> TestIds);

public record MonitoringSittingTestDto(Guid Id, string Title, string SubjectName, string Status, int ItemCount, int? TimeLimitMin, int Participants, int Done);

public record MonitoringSittingListItemDto(Guid Id, string Title, string TestDate, List<MonitoringSittingTestDto> Tests, int Pupils, int Finished, string? RoomPin);

/// <param name="Steps">Har test uchun o'quvchi holati (<see cref="SchoolLms.Domain.MonitoringParticipantStatus"/>) yoki "" — bu fanda yo'q.</param>
public record MonitoringSittingRowDto(
    Guid CodeId, string StudentId, string StudentName, string OwnerName, string Code, bool DeviceBound, List<string> Steps, int Leaves);

public record MonitoringSittingDetailDto(Guid Id, string Title, string TestDate, List<MonitoringSittingTestDto> Tests, List<MonitoringSittingRowDto> Rows,
    string? RoomPin = null);

/// <summary>Halollik hodisasi (<see cref="SchoolLms.Domain.MonitoringEventKind"/>).</summary>
public record MonitoringEventRequest(string Code, string Device, string Kind, Guid? TestId = null, string? Pin = null);

/// <summary>Doimiy kodlar ro'yxati (o'quv bo'limi) — sinf bo'yicha, chop etish uchun.</summary>
public record MonitoringStudentCodeDto(string StudentId, string StudentName, string ClassName, string? Code);

public record MonitoringEventResultDto(int Leaves, int? MaxLeaves, bool Finished);
