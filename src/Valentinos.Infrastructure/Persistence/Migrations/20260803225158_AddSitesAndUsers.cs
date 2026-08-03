using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSitesAndUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "UnavailableReports",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "StatusCheckins",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CcEmails = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnavailableReports_SiteId_CreatedAt",
                table: "UnavailableReports",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StatusCheckins_SiteId_CreatedAt",
                table: "StatusCheckins",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SiteId",
                table: "Assets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_TenantId_Code",
                table: "Sites",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UnavailableReports_SiteId_CreatedAt",
                table: "UnavailableReports");

            migrationBuilder.DropIndex(
                name: "IX_StatusCheckins_SiteId_CreatedAt",
                table: "StatusCheckins");

            migrationBuilder.DropIndex(
                name: "IX_Assets_SiteId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "UnavailableReports");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "StatusCheckins");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Assets");
        }
    }
}
