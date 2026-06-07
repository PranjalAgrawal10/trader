using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScalperSettingsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScalperGraphType",
                table: "Users",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ScalperInterval",
                table: "Users",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ScalperRangePreset",
                table: "Users",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ScalperSafeModeEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ScalperSafeStopLossPoints",
                table: "Users",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ScalperSafeTriggerPoints",
                table: "Users",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScalperShowVolume",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScalperGraphType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperInterval",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperRangePreset",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperSafeModeEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperSafeStopLossPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperSafeTriggerPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperShowVolume",
                table: "Users");
        }
    }
}
