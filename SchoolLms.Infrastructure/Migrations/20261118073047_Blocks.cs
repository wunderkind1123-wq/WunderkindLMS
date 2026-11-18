using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Blocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "join_reason",
                table: "study_group_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_class_id",
                table: "study_group_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_id",
                table: "schedule_lesson",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_group_id",
                table: "quarter_grades",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_class_id",
                table: "quarter_grades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_group_id",
                table: "lesson_notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_group_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_class_id",
                table: "journal_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_group_id",
                table: "interim_tests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_class_id",
                table: "interim_test_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "block_group_id",
                table: "discipline_points",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_class_id",
                table: "discipline_points",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    block_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    day_pattern = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocks", x => x.id);
                    table.CheckConstraint("ck_blocks_block_type", "block_type in ('level','base_class')");
                    table.CheckConstraint("ck_blocks_day_pattern", "day_pattern in ('odd','even','custom')");
                    table.CheckConstraint("ck_blocks_name", "btrim(name) <> ''");
                    table.ForeignKey(
                        name: "fk_blocks_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_blocks_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "block_classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_block_classes", x => x.id);
                    table.CheckConstraint("ck_block_classes_owner_kind", "owner_kind in ('class','group')");
                    table.ForeignKey(
                        name: "fk_block_classes_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "block_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    base_owner_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    base_owner_id = table.Column<string>(type: "text", nullable: true),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_block_groups", x => x.id);
                    table.CheckConstraint("ck_block_groups_base_owner", "(base_owner_kind is null) = (base_owner_id is null) and (base_owner_kind is null or base_owner_kind in ('class','group'))");
                    table.CheckConstraint("ck_block_groups_code", "code in ('KATTA','KICHIK','custom')");
                    table.CheckConstraint("ck_block_groups_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_block_groups_sort_order", "sort_order in (1, 2)");
                    table.ForeignKey(
                        name: "fk_block_groups_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_block_groups_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_block_groups_study_groups_study_group_id",
                        column: x => x.study_group_id,
                        principalTable: "study_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "block_group_teachers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_block_group_teachers", x => x.id);
                    table.CheckConstraint("ck_block_group_teachers_period", "valid_to is null or valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_block_group_teachers_block_groups_block_group_id",
                        column: x => x.block_group_id,
                        principalTable: "block_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_block_group_teachers_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_block_group_teachers_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_lesson_block",
                table: "schedule_lesson",
                column: "block_id",
                filter: "block_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_quarter_grades_block_group_id",
                table: "quarter_grades",
                column: "block_group_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quarter_grades_block_group",
                table: "quarter_grades",
                sql: "block_group_id is null or owner_kind = 'group'");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_notes_block_group_id",
                table: "lesson_notes",
                column: "block_group_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lesson_notes_block_group",
                table: "lesson_notes",
                sql: "block_group_id is null or owner_kind = 'group'");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_block_group",
                table: "journal_entries",
                column: "block_group_id",
                filter: "block_group_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_source_class",
                table: "journal_entries",
                column: "source_class_id",
                filter: "source_class_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_block_group",
                table: "journal_entries",
                sql: "block_group_id is null or owner_kind = 'group'");

            migrationBuilder.CreateIndex(
                name: "ix_interim_tests_block_group_id",
                table: "interim_tests",
                column: "block_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_discipline_points_block_group",
                table: "discipline_points",
                column: "block_group_id",
                filter: "block_group_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_block_classes_owner",
                table: "block_classes",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_block_classes_block_owner",
                table: "block_classes",
                columns: new[] { "block_id", "owner_kind", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_block_group_teachers_created_by",
                table: "block_group_teachers",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_block_group_teachers_teacher",
                table: "block_group_teachers",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ux_block_group_teachers_one_current",
                table: "block_group_teachers",
                column: "block_group_id",
                unique: true,
                filter: "valid_to is null");

            migrationBuilder.CreateIndex(
                name: "ix_block_groups_room_id",
                table: "block_groups",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ux_block_groups_block_sort",
                table: "block_groups",
                columns: new[] { "block_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_block_groups_study_group",
                table: "block_groups",
                column: "study_group_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_blocks_created_by",
                table: "blocks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_blocks_subject_id",
                table: "blocks",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_blocks_year_subject",
                table: "blocks",
                columns: new[] { "academic_year", "subject_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_discipline_points_block_groups_block_group_id",
                table: "discipline_points",
                column: "block_group_id",
                principalTable: "block_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_interim_tests_block_groups_block_group_id",
                table: "interim_tests",
                column: "block_group_id",
                principalTable: "block_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_block_groups_block_group_id",
                table: "journal_entries",
                column: "block_group_id",
                principalTable: "block_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_lesson_notes_block_groups_block_group_id",
                table: "lesson_notes",
                column: "block_group_id",
                principalTable: "block_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_quarter_grades_block_groups_block_group_id",
                table: "quarter_grades",
                column: "block_group_id",
                principalTable: "block_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_schedule_lesson_blocks_block_id",
                table: "schedule_lesson",
                column: "block_id",
                principalTable: "blocks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discipline_points_block_groups_block_group_id",
                table: "discipline_points");

            migrationBuilder.DropForeignKey(
                name: "fk_interim_tests_block_groups_block_group_id",
                table: "interim_tests");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_block_groups_block_group_id",
                table: "journal_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_lesson_notes_block_groups_block_group_id",
                table: "lesson_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_quarter_grades_block_groups_block_group_id",
                table: "quarter_grades");

            migrationBuilder.DropForeignKey(
                name: "fk_schedule_lesson_blocks_block_id",
                table: "schedule_lesson");

            migrationBuilder.DropTable(
                name: "block_classes");

            migrationBuilder.DropTable(
                name: "block_group_teachers");

            migrationBuilder.DropTable(
                name: "block_groups");

            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropIndex(
                name: "ix_schedule_lesson_block",
                table: "schedule_lesson");

            migrationBuilder.DropIndex(
                name: "ix_quarter_grades_block_group_id",
                table: "quarter_grades");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quarter_grades_block_group",
                table: "quarter_grades");

            migrationBuilder.DropIndex(
                name: "ix_lesson_notes_block_group_id",
                table: "lesson_notes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lesson_notes_block_group",
                table: "lesson_notes");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_block_group",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_source_class",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_block_group",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_interim_tests_block_group_id",
                table: "interim_tests");

            migrationBuilder.DropIndex(
                name: "ix_discipline_points_block_group",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "join_reason",
                table: "study_group_members");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "study_group_members");

            migrationBuilder.DropColumn(
                name: "block_id",
                table: "schedule_lesson");

            migrationBuilder.DropColumn(
                name: "block_group_id",
                table: "quarter_grades");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "quarter_grades");

            migrationBuilder.DropColumn(
                name: "block_group_id",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "block_group_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "block_group_id",
                table: "interim_tests");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "interim_test_results");

            migrationBuilder.DropColumn(
                name: "block_group_id",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "discipline_points");
        }
    }
}
