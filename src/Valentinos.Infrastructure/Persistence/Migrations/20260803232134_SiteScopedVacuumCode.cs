using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteScopedVacuumCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_SiteId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TenantId_Codigo",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SiteId_Codigo",
                table: "Assets",
                columns: new[] { "SiteId", "Codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_SiteId_Codigo",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SiteId",
                table: "Assets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_Codigo",
                table: "Assets",
                columns: new[] { "TenantId", "Codigo" },
                unique: true);
        }
    }
}
