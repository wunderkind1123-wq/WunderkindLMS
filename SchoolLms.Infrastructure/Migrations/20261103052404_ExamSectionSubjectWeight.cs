using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Blok testda bir fan ikki blokda bo'lishi mumkin — asosiy 3,1 va majburiy 1,1 (mijoz, 2026-11-03).
    /// Down: shunday imtihon yaratilgan bo'lsa eski indeks qurilmaydi — avval takror bo'limni olib tashlang.
    /// </summary>
    public partial class ExamSectionSubjectWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_exam_sections_exam_subject",
                table: "exam_sections");

            migrationBuilder.CreateIndex(
                name: "ux_exam_sections_exam_subject_weight",
                table: "exam_sections",
                columns: new[] { "exam_id", "subject_id", "points_per_correct" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_exam_sections_exam_subject_weight",
                table: "exam_sections");

            migrationBuilder.CreateIndex(
                name: "ux_exam_sections_exam_subject",
                table: "exam_sections",
                columns: new[] { "exam_id", "subject_id" },
                unique: true);
        }
    }
}
