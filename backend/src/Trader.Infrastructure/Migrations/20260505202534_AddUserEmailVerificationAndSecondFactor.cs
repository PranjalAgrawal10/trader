using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmailVerificationAndSecondFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerificationExpiresAtUtc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationTokenHash",
                table: "Users",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAtUtc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordResetExpiresAtUtc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "Users",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<byte>(
                name: "SecondFactorMethod",
                table: "Users",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "Users",
                column: "EmailVerificationTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PasswordResetTokenHash",
                table: "Users",
                column: "PasswordResetTokenHash",
                unique: true);

            migrationBuilder.Sql(
"""
UPDATE `Users` SET `EmailVerifiedAtUtc` = `CreatedAt` WHERE `EmailVerifiedAtUtc` IS NULL;
UPDATE `Users` SET `SecondFactorMethod` = CASE WHEN `TwoFactorEnabled` = 1 THEN 1 ELSE 0 END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PasswordResetTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondFactorMethod",
                table: "Users");
        }
    }
}
