using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trader.Infrastructure.Persistence;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    [DbContext(typeof(TraderDbContext))]
    [Migration("20260722030000_AddOpeningAtmUnderlying")]
    public class AddOpeningAtmUnderlying : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NiftyOpenAutoTradeUnderlying",
                table: "Users",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "nifty")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NiftyOpenAutoTradeUnderlying",
                table: "Users");
        }
    }
}
