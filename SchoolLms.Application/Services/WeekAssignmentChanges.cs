using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

/// <summary>
/// Hafta biriktirishlarini saqlashda haqiqatan O'ZGARAYOTGAN haftalar.
///
/// <para>
/// Jadval sahifasi chorakning BARCHA haftalarini yuboradi — o'tgan haftalarni ham, o'zgarmagan
/// holda. Tekshiruvlar (bloklar 3-qoidasi, o'quvchi ziddiyati G-11) faqat o'zgarayotgan haftalarga
/// qaralishi kerak: aks holda o'tgan haftadagi eski shablon (keyinchalik o'sha soatga blok
/// qo'yilgan) butun saqlashni rad etadi, garchi tarixga hech kim tegmayotgan bo'lsa ham.
/// </para>
/// </summary>
public static class WeekAssignmentChanges
{
    /// <summary>
    /// <paramref name="wanted"/> dan faqat yangi haftalar yoki shabloni mavjudidan farq qiladigan
    /// haftalar. Olib tashlanayotgan haftalar qaytmaydi — ular hech qanday dars qo'shmaydi.
    /// </summary>
    public static List<WeekAssignmentDto> Changed(
        IEnumerable<(int Week, string? TemplateId)> existing, IEnumerable<WeekAssignmentDto> wanted)
    {
        var was = new Dictionary<int, string?>();
        foreach (var (week, templateId) in existing) was.TryAdd(week, templateId);

        return [.. wanted.Where(a =>
            !was.TryGetValue(a.Week, out var before) || !string.Equals(before, a.TemplateId, StringComparison.Ordinal))];
    }
}
