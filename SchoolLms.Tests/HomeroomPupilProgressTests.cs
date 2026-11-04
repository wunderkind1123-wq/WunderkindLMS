using System.Globalization;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "Sinfim" (2026-11-04): the homeroom teacher reads the marks and points OTHER teachers gave their pupils,
/// with who gave them — and nobody else's pupils. Each test has its own database.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HomeroomPupilProgressTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // A fixed Wednesday, so the timetable cell below is the lesson of this mark.
    private const string Wednesday = "2026-10-14";
    private const string Thursday = "2026-10-15";
    private const string Friday = "2026-10-16";

    [Fact]
    public async Task Rahbar_boshqa_ustoz_qoygan_baho_va_ballni_kim_qoygani_bilan_koradi()
    {
        var (db, s) = await SetupAsync("homeroom_pupil_view");

        var page = await HomeroomPupilProgress.ForTeacherAsync(db, s.Homeroom.Id, s.Ali.Id, 1);

        Assert.NotNull(page);
        Assert.Equal("5-A", page.Group);
        Assert.Equal([1, 2], page.Quarters);

        // Newest first; the author comes from the timetable, a substitution wins over it.
        Assert.Equal(
            new[] { (Thursday, 5, "O'rinbosar Ustoz"), (Wednesday, 2, "Fan Ustoz") },
            page.Grades.Select(g => (g.Date, g.Grade, g.TeacherName)));
        Assert.Equal("Kasrlar", page.Grades[1].Topic);
        Assert.Equal(3.5, page.Subjects.Single(x => x.SubjectName == "Matematika").Average);

        // Point: the reason, the note and the teacher who gave it — and the score that adds up.
        var point = Assert.Single(page.Discipline.Items);
        Assert.Equal(("Darsda shovqin", -5, "Telefon bilan o'ynadi", "Fan Ustoz"),
            (point.ReasonName, point.Points, point.Note, point.CreatedBy));
        Assert.Equal(95, page.Discipline.Remaining);

        var seasonal = Assert.Single(page.SeasonalMarks);
        Assert.Equal((88m, "Fan Ustoz"), (seasonal.Score, seasonal.AuthorName));

        // Another quarter: its own (empty) marks.
        var second = await HomeroomPupilProgress.ForTeacherAsync(db, s.Homeroom.Id, s.Ali.Id, 2);
        Assert.Empty(second!.Grades);
    }

    [Fact]
    public async Task Boshqa_sinf_oquvchisi_korinmaydi_fan_oqituvchisiga_ham()
    {
        var (db, s) = await SetupAsync("homeroom_pupil_scope");

        Assert.Null(await HomeroomPupilProgress.ForTeacherAsync(db, s.Homeroom.Id, s.Eli.Id, 1));
        // The subject teacher reaches the class through the journal, but does not lead it.
        Assert.Null(await HomeroomPupilProgress.ForTeacherAsync(db, s.SubjectTeacher.Id, s.Ali.Id, 1));
        Assert.Empty(await HomeroomPupilProgress.ScoresAsync(db, s.SubjectTeacher.Id));
    }

    [Fact]
    public async Task Royxatda_faqat_oz_oquvchilarining_balli()
    {
        var (db, s) = await SetupAsync("homeroom_pupil_scores");

        var scores = await HomeroomPupilProgress.ScoresAsync(db, s.Homeroom.Id);

        Assert.Equal(95, Assert.Single(scores, x => x.StudentId == s.Ali.Id).Score);
        Assert.Equal(100, Assert.Single(scores, x => x.StudentId == s.Bek.Id).Score);
        Assert.DoesNotContain(scores, x => x.StudentId == s.Eli.Id);
    }

    [Fact]
    public async Task Bolingan_darsda_oz_guruhining_orinbosari_chiqadi()
    {
        var (db, s) = await SetupAsync("homeroom_pupil_subgroup");
        var classId = db.Classes.Single(c => c.Name == "5-A").Id;
        var subjectId = db.Subjects.Single().Id;
        var date = DateOnly.ParseExact(Friday, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var (subGroup, absent, teacher) in new[] { (1, s.SubjectTeacher, s.Homeroom), (2, s.Homeroom, s.SubjectTeacher) })
            db.LessonSubstitutions.Add(new LessonSubstitution
            {
                Date = date, Period = 1, OwnerId = classId, SubjectId = subjectId, SubGroup = subGroup,
                AbsentTeacherId = absent.Id, SubstituteTeacherId = teacher.Id, CreatedBy = "test",
                CreatedAt = AppClock.NowInstant,
            });
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = classId, SubjectId = subjectId, Quarter = 1, StudentId = s.Ali.Id, Date = Friday, Period = 1,
            Grade = 4, SubGroup = 2,
        });
        await db.SaveChangesAsync();

        var page = await HomeroomPupilProgress.ForTeacherAsync(db, s.Homeroom.Id, s.Ali.Id, 1);

        Assert.Equal("Fan Ustoz", page!.Grades.Single(g => g.Date == Friday).TeacherName);
    }

    private sealed record School(Teacher Homeroom, Teacher SubjectTeacher, Student Ali, Student Bek, Student Eli);

    private async Task<(AppDbContext, School)> SetupAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var a = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var b = new SchoolClass { Name = "5-B", Grade = 5, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        var author = new AppUser { FullName = "Fan Ustoz", Role = Roles.Teacher, Email = $"fan.{name}" };
        var homeroom = new Teacher { FullName = "Rahbar Ustoz", UserId = Guid.NewGuid().ToString() };
        var subjectTeacher = new Teacher { FullName = "Fan Ustoz", UserId = author.Id };
        var substitute = new Teacher { FullName = "O'rinbosar Ustoz", UserId = Guid.NewGuid().ToString() };
        var stranger = new Teacher { FullName = "Begona Ustoz", UserId = Guid.NewGuid().ToString() };
        var ali = Pupil("Ali Test", "5-A");
        var bek = Pupil("Bek Test", "5-A");
        var eli = Pupil("Eli Test", "5-B");
        db.Users.Add(author);
        db.Classes.AddRange(a, b);
        db.Subjects.Add(math);
        db.Teachers.AddRange(homeroom, subjectTeacher, substitute, stranger);
        db.Students.AddRange(ali, bek, eli);
        db.Quarters.AddRange(
            new QuarterPeriod { Quarter = 1, StartDate = "2026-09-02", EndDate = "2026-11-03" },
            new QuarterPeriod { Quarter = 2, StartDate = "2026-11-09", EndDate = "2026-12-29" });
        await db.SaveChangesAsync();

        db.ClassHomeroomTeachers.AddRange(
            new ClassHomeroomTeacher { ClassId = a.Id, TeacherId = homeroom.Id, CreatedAt = AppClock.NowInstant },
            new ClassHomeroomTeacher { ClassId = b.Id, TeacherId = stranger.Id, CreatedAt = AppClock.NowInstant });
        var template = new ScheduleTemplate { ClassId = a.Id, Name = "Asosiy" };
        template.Lessons.Add(new ScheduleLesson { Day = 2, Period = 1, SubjectId = math.Id, TeacherId = subjectTeacher.Id });
        db.ScheduleTemplates.Add(template);

        db.JournalEntries.AddRange(
            new JournalEntry { ClassId = a.Id, SubjectId = math.Id, Quarter = 1, StudentId = ali.Id, Date = Wednesday, Period = 1, Grade = 2 },
            new JournalEntry { ClassId = a.Id, SubjectId = math.Id, Quarter = 1, StudentId = ali.Id, Date = Thursday, Period = 3, Grade = 5 },
            new JournalEntry { ClassId = b.Id, SubjectId = math.Id, Quarter = 1, StudentId = eli.Id, Date = Wednesday, Period = 1, Grade = 4 });
        db.LessonNotes.Add(new LessonNote
        {
            ClassId = a.Id, SubjectId = math.Id, Quarter = 1, Date = Wednesday, Period = 1, Topic = "Kasrlar", Conducted = true,
        });
        db.LessonSubstitutions.Add(new LessonSubstitution
        {
            Date = DateOnly.ParseExact(Thursday, "yyyy-MM-dd", CultureInfo.InvariantCulture), Period = 3,
            OwnerId = a.Id, SubjectId = math.Id, AbsentTeacherId = subjectTeacher.Id,
            SubstituteTeacherId = substitute.Id, CreatedBy = "test", CreatedAt = AppClock.NowInstant,
        });
        db.DisciplinePoints.Add(new DisciplinePoint
        {
            StudentId = ali.Id, ReasonId = Guid.NewGuid().ToString(), ReasonName = "Darsda shovqin", Points = -5,
            Note = "Telefon bilan o'ynadi", CreatedAt = $"{Wednesday}T10:00:00", CreatedBy = "Fan Ustoz",
            CreatedByUserId = author.Id,
        });
        db.SeasonalMarks.Add(new SeasonalMark
        {
            StudentId = ali.Id, ClassId = a.Id, SubjectId = math.Id, PeriodKind = SeasonalPeriodKind.Monthly,
            Year = 2026, Month = 10, Score = 88m, CreatedByUserId = author.Id,
        });
        await db.SaveChangesAsync();

        return (db, new School(homeroom, subjectTeacher, ali, bek, eli));
    }

    private static Student Pupil(string fullName, string className)
    {
        var p = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        p.Id = Guid.NewGuid().ToString();
        return p;
    }
}
