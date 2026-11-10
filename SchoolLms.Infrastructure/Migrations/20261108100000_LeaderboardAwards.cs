using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaderboardAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O'quvchilar leaderboardi (2026-11-08): the points staff add by hand, with a reason list they
            // keep themselves. Everything else the leaderboard shows is read from existing tables.
            migrationBuilder.CreateTable(
                name: "leaderboard_reasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_reasons", x => x.id);
                    table.CheckConstraint("ck_leaderboard_reasons_points", "points between -1000 and 1000");
                });

            migrationBuilder.CreateTable(
                name: "leaderboard_awards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by_user_id = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_awards", x => x.id);
                    table.CheckConstraint("ck_leaderboard_awards_points", "points between -1000 and 1000 and points <> 0");
                    table.ForeignKey(
                        name: "fk_leaderboard_awards_leaderboard_reasons_reason_id",
                        column: x => x.reason_id,
                        principalTable: "leaderboard_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leaderboard_awards_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_awards_reason_id",
                table: "leaderboard_awards",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_awards_student_date",
                table: "leaderboard_awards",
                columns: new[] { "student_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "leaderboard_awards");
            migrationBuilder.DropTable(name: "leaderboard_reasons");
        }
    }
}
