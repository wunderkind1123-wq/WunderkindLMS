using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SchoolLms.Infrastructure.Data;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// MCP (app_ro) blok jadvallarini o'qiy oladi: `LessonRoster` qaysi blok guruhlari tirikligini
    /// `blocks` / `block_groups` dan biladi, va ro'yxatga tayanadigan har bir MCP vositasi (davomat,
    /// o'quvchi profili, maktab sharhi) usiz "permission denied" bilan yiqilardi (docs/modules/blocks.md).
    /// Jadvallar mcp_guards.sql allow-list'iga qo'shildi; uni qayta ishga tushirish grant'larni qayta
    /// qo'llaydi. Sxema o'zgarmaydi.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261118150000_BlocksMcpGrants")]
    public partial class BlocksMcpGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Jadvallar Blocks migratsiyasi Down'ida o'chadi; app_ro grant'lari ular bilan ketadi.
        }
    }
}
