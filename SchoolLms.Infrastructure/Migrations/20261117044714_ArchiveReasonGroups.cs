using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveReasonGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lead_conversions_source",
                table: "lead_conversions");

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "student_archive_reasons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "from_student_id",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "student_archive_reason_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false, defaultValue: "slate"),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_archive_reason_groups", x => x.id);
                    table.CheckConstraint("ck_student_archive_reason_groups_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_student_archive_reason_groups_position", "position >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_archive_reasons_group_id",
                table: "student_archive_reasons",
                column: "group_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source",
                table: "leads",
                sql: "source in ('manual','survey','olympiad','archive')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source_student",
                table: "leads",
                sql: "(source = 'archive') = (from_student_id is not null)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lead_conversions_source",
                table: "lead_conversions",
                sql: "source in ('manual','survey','olympiad','archive')");

            migrationBuilder.CreateIndex(
                name: "ix_student_archive_reason_groups_name",
                table: "student_archive_reason_groups",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_student_archive_reasons_student_archive_reason_groups_group",
                table: "student_archive_reasons",
                column: "group_id",
                principalTable: "student_archive_reason_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Uchta boshlang'ich guruh va seed sabablarni ularga qo'yish (idempotent) —
            // docs/modules/archive-reason-groups.md. Yuqoridagi ikkita DropCheckConstraint —
            // CHECK'ni kengaytirish ('archive' manbasi), ma'lumot o'chirilmaydi.
            migrationBuilder.Sql(MigrationSql.Read("archive_reason_groups_seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_student_archive_reasons_student_archive_reason_groups_group",
                table: "student_archive_reasons");

            migrationBuilder.DropTable(
                name: "student_archive_reason_groups");

            migrationBuilder.DropIndex(
                name: "ix_student_archive_reasons_group_id",
                table: "student_archive_reasons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source_student",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lead_conversions_source",
                table: "lead_conversions");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "student_archive_reasons");

            migrationBuilder.DropColumn(
                name: "from_student_id",
                table: "leads");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source",
                table: "leads",
                sql: "source in ('manual','survey','olympiad')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lead_conversions_source",
                table: "lead_conversions",
                sql: "source in ('manual','survey')");
        }
    }
}
