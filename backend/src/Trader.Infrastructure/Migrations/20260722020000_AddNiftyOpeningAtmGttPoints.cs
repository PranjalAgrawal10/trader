using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNiftyOpeningAtmGttPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "NiftyOpenAutoTradeMaxLots",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 10,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 5);

            migrationBuilder.AddColumn<decimal>(
                name: "NiftyOpenAutoTradeStopLossPoints",
                table: "Users",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "NiftyOpenAutoTradeTargetPoints",
                table: "Users",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<bool>(
                name: "NiftyOpenAutoTradeStopLossEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NiftyOpenAutoTradeTargetEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeStopLossPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeTargetPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeStopLossEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeTargetEnabled",
                table: "Users");

            migrationBuilder.AlterColumn<int>(
                name: "NiftyOpenAutoTradeMaxLots",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 10);
        }
    }
}
