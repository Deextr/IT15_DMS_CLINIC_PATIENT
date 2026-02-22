using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DMS_CPMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionLevelArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArchiveDocument_DocumentID",
                table: "ArchiveDocument");

            migrationBuilder.AlterColumn<string>(
                name: "ArchiveReason",
                table: "ArchiveDocument",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "VersionID",
                table: "ArchiveDocument",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocument_DocumentID",
                table: "ArchiveDocument",
                column: "DocumentID");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocument_VersionID",
                table: "ArchiveDocument",
                column: "VersionID");

            migrationBuilder.AddForeignKey(
                name: "FK_ArchiveDocument_DocumentVersion_VersionID",
                table: "ArchiveDocument",
                column: "VersionID",
                principalTable: "DocumentVersion",
                principalColumn: "VersionID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArchiveDocument_DocumentVersion_VersionID",
                table: "ArchiveDocument");

            migrationBuilder.DropIndex(
                name: "IX_ArchiveDocument_DocumentID",
                table: "ArchiveDocument");

            migrationBuilder.DropIndex(
                name: "IX_ArchiveDocument_VersionID",
                table: "ArchiveDocument");

            migrationBuilder.DropColumn(
                name: "VersionID",
                table: "ArchiveDocument");

            migrationBuilder.AlterColumn<string>(
                name: "ArchiveReason",
                table: "ArchiveDocument",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocument_DocumentID",
                table: "ArchiveDocument",
                column: "DocumentID",
                unique: true);
        }
    }
}
