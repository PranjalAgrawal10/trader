using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddKiteInstrumentsChartZoomJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "KiteInstrumentsChartZoomJson",
            table: "Users",
            type: "longtext",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "KiteInstrumentsChartZoomJson",
            table: "Users");
    }
}
