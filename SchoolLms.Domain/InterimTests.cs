namespace SchoolLms.Domain;

// ===========================================================================
//  Oraliq test (client, 2026-11-10): track groups get no journal marks; instead the teacher gives a
//  short test at the end of each week — three in a month, and the fourth is the monthly block test.
//  The teacher sets the question count and types each pupil's correct answers; the result is
//  read as a percentage (correct / questions), so tests of any length weigh the same. It is the track
//  pupil's "mark", and the track leaderboard reads it.
//
//  Only track groups (StudyGroup.IsTrack) take it, one test per (group, subject, date).
// ===========================================================================

/// <summary>One interim test of one track group in one subject.</summary>
public class InterimTest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="StudyGroup.Id"/> of a track group; <c>on delete cascade</c>.</summary>
    public Guid GroupId { get; set; }

    /// <summary><see cref="Subject.Id"/>; <c>on delete restrict</c>.</summary>
    public string SubjectId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    /// <summary>How many questions the test had, 1–300 — set by the teacher.</summary>
    public int QuestionCount { get; set; }

    /// <summary>Optional topic ("Kvadrat tenglamalar").</summary>
    public string? Topic { get; set; }

    /// <summary>users.id of the author.</summary>
    public string? CreatedByUserId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One pupil's result on an <see cref="InterimTest"/>: correct answers, or absent.</summary>
public class InterimTestResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="InterimTest.Id"/>; <c>on delete cascade</c>.</summary>
    public Guid TestId { get; set; }

    /// <summary><see cref="Student.Id"/>; <c>on delete cascade</c>.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Correct answers, 0..QuestionCount; null when <see cref="Absent"/>.</summary>
    public int? Correct { get; set; }

    /// <summary>Did not sit the test — counts nowhere.</summary>
    public bool Absent { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
