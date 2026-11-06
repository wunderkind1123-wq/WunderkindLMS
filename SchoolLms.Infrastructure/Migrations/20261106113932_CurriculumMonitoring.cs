using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "block_subject1id",
                table: "study_groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "block_subject2id",
                table: "study_groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "curriculum_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curriculum_plans", x => x.id);
                    table.CheckConstraint("ck_curriculum_plans_grade", "grade is null or grade between 0 and 11");
                    table.CheckConstraint("ck_curriculum_plans_owner", "(grade is null) <> (group_id is null)");
                    table.ForeignKey(
                        name: "fk_curriculum_plans_study_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "study_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_curriculum_plans_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_banks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_banks", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitoring_banks_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    scoring = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    national_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    test_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_limit_min = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_tests", x => x.id);
                    table.CheckConstraint("ck_monitoring_tests_delivery", "delivery in ('paper','online')");
                    table.CheckConstraint("ck_monitoring_tests_method", "(scoring = 'simple' and national_method is null) or (scoring = 'national' and national_method in ('rasch','weights'))");
                    table.CheckConstraint("ck_monitoring_tests_scoring", "scoring in ('simple','national')");
                    table.CheckConstraint("ck_monitoring_tests_status", "status in ('draft','published','closed')");
                    table.ForeignKey(
                        name: "fk_monitoring_tests_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "curriculum_topics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: true),
                    week = table.Column<int>(type: "integer", nullable: true),
                    section = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    hours = table.Column<int>(type: "integer", nullable: false),
                    goal = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    homework = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curriculum_topics", x => x.id);
                    table.CheckConstraint("ck_curriculum_topics_hours", "hours >= 1");
                    table.CheckConstraint("ck_curriculum_topics_quarter", "quarter is null or quarter between 1 and 4");
                    table.ForeignKey(
                        name: "fk_curriculum_topics_curriculum_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "curriculum_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    answers = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    access_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    device_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correct = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    points = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    level = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_participants", x => x.id);
                    table.CheckConstraint("ck_monitoring_participants_owner_kind", "owner_kind in ('class','group')");
                    table.CheckConstraint("ck_monitoring_participants_status", "status in ('pending','in_progress','done','absent')");
                    table.ForeignKey(
                        name: "fk_monitoring_participants_monitoring_tests_test_id",
                        column: x => x.test_id,
                        principalTable: "monitoring_tests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_monitoring_participants_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curriculum_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    passed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    marked_by = table.Column<string>(type: "text", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curriculum_progress", x => x.id);
                    table.CheckConstraint("ck_curriculum_progress_owner_kind", "owner_kind in ('class','group')");
                    table.ForeignKey(
                        name: "fk_curriculum_progress_curriculum_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "curriculum_topics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_questions", x => x.id);
                    table.CheckConstraint("ck_monitoring_questions_correct", "correct_index >= 0 and correct_index < 6");
                    table.CheckConstraint("ck_monitoring_questions_weight", "weight > 0");
                    table.ForeignKey(
                        name: "fk_monitoring_questions_curriculum_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "curriculum_topics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_questions_monitoring_banks_bank_id",
                        column: x => x.bank_id,
                        principalTable: "monitoring_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    option_count = table.Column<int>(type: "integer", nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_items", x => x.id);
                    table.CheckConstraint("ck_monitoring_items_correct", "correct_index >= 0 and correct_index < option_count");
                    table.CheckConstraint("ck_monitoring_items_options", "option_count between 2 and 6");
                    table.CheckConstraint("ck_monitoring_items_weight", "weight > 0");
                    table.ForeignKey(
                        name: "fk_monitoring_items_curriculum_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "curriculum_topics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_items_monitoring_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "monitoring_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_items_monitoring_tests_test_id",
                        column: x => x.test_id,
                        principalTable: "monitoring_tests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_curriculum_plans_group_id",
                table: "curriculum_plans",
                column: "group_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_curriculum_progress_owner_kind_owner_id",
                table: "curriculum_progress",
                columns: new[] { "owner_kind", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_progress_once",
                table: "curriculum_progress",
                columns: new[] { "topic_id", "owner_kind", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_curriculum_topics_order",
                table: "curriculum_topics",
                columns: new[] { "plan_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_banks_subject_id",
                table: "monitoring_banks",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_items_question_id",
                table: "monitoring_items",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_items_topic_id",
                table: "monitoring_items",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_items_order",
                table: "monitoring_items",
                columns: new[] { "test_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_participants_student_id",
                table: "monitoring_participants",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_participants_code",
                table: "monitoring_participants",
                column: "access_code",
                unique: true,
                filter: "access_code is not null");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_participants_once",
                table: "monitoring_participants",
                columns: new[] { "test_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_questions_bank_id_order",
                table: "monitoring_questions",
                columns: new[] { "bank_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_questions_topic_id",
                table: "monitoring_questions",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_tests_subject_id",
                table: "monitoring_tests",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_tests_test_date",
                table: "monitoring_tests",
                column: "test_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "curriculum_progress");

            migrationBuilder.DropTable(
                name: "monitoring_items");

            migrationBuilder.DropTable(
                name: "monitoring_participants");

            migrationBuilder.DropTable(
                name: "monitoring_questions");

            migrationBuilder.DropTable(
                name: "monitoring_tests");

            migrationBuilder.DropTable(
                name: "curriculum_topics");

            migrationBuilder.DropTable(
                name: "monitoring_banks");

            migrationBuilder.DropTable(
                name: "curriculum_plans");

            migrationBuilder.DropColumn(
                name: "block_subject1id",
                table: "study_groups");

            migrationBuilder.DropColumn(
                name: "block_subject2id",
                table: "study_groups");
        }
    }
}
