using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalSubstitutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "substitute_teacher_id",
                table: "lesson_substitutions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "external_substitute_id",
                table: "lesson_substitutions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_substitutes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_substitutes", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_substitutes_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_substitutions_external_substitute_id_date",
                table: "lesson_substitutions",
                columns: new[] { "external_substitute_id", "date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lesson_substitutions_substitute",
                table: "lesson_substitutions",
                sql: "(substitute_teacher_id is null) <> (external_substitute_id is null)");

            migrationBuilder.CreateIndex(
                name: "ix_external_substitutes_phone",
                table: "external_substitutes",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_substitutes_subject_id",
                table: "external_substitutes",
                column: "subject_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lesson_substitutions_external_substitutes_external_substitu",
                table: "lesson_substitutions",
                column: "external_substitute_id",
                principalTable: "external_substitutes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lesson_substitutions_external_substitutes_external_substitu",
                table: "lesson_substitutions");

            migrationBuilder.DropTable(
                name: "external_substitutes");

            migrationBuilder.DropIndex(
                name: "ix_lesson_substitutions_external_substitute_id_date",
                table: "lesson_substitutions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lesson_substitutions_substitute",
                table: "lesson_substitutions");

            migrationBuilder.DropColumn(
                name: "external_substitute_id",
                table: "lesson_substitutions");

            migrationBuilder.AlterColumn<string>(
                name: "substitute_teacher_id",
                table: "lesson_substitutions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
