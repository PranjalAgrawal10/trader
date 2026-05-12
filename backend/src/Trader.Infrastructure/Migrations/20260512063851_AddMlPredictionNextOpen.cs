using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMlPredictionNextOpen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NextOpen",
                table: "MlPriceDirectionPredictions",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextOpen",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextOpen",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "NextOpen",
                table: "MlLightGbmTripleBarrierPredictions");
        }
    }
}
