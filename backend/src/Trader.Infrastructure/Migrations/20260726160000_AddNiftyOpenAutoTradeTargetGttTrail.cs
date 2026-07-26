using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trader.Infrastructure.Persistence;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    [DbContext(typeof(TraderDbContext))]
    [Migration("20260726160000_AddNiftyOpenAutoTradeTargetGttTrail")]
    public class AddNiftyOpenAutoTradeTargetGttTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetGttTriggerId",
                table: "NiftyOpenAutoTradeRuns",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "TrailTargetPrice",
                table: "NiftyOpenAutoTradeRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetGttTriggerId",
                table: "NiftyOpenAutoTradeRuns");

            migrationBuilder.DropColumn(
                name: "TrailTargetPrice",
                table: "NiftyOpenAutoTradeRuns");
        }
    }
}
