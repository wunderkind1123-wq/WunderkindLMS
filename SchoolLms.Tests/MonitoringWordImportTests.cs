using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using M = DocumentFormat.OpenXml.Math;

namespace SchoolLms.Tests;

// Ustoz Word testini monitoring bankiga yuklaydi (curriculum-monitoring.md §2.2, 2026-11-24):
// formulalar matnga, savoldagi rasm savolga; eski formula va variantdagi rasm — sababli xato;
// ustoz faqat o'zi dars beradigan fan × sinf banklarini ko'radi.
[Collection(SchoolLmsCollection.Name)]
public class MonitoringWordImportTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 1×1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Paragraph P(string text) => new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    /// <summary>"Hisoblang: " + Word tenglamasi (a+b)/c + x².</summary>
    private static Paragraph MathParagraph() => new(
        new Run(new Text("1. Hisoblang: ") { Space = SpaceProcessingModeValues.Preserve }),
        new M.OfficeMath(
            new M.Fraction(
                new M.Numerator(new M.Run(new M.Text("a+b"))),
                new M.Denominator(new M.Run(new M.Text("c")))),
            new M.Run(new M.Text("+")),
            new M.Superscript(
                new M.Base(new M.Run(new M.Text("x"))),
                new M.SuperArgument(new M.Run(new M.Text("2"))))));

    private static Paragraph ImageParagraph(string relId) => new(new Run(new Drawing(new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
        new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = 9525, Cy = 9525 },
        new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = 1U, Name = "rasm" },
        new DocumentFormat.OpenXml.Drawing.Graphic(new DocumentFormat.OpenXml.Drawing.GraphicData(
            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties { Id = 0U, Name = "rasm.png" },
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                    new DocumentFormat.OpenXml.Drawing.Blip { Embed = relId },
                    new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties()))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })))));

    private static byte[] TestDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var image = main.AddImagePart(ImagePartType.Png);
            using (var s = new MemoryStream(Png)) image.FeedData(s);
            var relId = main.GetIdOfPart(image);
            main.Document = new Document(new Body(
                MathParagraph(), P("A) 1"), P("*B) 2"), P(""),
                P("2. Rasmda nechta burchak bor?"), ImageParagraph(relId), P("A) 3"), P("B) 4"), P("Javob: A"), P(""),
                new Paragraph(new Run(new Text("3. Eski formula: ")), new Run(new EmbeddedObject())), P("A) 1"), P("*B) 2"), P(""),
                P("4. Variantsiz savol")));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Namuna o'zgartirilmasdan yuklansa — 5 ta savol, uch usulda belgilangan javoblar, formula.</summary>
    [Fact]
    public async Task Word_namunaning_ozi_xatosiz_yuklanadi()
    {
        var doc = MonitoringWordImport.Read(new MemoryStream(MonitoringWordImport.Template()));
        var preview = await MonitoringWordImport.ToPreviewAsync(doc, _ => Task.FromResult("/uploads/x.png"));
        Assert.Empty(preview.Problems);
        Assert.Equal([1, 2, 2, 0, 0], preview.Questions.Select(q => q.CorrectIndex));
        Assert.Equal("a = 4 va b = 6 bo'lsa, (a+b)/2 ning qiymatini toping.", preview.Questions[3].Text);
        Assert.Equal("Tenglamani yeching: x^2=49", preview.Questions[4].Text);
    }

    [Fact]
    public void Formula_chiziqli_matnga_aylanadi()
    {
        var doc = MonitoringWordImport.Read(new MemoryStream(TestDocx()));
        Assert.Contains("Hisoblang: (a+b)/c+x^2", doc.Text);
        Assert.Single(doc.Images);
    }

    [Fact]
    public async Task Variantdagi_rasm_variant_rasmiga_aylanadi()
    {
        var png = new MonitoringWordImport.Image([1, 2, 3], ".png");
        var doc = new MonitoringWordImport.Document(
            "1. Qaysi biri uchburchak?\nA) ⟦rasm1⟧\n*B) Doira ⟦rasm2⟧\nC) Kvadrat\n\n2. Ikki rasm\nA) ⟦rasm3⟧ ⟦rasm4⟧\n*B) x",
            new Dictionary<string, MonitoringWordImport.Image> { ["⟦rasm1⟧"] = png, ["⟦rasm2⟧"] = png, ["⟦rasm3⟧"] = png, ["⟦rasm4⟧"] = png });
        var saved = 0;
        var preview = await MonitoringWordImport.ToPreviewAsync(doc, _ => Task.FromResult($"/uploads/r{++saved}.png"));

        var q = Assert.Single(preview.Questions);
        Assert.Equal(["", "Doira", "Kvadrat"], q.Options);
        Assert.Equal(["/uploads/r1.png", "/uploads/r2.png", ""], q.OptionImages);
        Assert.Equal(1, q.CorrectIndex);
        Assert.Contains(preview.Problems, p => p.Contains("bir nechta rasm"));
    }

    [Fact]
    public async Task Ustoz_word_testini_ozining_bankiga_yuklaydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher, fullName: $"Ustoz {tag}");
        var cls = new SchoolClass { Name = $"7-W-{tag}", Grade = 7 };
        var mine = new Subject { Name = $"Fan {tag}" };
        var other = new Subject { Name = $"Boshqa {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            var teacher = await db.Teachers.FirstAsync(t => t.UserId == user.Id);
            db.Classes.Add(cls);
            db.Subjects.AddRange(mine, other);
            var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy" };
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = mine.Id, TeacherId = teacher.Id });
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });
        using var t = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Staff, "monitoring");

        // a bank of a subject the teacher does not teach: cannot create, cannot see
        Assert.Equal(HttpStatusCode.BadRequest, (await t.PostAsJsonAsync("/api/teacher/question-banks",
            new { title = "X", subjectId = other.Id, grade = 7 })).StatusCode);
        var foreign = await IdOf(await admin.PostAsJsonAsync("/api/admin/monitoring/banks", new { title = "Begona", subjectId = other.Id, grade = 7 }));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/teacher/question-banks/{foreign}")).StatusCode);

        var bankId = await IdOf(await t.PostAsJsonAsync("/api/teacher/question-banks", new { title = "Oylik", subjectId = mine.Id, grade = 7 }));
        try
        {
            var list = await t.GetFromJsonAsync<List<MonitoringBankDto>>("/api/teacher/question-banks", Json);
            Assert.Equal([bankId], list!.Select(b => b.Id).Where(id => id == bankId || id == foreign));

            var file = new ByteArrayContent(TestDocx());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var res = await t.PostAsync($"/api/teacher/question-banks/{bankId}/import/preview",
                new MultipartFormDataContent { { file, "file", "test.docx" } });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var preview = (await res.Content.ReadFromJsonAsync<MonitoringImportPreviewDto>(Json))!;

            Assert.Equal(2, preview.Questions.Count);
            Assert.Equal("Hisoblang: (a+b)/c+x^2", preview.Questions[0].Text);
            Assert.Equal(1, preview.Questions[0].CorrectIndex);
            Assert.Equal("Rasmda nechta burchak bor?", preview.Questions[1].Text);
            Assert.StartsWith("/uploads/", preview.Questions[1].ImageUrl);
            Assert.Equal(0, preview.Questions[1].CorrectIndex);
            Assert.True(preview.Problems.Count == 2, string.Join(" | ", preview.Problems));
            Assert.Contains("4-savol: variant 2 tadan kam, to'g'ri javob belgilanmagan", preview.Problems);
            Assert.Contains(preview.Problems, p => p.Contains("eski formula"));
            Assert.Contains(preview.Problems, p => p.Contains("variant 2 tadan kam"));

            // nothing saved until confirmed
            Assert.Empty((await t.GetFromJsonAsync<MonitoringBankDetailDto>($"/api/teacher/question-banks/{bankId}", Json))!.Questions);
            var added = await t.PostAsJsonAsync($"/api/teacher/question-banks/{bankId}/questions/bulk", preview.Questions);
            Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
            var detail = (await t.GetFromJsonAsync<MonitoringBankDetailDto>($"/api/teacher/question-banks/{bankId}", Json))!;
            Assert.Equal([1, 2], detail.Questions.Select(q => q.Order));
            Assert.NotNull(detail.Questions[1].ImageUrl);

            // a foreign path cannot be smuggled in as an image
            Assert.Equal(HttpStatusCode.BadRequest, (await t.PostAsJsonAsync($"/api/teacher/question-banks/{bankId}/questions/bulk",
                new[] { new { text = "Q", imageUrl = "/uploads/../appsettings.json", options = new[] { "a", "b" }, correctIndex = 0 } })).StatusCode);

            // the question editor uploads an image and swaps it in; other file types are refused
            var png = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var up = await t.PostAsync($"/api/teacher/question-banks/{bankId}/images", new MultipartFormDataContent { { png, "file", "chizma.png" } });
            Assert.True(up.IsSuccessStatusCode, await up.Content.ReadAsStringAsync());
            var url = (await up.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
            Assert.Matches("^/uploads/[0-9a-f]{32}\\.png$", url);
            var q1 = detail.Questions[0];
            Assert.True((await t.PutAsJsonAsync($"/api/teacher/question-banks/{bankId}/questions/{q1.Id}",
                new { text = q1.Text, imageUrl = url, options = q1.Options, correctIndex = q1.CorrectIndex })).IsSuccessStatusCode);
            detail = (await t.GetFromJsonAsync<MonitoringBankDetailDto>($"/api/teacher/question-banks/{bankId}", Json))!;
            Assert.Equal(url, detail.Questions[0].ImageUrl);
            var exe = new ByteArrayContent([1, 2, 3]);
            Assert.Equal(HttpStatusCode.BadRequest, (await t.PostAsync($"/api/teacher/question-banks/{bankId}/images",
                new MultipartFormDataContent { { exe, "file", "virus.exe" } })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await t.PostAsync($"/api/teacher/question-banks/{foreign}/images",
                new MultipartFormDataContent { { new ByteArrayContent([0x89, 0x50]), "file", "a.png" } })).StatusCode);

            // o'quv bo'limi sees the teacher's bank and its questions
            Assert.Equal(2, (await admin.GetFromJsonAsync<MonitoringBankDetailDto>($"/api/admin/monitoring/banks/{bankId}", Json))!.Questions.Count);
        }
        finally
        {
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{bankId}");
            await admin.DeleteAsync($"/api/admin/monitoring/banks/{foreign}");
        }
    }

    private static async Task<Guid> IdOf(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
