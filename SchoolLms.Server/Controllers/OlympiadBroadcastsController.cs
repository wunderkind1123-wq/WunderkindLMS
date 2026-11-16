using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Olympiad;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>Olimpiada → rassilka: qoralama, auditoriya hisobi, darhol yoki rejalashtirilgan yuborish, bekor qilish, hisobot.</summary>
[ApiController]
[Authorize]
[AdminPerm(OlympiadPerms.Manage, GatedRead = true)]
[Route("api/admin/olympiad/broadcasts")]
public sealed class OlympiadBroadcastsController(AppDbContext db, AuditService audit, OlympiadTelegram tg) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";
    private ObjectResult Error(int status, string message) => StatusCode(status, new { message });

    private async Task<List<OlympiadBroadcastDto>> DtosAsync(Guid? onlyId, CancellationToken ct)
    {
        var q = db.OlympiadBroadcasts.AsNoTracking();
        if (onlyId is { } id) q = q.Where(b => b.Id == id);
        var list = await q.OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct);
        var ids = list.Select(b => b.Id).ToList();
        var pending = await db.OlympiadDeliveries.AsNoTracking()
            .Where(d => d.Kind == OlympiadDeliveryKind.Broadcast && ids.Contains(d.RefId) && d.Status == OlympiadDeliveryStatus.Pending)
            .GroupBy(d => d.RefId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return list.Select(b => new OlympiadBroadcastDto(b.Id, b.Title, b.Text,
            OlympiadMessenger.ParseAttachments(b.AttachmentsJson), OlympiadMessenger.ParseButtons(b.ButtonsJson),
            OlympiadAudience.Parse(b.AudienceJson), b.ScheduledAt, b.Status, b.Total, b.Sent, b.Failed, b.Blocked,
            pending.FirstOrDefault(p => p.Key == b.Id)?.Count ?? 0, b.CreatedBy, b.CreatedAt, b.StartedAt, b.FinishedAt)).ToList();
    }

    [HttpGet]
    public async Task<List<OlympiadBroadcastDto>> List(CancellationToken ct) => await DtosAsync(null, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OlympiadBroadcastDto>> Get(Guid id, CancellationToken ct) =>
        (await DtosAsync(id, ct)).FirstOrDefault() is { } b ? b : NotFound(new { message = "Rassilka topilmadi" });

    /// <summary>Telegram cheklovlari: albom ≤ 10 rasm, caption ≤ 1024 (oshsa matn alohida xabar bo'ladi), tugma havolasi http(s).</summary>
    private static string? Validate(OlympiadBroadcastInput i)
    {
        var atts = i.Attachments ?? [];
        if (string.IsNullOrWhiteSpace(i.Text) && atts.Count == 0) return "Matn yoki biriktirma kiriting";
        if (atts.Count > 10) return "Ko'pi bilan 10 ta biriktirma";
        if (atts.Any(a => !a.Url.StartsWith("/uploads/olympiad/", StringComparison.Ordinal))) return "Biriktirma noto'g'ri";
        if ((i.Buttons ?? []).Any(b => string.IsNullOrWhiteSpace(b.Text) || !b.Url.StartsWith("http", StringComparison.Ordinal)))
            return "Tugma uchun matn va http(s):// havola kiriting";
        if ((i.Text ?? "").Length > 4000) return "Matn 4000 belgidan oshmasin";
        return null;
    }

    private static void Apply(OlympiadBroadcast b, OlympiadBroadcastInput i)
    {
        // Nom berilmasa — matnning birinchi qatori.
        var title = string.IsNullOrWhiteSpace(i.Title) ? TelegramHtml.MarkupToPlain(i.Text ?? "").Split('\n')[0].Trim() : i.Title.Trim();
        if (title.Length > 60) title = title[..60] + "…";
        b.Title = title.Length == 0 ? "Rassilka" : title;
        b.Text = i.Text ?? "";
        b.AttachmentsJson = OlympiadMessenger.ToJson(i.Attachments ?? []);
        b.ButtonsJson = OlympiadMessenger.ToJson(i.Buttons ?? []);
        b.AudienceJson = OlympiadMessenger.ToJson(i.Audience ?? new OlympiadAudience());
    }

    [HttpPost]
    public async Task<ActionResult<OlympiadBroadcastDto>> Create(OlympiadBroadcastInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        var b = new OlympiadBroadcast { CreatedAt = AppClock.Now, CreatedBy = ActorName };
        Apply(b, input);
        db.OlympiadBroadcasts.Add(b);
        await db.SaveChangesAsync(ct);
        return (await DtosAsync(b.Id, ct)).First();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OlympiadBroadcastDto>> Update(Guid id, OlympiadBroadcastInput input, CancellationToken ct)
    {
        var b = await db.OlympiadBroadcasts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return NotFound(new { message = "Rassilka topilmadi" });
        if (b.Status is not (OlympiadBroadcastStatus.Draft or OlympiadBroadcastStatus.Scheduled))
            return Error(409, "Yuborilgan rassilkani tahrirlab bo'lmaydi");
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        Apply(b, input);
        await db.SaveChangesAsync(ct);
        return (await DtosAsync(id, ct)).First();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var b = await db.OlympiadBroadcasts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return NotFound(new { message = "Rassilka topilmadi" });
        if (b.Status != OlympiadBroadcastStatus.Draft) return Error(409, "Faqat qoralamani o'chirish mumkin");
        db.OlympiadBroadcasts.Remove(b);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Filtrdan keyin oluvchilar soni.</summary>
    [HttpPost("audience-count")]
    public async Task<IActionResult> AudienceCount(OlympiadAudience audience, CancellationToken ct) =>
        Ok(new { count = (await audience.ResolveAsync(db, ct)).Count });

    /// <summary>Yuborish: <c>scheduledAt</c> bo'sh yoki o'tgan — darhol navbatga; aks holda rejalashtiriladi.</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<OlympiadBroadcastDto>> Send(Guid id, BroadcastSendInput input, CancellationToken ct)
    {
        var b = await db.OlympiadBroadcasts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return NotFound(new { message = "Rassilka topilmadi" });
        if (b.Status is not (OlympiadBroadcastStatus.Draft or OlympiadBroadcastStatus.Scheduled))
            return Error(409, "Rassilka allaqachon yuborilgan");
        if (!tg.IsConfigured) return Error(409, "Bot sozlanmagan — Sozlamalar → Bot");
        if (input.ScheduledAt is { } at && at > AppClock.Now)
        {
            b.ScheduledAt = at;
            b.Status = OlympiadBroadcastStatus.Scheduled;
            audit.Record("olympiad_broadcast", id.ToString(), "schedule", $"Olimpiada rassilkasi rejalashtirildi: {b.Title} ({at:dd.MM.yyyy HH:mm})");
            await db.SaveChangesAsync(ct);
        }
        else
        {
            b.ScheduledAt = null;
            await OlympiadDispatcher.StartBroadcastAsync(db, b, ct);
            audit.Record("olympiad_broadcast", id.ToString(), "send", $"Olimpiada rassilkasi yuborildi: {b.Title} ({b.Total} ta)");
            await db.SaveChangesAsync(ct);
        }
        return (await DtosAsync(id, ct)).First();
    }

    /// <summary>Bekor qilish — hali yuborilmaganlarga bormaydi.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OlympiadBroadcastDto>> Cancel(Guid id, CancellationToken ct)
    {
        var b = await db.OlympiadBroadcasts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return NotFound(new { message = "Rassilka topilmadi" });
        if (b.Status is not (OlympiadBroadcastStatus.Scheduled or OlympiadBroadcastStatus.Sending))
            return Error(409, "Bu rassilkani bekor qilib bo'lmaydi");
        await db.OlympiadDeliveries
            .Where(d => d.Kind == OlympiadDeliveryKind.Broadcast && d.RefId == id && d.Status == OlympiadDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, OlympiadDeliveryStatus.Cancelled), ct);
        b.Status = OlympiadBroadcastStatus.Cancelled;
        b.FinishedAt = AppClock.Now;
        audit.Record("olympiad_broadcast", id.ToString(), "cancel", $"Olimpiada rassilkasi bekor qilindi: {b.Title}");
        await db.SaveChangesAsync(ct);
        return (await DtosAsync(id, ct)).First();
    }

    /// <summary>Yetkazish hisoboti: xato va bloklaganlar ro'yxati.</summary>
    [HttpGet("{id:guid}/deliveries")]
    public async Task<IActionResult> Deliveries(Guid id, [FromQuery] string? status, CancellationToken ct)
    {
        var q = from d in db.OlympiadDeliveries.AsNoTracking()
                join u in db.OlympiadBotUsers.AsNoTracking() on d.BotUserId equals u.Id
                where d.Kind == OlympiadDeliveryKind.Broadcast && d.RefId == id
                select new { d.Status, d.Error, d.SentAt, u.FirstName, u.LastName, u.Username };
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        return Ok(await q.Take(500).ToListAsync(ct));
    }
}
