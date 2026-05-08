using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendMlPredictionResolutionLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CensorReason",
                table: "MlPriceDirectionPredictions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelN3",
                table: "MlPriceDirectionPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelN5",
                table: "MlPriceDirectionPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelNextBar",
                table: "MlPriceDirectionPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelThresholdFractionApplied",
                table: "MlPriceDirectionPredictions",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextBarTimeUtcN3",
                table: "MlPriceDirectionPredictions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextBarTimeUtcN5",
                table: "MlPriceDirectionPredictions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextCloseN3",
                table: "MlPriceDirectionPredictions",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextCloseN5",
                table: "MlPriceDirectionPredictions",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CensorReason",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelN3",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelN5",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<sbyte>(
                name: "LabelNextBar",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelThresholdFractionApplied",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextBarTimeUtcN3",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextBarTimeUtcN5",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextCloseN3",
                table: "MlLightGbmTripleBarrierPredictions",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextCloseN5",
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
                name: "CensorReason",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "LabelN3",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "LabelN5",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "LabelNextBar",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "LabelThresholdFractionApplied",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "NextBarTimeUtcN3",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "NextBarTimeUtcN5",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "NextCloseN3",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "NextCloseN5",
                table: "MlPriceDirectionPredictions");

            migrationBuilder.DropColumn(
                name: "CensorReason",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "LabelN3",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "LabelN5",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "LabelNextBar",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "LabelThresholdFractionApplied",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "NextBarTimeUtcN3",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "NextBarTimeUtcN5",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "NextCloseN3",
                table: "MlLightGbmTripleBarrierPredictions");

            migrationBuilder.DropColumn(
                name: "NextCloseN5",
                table: "MlLightGbmTripleBarrierPredictions");
        }
    }
}
