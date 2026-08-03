using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnavailableReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnavailableReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnavailableReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnavailableReports_TenantId_CreatedAt",
                table: "UnavailableReports",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnavailableReports");
        }
    }
}
