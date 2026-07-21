using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNiftyOpenAutoTradeTrailingStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrailActive",
                table: "NiftyOpenAutoTradeRuns",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TrailPeakPrice",
                table: "NiftyOpenAutoTradeRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrailPoints",
                table: "NiftyOpenAutoTradeRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrailStopPrice",
                table: "NiftyOpenAutoTradeRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NiftyOpenAutoTradeRuns_TrailActive",
                table: "NiftyOpenAutoTradeRuns",
                column: "TrailActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NiftyOpenAutoTradeRuns_TrailActive",
                table: "NiftyOpenAutoTradeRuns");

            migrationBuilder.DropColumn(
                name: "TrailActive",
                table: "NiftyOpenAutoTradeRuns");

            migrationBuilder.DropColumn(
                name: "TrailPeakPrice",
                table: "NiftyOpenAutoTradeRuns");

            migrationBuilder.DropColumn(
                name: "TrailPoints",
                table: "NiftyOpenAutoTradeRuns");

            migrationBuilder.DropColumn(
                name: "TrailStopPrice",
                table: "NiftyOpenAutoTradeRuns");
        }
    }
}
