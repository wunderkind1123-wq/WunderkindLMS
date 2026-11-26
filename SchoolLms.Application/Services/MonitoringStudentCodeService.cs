using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Doimiy shaxsiy test kodlari (curriculum-monitoring.md §2.9): o'quvchiga bir marta beriladi va
/// har oylik testda ishlaydi — test kuni nazoratchi doskaga yozgan xona PIN'i bilan birga.
/// </summary>
public static class MonitoringStudentCodeService
{
    /// <summary>Faol o'quvchilar va kodlari — sinf bo'yicha (bo'sh — hamma), chop etish uchun.</summary>
    public static async Task<List<MonitoringStudentCodeDto>> ListAsync(IAppDbContext db, string? className, CancellationToken ct)
    {
        var q = db.Students.AsNoTracking().Where(s => !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(className)) q = q.Where(s => s.ClassName == className);
        var pupils = await q.OrderBy(s => s.ClassName).ThenBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct);
        var ids = pupils.Select(p => p.Id).ToList();
        var codes = await db.MonitoringStudentCodes.AsNoTracking().Where(c => ids.Contains(c.StudentId))
            .ToDictionaryAsync(c => c.StudentId, c => c.Code, ct);
        return [.. pupils.Select(p => new MonitoringStudentCodeDto(p.Id, p.FullName, p.ClassName, codes.GetValueOrDefault(p.Id)))];
    }

    /// <summary>
    /// Kodlar Excel'da — har sinf alohida varaqda (sinf rahbariga tarqatish uchun) va "Ko'rsatma"
    /// varag'i: qayerga kiriladi, PIN qayerdan olinadi. Kodi yo'q o'quvchi ham ro'yxatda (bo'sh kod).
    /// </summary>
    public static byte[] Export(List<MonitoringStudentCodeDto> rows, string link)
    {
        var sheets = rows.GroupBy(r => r.ClassName)
            .OrderBy(g => Grade(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ExcelExport.SheetSpec(SheetName(g.Key), ["№", "O'quvchi", "Sinf", "Test kodi"],
                g.Select((r, i) => (IReadOnlyList<string>)[(i + 1).ToString(), r.StudentName, r.ClassName, r.Code ?? ""]).ToList()))
            .ToList();
        sheets.Add(new ExcelExport.SheetSpec("Ko'rsatma", ["Nima", "Izoh"],
        [
            ["Qayerga kiriladi", link],
            ["Test kodi", "Har o'quvchiga bir marta beriladi va har oylik testda ishlaydi. Yo'qolsa — LMS: Ichki monitoring → Doimiy kodlar → «Yangi kod»."],
            ["Xona PIN", "Har test sessiyasining o'z PIN'i bor. LMS: Ichki monitoring → Sessiyalar → sessiyani oching → sariq «Xona PIN» kartasi. Test boshida nazoratchi doskaga yozadi."],
            ["Kirish", "O'quvchi kodini va doskadagi PIN'ni kiritadi, ismini tasdiqlaydi va testni boshlaydi."],
        ]));
        return ExcelExport.Build(sheets);
    }

    /// <summary>Excel varaq nomi: 31 belgigacha, <c>[]:*?/\</c> belgilarsiz.</summary>
    private static string SheetName(string className)
    {
        var name = new string([.. (string.IsNullOrWhiteSpace(className) ? "Sinfsiz" : className).Select(c => "[]:*?/\\".Contains(c) ? '-' : c)]);
        return name.Length > 31 ? name[..31] : name;
    }

    /// <summary>"10-A" → 10 (sinflar 5, 6 … 10, 11 tartibida; raqamsiz — oxirida).</summary>
    private static int Grade(string className)
    {
        var digits = new string([.. className.TakeWhile(char.IsDigit)]);
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    /// <summary>Kodi yo'q faol o'quvchilarga kod beradi; bor kodlarga tegmaydi. Nechta berilganini qaytaradi.</summary>
    public static async Task<int> GenerateMissingAsync(IAppDbContext db, CancellationToken ct)
    {
        var missing = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && !db.MonitoringStudentCodes.Any(c => c.StudentId == s.Id))
            .Select(s => s.Id).ToListAsync(ct);
        if (missing.Count == 0) return 0;
        var taken = await MonitoringService.TakenCodesAsync(db, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var id in missing)
        {
            string code;
            do code = MonitoringService.NewCode(); while (!taken.Add(code));
            db.MonitoringStudentCodes.Add(new MonitoringStudentCode { StudentId = id, Code = code, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    /// <summary>Yangi kod (yo'qolgan yoki boshqaga ma'lum bo'lib qolgan kod o'rniga) — eskisi darhol ishlamaydi.</summary>
    public static async Task<string> RegenerateAsync(IAppDbContext db, string studentId, CancellationToken ct)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId && !s.IsArchived, ct))
            throw new MonitoringException("O'quvchi topilmadi", 404);
        var taken = await MonitoringService.TakenCodesAsync(db, ct);
        string code;
        do code = MonitoringService.NewCode(); while (!taken.Add(code));
        var row = await db.MonitoringStudentCodes.FirstOrDefaultAsync(c => c.StudentId == studentId, ct);
        if (row is null) db.MonitoringStudentCodes.Add(new MonitoringStudentCode { StudentId = studentId, Code = code, CreatedAt = DateTimeOffset.UtcNow });
        else
        {
            row.Code = code;
            row.CreatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return code;
    }
}
