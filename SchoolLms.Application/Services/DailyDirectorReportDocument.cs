using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Kunlik hisobot — the PDF (DailyDirectorReport builds the numbers). No database, no network: a
//  DailyDirectorReportModel in, PDF bytes out, in the weekly report's look (WeeklyReportDocument).
//
//  Order is what the director acts on first: the day in four figures, the lessons nobody conducted,
//  the teachers one by one, the marks per class, then every gamification entry.
// ===========================================================================

/// <summary>The academic director's daily PDF. See the file header.</summary>
public sealed class DailyDirectorReportDocument(DailyDirectorReportModel m) : IDocument
{
    static DailyDirectorReportDocument()
    {
        // QuestPDF Community — the same licence as ReceiptDocument (docs/ASSUMPTIONS.md).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly Color Ink = Color.FromHex("#111827");
    private static readonly Color Muted = Color.FromHex("#6B7280");
    private static readonly Color Line = Color.FromHex("#E5E7EB");
    private static readonly Color Soft = Color.FromHex("#F3F4F6");
    private static readonly Color Accent = Color.FromHex("#FFCC00");
    private static readonly Color AccentSoft = Color.FromHex("#FFF6CC");
    private static readonly Color Dark = Color.FromHex("#111111");
    private static readonly Color Good = Color.FromHex("#047857");
    private static readonly Color Warn = Color.FromHex("#B45309");
    private static readonly Color Bad = Color.FromHex("#B91C1C");
    private static readonly Color BadSoft = Color.FromHex("#FEF2F2");

    private string Day => DailyDirectorReport.Label(m.Date);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Kunlik hisobot — {Day}",
        Author = m.SchoolName,
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public byte[] Render() => this.GeneratePdf();

    /// <summary>"Kunlik_hisobot_16.11.2026.pdf".</summary>
    public static string FileName(DailyDirectorReportModel m) => $"Kunlik_hisobot_{m.Date:dd.MM.yyyy}.pdf";

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink).LineHeight(1.2f));

            page.Header().ShowOnce().Element(ComposeHeader);
            page.Content().PaddingHorizontal(30).PaddingVertical(16).Element(ComposeBody);
            page.Footer().PaddingHorizontal(30).PaddingBottom(16).Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Background(Dark).PaddingHorizontal(30).PaddingVertical(18).Column(h =>
            {
                h.Item().Text(m.SchoolName.ToUpperInvariant()).FontSize(9).Bold().FontColor(Accent).LetterSpacing(0.05f);
                h.Item().PaddingTop(4).Text("Kunlik o'quv hisoboti").FontSize(20).Bold().FontColor(Colors.White);
                h.Item().PaddingTop(2).Text($"{Day} · holat {m.GeneratedAt:HH:mm} dagi")
                    .FontSize(10).FontColor(Colors.Grey.Lighten2);
            });
            col.Item().Height(4).Background(Accent);
        });

    private void ComposeBody(IContainer c) =>
        c.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeFigures);
            col.Item().Element(ComposeAttention);
            if (m.Lessons.Any(l => l.State != DailyLessonState.Conducted)) col.Item().Element(ComposeNotConducted);
            if (m.Teachers.Count > 0) col.Item().Element(ComposeTeachers);
            if (m.Classes.Count > 0) col.Item().Element(ComposeClasses);
            if (m.Game.Count > 0) col.Item().Element(ComposeGame);
            col.Item().Element(ComposeNote);
        });

    // ------------------------------------------------------------------

    private void ComposeFigures(IContainer c) =>
        c.Row(row =>
        {
            row.Spacing(8);
            Tile(row.RelativeItem(), m.ConductedPercent is { } p ? $"{p}%" : "—", "Darslar o'tildi",
                m.Planned == 0 ? "Bugun jadvalda dars yo'q" : $"{m.Planned} darsdan {m.Conducted} tasi", highlight: true);
            Tile(row.RelativeItem(), m.Missed.ToString(), "O'tilmagan darslar",
                m.Marked > 0 ? $"yana {m.Marked} tasida faqat davomat bor" : "belgi ham, davomat ham yo'q",
                bad: m.Missed > 0);
            Tile(row.RelativeItem(), m.GradeCount.ToString(), "Qo'yilgan baholar",
                m.GradeAverage is { } a ? $"o'rtacha {WeeklyReport.Num(a)}" : "bugun baho qo'yilmagan");
            Tile(row.RelativeItem(), m.Game.Count.ToString(), "Gamifikatsiya yozuvlari",
                m.Game.Count == 0 ? "bugun ball qo'yilmagan" : $"+{m.GamePlus} / −{m.GameMinus} ball");
        });

    private static void Tile(IContainer c, string value, string label, string note, bool highlight = false, bool bad = false) =>
        c.Background(highlight ? Accent : bad ? BadSoft : Soft).Padding(10).Column(col =>
        {
            col.Item().Text(value).FontSize(20).Bold().FontColor(bad ? Bad : Ink);
            col.Item().Text(label).FontSize(8.5f).SemiBold();
            col.Item().PaddingTop(1).Text(note).FontSize(7.5f).FontColor(highlight ? Ink : Muted);
        });

    /// <summary>The short list the director reads first — fixed rules over the numbers.</summary>
    private void ComposeAttention(IContainer c)
    {
        var lines = new List<string>();
        foreach (var t in m.Teachers.Where(t => t.Missed > 0).Take(8))
            lines.Add($"{t.Name}: {t.Lessons} darsdan {t.Missed} tasi o'tilmagan.");
        foreach (var t in m.Teachers.Where(t => t.Missed == 0 && t.Idle).Take(5))
            lines.Add($"{t.Name}: darslarda faqat davomat bor — «dars o'tildi», baho va ball yo'q.");
        var noTopic = m.Teachers.Where(t => t.Conducted > 0 && t.WithTopic < t.Conducted).ToList();
        if (noTopic.Count > 0)
            lines.Add($"Mavzu yozilmagan o'tilgan darslar: {noTopic.Sum(t => t.Conducted - t.WithTopic)} ta "
                      + $"({string.Join(", ", noTopic.Take(5).Select(t => t.Name))}{(noTopic.Count > 5 ? " va boshqalar" : "")}).");
        var noGrades = m.Teachers.Where(t => t.Conducted >= 2 && t.Grades == 0).Select(t => t.Name).ToList();
        if (noGrades.Count > 0)
            lines.Add($"Dars o'tib, birorta baho qo'ymagan: {string.Join(", ", noGrades.Take(6))}{(noGrades.Count > 6 ? " va boshqalar" : "")}.");
        if (lines.Count == 0)
            lines.Add(m.Planned == 0 ? "Bugun jadvalda dars bo'lmagan." : "Barcha darslar o'tilgan — jiddiy e'tibor talab qiladigan holat yo'q.");

        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("E'tibor talab qiladi").FontSize(13).Bold();
            col.Item().BorderLeft(3).BorderColor(lines.Count > 0 && m.Missed > 0 ? Bad : Accent).Background(AccentSoft)
                .PaddingVertical(8).PaddingHorizontal(10).Column(list =>
                {
                    list.Spacing(2);
                    foreach (var line in lines)
                        list.Item().Row(r =>
                        {
                            r.ConstantItem(10).Text("•").Bold();
                            r.RelativeItem().Text(line);
                        });
                });
        });
    }

    private void ComposeNotConducted(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Text("O'tilmagan darslar").FontSize(13).Bold();
            col.Item().PaddingBottom(6).Text("«Dars o'tildi» belgisi qo'yilmagan darslar. «Faqat davomat» — davomat olingan, lekin o'qituvchi darsni o'tildi deb belgilamagan.")
                .FontSize(8).FontColor(Muted);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(34);
                    cols.RelativeColumn(1.3f);
                    cols.RelativeColumn(2.2f);
                    cols.RelativeColumn(3f);
                    cols.RelativeColumn(1.6f);
                });
                Header(t, "Soat", "Sinf", "Fan", "O'qituvchi", "Holat");
                var i = 0;
                foreach (var l in m.Lessons.Where(l => l.State != DailyLessonState.Conducted)
                             .OrderBy(l => l.State == DailyLessonState.Missed ? 0 : 1).ThenBy(l => l.Period))
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Soft;
                    Cell(t, bg).Text($"{l.Period}");
                    Cell(t, bg).Text(l.OwnerName + Half(l.SubGroup));
                    Cell(t, bg).Text(l.Subject);
                    Cell(t, bg).Text(l.TeacherName);
                    var missed = l.State == DailyLessonState.Missed;
                    Cell(t, bg).Text(missed ? "O'tilmagan" : "Faqat davomat").Bold().FontColor(missed ? Bad : Warn);
                }
            });
        });

    private void ComposeTeachers(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Text("O'qituvchilar kesimida").FontSize(13).Bold();
            col.Item().PaddingBottom(6).Text("Dars — jadvaldagi darslar (o'rindosh darsi o'rindoshniki). Mavzu — mavzusi yozilgan o'tilgan darslar. Ball — o'qituvchi qo'ygan gamifikatsiya ballari.")
                .FontSize(8).FontColor(Muted);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.4f);
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1.1f);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1.1f);
                    cols.RelativeColumn(1.5f);
                });
                Header(t, "O'qituvchi", "Dars", "O'tildi", "O'tilmagan", "Mavzu", "Baho", "O'rtacha", "Ball (+/−)");
                var i = 0;
                foreach (var r in m.Teachers)
                {
                    var bg = r.Missed > 0 || r.Idle ? BadSoft : i % 2 == 0 ? Colors.White : Soft;
                    i++;
                    Cell(t, bg).Text(r.Name);
                    Cell(t, bg).AlignCenter().Text(r.Lessons == 0 ? "—" : r.Lessons.ToString());
                    Cell(t, bg).AlignCenter().Text(r.Lessons == 0 && r.Conducted == 0 ? "—" : r.Conducted.ToString()).Bold()
                        .FontColor(r.Lessons > 0 && r.Conducted == r.Lessons ? Good : Ink);
                    Cell(t, bg).AlignCenter().Text(r.Missed == 0 ? "—" : r.Missed.ToString()).Bold().FontColor(r.Missed > 0 ? Bad : Ink);
                    Cell(t, bg).AlignCenter().Text(r.Conducted == 0 ? "—" : $"{r.WithTopic}/{r.Conducted}");
                    Cell(t, bg).AlignCenter().Text(r.Grades == 0 ? "—" : r.Grades.ToString());
                    Cell(t, bg).AlignCenter().Text(r.Average is { } a ? WeeklyReport.Num(a) : "—");
                    Cell(t, bg).AlignCenter().Text(r.GameEntries == 0 ? "—" : $"+{r.GamePlus} / −{r.GameMinus}");
                }
                Cell(t, Accent).Text("Jami").Bold();
                Cell(t, Accent).AlignCenter().Text(m.Planned.ToString()).Bold();
                Cell(t, Accent).AlignCenter().Text(m.Conducted.ToString()).Bold();
                Cell(t, Accent).AlignCenter().Text((m.Missed + m.Marked).ToString()).Bold();
                Cell(t, Accent).AlignCenter().Text($"{m.Teachers.Sum(r => r.WithTopic)}/{m.Conducted}");
                Cell(t, Accent).AlignCenter().Text(m.GradeCount.ToString()).Bold();
                Cell(t, Accent).AlignCenter().Text(m.GradeAverage is { } ga ? WeeklyReport.Num(ga) : "—").Bold();
                Cell(t, Accent).AlignCenter().Text($"+{m.GamePlus} / −{m.GameMinus}").Bold();
            });
        });

    private void ComposeClasses(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Sinflar va yo'nalishlar kesimida").FontSize(13).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2.4f);
                    for (var k = 0; k < 6; k++) cols.RelativeColumn(1f);
                    cols.RelativeColumn(1.6f);
                });
                Header(t, "Sinf / yo'nalish", "«5»", "«4»", "«3»", "«2»", "Jami", "O'rtacha", "Ball (+/−)");
                var i = 0;
                foreach (var r in m.Classes)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Soft;
                    Cell(t, bg).Text(r.Name);
                    Cell(t, bg).AlignCenter().Text(Dash(r.Five));
                    Cell(t, bg).AlignCenter().Text(Dash(r.Four));
                    Cell(t, bg).AlignCenter().Text(Dash(r.Three));
                    Cell(t, bg).AlignCenter().Text(Dash(r.Two)).FontColor(r.Two > 0 ? Bad : Ink);
                    Cell(t, bg).AlignCenter().Text(Dash(r.Grades)).Bold();
                    Cell(t, bg).AlignCenter().Text(r.Average is { } a ? WeeklyReport.Num(a) : "—").Bold()
                        .FontColor(r.Average is < 3.5 ? Bad : Ink);
                    Cell(t, bg).AlignCenter().Text(r.GamePlus == 0 && r.GameMinus == 0 ? "—" : $"+{r.GamePlus} / −{r.GameMinus}");
                }
            });
        });

    private void ComposeGame(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text($"Gamifikatsiya: bugungi yozuvlar ({m.Game.Count} ta)").FontSize(13).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(38);
                    cols.RelativeColumn(2.4f);
                    cols.RelativeColumn(2.6f);
                    cols.RelativeColumn(1.3f);
                    cols.RelativeColumn(3f);
                    cols.ConstantColumn(40);
                });
                Header(t, "Vaqt", "Kim qo'ydi", "O'quvchi", "Sinf", "Sabab", "Ball");
                var i = 0;
                foreach (var g in m.Game)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Soft;
                    Cell(t, bg).Text(g.Time);
                    Cell(t, bg).Text(g.Author);
                    Cell(t, bg).Text(g.Student);
                    Cell(t, bg).Text(g.ClassName);
                    Cell(t, bg).Text(g.Reason);
                    Cell(t, bg).AlignRight().Text($"{(g.Points > 0 ? "+" : "")}{g.Points}").Bold()
                        .FontColor(g.Points > 0 ? Good : Bad);
                }
            });
        });

    private void ComposeNote(IContainer c) =>
        c.PaddingTop(4).Text(
                "Izoh: ma'lumotlar elektron jurnal, dars jadvali, davomat va gamifikatsiya yozuvlaridan hisobot tuzilgan vaqtdagi holatda olingan. "
                + "«O'tildi» — o'qituvchi darsni «dars o'tildi» deb belgilagan (maosh hisobi ham shu belgidan). Baholar — shu kun sanasi bilan qo'yilgan baholar.")
            .FontSize(7.5f).FontColor(Muted);

    private void ComposeFooter(IContainer c) =>
        c.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text($"{m.SchoolName} · Kunlik o'quv hisoboti · {Day}").FontSize(7.5f).FontColor(Muted);
            r.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span("-bet");
            });
        });

    // ------------------------------------------------------------------

    private static void Header(TableDescriptor t, params string[] titles) =>
        t.Header(h =>
        {
            foreach (var title in titles)
                h.Cell().Background(Dark).PaddingVertical(5).PaddingHorizontal(5)
                    .Text(title).FontColor(Colors.White).Bold().FontSize(8);
        });

    private static IContainer Cell(TableDescriptor t, Color bg) =>
        t.Cell().Background(bg).BorderBottom(1).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(5).AlignMiddle()
            .DefaultTextStyle(s => s.FontSize(8.5f));

    private static string Half(int sub) => sub == 0 ? "" : $" ({sub}-guruh)";

    private static string Dash(int n) => n == 0 ? "—" : n.ToString();
}
