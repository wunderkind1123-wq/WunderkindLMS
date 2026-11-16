using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOlympiad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source",
                table: "leads");

            migrationBuilder.CreateTable(
                name: "olympiad_bot_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    subscribed_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_bot_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_broadcasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    attachments_json = table.Column<string>(type: "text", nullable: false),
                    buttons_json = table.Column<string>(type: "text", nullable: false),
                    audience_json = table.Column<string>(type: "text", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    sent = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    blocked = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_broadcasts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chat_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invite_link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    location_map_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    on_full = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    registration_opens_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    registration_closes_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    max_score = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    show_score = table.Column<bool>(type: "boolean", nullable: false),
                    show_rank = table.Column<bool>(type: "boolean", nullable: false),
                    show_certificate = table.Column<bool>(type: "boolean", nullable: false),
                    show_invite = table.Column<bool>(type: "boolean", nullable: false),
                    rules_text = table.Column<string>(type: "text", nullable: false),
                    rules_file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    rules_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rules_display = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    seats_taken = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false),
                    last_waitlist_seq = table.Column<int>(type: "integer", nullable: false),
                    checkin_closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    results_sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_media_cache",
                columns: table => new
                {
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_media_cache", x => x.path);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    bot_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bot_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bot_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    grade_from = table.Column<int>(type: "integer", nullable: false),
                    grade_to = table.Column<int>(type: "integer", nullable: false),
                    default_on_full = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rules_text = table.Column<string>(type: "text", nullable: false),
                    rules_file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    rules_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rules_display = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    reminder1enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reminder1minutes = table.Column<int>(type: "integer", nullable: false),
                    reminder2enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reminder2minutes = table.Column<int>(type: "integer", nullable: false),
                    result_show_score = table.Column<bool>(type: "boolean", nullable: false),
                    result_show_rank = table.Column<bool>(type: "boolean", nullable: false),
                    result_show_certificate = table.Column<bool>(type: "boolean", nullable: false),
                    result_show_invite = table.Column<bool>(type: "boolean", nullable: false),
                    invite_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invite_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    certificate_format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    admin_link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_templates",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    attachments_json = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_templates", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false),
                    school = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    lead_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    lead_converted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_olympiad_participants_olympiad_bot_users_bot_user_id",
                        column: x => x.bot_user_id,
                        principalTable: "olympiad_bot_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    offset_minutes = table.Column<int>(type: "integer", nullable: true),
                    send_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    attachments_json = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    sent_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_olympiad_reminders_olympiad_events_event_id",
                        column: x => x.event_id,
                        principalTable: "olympiad_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "olympiad_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    waitlist_seq = table.Column<int>(type: "integer", nullable: false),
                    qr_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rules_accepted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    checked_in_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    checked_in_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    checkin_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    score = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    score_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    score_updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    score_updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    result_sent_score = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    result_sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_via = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_olympiad_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_olympiad_registrations_olympiad_events_event_id",
                        column: x => x.event_id,
                        principalTable: "olympiad_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_olympiad_registrations_olympiad_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "olympiad_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source",
                table: "leads",
                sql: "source in ('manual','survey','olympiad')");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_bot_users_telegram_user_id",
                table: "olympiad_bot_users",
                column: "telegram_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_broadcasts_status",
                table: "olympiad_broadcasts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_deliveries_kind_ref_id_bot_user_id_registration_id",
                table: "olympiad_deliveries",
                columns: new[] { "kind", "ref_id", "bot_user_id", "registration_id", "tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_deliveries_status_id",
                table: "olympiad_deliveries",
                columns: new[] { "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_events_code",
                table: "olympiad_events",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_events_status_starts_at",
                table: "olympiad_events",
                columns: new[] { "status", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_participants_bot_user_id",
                table: "olympiad_participants",
                column: "bot_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_participants_phone",
                table: "olympiad_participants",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_registrations_event_id_participant_id",
                table: "olympiad_registrations",
                columns: new[] { "event_id", "participant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_registrations_participant_id",
                table: "olympiad_registrations",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_registrations_qr_token",
                table: "olympiad_registrations",
                column: "qr_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_reminders_event_id",
                table: "olympiad_reminders",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_olympiad_reminders_status_event_id",
                table: "olympiad_reminders",
                columns: new[] { "status", "event_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "olympiad_broadcasts");

            migrationBuilder.DropTable(
                name: "olympiad_channels");

            migrationBuilder.DropTable(
                name: "olympiad_deliveries");

            migrationBuilder.DropTable(
                name: "olympiad_media_cache");

            migrationBuilder.DropTable(
                name: "olympiad_registrations");

            migrationBuilder.DropTable(
                name: "olympiad_reminders");

            migrationBuilder.DropTable(
                name: "olympiad_settings");

            migrationBuilder.DropTable(
                name: "olympiad_templates");

            migrationBuilder.DropTable(
                name: "olympiad_participants");

            migrationBuilder.DropTable(
                name: "olympiad_events");

            migrationBuilder.DropTable(
                name: "olympiad_bot_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source",
                table: "leads");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source",
                table: "leads",
                sql: "source in ('manual','survey')");
        }
    }
}
