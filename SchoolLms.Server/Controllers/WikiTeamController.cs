using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Wiki;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// XODIMLAR ESLATMALARI — klientda HR → Bilimlar bazasi → «Xodimlar» tabi.
/// <list type="bullet">
///   <item><b>Faqat RAHBARIYAT</b> (admin, superadmin, HR «direktor» — <see cref="WikiAccess"/>): barcha xodimlarning
///     eslatmalarini o'qiydi, xodimga yangi sahifa/bo'lim qo'shadi, tahrirlaydi (klient oldin "o'zgartirasizmi?" deb
///     so'raydi), QULFLAYDI (shunda egasi ham faqat o'qiydi) va o'chiradi.</item>
///   <item>Boshqa hamma (menejer ham) — <c>GET members</c> da <c>isLeader = false</c> va bo'sh ro'yxat (tab chiqmaydi),
///     qolgan endpointlar — 404 (eslatma borligi ham oshkor bo'lmasin). Mijoz tanlovi, 2026-11-03.</item>
/// </list>
/// Ruxsat kaliti YO'Q: rahbariyat roli (yoki HR direktorligi) o'zi yetadi. O'z eslatmasi bu yerda emas — <c>MyNotesController</c>.
/// ⚠️ Shaxsiy eslatma tarixga YOZILMAYDI (rahbariyatning amallari ham) — kim oxirgi o'zgartirgani sahifaning o'zida (`UpdatedBy`).
/// Qoidalar: <c>docs/modules/wiki.md</c>.
/// </summary>
[ApiController]
[Authorize]
[NoStore]
[Route("api/admin/wiki/team")]
public class WikiTeamController(AppDbContext db) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private WikiViewer? _viewer;
    private async Task<WikiViewer> ViewerAsync() => _viewer ??= await WikiAccess.ResolveAsync(db, User, HttpContext.RequestAborted);

    private static object NotFoundBody(string what) => new { message = $"{what} topilmadi" };

    /// <summary>Begona (o'zinikidan boshqa) va ko'rinadigan xodim — faqat rahbariyatga.</summary>
    private static bool Visible(WikiViewer v, string userId) =>
        v.IsLeader && WikiRules.IsRealUserId(userId) && userId != v.UserId;

    private static IEnumerable<(string Id, string ParentId, bool Locked)> Shape(IEnumerable<WikiPage> pages) =>
        pages.Select(x => (x.Id, x.ParentId, x.Locked));

    /// <summary>
    /// Barcha faol xodimlar (o'zisiz): ism, lavozim, HR roli, eslatmalar soni va oxirgi yozgan vaqti.
    /// Eslatmasi yo'q xodim ham ro'yxatda (direktor "kim umuman yozmayapti"ni ham ko'rsin).
    /// Rahbariyat bo'lmasa — <c>isLeader = false</c>, bo'sh ro'yxat (klient «Xodimlar» tabini chiqarmaydi).
    /// </summary>
    [HttpGet("members")]
    public async Task<ActionResult<WikiTeamDto>> Members()
    {
        var viewer = await ViewerAsync();
        if (!viewer.IsLeader) return new WikiTeamDto(false, []);

        var employees = await db.HrEmployees.AsNoTracking().Where(e => e.IsActive).ToListAsync();
        var visible = employees.Where(e => Visible(viewer, e.UserId)).ToList();
        var userIds = visible.Select(e => e.UserId).ToList();

        var stats = (await db.WikiPages.AsNoTracking()
                .Where(p => userIds.Contains(p.OwnerUserId))
                .GroupBy(p => p.OwnerUserId)
                // Bo'limlar (papkalar) eslatma sanalmaydi.
                .Select(g => new { UserId = g.Key, Count = g.Count(p => !p.IsSection), Last = g.Max(p => p.UpdatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.UserId);
        var positions = await db.HrPositions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

        var members = visible
            .Select(e => new WikiTeamMemberDto(
                e.UserId, e.FullName,
                e.PositionId is not null && positions.TryGetValue(e.PositionId, out var pos) ? pos : "",
                e.HrRole,
                stats.GetValueOrDefault(e.UserId)?.Count ?? 0,
                stats.GetValueOrDefault(e.UserId)?.Last ?? ""))
            // Yaqinda yozganlar tepada, keyin ism bo'yicha.
            .OrderByDescending(m => m.LastUpdatedAt).ThenBy(m => m.FullName)
            .ToList();

        return new WikiTeamDto(true, members);
    }

    /// <summary>Bitta xodimning eslatmalar daraxti (matnsiz, qulf belgilari bilan).</summary>
    [HttpGet("members/{userId}")]
    public async Task<ActionResult<WikiNotesTreeDto>> MemberTree(string userId)
    {
        var viewer = await ViewerAsync();
        if (!Visible(viewer, userId)) return NotFound(NotFoundBody("Xodim"));
        var name = await MemberNameAsync(userId);
        if (name is null) return NotFound(NotFoundBody("Xodim"));
        var nodes = await db.WikiPages.AsNoTracking().Where(p => p.OwnerUserId == userId)
            .OrderBy(p => p.Order).ThenBy(p => p.CreatedAt)
            .Select(p => new WikiTreeNodeDto(p.Id, p.ParentId, p.Title, p.Icon, p.Order, p.Locked, p.IsSection)).ToListAsync();
        return new WikiNotesTreeDto(userId, name, nodes);
    }

    /// <summary>Bitta eslatma. Klient uni ochiq turganda bir necha soniyada qayta so'raydi — xodim yozayotganini
    /// rahbar "jonli" ko'radi (versiya o'zgarsa matn yangilanadi).</summary>
    [HttpGet("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> Page(string id)
    {
        var viewer = await ViewerAsync();
        var page = await db.WikiPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        var shape = await OwnerShapeAsync(page.OwnerUserId);
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(shape, page.Id));
    }

    /// <summary>Xodimga YANGI sahifa qo'shish (masalan shaxsiy vazifa yoki ko'rsatma). U xodimning o'z daraxtida chiqadi.</summary>
    [HttpPost("members/{userId}/pages")]
    public async Task<ActionResult<WikiPageDto>> CreateForMember(string userId, WikiPageCreatePayload p)
    {
        var viewer = await ViewerAsync();
        if (!Visible(viewer, userId) || await MemberNameAsync(userId) is null) return NotFound(NotFoundBody("Xodim"));
        var pages = await db.WikiPages.Where(x => x.OwnerUserId == userId).ToListAsync();
        if (WikiPageOps.Create(pages, userId, p, Actor, out var page) is { } e) return StatusCode(e.Status, e.Body);
        db.WikiPages.Add(page!);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page!, WikiRules.IsLocked(Shape(pages), page!.ParentId));
    }

    /// <summary>Xodim eslatmasini TAHRIRLASH (qulflangan bo'lsa ham). Versiya tekshiruvi odatdagidek (409).</summary>
    [HttpPut("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> SaveMemberPage(string id, WikiPageSavePayload p)
    {
        var viewer = await ViewerAsync();
        var page = await db.WikiPages.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        if (WikiPageOps.ApplySave(page, p, Actor) is { } e) return StatusCode(e.Status, e.Body);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(await OwnerShapeAsync(page.OwnerUserId), page.Id));
    }

    /// <summary>QULF: eslatma (va ichidagilar) egasining o'ziga ham faqat o'qish bo'ladi.</summary>
    [HttpPut("pages/{id}/lock")]
    public async Task<ActionResult<WikiPageDto>> LockMemberPage(string id, WikiLockPayload p)
    {
        var viewer = await ViewerAsync();
        var page = await db.WikiPages.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        page.Locked = p.Locked;
        page.LockedBy = p.Locked ? Actor : "";
        page.LockedAt = p.Locked ? AppClock.Iso() : "";
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(await OwnerShapeAsync(page.OwnerUserId), page.Id));
    }

    /// <summary>Xodim eslatmasini (ichidagilari bilan) o'chirish — masalan rahbar xato qo'shgan sahifa.</summary>
    [HttpDelete("pages/{id}")]
    public async Task<ActionResult<object>> DeleteMemberPage(string id)
    {
        var viewer = await ViewerAsync();
        var page = await db.WikiPages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        var all = await db.WikiPages.Where(x => x.OwnerUserId == page.OwnerUserId).ToListAsync();
        var subtree = WikiRules.SubtreeIds(all.Select(x => (x.Id, x.ParentId)), id);
        db.WikiPages.RemoveRange(all.Where(x => subtree.Contains(x.Id)));
        await db.SaveChangesAsync();
        return new { deleted = subtree.Count };
    }

    private async Task<string?> MemberNameAsync(string userId) =>
        await db.HrEmployees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.FullName).FirstOrDefaultAsync()
        ?? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync();

    private async Task<List<(string Id, string ParentId, bool Locked)>> OwnerShapeAsync(string ownerUserId) =>
        (await db.WikiPages.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => new { x.Id, x.ParentId, x.Locked }).ToListAsync())
        .Select(x => (x.Id, x.ParentId, x.Locked)).ToList();
}
