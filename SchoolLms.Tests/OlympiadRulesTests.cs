using SchoolLms.Application.Olympiad;
using Xunit;

namespace SchoolLms.Tests;

/// <summary>Olimpiada — sof qoidalar (bazasiz): o'rin, telefon, ism, shablon, raqam, foiz. docs/modules/olympiad.md.</summary>
public class OlympiadRulesTests
{
    [Fact]
    public void Teng_ballga_bir_xil_orin_keyingisi_sakraydi()
    {
        var ranks = OlympiadText.Ranks(new[] { ("a", 90m), ("b", 80m), ("c", 80m), ("d", 70m), ("e", 95m) });
        Assert.Equal(1, ranks["e"]);
        Assert.Equal(2, ranks["a"]);
        Assert.Equal(3, ranks["b"]);
        Assert.Equal(3, ranks["c"]);
        Assert.Equal(5, ranks["d"]);
    }

    [Theory]
    [InlineData("+998 90 123 45 67", "+998901234567")]
    [InlineData("998901234567", "+998901234567")]
    [InlineData("90 123-45-67", "+998901234567")]
    [InlineData("(90) 123 45 67", "+998901234567")]
    [InlineData("12345", null)]
    [InlineData("+7 900 123 45 67", null)]
    [InlineData("", null)]
    public void Telefon_998_formatiga_keltiriladi(string input, string? expected) =>
        Assert.Equal(expected, OlympiadText.NormalizePhone(input));

    [Theory]
    [InlineData("Karimov Aziz", true)]
    [InlineData("  Karimov   Aziz  ", true)]
    [InlineData("Aziz", false)]
    [InlineData("Ab", false)]
    [InlineData("Aziz 2", false)]
    public void Ism_kamida_ikki_soz(string name, bool ok) => Assert.Equal(ok, OlympiadText.ValidateName(name) is null);

    [Fact]
    public void Ochirilgan_qism_qatori_butunlay_olib_tashlanadi_va_qiymat_ekranlanadi()
    {
        var html = OlympiadText.Render("Natija: **{bola}**\nBall: {ball}\nO'rin: {orin}\n\nRahmat",
            new Dictionary<string, string?> { ["bola"] = "<Ali>", ["ball"] = "80", ["orin"] = "2" }, ["orin"]);
        Assert.Equal("Natija: <b>&lt;Ali&gt;</b>\nBall: 80\n\nRahmat", html);
    }

    [Theory]
    [InlineData("M3-0012", 3, 12)]
    [InlineData("m3-12", 3, 12)]
    [InlineData("12", 3, 12)]
    [InlineData("M4-0012", 3, null)]
    [InlineData("abc", 3, null)]
    public void Ishtirokchi_raqami_oqiladi(string text, int code, int? expected) =>
        Assert.Equal(expected, OlympiadText.ParseNumber(text, code));

    [Fact]
    public void Raqam_formati_va_foiz()
    {
        Assert.Equal("M7-0003", OlympiadText.Number(7, 3));
        Assert.Equal("—", OlympiadText.Number(7, 0));
        Assert.Equal(66.7m, OlympiadText.Percent(20, 30));
    }

    [Fact]
    public void Qr_token_tasodifiy_va_128_bit()
    {
        var a = OlympiadText.NewQrToken();
        Assert.Equal(22, a.Length);
        Assert.NotEqual(a, OlympiadText.NewQrToken());
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('+', a);
    }

    [Fact]
    public void Uzun_matn_4096_dan_bolaklarga_bolinadi()
    {
        var html = string.Join("\n", Enumerable.Repeat(new string('a', 100), 100));
        var parts = OlympiadMessenger.Split(html);
        Assert.True(parts.Count >= 3);
        Assert.All(parts, p => Assert.True(p.Length <= OlympiadMessenger.TextLimit));
    }

    [Fact]
    public void Sertifikat_va_qr_karta_yaratiladi()
    {
        var pdf = OlympiadDocuments.Certificate(new OlympiadCertificateModel(
            "Karimov Aziz", "5", "Yaypan 5-maktab", "Matematika olimpiadasi", "20-noyabr, 2026", "87", "100", "87", 2, "M1-0004"), png: false);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var png = OlympiadDocuments.QrCard("tok", "Karimov Aziz", "M1-0004", "Matematika olimpiadasi");
        Assert.Equal(0x89, png[0]);
    }
}
