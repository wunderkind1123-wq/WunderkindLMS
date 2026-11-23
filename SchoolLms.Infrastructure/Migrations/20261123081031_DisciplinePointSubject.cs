using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DisciplinePointSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "subject_id",
                table: "discipline_points",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject_name",
                table: "discipline_points",
                type: "text",
                nullable: true);

            // Eski yozuvlardan FAQAT uy vazifasi avtomatik ballari tiklanadi: ularning kaliti
            // ("hw:ega:fan:sana:soat:o'quvchi", HomeworkDisciplinePoints.Key) fanni aniq saqlaydi.
            // Qo'lda qo'yilgan eski ballar ATAYLAB taxmin qilinmaydi — noto'g'ri fan ko'rsatish
            // umuman ko'rsatmaslikdan yomonroq (mijoz bilan kelishilgan, 2026-11-23).
            migrationBuilder.Sql("""
                UPDATE discipline_points p
                   SET subject_id = s.id, subject_name = s.name
                  FROM subjects s
                 WHERE p.source_key LIKE 'hw:%'
                   AND p.subject_id IS NULL
                   AND s.id = split_part(p.source_key, ':', 3);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "subject_name",
                table: "discipline_points");
        }
    }
}
