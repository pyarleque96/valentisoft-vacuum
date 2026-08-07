using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeSiteScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_Nombre",
                table: "Employees");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_SiteId_Nombre",
                table: "Employees",
                columns: new[] { "TenantId", "SiteId", "Nombre" });

            // Backfill: los empleados existentes son todos del site 069. Va en la
            // migración y no en el seeder para que se aplique aunque DemoSeeder no corra.
            migrationBuilder.Sql(@"
                UPDATE e
                SET e.SiteId = s.Id
                FROM Employees e
                INNER JOIN Sites s ON s.TenantId = e.TenantId AND s.Code = '069'
                WHERE e.SiteId = '00000000-0000-0000-0000-000000000000';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_SiteId_Nombre",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Employees");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Nombre",
                table: "Employees",
                columns: new[] { "TenantId", "Nombre" });
        }
    }
}
