using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyReportRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The weekly report to parents (2026-11-08): one more built-in rule, switched OFF like every
            // rule until the school turns it on. Sundays at 09:00; the text is the PDF's caption
            // (AutoMessageService.DefaultText).
            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment','weekly_report') or key ~ '^c_[0-9a-f]{8}$'");

            migrationBuilder.Sql("""
                INSERT INTO auto_message_rules
                    (key, enabled, send_time, day_of_month, min_amount, text, requires_approval, image_urls,
                     class_ids, week_days, student_ids, is_archived, only_debtors, updated_at)
                VALUES
                ('weekly_report', false, '09:00', 1, 0, $t$📊 **Haftalik hisobot**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) ning {hafta} kunlaridagi o'qishi bo'yicha haftalik hisobot ilova qilindi: baholar, davomat va intizom.

                Hurmat bilan, maktab o'quv bo'limi.$t$, false, '{}', '{}', '{}', '{}', false, false, now())
                ON CONFLICT (key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rule goes (its runs cascade; its log rows stay, as they would for any rule).
            migrationBuilder.Sql("delete from auto_message_rules where key = 'weekly_report';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment') or key ~ '^c_[0-9a-f]{8}$'");
        }
    }
}
