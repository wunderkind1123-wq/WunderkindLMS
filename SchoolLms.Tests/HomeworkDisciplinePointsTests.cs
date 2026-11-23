using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Uy vazifasi belgisi → gamifikatsiya bali (client, 2026-11-16): «−» chala, «*» umuman yo'q and «K»
//  give the reason linked to them automatically; the entry follows the mark (HomeworkDisciplinePoints).
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HomeworkDisciplinePointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Entry_follows_the_homework_mark_of_the_cell()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"HW-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Tarix {tag}" };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Sardor {tag}", cls.Name, "+998900000081");
        var partial = new DisciplineReason { Name = $"Vazifa chala {tag}", Points = -2 };
        var notDone = new DisciplineReason { Name = $"Vazifa yo'q {tag}", Points = -5 };
        const string date = "2026-11-16";

        await fixture.Api.WithDbAsync(async db =>
        {
            // One reason per mark across the school — free the two marks for this world.
            foreach (var r in await db.DisciplineReasons.Where(r => r.HomeworkMark != null).ToListAsync()) r.HomeworkMark = null;
            await db.SaveChangesAsync();
            partial.HomeworkMark = HomeworkStatus.Partial;
            notDone.HomeworkMark = HomeworkStatus.NotDone;
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.Add(pupil);
            db.DisciplineReasons.AddRange(partial, notDone);
            await db.SaveChangesAsync();
        });

        async Task<List<DisciplinePoint>> PointsAsync()
        {
            List<DisciplinePoint> rows = [];
            await fixture.Api.WithDbAsync(async db =>
                rows = await db.DisciplinePoints.AsNoTracking().Where(p => p.StudentId == pupil.Id).ToListAsync());
            return rows;
        }
        Task SetAsync(int homework, int? grade = null) => fixture.Api.WithDbAsync(async db =>
            Assert.Null(await JournalService.SetEntryAsync(db,
                new SetJournalEntryRequest(cls.Id, subject.Id, 2, pupil.Id, date, 3, grade, null, homework))));

        // «−» → the "chala" reason, once, even when the cell is saved again.
        await SetAsync(HomeworkStatus.Partial);
        await SetAsync(HomeworkStatus.Partial, grade: 4);
        var point = Assert.Single(await PointsAsync());
        Assert.Equal((partial.Id, -2, HomeworkDisciplinePoints.Author), (point.ReasonId, point.Points, point.CreatedBy));
        Assert.Contains("«−»", point.Note);
        // The journal's subject — the parent sees which lesson it came from.
        Assert.Equal((subject.Id, subject.Name), (point.SubjectId, point.SubjectName));

        // «*» → the same entry takes the other reason.
        await SetAsync(HomeworkStatus.NotDone, grade: 4);
        point = Assert.Single(await PointsAsync());
        Assert.Equal((notDone.Id, -5), (point.ReasonId, point.Points));

        // «+» (not linked) → the entry goes; «K» with no linked reason gives nothing.
        await SetAsync(HomeworkStatus.Done, grade: 4);
        Assert.Empty(await PointsAsync());
        await SetAsync(HomeworkStatus.Late, grade: 4);
        Assert.Empty(await PointsAsync());

        // Clearing the cell removes the entry too.
        await SetAsync(HomeworkStatus.Partial, grade: 4);
        Assert.Single(await PointsAsync());
        await fixture.Api.WithDbAsync(db => JournalService.ClearEntryAsync(db, cls.Id, subject.Id, 2, pupil.Id, date, 3));
        Assert.Empty(await PointsAsync());
    }

    [Fact]
    public async Task K_is_lesson_lateness_and_is_not_penalised_twice()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"HK-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Biologiya {tag}" };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Kamol {tag}", cls.Name, "+998900000082");
        var lateMark = new DisciplineReason { Name = $"Darsga kechikdi {tag}", Points = -3 };
        var lateReason = new AbsenceReason { Name = $"Kech qoldi {tag}", Short = "KQ", IsLate = true, Points = -1 };
        const string date = "2026-11-17";

        await fixture.Api.WithDbAsync(async db =>
        {
            foreach (var r in await db.DisciplineReasons.Where(r => r.HomeworkMark != null).ToListAsync()) r.HomeworkMark = null;
            await db.SaveChangesAsync();
            lateMark.HomeworkMark = HomeworkStatus.Late;
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.Add(pupil);
            db.DisciplineReasons.Add(lateMark);
            db.AbsenceReasons.Add(lateReason);
            await db.SaveChangesAsync();
        });

        async Task<int> PointsAsync()
        {
            var n = 0;
            await fixture.Api.WithDbAsync(async db => n = await db.DisciplinePoints.CountAsync(p => p.StudentId == pupil.Id));
            return n;
        }
        Task SetAsync(string? reasonId) => fixture.Api.WithDbAsync(async db =>
            Assert.Null(await JournalService.SetEntryAsync(db,
                new SetJournalEntryRequest(cls.Id, subject.Id, 2, pupil.Id, date, 1, null, reasonId, HomeworkStatus.Late))));

        // «K» alone → the "darsga kechikdi" entry.
        await SetAsync(null);
        Assert.Equal(1, await PointsAsync());

        // The same lesson also marked late in attendance, and that reason has points — no second penalty.
        await SetAsync(lateReason.Id);
        Assert.Equal(0, await PointsAsync());
    }

    [Fact]
    public async Task A_mark_links_to_one_reason_only()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Guid.NewGuid().ToString("N")[..8];
        await fixture.Api.WithDbAsync(async db =>
        {
            foreach (var r in await db.DisciplineReasons.Where(r => r.HomeworkMark == HomeworkStatus.Late).ToListAsync()) r.HomeworkMark = null;
            await db.SaveChangesAsync();
        });

        var first = await admin.PostAsJsonAsync("/api/admin/discipline/reasons",
            new { name = $"Kech topshirdi {tag}", points = 1, type = "negative", homeworkMark = HomeworkStatus.Late });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await admin.PostAsJsonAsync("/api/admin/discipline/reasons",
            new { name = $"Yana kech {tag}", points = 1, type = "negative", homeworkMark = HomeworkStatus.Late });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        // «+» (done) is not a mark a penalty can hang on.
        var done = await admin.PostAsJsonAsync("/api/admin/discipline/reasons",
            new { name = $"Bajardi {tag}", points = 1, type = "positive", homeworkMark = HomeworkStatus.Done });
        Assert.Equal(HttpStatusCode.BadRequest, done.StatusCode);
    }
}
