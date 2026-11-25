using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_leaves",
                table: "monitoring_tests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "shuffle",
                table: "monitoring_tests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "option_images",
                table: "monitoring_questions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "events",
                table: "monitoring_participants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "finish_reason",
                table: "monitoring_participants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "leaves",
                table: "monitoring_participants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "option_images",
                table: "monitoring_items",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_tests_max_leaves",
                table: "monitoring_tests",
                sql: "max_leaves is null or max_leaves between 0 and 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_participants_finish_reason",
                table: "monitoring_participants",
                sql: "finish_reason is null or finish_reason in ('time','leaves')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_participants_leaves",
                table: "monitoring_participants",
                sql: "leaves >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_tests_max_leaves",
                table: "monitoring_tests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_participants_finish_reason",
                table: "monitoring_participants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_participants_leaves",
                table: "monitoring_participants");

            migrationBuilder.DropColumn(
                name: "max_leaves",
                table: "monitoring_tests");

            migrationBuilder.DropColumn(
                name: "shuffle",
                table: "monitoring_tests");

            migrationBuilder.DropColumn(
                name: "option_images",
                table: "monitoring_questions");

            migrationBuilder.DropColumn(
                name: "events",
                table: "monitoring_participants");

            migrationBuilder.DropColumn(
                name: "finish_reason",
                table: "monitoring_participants");

            migrationBuilder.DropColumn(
                name: "leaves",
                table: "monitoring_participants");

            migrationBuilder.DropColumn(
                name: "option_images",
                table: "monitoring_items");
        }
    }
}
