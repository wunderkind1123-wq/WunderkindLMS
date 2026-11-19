using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyReportRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "staff_copy_at",
                table: "auto_message_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "copy_to_staff",
                table: "auto_message_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment','weekly_report','monthly_report') or key ~ '^c_[0-9a-f]{8}$'");

            // The monthly report to parents (TZ "O'quvchi hisoboti", 2026-11-17, 3-vazifa): one more built-in
            // rule, switched OFF like every rule until the school turns it on. The 1st of the month at 10:00,
            // for the month before; the text is the PDF's caption (AutoMessageService.DefaultText).
            migrationBuilder.Sql("""
                INSERT INTO auto_message_rules
                    (key, enabled, send_time, day_of_month, min_amount, text, requires_approval, image_urls,
                     class_ids, week_days, student_ids, is_archived, only_debtors, copy_to_staff, updated_at)
                VALUES
                ('monthly_report', false, '10:00', 1, 0, $t$📊 **Oylik hisobot**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) ning {oy} oyidagi o'qishi bo'yicha oylik hisobot ilova qilindi: baholar, haftalar kesimidagi dinamika, davomat va intizom.

                Hurmat bilan, maktab o'quv bo'limi.$t$, false, '{}', '{}', '{}', '{}', false, false, false, now())
                ON CONFLICT (key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rule goes (its runs cascade; its log rows stay, as they would for any rule).
            migrationBuilder.Sql("delete from auto_message_rules where key = 'monthly_report';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "staff_copy_at",
                table: "auto_message_runs");

            migrationBuilder.DropColumn(
                name: "copy_to_staff",
                table: "auto_message_rules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment','weekly_report') or key ~ '^c_[0-9a-f]{8}$'");
        }
    }
}
