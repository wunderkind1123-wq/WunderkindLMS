using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  "Oylik hisobot (PDF)" avtomatik qoidasi — TZ "O'quvchi hisoboti", 2026-11-17, 3-vazifa: the month before,
//  on the rule's day; never at the same time as Sunday's weekly report; the staff copy as one ZIP per class.
//  Every Telegram call lands in a recording handler — nothing leaves the machine.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class MonthlyReportRuleTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public void Runs_on_its_day_for_the_month_before_and_waits_for_sundays_weekly_report()
    {
        // The run of December reports on November.
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            AutoMessageService.ReportRange(AutoMessageKind.MonthlyReport, "2026-12"));
        Assert.Equal((new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 21)),
            AutoMessageService.ReportRange(AutoMessageKind.WeeklyReport, "2026-11-22"));

        var rule = new AutoMessageRule { Key = AutoMessageKind.MonthlyReport, SendTime = "10:00", DayOfMonth = 1, Enabled = true };
        Assert.True(AutoMessageKind.IsMonthly(rule));
        // Tuesday 01.12.2026 (TZ example): 10:00.
        Assert.Null(AutoMessageService.DuePeriod(rule, new DateTime(2026, 12, 1, 9, 59, 0), "09:00"));
        Assert.Equal("2026-12", AutoMessageService.DuePeriod(rule, new DateTime(2026, 12, 1, 10, 0, 0), "09:00"));

        // Sunday 01.08.2027 with the weekly report at 09:00: the monthly one goes at 11:00, not 10:00.
        Assert.Null(AutoMessageService.DuePeriod(rule, new DateTime(2027, 8, 1, 10, 30, 0), "09:00"));
        Assert.Equal("2027-08", AutoMessageService.DuePeriod(rule, new DateTime(2027, 8, 1, 11, 0, 0), "09:00"));
        Assert.Equal(new DateTime(2027, 8, 1, 11, 0, 0),
            AutoMessageService.PlannedRun(rule, new DateTime(2027, 8, 1, 7, 0, 0), "09:00")!.Value.PlannedLocal);
        // The weekly report switched off — its own time.
        Assert.Equal("2027-08", AutoMessageService.DuePeriod(rule, new DateTime(2027, 8, 1, 10, 30, 0), weeklySendTime: null));

        // The weekly report's new token (TZ: "Noyabr, 3-hafta").
        Assert.Contains(AutoMessageService.TokensOf(AutoMessageKind.WeeklyReport), t => t.Token == "{hafta_raqami}");
        Assert.Contains(AutoMessageService.TokensOf(AutoMessageKind.MonthlyReport), t => t.Token == "{oy}");
    }

    [Fact]
    public async Task Monthly_report_goes_to_parents_and_one_zip_per_class_to_the_staff_copy()
    {
        var db = await FreshAsync("am_monthly");
        var cls = new SchoolClass { Name = "7-A", Grade = 7, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        db.Classes.Add(cls);
        db.Subjects.Add(math);
        var ali = Pupil("Aliyev Ali", "7-A");
        var vali = Pupil("Valiyev Vali", "7-A");   // no parent in the bot — still in the staff copy
        db.Students.AddRange(ali, vali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 801, ParentName = "Ota" });
        var director = new AppUser
        {
            FullName = "Direktor", Role = Roles.Staff, Email = $"d.{Guid.NewGuid():N}", PasswordHash = "x",
            Permissions = [AutoMessageService.StaffCopyKey],
        };
        db.Users.Add(director);
        await db.SaveChangesAsync();
        db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = 9001, UserId = director.Id });
        db.JournalEntries.AddRange(
            new JournalEntry { ClassId = cls.Id, SubjectId = math.Id, StudentId = ali.Id, Date = "2026-10-06", Period = 1, Quarter = 1, Grade = 5 },
            new JournalEntry { ClassId = cls.Id, SubjectId = math.Id, StudentId = vali.Id, Date = "2026-10-06", Period = 1, Quarter = 1, Grade = 4 });
        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == AutoMessageKind.MonthlyReport);
        Assert.False(rule.Enabled);                                  // ships off (the migration's row)
        Assert.Equal(("10:00", 1), (rule.SendTime, rule.DayOfMonth));
        rule.Enabled = true;
        rule.CopyToStaff = true;
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        // Sunday 01.11.2026 at 10:00, the weekly rule off: October's report.
        var result = Assert.Single(await service.RunDueAsync(new DateTime(2026, 11, 1, 10, 0, 0)), r => r.Key == AutoMessageKind.MonthlyReport);
        Assert.Equal(("2026-11", 2, 1, 1), (result.Period, result.Pupils, result.Sent, result.NoTelegram));

        var parent = Assert.Single(handler.Documents, d => d.ChatId == 801);
        Assert.StartsWith("Oylik_hisobot_", parent.FileName);
        Assert.EndsWith("_oktabr_2026.pdf", parent.FileName);
        Assert.Contains("Oktabr", parent.Caption);

        var copy = Assert.Single(handler.Documents, d => d.ChatId == 9001);
        Assert.Equal("Oylik_hisobot_7-A_10.2026.zip", copy.FileName);
        Assert.Contains("2 ta o'quvchi", copy.Caption);
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync(r => r.RuleKey == AutoMessageKind.MonthlyReport);
        Assert.NotNull(run.StaffCopyAt);

        // A later tick sends nothing again — neither to the parents nor to the staff.
        await service.RunDueAsync(new DateTime(2026, 11, 1, 12, 0, 0));
        Assert.Equal(2, handler.Documents.Count);
    }

    // ----- Helpers ------------------------------------------------------------

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static Student Pupil(string fullName, string className)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000001");
        s.Id = Guid.NewGuid().ToString();
        s.FirstName = "";
        return s;
    }

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Answers every call with 200 and keeps the documents: chat, file name and caption.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(long ChatId, string FileName, string Caption)> Documents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is MultipartFormDataContent form)
            {
                var parts = new Dictionary<string, string>();
                string fileName = "";
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name == "document") fileName = part.Headers.ContentDisposition?.FileName?.Trim('"') ?? part.Headers.ContentDisposition?.FileNameStar ?? "";
                    else if (name is not null) parts[name] = await part.ReadAsStringAsync(ct);
                }
                Documents.Add((long.Parse(parts["chat_id"]), fileName, parts.GetValueOrDefault("caption") ?? ""));
            }
            else
                _ = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
