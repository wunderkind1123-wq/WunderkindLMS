using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

// ===========================================================================
//  Ish reja — aniq dars sanasi (2026-11-13 talabi): "Sana" ustuni, Word jadvali,
//  chorakdan tashqaridagi sana — xato, bayram / yakshanba — ogohlantirish.
//  Misol: M. Qosimova, ona tili, 2-chorak (09.11–26.12): 10.11 va 14.11 darslari
//  ikkalasi ham 09.11 bo'lib saqlanardi; 29.12 dagi dars jimgina 21.12 ga surilardi.
// ===========================================================================

public class CurriculumDatesTests
{
    private static readonly (int Quarter, DateOnly Start, DateOnly End)[] Quarters =
    [
        (1, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 31)),
        (2, new DateOnly(2026, 11, 9), new DateOnly(2026, 12, 26)),
        (3, new DateOnly(2027, 1, 11), new DateOnly(2027, 3, 20)),
        (4, new DateOnly(2027, 3, 30), new DateOnly(2027, 5, 25)),
    ];

    private static readonly Dictionary<DateOnly, string> NoHolidays = [];

    private static (List<CurriculumService.ParsedTopic> Topics, List<string> Errors, List<string> Warnings) Run(
        string[][] rows, IReadOnlyDictionary<DateOnly, string>? holidays = null)
    {
        var (parsed, errors) = CurriculumService.ParseRows(rows);
        Assert.Empty(errors);
        return CurriculumService.ResolveDates(parsed, Quarters, holidays ?? NoHolidays);
    }

    private static readonly string[] Header = CurriculumService.Columns;

    [Fact]
    public void Sana_ustuni_aniq_sana_bo_lib_saqlanadi_hafta_sanadan_hisoblanadi()
    {
        var (topics, errors, warnings) = Run(
        [
            Header,
            ["1", "2", "1", "10.11", "", "Ot", "1", "", ""],
            ["2", "2", "1", "14.11", "", "Sifat", "1", "", ""],
            ["3", "", "", "1.12.2026", "", "Son", "1", "", ""],
            ["4", "2", "3", "", "", "Olmosh", "1", "", ""],
        ]);
        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.Equal(new DateOnly(2026, 11, 10), topics[0].PlannedOn);
        Assert.Equal(new DateOnly(2026, 11, 14), topics[1].PlannedOn);
        Assert.Equal((2, 1), (topics[1].Quarter!.Value, topics[1].Week!.Value));
        // no quarter in the file — taken from the date; week 1 = 09.11, week 4 = 30.11
        Assert.Equal(new DateOnly(2026, 12, 1), topics[2].PlannedOn);
        Assert.Equal((2, 4), (topics[2].Quarter!.Value, topics[2].Week!.Value));
        // no date — stays a quarter/week estimate
        Assert.Null(topics[3].PlannedOn);
        Assert.Equal(3, topics[3].Week);
    }

    [Fact]
    public void Yil_yozilmasa_o_quv_yilidan_olinadi_yanvar_keyingi_yil()
    {
        var (topics, errors, _) = Run([Header, ["1", "", "", "12.01", "", "Takrorlash", "1", "", ""]]);
        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2027, 1, 12), topics[0].PlannedOn);
        Assert.Equal(3, topics[0].Quarter);
    }

    [Fact]
    public void Excel_sana_katagi_yozilgan_yilni_emas_o_quv_yilini_oladi()
    {
        // typed "14.01" in Nov 2026 → Excel stored 14.01.2026 (serial 46036)
        var serial = new DateTime(2026, 1, 14).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (topics, errors, _) = Run([Header, ["1", "", "", serial, "", "Takrorlash", "1", "", ""]]);
        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2027, 1, 14), topics[0].PlannedOn);
    }

    [Fact]
    public void Chorakdan_tashqaridagi_sana_xato_suriladi_emas()
    {
        var (parsed, _) = CurriculumService.ParseRows([Header, ["1", "2", "8", "29.12", "", "Bayram oldi darsi", "1", "", ""]]);
        var (_, errors, _) = CurriculumService.ResolveDates(parsed, Quarters, NoHolidays);
        Assert.Equal("2-qator: 29.12 sanasi 2-chorakdan (09.11–26.12) tashqarida", Assert.Single(errors));

        // without a quarter: a date in the holidays between quarters
        (parsed, _) = CurriculumService.ParseRows([Header, ["1", "", "", "03.01", "", "Ta'tilda", "1", "", ""]]);
        (_, errors, _) = CurriculumService.ResolveDates(parsed, Quarters, NoHolidays);
        Assert.StartsWith("2-qator: 03.01 sanasi hech bir chorakka tushmaydi", Assert.Single(errors));
    }

    [Fact]
    public void Bayram_va_yakshanba_ogohlantirish_saqlashga_to_sqinlik_qilmaydi()
    {
        var holidays = new Dictionary<DateOnly, string> { [new DateOnly(2026, 12, 8)] = "Konstitutsiya kuni" };
        var (topics, errors, warnings) = Run(
        [
            Header,
            ["1", "2", "", "08.12", "", "Bayramdagi dars", "1", "", ""],
            ["2", "2", "", "15.11", "", "Yakshanbadagi dars", "1", "", ""],
        ], holidays);
        Assert.Empty(errors);
        Assert.Equal(2, topics.Count(t => t.PlannedOn is not null));
        Assert.Equal(["2-qator: 08.12 — dam olish kuni (Konstitutsiya kuni)", "3-qator: 15.11 — yakshanba"], warnings);
    }

    [Fact]
    public void Haftasi_chorakdan_oshgan_sanasiz_mavzu_ogohlantiradi()
    {
        var (_, errors, warnings) = Run([Header, ["1", "2", "8", "", "", "Mavzu", "1", "", ""]]);
        Assert.Empty(errors);
        Assert.Contains("8-hafta", Assert.Single(warnings));
    }

    [Fact]
    public void Choraklar_yo_q_bo_lsa_yilsiz_sana_xato_to_liq_sana_qabul()
    {
        var (parsed, _) = CurriculumService.ParseRows([Header, ["1", "", "", "10.11", "", "A", "1", "", ""], ["2", "", "", "10.11.2026", "", "B", "1", "", ""]]);
        var (topics, errors, _) = CurriculumService.ResolveDates(parsed, [], NoHolidays);
        Assert.Contains("yil yo'q", Assert.Single(errors));
        Assert.Equal(new DateOnly(2026, 11, 10), Assert.Single(topics).PlannedOn);
    }

    [Fact]
    public void Noto_g_ri_sana_qator_raqami_bilan_xato()
    {
        var (_, errors) = CurriculumService.ParseRows([Header, ["1", "", "", "ertaga", "", "A", "1", "", ""]]);
        Assert.StartsWith("2-qator: «Sana» noto'g'ri", Assert.Single(errors));
        var (parsed, _) = CurriculumService.ParseRows([Header, ["1", "", "", "31.11", "", "A", "1", "", ""]]);
        var (_, dateErrors, _) = CurriculumService.ResolveDates(parsed, Quarters, NoHolidays);
        Assert.Contains("bunday kun yo'q", Assert.Single(dateErrors));
    }

    [Theory]
    [InlineData("10.11", 10, 11, null)]
    [InlineData("1.09", 1, 9, null)]
    [InlineData("10.11.2026", 10, 11, 2026)]
    [InlineData("10/11/26", 10, 11, 2026)]
    [InlineData("2026-11-10", 10, 11, 2026)]
    [InlineData("10.11\n12.11", 10, 11, null)]
    public void Sana_formatlari(string text, int day, int month, int? year)
    {
        var d = CurriculumService.ParseDate(text)!;
        Assert.Equal((day, month, year), (d.Day, d.Month, d.Year));
    }

    [Fact]
    public void Eski_tartib_sarlavhasiz_va_sarlavha_nomi_bo_yicha_ustunlar()
    {
        // an old file without a header row — legacy column order
        var (old, errors) = CurriculumService.ParseRows([["1", "1", "2", "Bo'lim", "Mavzu A", "2", "Maqsad", "Uy"]]);
        Assert.Empty(errors);
        var a = Assert.Single(old);
        Assert.Equal((1, 2, "Bo'lim", "Mavzu A", 2, "Maqsad", "Uy"), (a.Quarter!.Value, a.Week!.Value, a.Section, a.Title, a.Hours, a.Goal, a.Homework));
        Assert.Null(a.Date);

        // any order, a title line above the table, "1 soat", "1." numbering
        var (topics, errors2) = CurriculumService.ParseRows(
        [
            ["5-sinf ona tili ish rejasi (2026–2027)"],
            ["T/r", "Dars mavzusi", "Soati", "Sana", "O'tilgan sana", "Izoh"],
            ["1.", "Ot", "1 soat", "10.11", "11.11", ""],
            ["2.", "Jamiyat va inson", "1", "", "", ""],
            ["Jami:", "", "2", "", "", ""],
        ]);
        Assert.Empty(errors2);
        Assert.Equal(["Ot", "Jamiyat va inson"], topics.Select(x => x.Title));
        var t = topics[0];
        Assert.Equal((1, "Ot", 1, 10, 11), (t.Order, t.Title, t.Hours, t.Date!.Day, t.Date.Month));
    }

    [Fact]
    public void Word_jadvali_o_qiladi_birlashgan_kataklar_va_chorak_qatori_bilan()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var table = new Table(
                Row(Cell("№"), Cell("Chorak"), Cell("Mavzu"), Cell("Soat"), Cell("Sana")),
                Row(Cell("II chorak", span: 5)),
                Row(Cell("1"), Cell("2", vMerge: MergedCellValues.Restart), Cell("Ot"), Cell("1"), Cell("10.11")),
                Row(Cell("2"), Cell("", vMerge: MergedCellValues.Continue), Cell("Sifat"), Cell("2"), Cell("14.11", "16.11")),
                Row(Cell("Jami:", span: 3), Cell("3"), Cell("")));
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Ona tili ish reja"))), table));
        }
        ms.Position = 0;

        var (parsed, errors) = CurriculumService.Parse(ms, "reja.docx");
        Assert.Empty(errors);
        var (topics, dateErrors, _) = CurriculumService.ResolveDates(parsed, Quarters, NoHolidays);
        Assert.Empty(dateErrors);
        Assert.Equal(["Ot", "Sifat"], topics.Select(t => t.Title));
        Assert.All(topics, t => Assert.Equal(2, t.Quarter));
        Assert.Equal([new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 14)], topics.Select(t => t.PlannedOn!.Value));
        Assert.Equal(2, topics[1].Hours);

        static TableRow Row(params TableCell[] cells) => new(cells);
        static TableCell Cell(string text, string? line2 = null, int span = 1, MergedCellValues? vMerge = null)
        {
            var props = new TableCellProperties();
            if (span > 1) props.Append(new GridSpan { Val = span });
            if (vMerge is { } vm) props.Append(new VerticalMerge { Val = vm == MergedCellValues.Continue ? null : vm });
            var run = new Run(new Text(text));
            if (line2 is not null) { run.Append(new Break()); run.Append(new Text(line2)); }
            return new TableCell(props, new Paragraph(run));
        }
    }

    [Fact]
    public void Word_jadvalsiz_fayl_tushunarli_xato()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
            doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text("Matn")))));
        ms.Position = 0;
        var (_, errors) = CurriculumService.Parse(ms, "reja.docx");
        Assert.Equal("Word faylda «Mavzu» ustunli jadval topilmadi", Assert.Single(errors));
    }
}
