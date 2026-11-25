using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringStudentCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "room_pin",
                table: "monitoring_tests",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "room_pin",
                table: "monitoring_sittings",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "monitoring_student_codes",
                columns: table => new
                {
                    student_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_student_codes", x => x.student_id);
                    table.ForeignKey(
                        name: "fk_monitoring_student_codes_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_tests_room_pin",
                table: "monitoring_tests",
                column: "room_pin");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_sittings_room_pin",
                table: "monitoring_sittings",
                column: "room_pin");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_student_codes_code",
                table: "monitoring_student_codes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_student_codes");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_tests_room_pin",
                table: "monitoring_tests");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_sittings_room_pin",
                table: "monitoring_sittings");

            migrationBuilder.DropColumn(
                name: "room_pin",
                table: "monitoring_tests");

            migrationBuilder.DropColumn(
                name: "room_pin",
                table: "monitoring_sittings");
        }
    }
}
