using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpChallengePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOtpChallenges_NormalizedEmail_IsConsumed",
                table: "EmailOtpChallenges");

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "EmailOtpChallenges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_NormalizedEmail_Purpose_IsConsumed",
                table: "EmailOtpChallenges",
                columns: new[] { "NormalizedEmail", "Purpose", "IsConsumed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOtpChallenges_NormalizedEmail_Purpose_IsConsumed",
                table: "EmailOtpChallenges");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "EmailOtpChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_NormalizedEmail_IsConsumed",
                table: "EmailOtpChallenges",
                columns: new[] { "NormalizedEmail", "IsConsumed" });
        }
    }
}
