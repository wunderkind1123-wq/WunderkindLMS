using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SchoolLms.Application.Dtos;
using A = DocumentFormat.OpenXml.Drawing;
using M = DocumentFormat.OpenXml.Math;
using V = DocumentFormat.OpenXml.Vml;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Monitoring savollari — ustozning Word (yoki matn) testini bankka yuklash.
//  docs/modules/curriculum-monitoring.md §2.2 (2026-11-24).
// ===========================================================================
//
//  BITTA TAHLILCHI
//  ---------------
//  Savol / variant / to'g'ri javobni topish — topshiriqlar importining matn
//  formati (`AssignmentQuestionImport.FromText`, docs/modules/test-import.md):
//  "1." / "A)" / "*B)" / "Javob: B" … Bu yerda faqat Word'dan matn olish boshqacha:
//
//  * FORMULALAR. Word tenglamasi (OMML) topshiriq importida umuman tushib qoladi —
//    "Hisoblang: [formula]" "Hisoblang:" bo'lib qoladi. Bu yerda u o'qiladigan
//    chiziqli matnga aylanadi: kasr (a+b)/(c), daraja x^(2), indeks a_(n), ildiz √(x).
//  * RASMLAR. Savoldagi rasm (chizma, grafik, skrinshot formula) `⟦rasmN⟧` belgisi
//    bilan matnga qo'yiladi, tahlildan keyin savolning `ImageUrl` iga aylanadi. Savolga
//    bitta rasm (MonitoringQuestion.ImageUrl), har variantga bitta rasm (OptionImages) —
//    "A) [rasm]" yoki "A)" dan keyingi qatordagi rasm.
//  * ESKI FORMULA (Equation 3.0 / MathType, WMF/EMF rasm) brauzerda ko'rinmaydi —
//    shunday savol xato bilan qaytadi va ustozga nima qilish aytiladi.
//
//  Hech narsa saqlanmaydi: natija — oldindan ko'rish; ustoz tasdiqlagach
//  `MonitoringService.AddQuestionsAsync` yozadi. Rasmlar oldindan ko'rishda
//  /uploads ga yoziladi (ko'rsatish uchun) — tasdiqlanmasa egasiz qoladi, bu arzon.
// ===========================================================================

public static partial class MonitoringWordImport
{
    /// <summary>Bitta rasm — Word ichidan olingan baytlar va kengaytma (".png").</summary>
    public sealed record Image(byte[] Bytes, string Extension);

    /// <summary>Hujjat matni (rasm o'rnida <c>⟦rasmN⟧</c>) va rasmlar.</summary>
    public sealed record Document(string Text, IReadOnlyDictionary<string, Image> Images);

    public const int MaxImageBytes = 5_000_000;
    private const string OldEquation = "⟦eski-formula⟧";

    [GeneratedRegex(@"⟦rasm\d+⟧")]
    private static partial Regex ImageMarker();

    // ===================================================================== Word → matn

