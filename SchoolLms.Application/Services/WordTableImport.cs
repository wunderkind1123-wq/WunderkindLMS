using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SchoolLms.Application.Services;

/// <summary>
/// Oddiy .docx jadval o'qigich (OpenXML): hujjatdagi BARCHA yuqori darajali jadvallarning
/// qatorlari ketma-ket. Har qator — kataklar matni (ustun panjarasi bo'yicha): gorizontal
/// birlashgan katak (<c>gridSpan</c>) matnini birinchi ustunga yozib, qolganini "" qiladi;
/// vertikal birlashgan katakning davomi (<c>vMerge</c>) yuqoridagi katak matnini oladi —
/// masalan, bir nechta qatorga cho'zilgan "Chorak" katagi har qatorda ko'rinadi.
/// Katak ichidagi qator uzilishi "\n" bo'lib qoladi.
/// </summary>
public static class WordTableImport
{
    public static List<string[]> ReadRows(Stream stream)
    {
        var result = new List<string[]>();
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return result;

        foreach (var table in body.Descendants<Table>().Where(t => !t.Ancestors<Table>().Any()))
        {
            string[] above = [];
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = new List<string>();
                var before = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
                for (var i = 0; i < before; i++) cells.Add("");
                foreach (var cell in row.Elements<TableCell>())
                {
                    var props = cell.TableCellProperties;
                    var span = Math.Max(1, props?.GridSpan?.Val?.Value ?? 1);
                    var vMerge = props?.VerticalMerge;
                    var text = vMerge is not null && (vMerge.Val is null || vMerge.Val.Value == MergedCellValues.Continue)
                        ? (cells.Count < above.Length ? above[cells.Count] : "")
                        : CellText(cell);
                    cells.Add(text);
                    for (var i = 1; i < span; i++) cells.Add("");
                }
                above = [.. cells];
                result.Add(above);
            }
        }
        return result;
    }

    private static string CellText(TableCell cell)
    {
        var lines = new List<string>();
        foreach (var p in cell.Elements<Paragraph>())
        {
            var sb = new StringBuilder();
            foreach (var el in p.Descendants())
            {
                switch (el)
                {
                    case Text t: sb.Append(t.Text); break;
                    case TabChar: sb.Append(' '); break;
                    case Break or CarriageReturn: sb.Append('\n'); break;
                }
            }
            lines.Add(sb.ToString());
        }
        return string.Join("\n", lines).Trim();
    }
}
