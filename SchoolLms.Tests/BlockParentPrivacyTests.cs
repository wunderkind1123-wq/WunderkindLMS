using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bloklar — 6-bosqich: ota-onaga guruh nomi ko'rsatilmaydi (mijoz qarori, docs/modules/blocks.md):
/// API javobida ham yo'q, faqat ustoz ismi.
///
/// <para>
/// <b>Qanday isbotlanadi.</b> O'quvchi noyob nomli blok guruhiga qo'yiladi, darsi jadvalda, bahosi,
/// davomati va oraliq testi bor. Keyin ota-ona va o'quvchi ilovasining barcha GET endpointlari
/// chaqiriladi va 200 javoblarning BIRORTASIDA blok nomi, guruh nomi yoki guruhning o'quv guruhi
/// nomi uchramasligi tekshiriladi. Yangi endpoint qo'shilsa — ro'yxatga qo'shing.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BlockParentPrivacyTests(ApiFixture fixture)
{
    private static readonly string[] StudentEndpoints =
    [
        "me", "dashboard", "schedule", "grades", "journal", "attendance", "homework", "interim-tests",
        "rating", "leaderboard", "subjects-progress", "discipline", "notebook", "exam-results",
        "certificates", "assignments", "assignment-scores", "lms/subjects",
    ];

    private static readonly string[] ParentEndpoints =
        ["overview", "attendance", "grades", "schedule", "announcements"];

    [Fact]
    public async Task Ota_onaga_guruh_va_blok_nomi_chiqmaydi()
    {
        var w = await SeedAsync();
        try
        {
            await CheckAsync(w);
        }
        finally
        {
            await CleanupAsync(w);
        }
    }

    private async Task CheckAsync(World w)
    {
        using var parent = fixture.Api.ClientWithToken(fixture.Api.TokenFor("parent", w.ParentUserId));

        var bodies = new List<(string Path, string Body)>();
        foreach (var e in StudentEndpoints)
            bodies.Add(await GetAsync(parent, $"/api/student/{e}?studentId={w.StudentId}"));
        var children = await GetAsync(parent, "/api/tg/parent/children");
        bodies.Add(children);
        Assert.Contains(w.StudentId, children.Body);
        foreach (var e in ParentEndpoints)
            bodies.Add(await GetAsync(parent, $"/api/tg/parent/children/{w.StudentId}/{e}"));
        // Blok darsi biriktirilgan chorak va hafta — jadvalda albatta bor.
        bodies.Add(await GetAsync(parent, $"/api/student/schedule?studentId={w.StudentId}&quarter={w.Quarter}&week=1"));
        bodies.Add(await GetAsync(parent, $"/api/tg/parent/children/{w.StudentId}/schedule?quarter={w.Quarter}&week=1"));

        // Blok darsi haqiqatan ko'rindi: fan va ustoz ismi (ota-onaga ko'rsatiladigani — ustoz) bor.
        var all = string.Join('\n', bodies.Select(b => b.Path + " => " + b.Body));
        Assert.Contains($"Ingliz tili {w.Tag}", all);
        Assert.Contains($"Ustoz {w.Tag}", all);

        // Kamida jurnal va jadval javob berdi — tekshiruv bo'sh ro'yxat ustida o'tib ketmasin.
        Assert.True(bodies.Count(b => b.Body.Length > 2) >= 5, string.Join("\n", bodies.Select(b => b.Path)));
        foreach (var (path, body) in bodies)
            foreach (var secret in new[] { w.BlockName, w.GroupName, w.StudyGroupName })
                Assert.False(body.Contains(secret, StringComparison.OrdinalIgnoreCase),
                    $"{path} javobida \"{secret}\" bor:\n{body[..Math.Min(body.Length, 800)]}");
    }

    private static async Task<(string Path, string Body)> GetAsync(HttpClient client, string path)
    {
        var res = await client.GetAsync(path);
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Forbidden
            or HttpStatusCode.NoContent, $"{path}: {(int)res.StatusCode} {body}");
        return (path, res.StatusCode == HttpStatusCode.OK ? body : "");
    }

    private sealed record World(
        string StudentId, string ParentUserId, string BlockName, string GroupName, string StudyGroupName, string Tag,
        int Quarter, bool AddedQuarter);

    /// <summary>
    /// Umumiy bazada guruh jadvali/jurnali qolmasin: StudyGroupTests.Guruh_yaratish_darslarga_tegmaydi
    /// butun bazada guruh qatorlari yo'qligini (aniq 0) tekshiradi.
    /// </summary>
    private Task CleanupAsync(World w) => fixture.Api.WithDbAsync(async db =>
    {
        var block = await db.Blocks.SingleAsync(b => b.Name == w.BlockName);
        var groups = await db.BlockGroups.Where(g => g.BlockId == block.Id).ToListAsync();
        var sgIds = groups.Select(g => g.StudyGroupId).ToList();
        var sgKeys = sgIds.Select(id => id.ToString()).ToList();
        var tests = await db.InterimTests.Where(t => sgIds.Contains(t.GroupId)).ToListAsync();
        var testIds = tests.Select(t => t.Id).ToList();
        db.InterimTestResults.RemoveRange(await db.InterimTestResults.Where(r => testIds.Contains(r.TestId)).ToListAsync());
        db.InterimTests.RemoveRange(tests);
        db.JournalEntries.RemoveRange(await db.JournalEntries.Where(e => sgKeys.Contains(e.ClassId)).ToListAsync());
        db.LessonNotes.RemoveRange(await db.LessonNotes.Where(e => sgKeys.Contains(e.ClassId)).ToListAsync());
        db.WeekAssignments.RemoveRange(await db.WeekAssignments.Where(a => sgKeys.Contains(a.ClassId)).ToListAsync());
        db.ScheduleTemplates.RemoveRange(await db.ScheduleTemplates.Where(t => sgKeys.Contains(t.ClassId)).ToListAsync());
        db.DisciplinePoints.RemoveRange(await db.DisciplinePoints.Where(p => p.StudentId == w.StudentId).ToListAsync());
        await db.SaveChangesAsync();
        db.BlockGroupTeachers.RemoveRange(await db.BlockGroupTeachers.Where(t => groups.Select(g => g.Id).Contains(t.BlockGroupId)).ToListAsync());
        db.StudyGroupMembers.RemoveRange(await db.StudyGroupMembers.Where(m => sgIds.Contains(m.GroupId)).ToListAsync());
        db.StudyGroupTeachers.RemoveRange(await db.StudyGroupTeachers.Where(t => sgIds.Contains(t.GroupId)).ToListAsync());
        db.BlockGroups.RemoveRange(groups);
        db.BlockClasses.RemoveRange(await db.BlockClasses.Where(c => c.BlockId == block.Id).ToListAsync());
        db.Blocks.Remove(block);
        if (w.AddedQuarter) db.Quarters.RemoveRange(await db.Quarters.Where(q => q.Quarter == 4).ToListAsync());
        await db.SaveChangesAsync();
        db.StudyGroups.RemoveRange(await db.StudyGroups.Where(g => sgIds.Contains(g.Id)).ToListAsync());
        await db.SaveChangesAsync();
    });

    /// <summary>
    /// Yo'nalish (o'quvchi uning a'zosi), undan blok; o'quvchi 1-guruhda; jadvalda blok darsi (bazadagi
    /// barcha chorak haftalariga); jurnal bahosi va kelmagani; oraliq test natijasi; ota-ona akkaunti.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var blockName = $"BlokMaxfiy{tag}";
        var groupName = $"GuruhMaxfiy{tag}";
        var studyGroupName = BlockService.StudyGroupName(blockName, groupName);
        var (parentUser, _) = await fixture.Api.SeedUserAsync("parent", fullName: $"Ota {tag}", email: $"p{tag}");

        string studentId = "";
        var addedQuarter = false;
        var scheduleQuarter = 0;
        await fixture.Api.WithDbAsync(async db =>
        {
            var admin = await db.Users.FirstAsync();
            var subject = new Subject { Name = $"Ingliz tili {tag}" };
            var teacher = new Teacher { FullName = $"Ustoz {tag}" };
            var track = new StudyGroup { Name = $"Filologiya {tag}", IsTrack = true, CreatedBy = admin.Id };
            var student = new Student
            {
                FullName = $"Bola {tag}", FirstName = "Bola", LastName = tag, BirthDate = "2010-01-01",
                Gender = "male", ClassName = $"10-{tag}", EnrollmentDate = "2026-09-01",
            };
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            db.StudyGroups.Add(track);
            db.Students.Add(student);
            var guardian = new Guardian { FullName = $"Ota {tag}", Phone = "+99890" + tag[..7], UserId = parentUser.Id };
            db.Guardians.Add(guardian);
            db.StudentGuardians.Add(new StudentGuardian
            {
                StudentId = student.Id, GuardianId = guardian.Id, Relation = GuardianRelation.Parent, IsPrimary = true,
            });
            var since = AppClock.Today.AddDays(-30);
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = track.Id, StudentId = student.Id, JoinedOn = since, CreatedBy = admin.Id,
            });

            var block = new Block
            {
                Name = blockName, SubjectId = subject.Id, AcademicYear = BlockService.AcademicYearOf(AppClock.Today),
                BlockType = BlockType.Level, DayPattern = BlockDayPattern.Custom, IsActive = true,
                CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
            };
            db.Blocks.Add(block);
            db.BlockClasses.Add(new BlockClass { BlockId = block.Id, OwnerKind = LessonOwnerKind.Group, OwnerId = track.Id.ToString() });
            var sg = new StudyGroup { Name = studyGroupName, SubjectId = subject.Id, CreatedBy = admin.Id };
            var sg2 = new StudyGroup { Name = $"{blockName} · Ikkinchi{tag}", SubjectId = subject.Id, CreatedBy = admin.Id };
            db.StudyGroups.AddRange(sg, sg2);
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = sg.Id, TeacherId = teacher.Id });
            var bg = new BlockGroup { BlockId = block.Id, StudyGroupId = sg.Id, Name = groupName, Code = BlockGroupCode.Katta, SortOrder = 1 };
            var bg2 = new BlockGroup { BlockId = block.Id, StudyGroupId = sg2.Id, Name = $"Ikkinchi{tag}", Code = BlockGroupCode.Kichik, SortOrder = 2 };
            db.BlockGroups.AddRange(bg, bg2);
            db.BlockGroupTeachers.Add(new BlockGroupTeacher
            {
                BlockGroupId = bg.Id, TeacherId = teacher.Id, ValidFrom = since, CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();

            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = sg.Id, SubjectId = subject.Id, StudentId = student.Id, JoinedOn = since,
                SourceClassId = track.Id.ToString(), CreatedBy = admin.Id,
            });

            // Jadval: bazadagi har bir chorak haftasiga — qaysi biri "joriy" bo'lmasin, dars ko'rinadi.
            var tpl = new ScheduleTemplate
            {
                ClassId = sg.Id.ToString(), OwnerKind = LessonOwnerKind.Group, Name = "Blok darslari",
                Lessons = [.. Enumerable.Range(0, 6).Select(d => new ScheduleLesson
                {
                    Day = d, Period = 9, SubjectId = subject.Id, TeacherId = teacher.Id, BlockId = block.Id,
                })],
            };
            db.ScheduleTemplates.Add(tpl);
            // Chorak bo'lmasa — 4-chorak qo'shiladi (tozalashda olib tashlanadi): jadvalni ochiq chorak va
            // hafta bilan so'raymiz, bugungi "joriy hafta"ga tayanmaymiz.
            if (!await db.Quarters.AnyAsync())
            {
                db.Quarters.Add(new QuarterPeriod { Quarter = 4, StartDate = "2027-04-01", EndDate = "2027-05-25" });
                addedQuarter = true;
            }
            await db.SaveChangesAsync();
            foreach (var q in await db.Quarters.AsNoTracking().ToListAsync())
                foreach (var wk in ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate))
                    db.WeekAssignments.Add(new WeekAssignment
                    {
                        ClassId = sg.Id.ToString(), OwnerKind = LessonOwnerKind.Group, Quarter = q.Quarter, Week = wk.Week,
                        TemplateId = tpl.Id,
                    });

            var quarter = (await db.Quarters.AsNoTracking().ToListAsync())
                .FirstOrDefault(q => string.CompareOrdinal(q.StartDate, AppClock.Today.ToString("yyyy-MM-dd")) <= 0
                                     && string.CompareOrdinal(AppClock.Today.ToString("yyyy-MM-dd"), q.EndDate) <= 0)?.Quarter ?? 2;
            foreach (var (days, grade) in new[] { (-2, (int?)5), (-1, null) })
                db.JournalEntries.Add(new JournalEntry
                {
                    ClassId = sg.Id.ToString(), OwnerKind = LessonOwnerKind.Group, SubjectId = subject.Id, Quarter = quarter,
                    StudentId = student.Id, Date = AppClock.Today.AddDays(days).ToString("yyyy-MM-dd"), Period = 9, Grade = grade,
                    Homework = 1,
                });
            var test = new InterimTest
            {
                GroupId = sg.Id, SubjectId = subject.Id, Date = AppClock.Today.AddDays(-1), QuestionCount = 10,
                CreatedBy = "Admin", CreatedAt = AppClock.NowInstant,
            };
            db.InterimTests.Add(test);
            db.InterimTestResults.Add(new InterimTestResult
            {
                TestId = test.Id, StudentId = student.Id, Correct = 8, UpdatedBy = "Admin", UpdatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
            studentId = student.Id;
            scheduleQuarter = (await db.Quarters.AsNoTracking().OrderBy(q => q.Quarter).FirstAsync()).Quarter;
        });

        return new World(studentId, parentUser.Id, blockName, groupName, studyGroupName, tag, scheduleQuarter, addedQuarter);
    }
}