    /// <summary>Word hujjatini o'qiydi. Buzilgan fayl — istisno (chaqiruvchi xabarga aylantiradi).</summary>
    public static Document Read(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body;
        if (main is null || body is null) return new Document("", new Dictionary<string, Image>());

        var reader = new Reader(main);
        var lines = new List<string>();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            // mc:AlternateContent ichidagi matn qutisi ikki marta keladi (Choice va Fallback).
            if (paragraph.Ancestors<AlternateContentFallback>().Any()) continue;
            var label = reader.Numbering.LabelFor(paragraph);
            var sb = new StringBuilder();
            foreach (var child in paragraph.ChildElements) reader.Walk(child, sb, paragraph);
            var content = sb.ToString();
            // A wholly bold paragraph is how a teacher marks the answer in Word; the text parser
            // reads that as Markdown bold ("**C) 13**", or "C) **13**" after a Word list label).
            if (IsWhollyBold(paragraph) && !content.Contains('\n') && content.Trim().Length > 0)
                content = $"**{content.Trim()}**";
            lines.AddRange((label + content).Split('\n'));
        }
        return new Document(string.Join('\n', AttachLoneImages(lines)), reader.Images);
    }

    /// <summary>Abzatsning matnli har bir bo'lagi qalin (Bold) — oddiy matnli bo'lagi yo'q.</summary>
    private static bool IsWhollyBold(Paragraph paragraph)
    {
        var runs = paragraph.Elements<Run>().Where(r => r.Elements<Text>().Any(t => t.Text.Trim().Length > 0)).ToList();
        return runs.Count > 0 && runs.All(r => r.RunProperties?.Bold is { } b && (b.Val is null || b.Val.Value));
    }

    /// <summary>
    /// Faqat rasmdan iborat qator (Word'da rasm odatda alohida abzats) oldingi matnli qatorga
    /// qo'shiladi — aks holda tahlilchi uni yangi savol deb o'ylardi.
    /// </summary>
    private static List<string> AttachLoneImages(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var isImageOnly = line.Trim().Length > 0 && ImageMarker().Replace(line.Replace(OldEquation, ""), "").Trim().Length == 0;
            var previous = result.FindLastIndex(l => l.Trim().Length > 0);
            if (isImageOnly && previous >= 0)
            {
                result[previous] = result[previous].TrimEnd() + " " + line.Trim();
                continue;
            }
            result.Add(line);
        }
        return result;
    }

    private sealed class Reader(MainDocumentPart main)
    {
        public readonly Dictionary<string, Image> Images = [];
        public readonly AssignmentQuestionImport.WordNumbering Numbering = new(main.NumberingDefinitionsPart?.Numbering);

        public void Walk(OpenXmlElement element, StringBuilder sb, Paragraph owner)
        {
            switch (element)
            {
                case Paragraph p when p != owner:
                    return; // matn qutisining abzatsi — o'z navbatida keladi
                case Text t:
                    sb.Append(t.Text);
                    return;
                case TabChar:
                    sb.Append(' ');
                    return;
                case Break or CarriageReturn:
                    sb.Append('\n');
                    return;
                case M.OfficeMath math:
                    sb.Append(MathText(math));
                    return;
                case AlternateContent ac:
                    // birinchi tanlov yetarli; Fallback — o'shaning nusxasi
                    if (ac.ChildElements.FirstOrDefault(c => c is AlternateContentChoice) is { } choice)
                        foreach (var c in choice.ChildElements) Walk(c, sb, owner);
                    return;
                case Drawing drawing:
                    AppendImage(drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value, sb);
                    return;
                case EmbeddedObject:
                    // Equation 3.0 / MathType — faqat WMF ko'rinishi bor
                    sb.Append(OldEquation);
                    return;
                case Picture pict:
                    AppendImage(pict.Descendants<V.ImageData>().FirstOrDefault()?.RelationshipId?.Value, sb);
                    return;
            }
            foreach (var child in element.ChildElements) Walk(child, sb, owner);
        }

        private void AppendImage(string? relId, StringBuilder sb)
        {
            if (relId is null || main.GetPartById(relId) is not ImagePart part) return;
            var ext = part.ContentType switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                "image/webp" => ".webp",
                _ => null, // emf / wmf — brauzer ko'rsatmaydi
            };
            if (ext is null) { sb.Append(OldEquation); return; }
            using var s = part.GetStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            if (ms.Length > MaxImageBytes) { sb.Append(OldEquation); return; }
            var key = $"⟦rasm{Images.Count + 1}⟧";
            Images[key] = new Image(ms.ToArray(), ext);
            sb.Append(' ').Append(key).Append(' ');
        }
    }

    // ===================================================================== formula → chiziqli matn

    /// <summary>Word tenglamasi — o'qiladigan bir qatorli matn: (a+b)/(c), x^(2), a_(n), √(x).</summary>
    public static string MathText(OpenXmlElement element) => element switch
    {
        M.Text t => t.Text,
        M.Fraction f => $"{Group(Of(f.Numerator))}/{Group(Of(f.Denominator))}",
        M.Superscript s => $"{Of(s.Base)}^{Group(Of(s.SuperArgument))}",
        M.Subscript s => $"{Of(s.Base)}_{Group(Of(s.SubArgument))}",
        M.SubSuperscript s => $"{Of(s.Base)}_{Group(Of(s.SubArgument))}^{Group(Of(s.SuperArgument))}",
        M.Radical r => (Of(r.Degree) is { Length: > 0 } d ? $"{Group(d)}" : "") + $"√{Group(Of(r.Base))}",
        M.Delimiter d => Delimited(d),
        M.Nary n => Nary(n),
        M.MathFunction fn => $"{Of(fn.FunctionName)} {Of(fn.Base)}",
        M.Box or M.BorderBox or M.Bar or M.Accent or M.GroupChar or M.Limit or M.LimitLower or M.LimitUpper
            or M.EquationArray or M.Matrix or M.Run or M.Base or M.OfficeMath or M.Paragraph
            or M.Numerator or M.Denominator or M.SuperArgument or M.SubArgument or M.Degree or M.FunctionName
            => Children(element),
        _ when element.LocalName.EndsWith("Pr", StringComparison.Ordinal) => "", // xossalar
        _ => Children(element),
    };

    private static string Of(OpenXmlElement? e) => e is null ? "" : MathText(e).Trim();

    private static string Children(OpenXmlElement e) => string.Concat(e.ChildElements.Select(MathText));

    /// <summary>Bitta belgi yoki son qavssiz, qolgani qavs ichida.</summary>
    private static string Group(string s) =>
        s.Length <= 1 || s.All(char.IsLetterOrDigit) || (s.StartsWith('(') && s.EndsWith(')') && s.Count(c => c == '(') == 1)
            ? s : $"({s})";

    private static string Delimited(M.Delimiter d)
    {
        var pr = d.DelimiterProperties;
        var begin = pr?.BeginChar?.Val?.Value ?? "(";
        var end = pr?.EndChar?.Val?.Value ?? ")";
        var sep = pr?.SeparatorChar?.Val?.Value ?? "|";
        return begin + string.Join(sep, d.Elements<M.Base>().Select(Of)) + end;
    }

    private static string Nary(M.Nary n)
    {
        var sign = n.NaryProperties?.AccentChar?.Val?.Value ?? "∫";
        var sub = Of(n.SubArgument);
        var sup = Of(n.SuperArgument);
        return sign + (sub.Length > 0 ? $"_{Group(sub)}" : "") + (sup.Length > 0 ? $"^{Group(sup)}" : "") + " " + Of(n.Base);
    }

    // ===================================================================== matn → savollar

    private static readonly Dictionary<string, string> ReasonText = new()
    {
        [AssignmentQuestionReason.EmptyText] = "savol matni bo'sh",
        [AssignmentQuestionReason.TooFewOptions] = "variant 2 tadan kam",
        [AssignmentQuestionReason.TooManyOptions] = "variant 6 tadan ko'p",
        [AssignmentQuestionReason.NoCorrect] = "to'g'ri javob belgilanmagan",
        [AssignmentQuestionReason.BadCorrect] = "javob hech bir variantga mos emas",
        [AssignmentQuestionReason.ManyCorrect] = "bir nechta to'g'ri javob belgilangan",
        [AssignmentQuestionReason.DuplicateOption] = "bir xil variant takrorlangan",
    };

    /// <summary>
    /// Matnni (Word'dan yoki qo'yilgan) savollarga ajratadi. Rasmlar <paramref name="saveImage"/>
    /// bilan /uploads ga yoziladi. Xatoli savol butunlay chiqariladi va sababi aytiladi.
    /// </summary>
    public static async Task<MonitoringImportPreviewDto> ToPreviewAsync(
        Document doc, Func<Image, Task<string>> saveImage)
    {
        var outcome = AssignmentQuestionImport.FromText(doc.Text);
        if (outcome.FatalMessage is { } fatal) throw new MonitoringException(fatal);
        var parsed = outcome.Result!;

        // the parser groups by reason; the teacher reads by question
        var problems = parsed.Errors
            .SelectMany(e => e.Rows.Select(row => (Row: row, Reason: ReasonText.GetValueOrDefault(e.Reason, e.Reason))))
            .GroupBy(x => x.Row).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}-savol: {string.Join(", ", g.Select(x => x.Reason).Distinct())}")
            .ToList();
        var questions = new List<MonitoringQuestionPayload>();
        foreach (var q in parsed.Questions)
        {
            var label = Short(q.Text);
            if (q.Text.Contains(OldEquation) || q.Options.Any(o => o.Contains(OldEquation)))
            {
                problems.Add($"«{label}»: eski formula (Equation 3.0 / MathType) yoki ko'rinmaydigan rasm bor — Word'da " +
                    "uni «Вставка → Уравнение» (Insert → Equation) bilan qayta yozing yoki rasm (PNG) qilib qo'ying");
                continue;
            }
            if (q.Options.FindIndex(o => ImageMarker().Matches(o).Count > 1) is var many and >= 0)
            {
                problems.Add($"«{label}»: {(char)('A' + many)} variantda bir nechta rasm — bitta variantga bitta rasm");
                continue;
            }
            var keys = ImageMarker().Matches(q.Text).Select(m => m.Value).ToList();
            if (keys.Count > 1)
            {
                problems.Add($"«{label}»: savolda {keys.Count} ta rasm — bitta savolga bitta rasm (ularni bitta rasmga birlashtiring)");
                continue;
            }
            var text = Regex.Replace(ImageMarker().Replace(q.Text, " "), @"\s{2,}", " ").Trim();
            string? imageUrl = null;
            if (keys.Count == 1 && doc.Images.TryGetValue(keys[0], out var image))
            {
                imageUrl = await saveImage(image);
                if (text.Length == 0) text = "Rasmdagi savol";
            }
            var options = new List<string>(q.Options.Count);
            var optionImages = new List<string>(q.Options.Count);
            foreach (var option in q.Options)
            {
                var key = ImageMarker().Match(option);
                options.Add(Regex.Replace(ImageMarker().Replace(option, " "), @"\s{2,}", " ").Trim());
                optionImages.Add(key.Success && doc.Images.TryGetValue(key.Value, out var img) ? await saveImage(img) : "");
            }
            questions.Add(new MonitoringQuestionPayload(text, imageUrl, options, q.CorrectIndex, null, null,
                optionImages.Any(x => x.Length > 0) ? optionImages : null));
        }
        return new MonitoringImportPreviewDto(questions, problems);
    }

    // ===================================================================== namuna

    public const string TemplateFileName = "test-savollari-namuna.docx";

    /// <summary>
    /// Ustozga beriladigan Word namuna: qisqa ko'rsatma va to'g'ri javob belgilashning uch usuli,
    /// formula bilan namuna savollar. Ko'rsatma qatorlari raqam yoki "A)" bilan boshlanmaydi — fayl
    /// o'zgartirilmasdan yuklansa ham faqat namuna savollar o'qiladi (test shuni tekshiradi).
    /// </summary>
    public static byte[] Template()
    {
        static Run R(string text, bool bold = false, bool italic = false, string? color = null)
        {
            var props = new RunProperties();
            if (bold) props.Append(new Bold());
            if (italic) props.Append(new Italic());
            if (color is not null) props.Append(new Color { Val = color });
            return new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }
        static Paragraph P(params OpenXmlElement[] runs) => new(runs);
        static Paragraph Line(string text, bool bold = false) => P(R(text, bold));
        static Paragraph Note(string text) => P(R(text, italic: true, color: "6B7280"));
        static Paragraph Heading(string text) =>
            new(new ParagraphProperties(new SpacingBetweenLines { After = "120" }), R(text, bold: true, color: "1D4ED8"));
        var blank = () => new Paragraph();

        var fraction = new M.OfficeMath(
            new M.Fraction(
                new M.Numerator(new M.Run(new M.Text("a+b"))),
                new M.Denominator(new M.Run(new M.Text("2")))));
        var square = new M.OfficeMath(
            new M.Superscript(new M.Base(new M.Run(new M.Text("x"))), new M.SuperArgument(new M.Run(new M.Text("2")))),
            new M.Run(new M.Text("=49")));

        var body = new Body(
            Heading("TEST SAVOLLARI — NAMUNA (Wunderkind LMS)"),
            Note("Ko'rsatma: shu ko'k va kulrang yozuvlarni o'chirib, o'z savollaringizni xuddi pastdagidek yozing."),
            Note("• Har savol raqam bilan boshlanadi: «1.» yoki «1)». Savollar orasida bitta bo'sh qator qoldiring."),
            Note("• Variantlar alohida qatorlarda, harf bilan: «A)», «B)», «C)», «D)» — 2 tadan 6 tagacha."),
            Note("• To'g'ri javobni uch usulning biri bilan belgilang (pastdagi 1-, 2-, 3-savollarga qarang):"),
            Note("    — variant oldiga yulduzcha: «*B) 5»;"),
            Note("    — variantlardan keyingi qatorga: «Javob: C»;"),
            Note("    — to'g'ri variantni qalin (Bold, Ctrl+B) qiling."),
            Note("• Formula: Word menyusi «Вставка → Уравнение» (Insert → Equation). Eski «Equation 3.0» / MathType ishlatmang."),
            Note("• Rasm (chizma, grafik): savol matnidan keyingi qatorga qo'ying, bir savolga bitta rasm. Variant rasm bo'lsa: «A)» dan keyin shu qatorga rasmni qo'ying."),
            Note("• Faylni .docx ko'rinishida saqlang. Bir faylda 200 tagacha savol."),
            blank(),
            Line("1. 2 + 3 nechaga teng?"),
            Line("A) 4"),
            Line("*B) 5"),
            Line("C) 6"),
            Line("D) 7"),
            blank(),
            Line("2. O'zbekiston Respublikasining poytaxti qaysi shahar?"),
            Line("A) Samarqand"),
            Line("B) Buxoro"),
            Line("C) Toshkent"),
            Line("D) Xiva"),
            Line("Javob: C"),
            blank(),
            Line("3. Quyidagilardan qaysi biri tub son?"),
            Line("A) 9"),
            Line("B) 15"),
            Line("C) 13", bold: true),
            Line("D) 21"),
            blank(),
            P(R("4. a = 4 va b = 6 bo'lsa, "), fraction, R(" ning qiymatini toping.")),
            Line("A) 5"),
            Line("B) 10"),
            Line("C) 4"),
            Line("D) 2"),
            Line("Javob: A"),
            blank(),
            P(R("5. Tenglamani yeching: "), square),
            Line("A) 7 va −7"),
            Line("B) 7"),
            Line("C) 49"),
            Line("D) −7"),
            Line("Javob: A"));

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static string Short(string text)
    {
        var t = ImageMarker().Replace(text.Replace(OldEquation, "[formula]"), "[rasm]").Trim();
        return t.Length <= 50 ? t : t[..50] + "…";
    }
}
