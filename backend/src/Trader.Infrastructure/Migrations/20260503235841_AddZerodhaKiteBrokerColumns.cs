using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddZerodhaKiteBrokerColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
