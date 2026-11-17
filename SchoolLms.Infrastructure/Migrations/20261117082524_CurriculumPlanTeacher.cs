using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumPlanTeacher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_grade",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_group",
                table: "curriculum_plans");

            migrationBuilder.AddColumn<string>(
                name: "teacher_id",
                table: "curriculum_plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_curriculum_plans_teacher_id",
                table: "curriculum_plans",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_grade",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "grade" },
                unique: true,
                filter: "grade is not null and teacher_id is null");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_grade_teacher",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "grade", "teacher_id" },
                unique: true,
                filter: "grade is not null and teacher_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_group",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "group_id" },
                unique: true,
                filter: "group_id is not null and teacher_id is null");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_group_teacher",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "group_id", "teacher_id" },
                unique: true,
                filter: "group_id is not null and teacher_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_curriculum_plans_teachers_teacher_id",
                table: "curriculum_plans",
                column: "teacher_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_curriculum_plans_teachers_teacher_id",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ix_curriculum_plans_teacher_id",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_grade",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_grade_teacher",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_group",
                table: "curriculum_plans");

            migrationBuilder.DropIndex(
                name: "ux_curriculum_plans_subject_group_teacher",
                table: "curriculum_plans");

            migrationBuilder.DropColumn(
                name: "teacher_id",
                table: "curriculum_plans");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_grade",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "grade" },
                unique: true,
                filter: "grade is not null");

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_plans_subject_group",
                table: "curriculum_plans",
                columns: new[] { "subject_id", "group_id" },
                unique: true,
                filter: "group_id is not null");
        }
    }
}
