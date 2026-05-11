using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteMlAutomationUserIntervalPoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FavoriteMlAutomationInterval",
                table: "Users",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FavoriteMlAutomationLastNewPassUtc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FavoriteMlAutomationPollIntervalSeconds",
                table: "Users",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FavoriteMlAutomationInterval",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FavoriteMlAutomationLastNewPassUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FavoriteMlAutomationPollIntervalSeconds",
                table: "Users");
        }
    }
}
