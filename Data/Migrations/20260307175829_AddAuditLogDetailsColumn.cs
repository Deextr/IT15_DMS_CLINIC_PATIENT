using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DMS_CPMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogDetailsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "AuditLog",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Details",
                table: "AuditLog");
        }
    }
}
