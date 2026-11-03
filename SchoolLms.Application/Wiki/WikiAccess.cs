using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Wiki;

/// <summary>
/// Bilimlar bazasidagi so'rov egasi.
/// <para><see cref="IsLeader"/> — RAHBARIYAT: admin, superadmin yoki HR'da «direktor» (faol `HrEmployee.HrRole =
/// director`, «Topshiriqlar → Xodimlar» da belgilanadi). Rahbariyat HAMMA xodimning eslatmalarini o'qiydi, ularga
/// sahifa/bo'lim qo'shadi, qulf qo'yadi/oladi va qulflangan eslatmani tahrirlaydi.</para>
/// <para>Boshqa hech kim (menejer ham) birovning eslatmasini ko'rmaydi — mijoz tanlovi, 2026-11-03.</para>
/// </summary>
public sealed record WikiViewer(string UserId, string Name, bool IsLeader)
{
    /// <summary>Shu odamning shaxsiy eslatmalarini o'qiy oladimi.</summary>
    public bool CanSeeNotesOf(string ownerUserId) => ownerUserId == UserId || IsLeader;
}

/// <summary>So'rov egasini aniqlash (IO). Qoidalarning sof qismi — <see cref="WikiRules"/>.</summary>
public static class WikiAccess
{
    public static bool IsLeaderRole(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin);

    public static async Task<WikiViewer> ResolveAsync(IAppDbContext db, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
        if (IsLeaderRole(user)) return new WikiViewer(userId, name, true);
        if (userId.Length == 0) return new WikiViewer(userId, name, false);

        var director = await db.HrEmployees.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.IsActive && e.HrRole == HrRoles.Director, ct);
        return new WikiViewer(userId, name, director);
    }
}
