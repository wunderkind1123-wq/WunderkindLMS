using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringSittings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_sittings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    test_date = table.Column<DateOnly>(type: "date", nullable: false),
                    test_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_sittings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_sitting_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sitting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    device_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_sitting_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitoring_sitting_codes_monitoring_sittings_sitting_id",
                        column: x => x.sitting_id,
                        principalTable: "monitoring_sittings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_monitoring_sitting_codes_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_sitting_codes_student_id",
                table: "monitoring_sitting_codes",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_sitting_codes_code",
                table: "monitoring_sitting_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_sitting_codes_once",
                table: "monitoring_sitting_codes",
                columns: new[] { "sitting_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_sittings_test_date",
                table: "monitoring_sittings",
                column: "test_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_sitting_codes");

            migrationBuilder.DropTable(
                name: "monitoring_sittings");
        }
    }
}
