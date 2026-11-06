using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using static SchoolLms.Tests.HrTasks.HrTasksKit;

namespace SchoolLms.Tests.HrTasks;

// ===========================================================================
//  Client request of 2026-11-06 (docs/modules/curriculum-monitoring.md §4):
//  a task with a START day ("bugun berilsa bir oy davomida bajarish"), editing
//  only by the giver and only before the due time, self tasks edited by their
//  owner, recurring rules for oneself, and "only at the start and the end"
//  reminders for long tasks.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HrTasksLongAndSelfTests(ApiFixture fixture)
{
    private readonly HrTasksKit kit = new(fixture);

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<bool> InDayAsync(HttpClient client, DateOnly day, string id)
    {
        var list = await ReadAsync(await client.GetAsync($"{Me}/tasks?date={Day(day)}"));
        return list.GetProperty("items").EnumerateArray().Any(t => t.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Muddati_uzoq_topshiriq_bugundan_korinadi_boshlanish_tanlanadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync(Roles.Teacher);
        var today = HrValues.LocalDate(DateTimeOffset.UtcNow, Tz);
        var due = HrValues.At(today.AddDays(30), new TimeOnly(18, 0), Tz);

        // no startDate → today: in today's list though due in a month
        var month = await ReadAsync(await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Video darsni konspekt qilish", dueAt = due.ToString("o"),
        }));
        Assert.Equal(Day(today), month.GetProperty("periodStart").GetString());
        Assert.True(await InDayAsync(e.Client, today, month.GetProperty("id").GetString()!));

        // a later start → not yet in today's list, there from that day
        var later = await ReadAsync(await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee.Id, title = "Keyingi haftadan", dueAt = due.ToString("o"), startDate = Day(today.AddDays(7)),
        }));
        var laterId = later.GetProperty("id").GetString()!;
        Assert.False(await InDayAsync(e.Client, today, laterId));
        Assert.True(await InDayAsync(e.Client, today.AddDays(7), laterId));

        // start after the due day → 422
        var bad = await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee.Id, title = "Noto'g'ri", dueAt = due.ToString("o"), startDate = Day(today.AddDays(31)),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);

        // self task: the same start rule
        var self = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks", new
        {
            title = "O'zim uchun oylik ish", dueAt = due.ToString("o"),
        }));
        Assert.Equal(Day(today), self.GetProperty("periodStart").GetString());
        Assert.True(await InDayAsync(e.Client, today, self.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task Tahrirlash_faqat_bergan_rahbarga_va_muddat_otmaguncha()
    {
        var giver = await kit.PersonAsync(Roles.Admin);
        var other = await kit.PersonAsync(Roles.Admin);
        var e = await kit.PersonAsync(Roles.Teacher);
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var created = await ReadAsync(await giver.Client.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Xato yuklangan", dueAt = due.ToString("o"),
        }));
        var id = created.GetProperty("id").GetString()!;

        // another manager — 403
        var foreign = await SendAsync(other.Client, "PATCH", $"{Panel}/tasks/{id}", new { title = "Boshqa rahbar" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("hr_not_task_giver", await CodeAsync(foreign));

        // the giver — title, start and due
        var newStart = HrValues.LocalDate(DateTimeOffset.UtcNow, Tz).AddDays(1);
        var ok = await SendAsync(giver.Client, "PATCH", $"{Panel}/tasks/{id}", new
        {
            title = "To'g'rilangan", startDate = Day(newStart), dueAt = due.AddDays(2).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ReadAsync(ok);
        Assert.Equal("To'g'rilangan", body.GetProperty("title").GetString());
        Assert.Equal(Day(newStart), body.GetProperty("periodStart").GetString());

        // past due → 409, nothing changes
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.HrTasks.SingleAsync(x => x.Id == id);
            t.DueAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });
        var late = await SendAsync(giver.Client, "PATCH", $"{Panel}/tasks/{id}", new { title = "Kech" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("hr_task_past_due", await CodeAsync(late));
    }

    [Fact]
    public async Task Xodim_faqat_ozi_qoshgan_topshiriqni_tahrirlaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync(Roles.Teacher);
        var due = DateTimeOffset.UtcNow.AddDays(5);
        var own = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks", new { title = "Konspekt", dueAt = due.ToString("o") }));
        var ownId = own.GetProperty("id").GetString()!;

        var edited = await SendAsync(e.Client, "PATCH", $"{Me}/tasks/{ownId}", new
        {
            title = "Konspekt (1-3 dars)", description = "Video 1-3", dueAt = due.AddDays(1).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await ReadAsync(edited);
        Assert.Equal("Konspekt (1-3 dars)", body.GetProperty("title").GetString());
        Assert.Equal("Video 1-3", body.GetProperty("description").GetString());

        // a manager's task is not the employee's to edit
        var given = await ReadAsync(await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Rahbardan", dueAt = due.ToString("o"),
        }));
        var refused = await SendAsync(e.Client, "PATCH", $"{Me}/tasks/{given.GetProperty("id").GetString()}", new { title = "O'zgartirdim" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("not_self_task", await CodeAsync(refused));

        // someone else's self task → 403 task_not_owned
        var stranger = await kit.PersonAsync(Roles.Teacher);
        var notMine = await SendAsync(stranger.Client, "PATCH", $"{Me}/tasks/{ownId}", new { title = "Begona" });
        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);

        // past due → 409
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.HrTasks.SingleAsync(x => x.Id == ownId);
            t.DueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        var late = await SendAsync(e.Client, "PATCH", $"{Me}/tasks/{ownId}", new { title = "Kech" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("task_past_due", await CodeAsync(late));
    }

    [Fact]
    public async Task Xodim_ozi_uchun_takroriy_topshiriq_yaratadi_va_boshqaradi()
    {
        var e = await kit.PersonAsync(Roles.Teacher);
        var stranger = await kit.PersonAsync(Roles.Teacher);

        // the target fields in the body are ignored — always oneself
        var created = await e.Client.PostAsJsonAsync($"{Me}/templates", new
        {
            title = "Haftalik reja", recurrence = "weekly", weekdays = new[] { 1 }, dueTime = "18:00", kind = "check",
            targetType = "employee", targetEmployeeId = stranger.Employee!.Id,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tpl = await ReadAsync(created);
        var id = tpl.GetProperty("id").GetString()!;
        Assert.Equal(e.Employee!.Id, tpl.GetProperty("targetEmployeeId").GetString());

        var mine = await ReadAsync(await e.Client.GetAsync($"{Me}/templates"));
        Assert.Contains(mine.EnumerateArray(), t => t.GetProperty("id").GetString() == id);
        var theirs = await ReadAsync(await stranger.Client.GetAsync($"{Me}/templates"));
        Assert.DoesNotContain(theirs.EnumerateArray(), t => t.GetProperty("id").GetString() == id);

        // a stranger cannot touch it
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(stranger.Client, "PATCH", $"{Me}/templates/{id}", new { title = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.DeleteAsync($"{Me}/templates/{id}")).StatusCode);

        // edit cannot move the target; toggle; delete
        var edited = await ReadAsync(await SendAsync(e.Client, "PATCH", $"{Me}/templates/{id}", new
        {
            title = "Haftalik reja (yangi)", targetEmployeeId = stranger.Employee.Id,
        }));
        Assert.Equal("Haftalik reja (yangi)", edited.GetProperty("title").GetString());
        Assert.Equal(e.Employee.Id, edited.GetProperty("targetEmployeeId").GetString());
        var paused = await ReadAsync(await e.Client.PostAsync($"{Me}/templates/{id}/toggle", null));
        Assert.False(paused.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await e.Client.DeleteAsync($"{Me}/templates/{id}")).StatusCode);
        await fixture.Api.WithDbAsync(async db => Assert.False(await db.HrTaskTemplates.AnyAsync(t => t.Id == id)));

        // a manager's rule aimed at the employee is not theirs
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var managers = await ReadAsync(await admin.PostAsJsonAsync($"{Panel}/templates", new
        {
            title = "Rahbar qoidasi", targetType = "employee", targetEmployeeId = e.Employee.Id, recurrence = "weekly",
            weekdays = new[] { 2 }, kind = "check",
        }));
        var managersId = managers.GetProperty("id").GetString()!;
        var list = await ReadAsync(await e.Client.GetAsync($"{Me}/templates"));
        Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("id").GetString() == managersId);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Client.DeleteAsync($"{Me}/templates/{managersId}")).StatusCode);
    }

    [Fact]
    public async Task Uzoq_topshiriq_faqat_boshida_va_oxirida_eslatiladi()
    {
        var sender = new FakeHrSender();
        var e = await kit.PersonAsync(telegram: true);
        var start = new DateOnly(2039, 4, 1);
        var due = At(2039, 4, 30, 18);
        var title = "Oylik konspekt " + Guid.NewGuid().ToString("N")[..6];
        var task = new HrTask
        {
            EmployeeId = e.Employee!.Id, Title = title, Source = HrTaskSources.Director, Kind = HrTaskKinds.Check,
            PeriodStart = start, DueAt = due, Status = HrTaskStatuses.Open, OverduePolicy = HrOverduePolicies.KeepOpen,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        await fixture.Api.WithDbAsync(async db => { db.HrTasks.Add(task); await db.SaveChangesAsync(); });

        await kit.WithServiceAsync(async (s, _) =>
        {
            // morning list: the start day, not the days in between, then the day before and the due day
            await s.SendMorningListsAsync(start, At(2039, 4, 1, 7, 30));
            Assert.Contains(sender.To(e.Chat!.Value), m => m.Contains(title));
            sender.Messages.Clear();
            await s.SendMorningListsAsync(new DateOnly(2039, 4, 15), At(2039, 4, 15, 7, 30));
            Assert.DoesNotContain(sender.To(e.Chat.Value), m => m.Contains(title));
            await s.SendMorningListsAsync(new DateOnly(2039, 4, 29), At(2039, 4, 29, 7, 30));
            Assert.Contains(sender.To(e.Chat.Value), m => m.Contains(title));
            sender.Messages.Clear();

            // reminders: a day before and two hours before — not the short 60/30 ones
            Assert.Equal(0, await s.SendRemindersAsync(due.AddHours(-25)));
            Assert.Equal(1, await s.SendRemindersAsync(due.AddHours(-23)));
            Assert.Equal(1, await s.SendRemindersAsync(due.AddMinutes(-100)));
            Assert.Equal(0, await s.SendRemindersAsync(due.AddMinutes(-20)));
        }, sender);

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal([120, 1440], (await db.HrTaskReminders.Where(r => r.TaskId == task.Id).Select(r => (int)r.OffsetMinutes).ToListAsync()).Order()));
    }
}
