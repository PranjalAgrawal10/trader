using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trader.Infrastructure.Persistence;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    [DbContext(typeof(TraderDbContext))]
    [Migration("20260722040000_AddOpeningAtmTrailPrefs")]
    public class AddOpeningAtmTrailPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NiftyOpenAutoTradeTrailEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "NiftyOpenAutoTradeTrailPoints",
                table: "Users",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeTrailEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeTrailPoints",
                table: "Users");
        }
    }
}
