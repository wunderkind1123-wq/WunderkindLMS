using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Olimpiada → tadbirlar, ishtirokchilar (tadbir ichida), eslatmalar, statistika, eksport
/// (docs/modules/olympiad.md). Ruxsat: <c>olympiad</c> (o'qish ham himoyalangan — bolalar telefonlari).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Manage, GatedRead = true)]
[Route("api/admin/olympiad")]
public sealed class OlympiadEventsController(AppDbContext db, AuditService audit, OlympiadTelegram tg, IWebHostEnvironment env) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";
    private OlympiadMessenger Messenger => new(db, tg, env.ContentRootPath);
    private ObjectResult Error(int status, string message) => StatusCode(status, new { message });

    // ======================= Tadbirlar =======================

    [HttpGet("events")]
    public async Task<List<OlympiadEventDto>> Events(CancellationToken ct) => await OlympiadQueries.EventsAsync(db, tg.Username, null, ct);

    [HttpGet("events/{id:guid}")]
    public async Task<ActionResult<OlympiadEventDto>> Event(Guid id, CancellationToken ct) =>
        (await OlympiadQueries.EventsAsync(db, tg.Username, id, ct)).FirstOrDefault() is { } e ? e : NotFound(new { message = "Tadbir topilmadi" });

    private static string? Validate(OlympiadEventInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Title) || i.Title.Trim().Length > 200) return "Tadbir nomini kiriting (200 belgigacha)";
        if (i.EndTime <= i.StartTime) return "Tugash vaqti boshlanishdan keyin bo'lishi kerak";
        if (i.Capacity is < 1) return "Limit kamida 1 bo'lishi kerak (yoki bo'sh — limitsiz)";
        if (i.MaxScore <= 0) return "Maksimal ball 0 dan katta bo'lishi kerak";
        if (i.RegistrationOpensAt is { } o && i.RegistrationClosesAt is { } c && c <= o) return "Ro'yxat yopilishi ochilishidan keyin bo'lishi kerak";
        if (i.RegistrationClosesAt is { } close && close > i.Date.ToDateTime(i.StartTime)) return "Ro'yxat tadbir boshlanishidan oldin yopilishi kerak";
        return null;
    }

    private static void Apply(OlympiadEvent e, OlympiadEventInput i)
    {
        var rulesChanged = e.RulesText != (i.RulesText ?? "") || e.RulesFileUrl != (i.RulesFileUrl ?? "");
        e.Title = i.Title.Trim();
        e.Description = i.Description ?? "";
        e.Date = i.Date;
        e.StartTime = i.StartTime;
        e.EndTime = i.EndTime;
        e.StartsAt = i.Date.ToDateTime(i.StartTime);
        e.Location = (i.Location ?? "").Trim();
        e.LocationMapUrl = (i.LocationMapUrl ?? "").Trim();
        e.Capacity = i.Capacity;
        e.OnFull = i.OnFull == OlympiadOnFull.Close ? OlympiadOnFull.Close : OlympiadOnFull.Waitlist;
        e.RegistrationOpensAt = i.RegistrationOpensAt;
        e.RegistrationClosesAt = i.RegistrationClosesAt;
        e.MaxScore = i.MaxScore;
        e.ShowScore = i.ShowScore;
        e.ShowRank = i.ShowRank;
        e.ShowCertificate = i.ShowCertificate;
        e.ShowInvite = i.ShowInvite;
        e.RulesText = i.RulesText ?? "";
        e.RulesFileUrl = i.RulesFileUrl ?? "";
        e.RulesFileName = i.RulesFileName ?? "";
        e.RulesDisplay = i.RulesDisplay is OlympiadRulesDisplay.Pdf or OlympiadRulesDisplay.Both ? i.RulesDisplay : OlympiadRulesDisplay.Text;
        if (rulesChanged) e.RulesVersion++;
        e.UpdatedAt = AppClock.Now;
    }

    /// <summary>Standart eslatmalar (sozlamadagi offsetlar bilan) — har yangi tadbirga.</summary>
    private async Task AddStandardRemindersAsync(Guid eventId, CancellationToken ct)
    {
        var s = await db.OlympiadSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new OlympiadSettings();
        if (s.Reminder1Enabled)
            db.OlympiadReminders.Add(new OlympiadReminder { EventId = eventId, Kind = OlympiadReminderKind.Standard1, OffsetMinutes = s.Reminder1Minutes });
        if (s.Reminder2Enabled)
            db.OlympiadReminders.Add(new OlympiadReminder { EventId = eventId, Kind = OlympiadReminderKind.Standard2, OffsetMinutes = s.Reminder2Minutes });
    }

    private async Task<int> NextCodeAsync(CancellationToken ct) =>
        (await db.OlympiadEvents.MaxAsync(e => (int?)e.Code, ct) ?? 0) + 1;

    [HttpPost("events")]
    public async Task<ActionResult<OlympiadEventDto>> Create(OlympiadEventInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        var now = AppClock.Now;
        var e = new OlympiadEvent { Code = await NextCodeAsync(ct), CreatedAt = now, CreatedBy = ActorName };
        Apply(e, input);
        e.RulesVersion = 1;
        db.OlympiadEvents.Add(e);
        await AddStandardRemindersAsync(e.Id, ct);
        audit.Record("olympiad_event", e.Id.ToString(), "create", $"Olimpiada yaratildi: {e.Title}");
        await db.SaveChangesAsync(ct);
        return (await OlympiadQueries.EventsAsync(db, tg.Username, e.Id, ct)).First();
    }

    [HttpPut("events/{id:guid}")]
    public async Task<ActionResult<OlympiadEventDto>> Update(Guid id, OlympiadEventInput input, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        Apply(e, input);
        audit.Record("olympiad_event", e.Id.ToString(), "update", $"Olimpiada tahrirlandi: {e.Title}");
        await db.SaveChangesAsync(ct);
        // Hisoblagichni haqiqiy qatorlar bilan bitta SQL'da tekislaymiz (parallel ro'yxatdan o'tish yo'qolmaydi).
        await OlympiadRegistrar.RecountSeatsAsync(db, id, ct);
        // Limit oshirilgan bo'lsa — kutish ro'yxatidan o'tkazamiz va xabar beramiz.
        foreach (var p in await OlympiadRegistrar.PromoteAsync(db, id, ct))
            await OlympiadRegistrar.SendTicketAsync(db, Messenger, p.Id, "promoted_from_waitlist", ct);
        return (await OlympiadQueries.EventsAsync(db, tg.Username, id, ct)).First();
    }

    [HttpPost("events/{id:guid}/duplicate")]
    public async Task<ActionResult<OlympiadEventDto>> Duplicate(Guid id, CancellationToken ct)
    {
        var src = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (src is null) return NotFound(new { message = "Tadbir topilmadi" });
        var now = AppClock.Now;
        var copy = new OlympiadEvent
        {
            Code = await NextCodeAsync(ct), Title = src.Title + " (nusxa)", Description = src.Description,
            Date = src.Date, StartTime = src.StartTime, EndTime = src.EndTime, StartsAt = src.StartsAt,
            Location = src.Location, LocationMapUrl = src.LocationMapUrl, Capacity = src.Capacity, OnFull = src.OnFull,
            RegistrationOpensAt = src.RegistrationOpensAt, RegistrationClosesAt = src.RegistrationClosesAt,
            MaxScore = src.MaxScore, ShowScore = src.ShowScore, ShowRank = src.ShowRank, ShowCertificate = src.ShowCertificate,
            ShowInvite = src.ShowInvite, RulesText = src.RulesText, RulesFileUrl = src.RulesFileUrl, RulesFileName = src.RulesFileName,
            RulesDisplay = src.RulesDisplay, Status = OlympiadEventStatus.Draft, CreatedAt = now, UpdatedAt = now, CreatedBy = ActorName,
        };
        db.OlympiadEvents.Add(copy);
        // Eslatmalar ham ko'chadi (yuborilmagan holatda), ishtirokchilar — yo'q.
        var reminders = await db.OlympiadReminders.AsNoTracking().Where(r => r.EventId == id && r.OffsetMinutes != null).ToListAsync(ct);
        foreach (var r in reminders)
            db.OlympiadReminders.Add(new OlympiadReminder
            {
                EventId = copy.Id, Kind = r.Kind, OffsetMinutes = r.OffsetMinutes, Text = r.Text,
                AttachmentsJson = r.AttachmentsJson, Target = r.Target,
            });
        if (reminders.Count == 0) await AddStandardRemindersAsync(copy.Id, ct);
        audit.Record("olympiad_event", copy.Id.ToString(), "create", $"Olimpiada nusxalandi: {copy.Title}");
        await db.SaveChangesAsync(ct);
        return (await OlympiadQueries.EventsAsync(db, tg.Username, copy.Id, ct)).First();
    }

    public sealed record StatusInput(string Status);

    [HttpPost("events/{id:guid}/status")]
    public async Task<ActionResult<OlympiadEventDto>> SetStatus(Guid id, StatusInput input, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (!OlympiadEventStatus.All.Contains(input.Status)) return BadRequest(new { message = "Noto'g'ri holat" });
        audit.Record("olympiad_event", e.Id.ToString(), "status", $"Olimpiada holati: {e.Status} → {input.Status}");
        e.Status = input.Status;
        e.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        return (await OlympiadQueries.EventsAsync(db, tg.Username, id, ct)).First();
    }

    [HttpDelete("events/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (await db.OlympiadRegistrations.AnyAsync(r => r.EventId == id, ct))
            return Error(409, "Tadbirda ishtirokchilar bor — o'chirib bo'lmaydi, arxivlang");
        db.OlympiadEvents.Remove(e);
        audit.Record("olympiad_event", e.Id.ToString(), "delete", $"Olimpiada o'chirildi: {e.Title}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ======================= Ishtirokchilar (tadbir ichida) =======================

    [HttpGet("events/{id:guid}/registrations")]
    public async Task<List<OlympiadRegistrationDto>> Registrations(Guid id, [FromQuery] string? status, [FromQuery] int? grade,
        [FromQuery] string? school, [FromQuery] string? q, CancellationToken ct) =>
        await OlympiadQueries.RegistrationsAsync(db, id, new(status, grade, school, q), ct);

    /// <summary>CRM'da qo'lda qo'shish: mavjud bola yoki yangi bola. Muddat tekshirilmaydi, limit — tekshiriladi.</summary>
    [HttpPost("events/{id:guid}/registrations")]
    public async Task<ActionResult<OlympiadRegistrationDto>> AddRegistration(Guid id, OlympiadManualRegistrationInput input, CancellationToken ct)
    {
        if (!await db.OlympiadEvents.AnyAsync(e => e.Id == id, ct)) return NotFound(new { message = "Tadbir topilmadi" });
        Guid participantId;
        if (input.ParticipantId is { } pid)
        {
            if (!await db.OlympiadParticipants.AnyAsync(p => p.Id == pid, ct)) return NotFound(new { message = "Ishtirokchi topilmadi" });
            participantId = pid;
        }
        else if (input.Participant is { } p)
        {
            if (ValidateParticipant(p) is { } err) return BadRequest(new { message = err });
            var now = AppClock.Now;
            var kid = new OlympiadParticipant
            {
                FullName = OlympiadText.CleanSpaces(p.FullName), Phone = OlympiadText.NormalizePhone(p.Phone)!, Grade = p.Grade,
                School = OlympiadText.CleanSpaces(p.School), CreatedAt = now, UpdatedAt = now,
            };
            db.OlympiadParticipants.Add(kid);
            await db.SaveChangesAsync(ct);
            participantId = kid.Id;
        }
        else return BadRequest(new { message = "Ishtirokchini tanlang yoki ma'lumotini kiriting" });

        var outcome = await OlympiadRegistrar.RegisterAsync(db, id, participantId, 0, null, "crm", ignoreWindow: true, ct);
        switch (outcome.Kind)
        {
            case RegisterKind.Duplicate: return Error(409, "Bu bola tadbirga allaqachon yozilgan");
            case RegisterKind.Full: return Error(409, "Joylar tugagan (limit to'lgan)");
            case RegisterKind.Closed: return Error(409, "Tadbir yakunlangan yoki arxivda");
        }
        audit.Record("olympiad_registration", outcome.Registration!.Id.ToString(), "create", "Olimpiadaga qo'lda qo'shildi");
        await db.SaveChangesAsync(ct);
        return (await OlympiadQueries.RegistrationsAsync(db, id, new(RegistrationId: outcome.Registration.Id), ct)).First();
    }

    internal static string? ValidateParticipant(OlympiadParticipantInput p)
    {
        if (OlympiadText.ValidateName(p.FullName) is { } e1) return e1;
        if (OlympiadText.NormalizePhone(p.Phone) is null) return "Telefon raqami noto'g'ri (+998XXXXXXXXX)";
        if (p.Grade is < 1 or > 11) return "Sinf 1 dan 11 gacha";
        if (OlympiadText.ValidateSchool(p.School) is { } e2) return e2;
        return null;
    }

    [HttpPost("registrations/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRegistration(Guid id, CancellationToken ct)
    {
        var (cancelled, promoted) = await OlympiadRegistrar.CancelAsync(db, id, ct);
        if (cancelled is null) return NotFound(new { message = "Yozuv topilmadi yoki allaqachon bekor qilingan" });
        audit.Record("olympiad_registration", id.ToString(), "cancel", "Olimpiada yozuvi bekor qilindi");
        await db.SaveChangesAsync(ct);
        foreach (var p in promoted)
            await OlympiadRegistrar.SendTicketAsync(db, Messenger, p.Id, "promoted_from_waitlist", ct);
        return NoContent();
    }

    [HttpPost("registrations/{id:guid}/promote")]
    public async Task<IActionResult> Promote(Guid id, CancellationToken ct)
    {
        var reg = await OlympiadRegistrar.ForcePromoteAsync(db, id, ct);
        if (reg is null) return NotFound(new { message = "Kutish ro'yxatidagi yozuv topilmadi" });
        audit.Record("olympiad_registration", id.ToString(), "promote", "Kutish ro'yxatidan qo'lda o'tkazildi");
        await db.SaveChangesAsync(ct);
        await OlympiadRegistrar.SendTicketAsync(db, Messenger, reg.Id, "promoted_from_waitlist", ct);
        return NoContent();
    }

    // ======================= Eslatmalar =======================

    private static OlympiadReminderDto ToDto(OlympiadReminder r, OlympiadEvent e) => new(
        r.Id, r.Kind, r.OffsetMinutes, r.SendAt, OlympiadDispatcher.DueAt(r, e), r.Text,
        OlympiadMessenger.ParseAttachments(r.AttachmentsJson), r.Target, r.Status, r.SentAt, r.SentCount);

    [HttpGet("events/{id:guid}/reminders")]
    public async Task<ActionResult<List<OlympiadReminderDto>>> Reminders(Guid id, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        var list = await db.OlympiadReminders.AsNoTracking().Where(r => r.EventId == id).ToListAsync(ct);
        return list.Select(r => ToDto(r, e)).OrderBy(r => r.DueAt).ToList();
    }

    private static string? ValidateReminder(OlympiadReminderInput i, bool standard)
    {
        if (i.OffsetMinutes is null && i.SendAt is null) return "Vaqtni kiriting: aniq sana-vaqt yoki tadbirgacha necha daqiqa";
        if (i.OffsetMinutes is < 1 or > 60 * 24 * 60) return "Oldin yuborish vaqti noto'g'ri";
        if (!standard && string.IsNullOrWhiteSpace(i.Text) && (i.Attachments?.Count ?? 0) == 0) return "Eslatma matnini kiriting";
        return null;
    }

    private static void Apply(OlympiadReminder r, OlympiadReminderInput i)
    {
        r.OffsetMinutes = i.OffsetMinutes;
        r.SendAt = i.OffsetMinutes is null ? i.SendAt : null;
        r.Text = i.Text ?? "";
        r.AttachmentsJson = OlympiadMessenger.ToJson(i.Attachments ?? []);
        r.Target = i.Target is OlympiadTarget.Waitlist or OlympiadTarget.All ? i.Target : OlympiadTarget.Registered;
    }

    [HttpPost("events/{id:guid}/reminders")]
    public async Task<ActionResult<OlympiadReminderDto>> AddReminder(Guid id, OlympiadReminderInput input, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        if (ValidateReminder(input, false) is { } err) return BadRequest(new { message = err });
        var r = new OlympiadReminder { EventId = id, Kind = OlympiadReminderKind.Custom };
        Apply(r, input);
        db.OlympiadReminders.Add(r);
        await db.SaveChangesAsync(ct);
        return ToDto(r, e);
    }

    [HttpPut("reminders/{id:guid}")]
    public async Task<ActionResult<OlympiadReminderDto>> UpdateReminder(Guid id, OlympiadReminderInput input, CancellationToken ct)
    {
        var r = await db.OlympiadReminders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "Eslatma topilmadi" });
        if (r.Status == "sent") return Error(409, "Eslatma allaqachon yuborilgan");
        if (ValidateReminder(input, r.Kind != OlympiadReminderKind.Custom) is { } err) return BadRequest(new { message = err });
        Apply(r, input);
        r.Status = "scheduled";
        await db.SaveChangesAsync(ct);
        var e = await db.OlympiadEvents.AsNoTracking().FirstAsync(x => x.Id == r.EventId, ct);
        return ToDto(r, e);
    }

    /// <summary>Eslatmani o'chirish/yoqish: standart eslatma o'chirilmaydi, faqat "cancelled" qilinadi.</summary>
    [HttpPost("reminders/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleReminder(Guid id, CancellationToken ct)
    {
        var r = await db.OlympiadReminders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "Eslatma topilmadi" });
        if (r.Status == "sent") return Error(409, "Eslatma allaqachon yuborilgan");
        r.Status = r.Status == "cancelled" ? "scheduled" : "cancelled";
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("reminders/{id:guid}")]
    public async Task<IActionResult> DeleteReminder(Guid id, CancellationToken ct)
    {
        var r = await db.OlympiadReminders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "Eslatma topilmadi" });
        db.OlympiadReminders.Remove(r);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ======================= Statistika =======================

    [HttpGet("events/{id:guid}/stats")]
    public async Task<IActionResult> Stats(Guid id, CancellationToken ct)
    {
        var regs = await (from r in db.OlympiadRegistrations.AsNoTracking()
                          join p in db.OlympiadParticipants.AsNoTracking() on r.ParticipantId equals p.Id
                          where r.EventId == id && r.Status != OlympiadRegStatus.Cancelled
                          select new { r.CreatedAt, r.Status, p.Grade, p.School }).ToListAsync(ct);
        var holding = regs.Where(r => r.Status != OlympiadRegStatus.Waitlist).ToList();
        var attended = regs.Count(r => r.Status == OlympiadRegStatus.Attended);
        return Ok(new
        {
            total = regs.Count,
            registered = holding.Count,
            waitlist = regs.Count - holding.Count,
            attended,
            attendancePercent = holding.Count == 0 ? (decimal?)null : Math.Round(attended * 100m / holding.Count, 1),
            byDay = regs.GroupBy(r => DateOnly.FromDateTime(r.CreatedAt)).OrderBy(g => g.Key)
                .Select(g => new { date = g.Key, count = g.Count() }),
            byGrade = regs.GroupBy(r => r.Grade).OrderBy(g => g.Key).Select(g => new { grade = g.Key, count = g.Count() }),
            bySchool = regs.GroupBy(r => r.School).OrderByDescending(g => g.Count()).Take(20)
                .Select(g => new { school = g.Key, count = g.Count() }),
        });
    }

    // ======================= Excel eksport =======================

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        [OlympiadRegStatus.Registered] = "Ro'yxatda", [OlympiadRegStatus.Waitlist] = "Kutishda",
        [OlympiadRegStatus.Attended] = "Keldi", [OlympiadRegStatus.NoShow] = "Kelmadi", [OlympiadRegStatus.Cancelled] = "Bekor qilingan",
    };

    [HttpGet("events/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string? kind, [FromQuery] string? status, [FromQuery] int? grade,
        [FromQuery] string? school, [FromQuery] string? q, CancellationToken ct)
    {
        var e = await db.OlympiadEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { message = "Tadbir topilmadi" });
        var rows = await OlympiadQueries.RegistrationsAsync(db, id, new(status, grade, school, q), ct);
        byte[] bytes;
        string name;
        switch (kind)
        {
            case "results":
                rows = rows.Where(r => r.Status != OlympiadRegStatus.Cancelled).OrderBy(r => r.Rank ?? int.MaxValue).ToList();
                bytes = ExcelExport.Build("Natijalar",
                    ["O'rin", "Raqam", "F.I.Sh", "Sinf", "Maktab", "Telefon", "Ball", "Foiz", "Holat", "Yuborildi"],
                    rows.Select(r => (IReadOnlyList<string>)[
                        r.Rank?.ToString() ?? "", r.Number, r.FullName, r.Grade.ToString(), r.School, r.Phone,
                        r.Score is { } s ? OlympiadText.Num(s) : "", r.Percent is { } p ? OlympiadText.Num(p) + "%" : "",
                        StatusLabels.GetValueOrDefault(r.Status, r.Status), r.ResultSentAt?.ToString("dd.MM.yyyy HH:mm") ?? ""]));
                name = "natijalar";
                break;
            case "checkin":
                bytes = ExcelExport.Build("Kirish",
                    ["Raqam", "F.I.Sh", "Sinf", "Maktab", "Telefon", "Holat", "Kirgan vaqti", "Kim belgiladi", "Usul"],
                    rows.Where(r => r.Status != OlympiadRegStatus.Cancelled && r.Status != OlympiadRegStatus.Waitlist)
                        .Select(r => (IReadOnlyList<string>)[
                            r.Number, r.FullName, r.Grade.ToString(), r.School, r.Phone, StatusLabels.GetValueOrDefault(r.Status, r.Status),
                            r.CheckedInAt?.ToString("dd.MM.yyyy HH:mm") ?? "", r.CheckedInBy, r.CheckinMethod == "qr" ? "QR" : r.CheckinMethod == "manual" ? "Qo'lda" : ""]));
                name = "kirish";
                break;
            default:
                bytes = ExcelExport.Build("Ishtirokchilar",
                    ["Raqam", "F.I.Sh", "Telefon", "Sinf", "Maktab", "Holat", "Navbat", "Ota-ona (Telegram)", "Username", "Yozilgan", "Nizom tasdiqlandi", "Manba"],
                    rows.Select(r => (IReadOnlyList<string>)[
                        r.Number, r.FullName, r.Phone, r.Grade.ToString(), r.School, StatusLabels.GetValueOrDefault(r.Status, r.Status),
                        r.WaitlistPosition > 0 ? r.WaitlistPosition.ToString() : "", r.ParentName, r.ParentUsername.Length > 0 ? "@" + r.ParentUsername : "",
                        r.CreatedAt.ToString("dd.MM.yyyy HH:mm"), r.RulesAcceptedAt?.ToString("dd.MM.yyyy HH:mm") ?? "",
                        r.CreatedVia == "crm" ? "CRM" : "Bot"]));
                name = "ishtirokchilar";
                break;
        }
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"olimpiada-M{e.Code}-{name}.xlsx");
    }
}
