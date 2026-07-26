using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trader.Infrastructure.Persistence;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    [DbContext(typeof(TraderDbContext))]
    [Migration("20260726163000_AddNiftyOpenAutoTradeInstrumentToken")]
    public class AddNiftyOpenAutoTradeInstrumentToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstrumentToken",
                table: "NiftyOpenAutoTradeRuns",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstrumentToken",
                table: "NiftyOpenAutoTradeRuns");
        }
    }
}
