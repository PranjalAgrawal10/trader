using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoPaperBuyLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoPaperBuyLegs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstrumentToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContractsRemaining = table.Column<int>(type: "int", nullable: false),
                    BoughtAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoPaperBuyLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoPaperBuyLegs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DemoPaperBuyLegs_UserId_InstrumentToken_BoughtAtUtc",
                table: "DemoPaperBuyLegs",
                columns: new[] { "UserId", "InstrumentToken", "BoughtAtUtc" });

            // Existing open positions prior to FIFO legs — one synthetic leg each so sells stay consistent with chart markers.
            migrationBuilder.Sql(
                @"INSERT INTO `DemoPaperBuyLegs` (`Id`, `UserId`, `InstrumentToken`, `ContractsRemaining`, `BoughtAtUtc`)
SELECT UUID(), `UserId`, `InstrumentToken`, `OpenContracts`, `UpdatedAtUtc`
FROM `DemoPaperPositions`
WHERE `OpenContracts` > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoPaperBuyLegs");
        }
    }
}
