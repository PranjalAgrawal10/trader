using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NormalizedEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OtpHash = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    IsConsumed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailedVerifyAttempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpChallenges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_NormalizedEmail_IsConsumed",
                table: "EmailOtpChallenges",
                columns: new[] { "NormalizedEmail", "IsConsumed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOtpChallenges");
        }
    }
}
