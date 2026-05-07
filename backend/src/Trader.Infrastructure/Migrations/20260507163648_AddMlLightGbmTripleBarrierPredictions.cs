using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMlLightGbmTripleBarrierPredictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MlLightGbmTripleBarrierPredictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstrumentToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Interval = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PredictedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RefBarTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RefClose = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Confidence = table.Column<int>(type: "int", nullable: false),
                    ModelId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextBarTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    NextClose = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MlLightGbmTripleBarrierPredictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MlLightGbmTripleBarrierPredictions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MlLightGbmTripleBarrierPredictions_UserId_InstrumentToken_In~",
                table: "MlLightGbmTripleBarrierPredictions",
                columns: new[] { "UserId", "InstrumentToken", "Interval", "PredictedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MlLightGbmTripleBarrierPredictions");
        }
    }
}
