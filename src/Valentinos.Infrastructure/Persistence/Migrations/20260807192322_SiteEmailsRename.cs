using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valentinos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteEmailsRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CcEmails",
                table: "Sites",
                newName: "Emails");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Emails",
                table: "Sites",
                newName: "CcEmails");
        }
    }
}
