using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScalperGttLossProfitEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ScalperGttLossEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScalperGttProfitEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(
                """
                UPDATE Users
                SET ScalperGttLossEnabled = ScalperGttEnabled,
                    ScalperGttProfitEnabled = ScalperGttEnabled;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScalperGttLossEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ScalperGttProfitEnabled",
                table: "Users");
        }
    }
}
