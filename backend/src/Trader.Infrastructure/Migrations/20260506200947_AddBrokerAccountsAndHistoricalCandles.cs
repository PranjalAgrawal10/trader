using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerAccountsAndHistoricalCandles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrokerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BrokerName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccessTokenProtected = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RefreshTokenProtected = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ExternalUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrokerAccounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HistoricalCandles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstrumentToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    High = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalCandles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerAccounts_UserId_BrokerName",
                table: "BrokerAccounts",
                columns: new[] { "UserId", "BrokerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalCandles_InstrumentToken_Timeframe_TimestampUtc",
                table: "HistoricalCandles",
                columns: new[] { "InstrumentToken", "Timeframe", "TimestampUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalCandles_InstrumentToken_TimestampUtc",
                table: "HistoricalCandles",
                columns: new[] { "InstrumentToken", "TimestampUtc" });

            migrationBuilder.Sql(
                """
                INSERT INTO `BrokerAccounts` (`Id`, `UserId`, `BrokerName`, `ApiKey`, `AccessTokenProtected`, `RefreshTokenProtected`, `TokenExpiresAt`, `ExternalUserId`, `ConnectedAt`)
                SELECT UUID(), `Id`, COALESCE(NULLIF(TRIM(`BrokerProvider`), ''), 'Zerodha'), NULL, `KiteAccessTokenProtected`, `KiteRefreshTokenProtected`, NULL, `KiteUserId`, `BrokerConnectedAt`
                FROM `Users`
                WHERE `KiteAccessTokenProtected` IS NOT NULL AND CHAR_LENGTH(`KiteAccessTokenProtected`) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "BrokerConnectedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BrokerProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KiteAccessTokenProtected",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KiteRefreshTokenProtected",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KiteUserId",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BrokerConnectedAt",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrokerProvider",
                table: "Users",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "KiteAccessTokenProtected",
                table: "Users",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "KiteRefreshTokenProtected",
                table: "Users",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "KiteUserId",
                table: "Users",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                """
                UPDATE `Users` `u`
                INNER JOIN `BrokerAccounts` `b` ON `b`.`UserId` = `u`.`Id` AND `b`.`BrokerName` = 'Zerodha'
                SET
                    `u`.`KiteAccessTokenProtected` = `b`.`AccessTokenProtected`,
                    `u`.`KiteRefreshTokenProtected` = `b`.`RefreshTokenProtected`,
                    `u`.`KiteUserId` = `b`.`ExternalUserId`,
                    `u`.`BrokerProvider` = 'Zerodha',
                    `u`.`BrokerConnectedAt` = `b`.`ConnectedAt`;
                """);

            migrationBuilder.DropTable(
                name: "BrokerAccounts");

            migrationBuilder.DropTable(
                name: "HistoricalCandles");
        }
    }
}
