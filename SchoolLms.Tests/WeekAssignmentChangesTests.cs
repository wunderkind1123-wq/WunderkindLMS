using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

/// <summary>
/// <see cref="WeekAssignmentChanges"/>: hafta biriktirishlarini saqlashda faqat o'zgarayotgan
/// haftalar tekshiriladi — sahifa yuborgan o'zgarmagan o'tgan haftalar emas.
/// </summary>
public class WeekAssignmentChangesTests
{
    private static List<WeekAssignmentDto> Changed(
        (int, string?)[] existing, params WeekAssignmentDto[] wanted) =>
        WeekAssignmentChanges.Changed(existing, wanted);

    [Fact]
    public void Ozgarmagan_otgan_haftalar_qaytmaydi_yangi_va_almashganlari_qaytadi()
    {
        var result = Changed(
            [(1, "eski"), (2, "eski"), (3, "eski")],
            new(1, "eski"), new(2, "eski"), new(3, "yangi"), new(4, "yangi"));

        Assert.Equal([new(3, "yangi"), new(4, "yangi")], result);
    }

    [Fact]
    public void Otgan_haftaning_shabloni_almashsa_qaytadi()
    {
        var result = Changed([(1, "eski"), (2, "yangi")], new(1, "boshqa"), new(2, "yangi"));

        Assert.Equal([new WeekAssignmentDto(1, "boshqa")], result);
    }

    [Fact]
    public void Nullga_boshatish_ozgarish_hisoblanadi()
    {
        Assert.Equal([new WeekAssignmentDto(2, null)], Changed([(1, "a"), (2, "a")], new(1, "a"), new(2, null)));
        // null → shablon ham o'zgarish.
        Assert.Equal([new WeekAssignmentDto(1, "a")], Changed([(1, null)], new WeekAssignmentDto(1, "a")));
    }

    [Fact]
    public void Hech_narsa_ozgarmasa_bosh()
    {
        Assert.Empty(Changed([(1, "a"), (2, null)], new(1, "a"), new(2, null)));
    }

    [Fact]
    public void Olib_tashlanayotgan_haftalar_qaytmaydi()
    {
        Assert.Empty(Changed([(1, "a"), (2, "a")], new WeekAssignmentDto(1, "a")));
    }

    [Fact]
    public void Avval_biriktirish_yoq_bolsa_hammasi_qaytadi()
    {
        var result = Changed([], new(1, "a"), new(2, null));

        Assert.Equal([new(1, "a"), new(2, null)], result);
    }

    [Fact]
    public void Shablon_idsi_ordinal_solishtiriladi()
    {
        Assert.Equal([new WeekAssignmentDto(1, "ABC")], Changed([(1, "abc")], new WeekAssignmentDto(1, "ABC")));
    }
}
