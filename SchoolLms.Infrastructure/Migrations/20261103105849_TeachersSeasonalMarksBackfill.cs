using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Mavsumiy baholash hamma o'qituvchiga ochiladi (mijoz, 2026-11-03). AdmissionAndExams back-fill'idan
    /// keyin forma orqali qo'shilgan o'qituvchilarda bu kalit yo'q edi: forma uni yubormasdi va tugmasi ham
    /// yo'q edi, shuning uchun bo'lim yopiq turardi va admin ocha olmasdi.
    /// Down hech narsa qilmaydi: kalit kimda avvaldan bor bo'lganini ajratib bo'lmaydi.
    /// </summary>
    public partial class TeachersSeasonalMarksBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE teachers SET permissions = array_append(permissions, 'seasonalMarks') "
                + "WHERE NOT ('seasonalMarks' = ANY (permissions));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
