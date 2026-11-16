using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolLms.Application.Billing;

namespace SchoolLms.Application.Olympiad;

/// <summary>Sertifikatdagi ma'lumot.</summary>
public sealed record OlympiadCertificateModel(
    string StudentName, string Grade, string School, string EventTitle, string Date,
    string Score, string MaxScore, string Percent, int? Rank, string Number);

/// <summary>
/// QR karta (PNG) va standart sertifikat (PDF yoki PNG). QuestPDF — loyihada allaqachon bor
/// (chek, tashakkurnoma); litsenziya turi <c>ReceiptDocument</c> / <c>ExamDiplomaDocument</c> statik
/// konstruktorida o'rnatiladi, shu yerda ham takrorlanadi.
/// </summary>
public static class OlympiadDocuments
{
    static OlympiadDocuments() => QuestPDF.Settings.License = LicenseType.Community;

    private static readonly Color Gold = Color.FromHex("#FFD006");
    private static readonly Color Muted = Colors.Grey.Darken1;

    /// <summary>O'zbek lotinidagi ʻ/’ belgilarini shriftda albatta bor apostrofga keltiradi.</summary>
    private static string Safe(string? s) => (s ?? "").Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('’', '\'').Replace('‘', '\'');

    /// <summary>QR rasmi, ostida bola ismi, raqam va tadbir nomi (PNG).</summary>
    public static byte[] QrCard(string qrToken, string childName, string number, string eventTitle)
    {
        var qr = ReceiptQr.Png(qrToken);
        var doc = Document.Create(c => c.Page(page =>
        {
            page.Size(90, 120, Unit.Millimetre);
            page.Margin(6, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));
            page.Content().Column(col =>
            {
                col.Spacing(3);
                col.Item().AlignCenter().Width(74, Unit.Millimetre).Image(qr).FitWidth();
                col.Item().AlignCenter().Text(Safe(childName)).Bold().FontSize(13);
                col.Item().AlignCenter().Text(number).Bold().FontSize(16);
                col.Item().AlignCenter().Text(Safe(eventTitle)).FontSize(9).FontColor(Muted);
            });
        }));
        return doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 200 }).First();
    }

    /// <summary>Standart sertifikat: A4 landshaft, logotip, ramka, "SERTIFIKAT", ism katta harfda, ball, sana, imzo joyi.</summary>
    public static byte[] Certificate(OlympiadCertificateModel m, bool png)
    {
        var doc = Document.Create(c => c.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(12, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(13).FontColor(Colors.Black));
            page.Content().Border(4).BorderColor(Gold).Padding(4, Unit.Millimetre)
                .Border(1).BorderColor(Colors.Black).Padding(12, Unit.Millimetre).Column(col =>
                {
                    col.Spacing(8);
                    if (ReceiptQr.LogoJpeg is { } logo)
                        col.Item().AlignCenter().Height(18, Unit.Millimetre).Image(logo).FitHeight();
                    else
                        col.Item().AlignCenter().Text("WUNDERKIND XALQARO MAKTABI").Bold().FontSize(16);

                    col.Item().PaddingTop(4).AlignCenter().Text("SERTIFIKAT").Bold().FontSize(44).LetterSpacing(0.12f);
                    col.Item().AlignCenter().Width(70, Unit.Millimetre).LineHorizontal(2).LineColor(Gold);
                    col.Item().PaddingTop(4).AlignCenter().Text("Ushbu sertifikat").FontSize(13).FontColor(Muted);

                    col.Item().AlignCenter().Text(Safe(m.StudentName).ToUpperInvariant()).Bold().FontSize(30);
                    var who = string.Join(", ", new[] { m.Grade.Length > 0 ? $"{m.Grade}-sinf" : "", Safe(m.School) }
                        .Where(s => s.Length > 0));
                    if (who.Length > 0) col.Item().AlignCenter().Text(who).FontSize(13).FontColor(Muted);

                    col.Item().PaddingTop(6).PaddingHorizontal(18, Unit.Millimetre).AlignCenter().Text(t =>
                    {
                        t.AlignCenter();
                        t.Span($"{m.Date} kuni o'tkazilgan «{Safe(m.EventTitle)}» da ishtirok etib, ");
                        t.Span($"{m.Score} / {m.MaxScore} ball ({m.Percent}%)").Bold();
                        if (m.Rank is { } r)
                        {
                            t.Span(" to'plagani va ");
                            t.Span($"{r}-o'rinni").Bold();
                            t.Span(" egallagani uchun berildi.");
                        }
                        else t.Span(" to'plagani uchun berildi.");
                    });

                    col.Item().ExtendVertical().AlignBottom().Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Column(c2 =>
                        {
                            c2.Item().Text(m.Date).FontSize(11).FontColor(Muted);
                            c2.Item().Text($"№ {m.Number}").FontSize(10).FontColor(Muted);
                        });
                        row.RelativeItem().AlignRight().AlignBottom().Text("Direktor: ____________________").FontSize(12);
                    });
                });
        }));
        return png
            ? doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 150 }).First()
            : doc.GeneratePdf();
    }
}
