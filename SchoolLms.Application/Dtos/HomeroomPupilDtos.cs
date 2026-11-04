namespace SchoolLms.Application.Dtos;

// "Sinfim" — the homeroom teacher reads the marks and points other teachers gave their pupils
// (client, 2026-11-04). Read-only. Rules: HomeroomPupilProgress.

/// <summary>One pupil's behaviour score, for the class list.</summary>
public record HomeroomPupilScoreDto(string StudentId, int Score);

/// <summary>One subject's result in the chosen quarter (official quarter grade over the daily average).</summary>
public record HomeroomSubjectAverageDto(string SubjectId, string SubjectName, double? Average);

/// <summary>One journal mark. <c>TeacherName</c> comes from the timetable: the journal keeps no author.</summary>
public record HomeroomGradeDto(string Date, int Period, string SubjectName, int Grade, string Topic, string TeacherName);

/// <summary>One "Mavsumiy baholash" mark with the name of whoever entered it.</summary>
public record HomeroomSeasonalMarkDto(
    string SubjectName, string PeriodKind, int Year, int? Month, int? Quarter,
    decimal? Score, string? Comment, string AuthorName);

/// <summary>Everything the homeroom teacher sees on one pupil's page.</summary>
public record HomeroomPupilProgressDto(
    string StudentId, string FullName, string Group, int Quarter, IReadOnlyList<int> Quarters,
    IReadOnlyList<HomeroomSubjectAverageDto> Subjects, IReadOnlyList<HomeroomGradeDto> Grades,
    IReadOnlyList<HomeroomSeasonalMarkDto> SeasonalMarks, StudentDisciplineDto Discipline);
