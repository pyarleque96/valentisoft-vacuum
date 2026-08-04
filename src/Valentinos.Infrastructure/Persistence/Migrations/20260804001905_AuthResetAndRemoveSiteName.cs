using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthResetAndRemoveSiteName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Sites");

            migrationBuilder.AddColumn<int>(
                name: "ResetCodeAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResetCodeExpiresUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetCodeHash",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResetCodeAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ResetCodeExpiresUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ResetCodeHash",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Sites",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
