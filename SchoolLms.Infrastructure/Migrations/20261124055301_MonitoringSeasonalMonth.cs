using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringSeasonalMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "seasonal_month",
                table: "monitoring_tests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seasonal_year",
                table: "monitoring_tests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_tests_seasonal",
                table: "monitoring_tests",
                sql: "(seasonal_year is null and seasonal_month is null) or (seasonal_year between 2000 and 2100 and seasonal_month between 1 and 12)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_tests_seasonal",
                table: "monitoring_tests");

            migrationBuilder.DropColumn(
                name: "seasonal_month",
                table: "monitoring_tests");

            migrationBuilder.DropColumn(
                name: "seasonal_year",
                table: "monitoring_tests");
        }
    }
}
