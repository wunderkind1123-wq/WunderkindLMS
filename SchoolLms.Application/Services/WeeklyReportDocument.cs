using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Haftalik o'quvchi hisoboti — the PDF (WeeklyReport builds the numbers). No database, no network:
//  a WeeklyReportModel in, PDF bytes out, as ReceiptDocument does.
//
//  Laid out after the client's sample (2026-11-08) but shorter: a header, the pupil, four figures, the
//  week's summary, the subjects with a bar per subject, the days, and a closing line from the study
//  office. Everything a parent reads is Uzbek.
// ===========================================================================

/// <summary>The weekly report PDF. See the file header.</summary>
public sealed class WeeklyReportDocument(WeeklyReportModel m) : IDocument
{
    static WeeklyReportDocument()
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
    private static readonly Color Bad = Color.FromHex("#B91C1C");
    private static readonly Color ClassBar = Color.FromHex("#9CA3AF");

    private string Period => WeeklyReport.Label(m.From, m.To);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Haftalik hisobot — {m.StudentName} — {Period}",
        Author = m.SchoolName,
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public byte[] Render() => this.GeneratePdf();

    /// <summary>"Haftalik_hisobot_Abdunumonova_Madinaxon_03.11-08.11.pdf" — no apostrophes or spaces.</summary>
    public static string FileName(WeeklyReportModel m)
    {
        var name = new string(m.StudentName.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2);
        return $"Haftalik_hisobot_{string.Join('_', parts)}_{m.From:dd.MM}-{m.To:dd.MM}.pdf";
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).LineHeight(1.25f));

            // The big header only on the first page; the footer names the report on every page.
            page.Header().ShowOnce().Element(ComposeHeader);
            page.Content().PaddingHorizontal(36).PaddingVertical(18).Element(ComposeBody);
            page.Footer().PaddingHorizontal(36).PaddingBottom(18).Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Background(Dark).PaddingHorizontal(36).PaddingVertical(22).Column(h =>
            {
                h.Item().Text(m.SchoolName.ToUpperInvariant()).FontSize(9).Bold().FontColor(Accent).LetterSpacing(0.05f);
                h.Item().PaddingTop(4).Text("Haftalik o'quvchi hisoboti").FontSize(22).Bold().FontColor(Colors.White);
                h.Item().PaddingTop(2).Text($"{Period} · {m.Days.Count} o'quv kuni · {m.Quarter}-chorak")
                    .FontSize(10).FontColor(Colors.Grey.Lighten2);
            });
            col.Item().Height(4).Background(Accent);
        });

    private void ComposeBody(IContainer c) =>
        c.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeIdentity);
            col.Item().Element(ComposeGreeting);
            col.Item().Element(ComposeFigures);
            col.Item().Element(ComposeSummary);
            if (m.Subjects.Count > 0) col.Item().Element(ComposeSubjects);
            if (m.Days.Count > 0) col.Item().Element(ComposeDays);
            if (m.Behaviour.Count > 0) col.Item().Element(ComposeBehaviour);
            if (m.Recommendations.Count > 0) col.Item().Element(ComposeRecommendations);
            if (m.Goals.Count > 0) col.Item().Element(ComposeGoals);
            col.Item().Element(ComposeClosing);
        });

    // ------------------------------------------------------------------

    private void ComposeIdentity(IContainer c) =>
        c.Background(Soft).Padding(12).Table(t =>
        {
            t.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); });
            Field(t.Cell(), "O'QUVCHI", m.StudentName, true);
            Field(t.Cell(), "SINF", m.ClassName, true);
            Field(t.Cell().PaddingTop(8), "SINF RAHBARI", Or(m.Homeroom));
            Field(t.Cell().PaddingTop(8), "OTA-ONA", Or(m.ParentName));
        });

    private static void Field(IContainer c, string label, string value, bool strong = false) =>
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).FontColor(Muted).LetterSpacing(0.04f);
            var text = col.Item().Text(value).FontSize(strong ? 12 : 10.5f);
            if (strong) text.Bold();
        });

    private void ComposeGreeting(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Text("Hurmatli ota-ona!").Bold();
            col.Item().PaddingTop(2).Text(t =>
            {
                t.Span($"Quyida farzandingiz {m.FirstName}ning {Period} kunlaridagi o'qishi bo'yicha qisqa hisobotni taqdim etamiz. ");
                t.Span("Har bir baho sinfning ");
                t.Span("aynan o'sha darsdagi").Bold();
                t.Span(" o'rtacha bahosi bilan taqqoslangan — shunda baho sinf darajasiga nisbatan yaxshi yoki past ekanini ko'rish mumkin.");
            });
        });

    private void ComposeFigures(IContainer c) =>
        c.Row(row =>
        {
            row.Spacing(8);
            var avg = m.Average;
            Tile(row.RelativeItem(), avg is null ? "—" : WeeklyReport.Num(avg.Value), "Haftalik o'rtacha baho",
                m.ClassAverage is { } cls ? $"Sinf o'rtachasi shu darslarda: {WeeklyReport.Num(cls)}" : "Bu hafta baho qo'yilmagan",
                highlight: true);
            Tile(row.RelativeItem(), m.AttendancePercent is { } p ? $"{p}%" : "—", "Davomat",
                m.Lessons == 0 ? "Dars qayd etilmagan"
                : m.Absent == 0 ? $"{m.Lessons} darsning barchasida qatnashdi"
                : $"{m.Lessons} darsdan {m.Lessons - m.Absent} tasida qatnashdi");
            Tile(row.RelativeItem(), m.Marks.Count.ToString(), "Olingan baholar", Distribution());
            Tile(row.RelativeItem(), m.DisciplineScore.ToString(), "Intizom bali",
                m.BehaviourDelta == 0 ? "Bu hafta o'zgarmadi"
                : $"Bu hafta: {(m.BehaviourDelta > 0 ? "+" : "")}{m.BehaviourDelta} ball");
        });

    private string Distribution()
    {
        if (m.Marks.Count == 0) return "—";
        return string.Join(", ", m.Marks.GroupBy(x => x.Grade).OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()} ta «{g.Key}»"));
    }

    private static void Tile(IContainer c, string value, string label, string note, bool highlight = false) =>
        c.Background(highlight ? Accent : Soft).Padding(10).Column(col =>
        {
            col.Item().Text(value).FontSize(20).Bold();
            col.Item().Text(label).FontSize(8.5f).SemiBold();
            col.Item().PaddingTop(1).Text(note).FontSize(7.5f).FontColor(highlight ? Ink : Muted);
        });

    private void ComposeSummary(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Haftaning qisqa xulosasi").FontSize(13).Bold();
            if (!string.IsNullOrWhiteSpace(m.Overview))
                col.Item().PaddingBottom(8).BorderLeft(3).BorderColor(Accent).Background(AccentSoft)
                    .PaddingVertical(8).PaddingHorizontal(10).Text(t =>
                    {
                        var dot = m.Overview.IndexOf(". ", StringComparison.Ordinal);
                        if (dot > 0)
                        {
                            t.Span(m.Overview[..(dot + 1)] + " ").Bold();
                            t.Span(m.Overview[(dot + 2)..]);
                        }
                        else t.Span(m.Overview);
                    });
            col.Item().Border(1).BorderColor(Dark).Table(t =>
            {
                t.ColumnsDefinition(cols => { cols.ConstantColumn(118); cols.RelativeColumn(); });
                SummaryRow(t, "Kuchli tomon", m.Strengths, Good);
                SummaryRow(t, "E'tibor talab qiladi", m.Attention, Bad);
                if (m.QuarterAverage is not null || m.PreviousAverage is not null)
                {
                    var lines = new List<string>();
                    if (m.PreviousAverage is { } prev)
                        lines.Add($"O'tgan hafta o'rtacha baho: {WeeklyReport.Num(prev)} ({m.PreviousCount} ta baho).");
                    if (m.QuarterAverage is { } q)
                        lines.Add($"{m.Quarter}-chorak bo'yicha umumiy o'rtacha: {WeeklyReport.Num(q)}.");
                    SummaryRow(t, "Taqqoslash", lines, Ink);
                }
            });
        });

    private static void SummaryRow(TableDescriptor t, string title, List<string> lines, Color mark)
    {
        t.Cell().Background(Dark).BorderBottom(1).BorderColor(Colors.Grey.Darken2).Padding(8)
            .Text(title).FontColor(Colors.White).Bold().FontSize(9.5f);
        t.Cell().BorderBottom(1).BorderColor(Line).Padding(8).Column(col =>
        {
            col.Spacing(2);
            foreach (var line in lines)
                col.Item().Row(r =>
                {
                    r.ConstantItem(10).Text("•").FontColor(mark).Bold();
                    r.RelativeItem().Text(line).FontSize(9.5f);
                });
        });
    }

    private void ComposeSubjects(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Text("Fanlar bo'yicha hafta natijasi").FontSize(13).Bold();
            col.Item().PaddingBottom(6).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("Sariq chiziq — farzandingizning bu haftadagi o'rtachasi, kulrang — sinfning o'sha darslardagi o'rtachasi (5 ball shkalasida).");
            });
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2.2f);
                    cols.RelativeColumn(1.4f);
                    cols.RelativeColumn(3f);
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1.1f);
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Fan", "Baholar", "Taqqoslash", "Hafta", "Sinf", "Chorak" })
                        h.Cell().Background(Dark).PaddingVertical(6).PaddingHorizontal(6)
                            .Text(title).FontColor(Colors.White).Bold().FontSize(8.5f);
                });
                var i = 0;
                foreach (var s in m.Subjects)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Soft;
                    Cell(t, bg).Text(s.Name).FontSize(9);
                    Cell(t, bg).Text(string.Join(" · ", s.Grades)).FontSize(9).Bold();
                    Cell(t, bg).Column(bars =>
                    {
                        bars.Spacing(2);
                        bars.Item().Element(x => Bar(x, s.Average, Accent));
                        bars.Item().Element(x => Bar(x, s.ClassAverage, ClassBar));
                    });
                    Cell(t, bg).Text(Fmt(s.Average)).FontSize(9).Bold()
                        .FontColor(s.Average is { } a && s.ClassAverage is { } ca && a < ca - 0.49 ? Bad : Ink);
                    Cell(t, bg).Text(Fmt(s.ClassAverage)).FontSize(9).FontColor(Muted);
                    Cell(t, bg).Text(Fmt(s.QuarterAverage)).FontSize(9);
                }
            });
        });

    private static IContainer Cell(TableDescriptor t, Color bg) =>
        t.Cell().Background(bg).BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(6).AlignMiddle();

    private static void Bar(IContainer c, double? value, Color color)
    {
        var share = Math.Clamp((float)((value ?? 0) / 5.0), 0f, 1f);
        c.Height(6).Row(r =>
        {
            if (share > 0) r.RelativeItem(share).Background(color);
            if (share < 1) r.RelativeItem(1 - share).Background(Line);
        });
    }

    private void ComposeDays(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Kunlik jadval: davomat va baholar").FontSize(13).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.7f);
                    cols.RelativeColumn(5f);
                    cols.RelativeColumn(1.1f);
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Sana", "Kun", "Davomat", "Olingan baholar", "O'rtacha" })
                        h.Cell().Background(Dark).PaddingVertical(6).PaddingHorizontal(6)
                            .Text(title).FontColor(Colors.White).Bold().FontSize(8.5f);
                });

                var weakest = m.Days.Where(d => d.Marks.Count > 0)
                    .OrderBy(d => d.Marks.Average(x => x.Grade)).FirstOrDefault();
                var flagWeakest = weakest is not null && weakest.Marks.Average(x => x.Grade) < 4.0;
                var i = 0;
                foreach (var d in m.Days)
                {
                    var bg = flagWeakest && d == weakest ? AccentSoft : i % 2 == 0 ? Colors.White : Soft;
                    i++;
                    Cell(t, bg).Text($"{d.Date:dd.MM}").FontSize(9);
                    Cell(t, bg).Text(WeeklyReport.WeekDay(d.Date)).FontSize(9);
                    var (status, color) = d.Absent == 0 && d.Late == 0 ? ("Keldi", Good)
                        : d.Absent >= d.Lessons ? ("Kelmadi", Bad)
                        : d.Absent > 0 ? ($"{d.Absent} dars yo'q", Bad)
                        : ("Kechikdi", Bad);
                    Cell(t, bg).Text(status).FontSize(9).Bold().FontColor(color);
                    Cell(t, bg).Text(d.Marks.Count == 0 ? "—" : string.Join(" · ", d.Marks.Select(x => $"{x.Subject} {x.Grade}")))
                        .FontSize(9);
                    Cell(t, bg).Text(d.Marks.Count == 0 ? "—" : WeeklyReport.Num(d.Marks.Average(x => x.Grade))).FontSize(9).Bold();
                }

                var total = m.Marks.Count;
                Cell(t, Accent).Text("Jami").Bold().FontSize(9);
                Cell(t, Accent).Text($"{m.Days.Count} kun").Bold().FontSize(9);
                Cell(t, Accent).Text(m.AttendancePercent is { } p ? $"{p}%" : "—").Bold().FontSize(9);
                Cell(t, Accent).Text(total == 0 ? "Baho yo'q" : $"{total} ta baho: {Distribution()}").FontSize(9);
                Cell(t, Accent).Text(m.Average is { } a ? WeeklyReport.Num(a) : "—").Bold().FontSize(9);
            });
        });

    private void ComposeBehaviour(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Intizom: bu haftadagi qaydlar").FontSize(13).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols => { cols.RelativeColumn(1f); cols.RelativeColumn(6f); cols.RelativeColumn(1.2f); });
                var i = 0;
                foreach (var b in m.Behaviour)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Soft;
                    Cell(t, bg).Text($"{b.Date:dd.MM}").FontSize(9);
                    Cell(t, bg).Text(b.Reason).FontSize(9);
                    Cell(t, bg).AlignRight().Text($"{(b.Points > 0 ? "+" : "")}{b.Points}").FontSize(9).Bold()
                        .FontColor(b.Points > 0 ? Good : Bad);
                }
            });
        });

    private void ComposeRecommendations(IContainer c) =>
        c.Column(col =>
        {
            col.Spacing(8);
            col.Item().Column(h =>
            {
                h.Item().Text("O'sish uchun tavsiyalar").FontSize(13).Bold();
                h.Item().Text("Har bir tavsiya shu haftaning aniq natijasiga asoslangan va uyda bajarish uchun mo'ljallangan. Kuniga jami 30–40 daqiqa yetarli.")
                    .FontSize(8).FontColor(Muted);
            });
            var n = 0;
            foreach (var r in m.Recommendations)
            {
                n++;
                col.Item().ShowEntire().BorderLeft(3).BorderColor(Accent).Background(Soft).Padding(10).Column(card =>
                {
                    card.Spacing(3);
                    card.Item().Text($"{n}. {r.Title}").Bold().FontSize(10.5f);
                    card.Item().Text(t =>
                    {
                        t.Span("Nega: ").Bold();
                        t.Span(r.Why);
                    });
                    foreach (var step in r.Steps)
                        card.Item().PaddingLeft(4).Row(row =>
                        {
                            row.ConstantItem(10).Text("•").Bold();
                            row.RelativeItem().Text(step).FontSize(9.5f);
                        });
                    card.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span("Maqsad: ").Bold();
                        t.Span(r.Goal);
                    });
                });
            }
        });

    private void ComposeGoals(IContainer c) =>
        c.ShowEntire().Column(col =>
        {
            col.Item().PaddingBottom(6).Text($"Keyingi hafta maqsadlari ({WeeklyReport.Label(m.From.AddDays(7), m.From.AddDays(12))})")
                .FontSize(13).Bold();
            col.Item().Row(row =>
            {
                row.Spacing(6);
                foreach (var g in m.Goals)
                    row.RelativeItem().Background(Accent).Padding(8).Column(x =>
                    {
                        x.Item().Text(g.Value).FontSize(15).Bold();
                        x.Item().Text(g.Label).FontSize(8);
                    });
            });
        });

    private void ComposeClosing(IContainer c) =>
        c.PaddingTop(4).Column(col =>
        {
            col.Item().Text(t =>
            {
                t.Span("Savollar bo'yicha sinf rahbari");
                if (!string.IsNullOrWhiteSpace(m.Homeroom)) t.Span($" {m.Homeroom}").Bold();
                t.Span(" bilan bog'lanishingiz mumkin. Farzandingizning yutuqlari — sizning e'tiboringiz va maktab bilan hamkorligimiz natijasi. Rahmat!");
            });
            col.Item().PaddingTop(6).Text($"Hurmat bilan, {m.SchoolName} o'quv bo'limi").Bold();
            col.Item().PaddingTop(8).Text(
                    "Izoh: ma'lumotlar maktabning elektron jurnali, davomat va intizom qaydlaridan olingan. «Sinf o'rtachasi» — "
                    + "sinfning aynan o'sha darsda baholangan o'quvchilari baholarining o'rtacha qiymati. Chorak baholari hali yakuniy emas.")
                .FontSize(7.5f).FontColor(Muted);
        });

    private void ComposeFooter(IContainer c) =>
        c.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text($"{m.SchoolName} · Haftalik o'quvchi hisoboti · {Period}").FontSize(7.5f).FontColor(Muted);
            r.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span("-bet");
            });
        });

    private static string Fmt(double? v) => v is { } x ? WeeklyReport.Num(x) : "—";
    private static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
