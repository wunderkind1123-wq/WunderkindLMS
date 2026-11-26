using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

/// <summary>
/// Doimiy test kodlari Excel'i (curriculum-monitoring.md §2.9): har sinf alohida varaqda, sinflar
/// raqam bo'yicha (5 … 10, 11 — "10" "5" dan oldin emas), oxirida PIN qayerdan olinishi yozilgan
/// "Ko'rsatma". Baza yaratilmaydi.
/// </summary>
public class MonitoringStudentCodeExportTests
{
    [Fact]
    public void Kodlar_sinf_boyicha_varaqlarda_korsatma_oxirida()
    {
        var bytes = MonitoringStudentCodeService.Export(
        [
            new("s1", "Aliyev Ali", "10-A", "AAAA1111"),
            new("s2", "Valiyev Vali", "5-A", "BBBB2222"),
            new("s3", "Karimova Zarina", "5-A", null),
        ], "https://lms.example.uz/monitoring");

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), isEditable: false);
        var names = doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(["5-A", "10-A", "Ko'rsatma"], names);

        var first = (WorksheetPart)doc.WorkbookPart.GetPartById(doc.WorkbookPart.Workbook.Sheets!.Elements<Sheet>().First().Id!);
        Assert.Equal(3, first.Worksheet.Descendants<Row>().Count()); // sarlavha + 2 o'quvchi (kodsizi ham)
    }
}
