using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiteInstrumentsChartSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                    name: "KiteInstrumentsChartGraphType",
                    table: "Users",
                    type: "varchar(16)",
                    maxLength: 16,
                    nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                    name: "KiteInstrumentsChartInterval",
                    table: "Users",
                    type: "varchar(16)",
                    maxLength: 16,
                    nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                    name: "KiteInstrumentsChartRangePreset",
                    table: "Users",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KiteInstrumentsChartGraphType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KiteInstrumentsChartInterval",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KiteInstrumentsChartRangePreset",
                table: "Users");
        }
    }
}
