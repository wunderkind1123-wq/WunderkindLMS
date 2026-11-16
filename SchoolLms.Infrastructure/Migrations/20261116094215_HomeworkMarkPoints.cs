using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HomeworkMarkPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "homework_mark",
                table: "discipline_reasons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_key",
                table: "discipline_points",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_discipline_reasons_homework_mark",
                table: "discipline_reasons",
                column: "homework_mark",
                unique: true,
                filter: "homework_mark is not null");

            migrationBuilder.CreateIndex(
                name: "ux_discipline_points_source_key",
                table: "discipline_points",
                column: "source_key",
                unique: true,
                filter: "source_key is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_discipline_reasons_homework_mark",
                table: "discipline_reasons");

            migrationBuilder.DropIndex(
                name: "ux_discipline_points_source_key",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "homework_mark",
                table: "discipline_reasons");

            migrationBuilder.DropColumn(
                name: "source_key",
                table: "discipline_points");
        }
    }
}
