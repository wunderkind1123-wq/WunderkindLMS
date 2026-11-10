using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InterimTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Oraliq test (2026-11-10): the track groups' weekly interim tests — the teacher sets the
            // question count and types each pupil's correct answers.
            migrationBuilder.CreateTable(
                name: "interim_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    question_count = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interim_tests", x => x.id);
                    table.CheckConstraint("ck_interim_tests_question_count", "question_count between 1 and 300");
                    table.ForeignKey(
                        name: "fk_interim_tests_study_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "study_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interim_tests_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interim_test_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    correct = table.Column<int>(type: "integer", nullable: true),
                    absent = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interim_test_results", x => x.id);
                    table.CheckConstraint("ck_interim_test_results_value", "(absent and correct is null) or (not absent and correct is not null and correct >= 0)");
                    table.ForeignKey(
                        name: "fk_interim_test_results_interim_tests_test_id",
                        column: x => x.test_id,
                        principalTable: "interim_tests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interim_test_results_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interim_tests_subject_id",
                table: "interim_tests",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_interim_tests_group_subject_date",
                table: "interim_tests",
                columns: new[] { "group_id", "subject_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interim_test_results_student_id",
                table: "interim_test_results",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_interim_test_results_test_student",
                table: "interim_test_results",
                columns: new[] { "test_id", "student_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "interim_test_results");
            migrationBuilder.DropTable(name: "interim_tests");
        }
    }
}
