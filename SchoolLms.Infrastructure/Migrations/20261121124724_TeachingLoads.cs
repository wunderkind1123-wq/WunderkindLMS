using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeachingLoads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "room_id",
                table: "schedule_lesson",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "owner_study_days",
                columns: table => new
                {
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner_study_days", x => x.owner_id);
                    table.CheckConstraint("ck_owner_study_days_days", "days between 5 and 6");
                });

            migrationBuilder.CreateTable(
                name: "teacher_unavailability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_unavailability", x => x.id);
                    table.CheckConstraint("ck_teacher_unavailability_day", "day between 0 and 5");
                    table.CheckConstraint("ck_teacher_unavailability_period", "period between 0 and 12");
                });

            migrationBuilder.CreateTable(
                name: "teaching_loads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sub_group = table.Column<int>(type: "integer", nullable: false),
                    teacher_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hours_per_week = table.Column<int>(type: "integer", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teaching_loads", x => x.id);
                    table.CheckConstraint("ck_teaching_loads_hours", "hours_per_week between 1 and 20");
                    table.CheckConstraint("ck_teaching_loads_owner_kind", "owner_kind in ('class','group')");
                    table.CheckConstraint("ck_teaching_loads_sub_group", "sub_group between 0 and 2");
                    table.ForeignKey(
                        name: "fk_teaching_loads_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_lesson_room_id",
                table: "schedule_lesson",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ux_teacher_unavailability",
                table: "teacher_unavailability",
                columns: new[] { "teacher_id", "day", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teaching_loads_room_id",
                table: "teaching_loads",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_teaching_loads_teacher",
                table: "teaching_loads",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ux_teaching_loads_owner_subject",
                table: "teaching_loads",
                columns: new[] { "owner_kind", "owner_id", "subject_id", "sub_group" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_schedule_lesson_rooms_room_id",
                table: "schedule_lesson",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_schedule_lesson_rooms_room_id",
                table: "schedule_lesson");

            migrationBuilder.DropTable(
                name: "owner_study_days");

            migrationBuilder.DropTable(
                name: "teacher_unavailability");

            migrationBuilder.DropTable(
                name: "teaching_loads");

            migrationBuilder.DropIndex(
                name: "ix_schedule_lesson_room_id",
                table: "schedule_lesson");

            migrationBuilder.DropColumn(
                name: "room_id",
                table: "schedule_lesson");
        }
    }
}
