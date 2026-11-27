using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  The leaderboard for a homeroom teacher (client, 2026-11-27) — Mini App, view only. The classes
//  they lead and the track groups they are the "Sinf rahbari" of; places only inside each class or
//  group; every pupil's score with its parts. Another class never leaks in; a teacher who leads
//  nothing sees nothing.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HomeroomLeaderboardTests(ApiFixture fixture)
{
    private const string Url = "/api/teacher/homeroom/leaderboard";

    [Fact]
    public async Task Rahbar_oz_sinfi_va_guruhi_reytingini_qismlari_bilan_koradi()
    {
        var w = await SeedAsync();

        var res = await w.Teacher.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var scopes = doc.RootElement.GetProperty("scopes").EnumerateArray().ToList();

        Assert.Equal(2, scopes.Count);
        var cls = scopes[0];
        Assert.Equal("class", cls.GetProperty("kind").GetString());
        Assert.Equal(w.LedClass, cls.GetProperty("name").GetString());
        var clsRows = cls.GetProperty("rows").EnumerateArray().ToList();
        // Ali and Bobur — not the archived pupil, not the other class.
        Assert.Equal(2, cls.GetProperty("count").GetInt32());
        Assert.Equal([w.Bobur, w.Ali], clsRows.Select(r => r.GetProperty("studentId").GetString()).ToArray());
        // Bobur leads by the hand-added 50 points; the place is inside the class.
        Assert.Equal(1, clsRows[0].GetProperty("rank").GetInt32());
        Assert.Equal(50, clsRows[0].GetProperty("manualPoints").GetInt32());
        // The parts are there for every pupil.
        foreach (var part in new[] { "grades", "attendance", "monthlyTest", "behaviour", "base", "total" })
            Assert.True(clsRows[1].TryGetProperty(part, out _), part);

        var track = scopes[1];
        Assert.Equal("track", track.GetProperty("kind").GetString());
        Assert.Equal(w.TrackName, track.GetProperty("name").GetString());
        var trackRows = track.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([w.Doniyor], trackRows.Select(r => r.GetProperty("studentId").GetString()).ToArray());
        Assert.Equal(1, trackRows[0].GetProperty("rank").GetInt32());

        var raw = doc.RootElement.GetRawText();
        Assert.DoesNotContain(w.Stranger, raw);
        Assert.DoesNotContain(w.Archived, raw);
    }

    [Fact]
    public async Task Rahbar_bolmagan_oqituvchi_hech_narsa_kormaydi()
    {
        await SeedAsync();
        var (_, plain) = await TeacherAsync();

        using var doc = JsonDocument.Parse(await plain.GetStringAsync(Url));
        Assert.Empty(doc.RootElement.GetProperty("scopes").EnumerateArray());
    }

    [Fact]
    public async Task Faqat_oqituvchi_tokeni_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url)).StatusCode);
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
    }

    private sealed record World(HttpClient Teacher, string LedClass, string TrackName,
        string Ali, string Bobur, string Doniyor, string Archived, string Stranger);

    private async Task<(string TeacherId, HttpClient Client)> TeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == user.Id)).Id);
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, client) = await TeacherAsync();
        var led = new SchoolClass { Name = $"L{tag}-A", Grade = 7 };
        var other = new SchoolClass { Name = $"L{tag}-B", Grade = 7 };
        var track = new StudyGroup { Name = $"Tabiiy fanlar {tag}", IsTrack = true };

        var ali = Pupil($"Ali {tag}", led.Name);
        var bobur = Pupil($"Bobur {tag}", led.Name);
        var archived = Pupil($"Arxiv {tag}", led.Name);
        archived.IsArchived = true;
        var doniyor = Pupil($"Doniyor {tag}", other.Name);
        var stranger = Pupil($"Begona {tag}", other.Name);

        await fixture.Api.WithDbAsync(async db =>
        {
            var createdBy = await db.Teachers.Where(t => t.Id == teacherId).Select(t => t.UserId!).SingleAsync();
            track.CreatedBy = createdBy;
            db.Classes.AddRange(led, other);
            db.Students.AddRange(ali, bobur, archived, doniyor, stranger);
            db.StudyGroups.Add(track);
            await db.SaveChangesAsync();

            db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = led.Id, TeacherId = teacherId, CreatedAt = AppClock.NowInstant });
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = track.Id, TeacherId = teacherId });
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = track.Id, StudentId = doniyor.Id, JoinedOn = AppClock.Today.AddDays(-30),
                CreatedBy = createdBy, CreatedAt = AppClock.NowInstant,
            });
            db.LeaderboardAwards.Add(new LeaderboardAward
            {
                StudentId = bobur.Id, ReasonName = "Olimpiada", Points = 50, Date = AppClock.Today,
                CreatedBy = "test", CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });

        return new World(client, led.Name, track.Name, ali.Id, bobur.Id, doniyor.Id, archived.Id, stranger.Id);
    }

    private static Student Pupil(string fullName, string className)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        s.Id = Guid.NewGuid().ToString();
        return s;
    }
}
