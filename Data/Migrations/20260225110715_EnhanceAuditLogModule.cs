using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DMS_CPMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceAuditLogModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the AuditLog table if it doesn't already exist
            // (handles case where prior migration was recorded but table was not created)
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[AuditLog]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AuditLog] (
                        [AuditLogID] int NOT NULL IDENTITY,
                        [Action] nvarchar(100) NOT NULL,
                        [EntityType] nvarchar(50) NOT NULL,
                        [EntityId] int NOT NULL,
                        [Details] nvarchar(500) NULL,
                        [UserId] nvarchar(100) NULL,
                        [UserName] nvarchar(100) NULL,
                        [Role] nvarchar(50) NOT NULL DEFAULT N'',
                        [DocumentName] nvarchar(150) NOT NULL DEFAULT N'',
                        [Status] nvarchar(20) NOT NULL DEFAULT N'Success',
                        [Timestamp] datetime2 NOT NULL,
                        [IpAddress] nvarchar(50) NULL,
                        CONSTRAINT [PK_AuditLog] PRIMARY KEY ([AuditLogID])
                    );
                END
                ELSE
                BEGIN
                    -- Table exists: alter Action column and add new columns if missing
                    ALTER TABLE [AuditLog] ALTER COLUMN [Action] nvarchar(100) NOT NULL;

                    IF COL_LENGTH(N'AuditLog', N'Role') IS NULL
                        ALTER TABLE [AuditLog] ADD [Role] nvarchar(50) NOT NULL DEFAULT N'';

                    IF COL_LENGTH(N'AuditLog', N'DocumentName') IS NULL
                        ALTER TABLE [AuditLog] ADD [DocumentName] nvarchar(150) NOT NULL DEFAULT N'';

                    IF COL_LENGTH(N'AuditLog', N'Status') IS NULL
                        ALTER TABLE [AuditLog] ADD [Status] nvarchar(20) NOT NULL DEFAULT N'Success';
                END
            ");

            // Create indexes (idempotent – SQL Server will error if they already exist,
            // but since this is a fresh migration that's fine)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_Role' AND object_id = OBJECT_ID(N'[AuditLog]'))
                    CREATE INDEX [IX_AuditLog_Role] ON [AuditLog] ([Role]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_Status' AND object_id = OBJECT_ID(N'[AuditLog]'))
                    CREATE INDEX [IX_AuditLog_Status] ON [AuditLog] ([Status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_Timestamp' AND object_id = OBJECT_ID(N'[AuditLog]'))
                    CREATE INDEX [IX_AuditLog_Timestamp] ON [AuditLog] ([Timestamp]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_UserId' AND object_id = OBJECT_ID(N'[AuditLog]'))
                    CREATE INDEX [IX_AuditLog_UserId] ON [AuditLog] ([UserId]);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLog_Role",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_Status",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_Timestamp",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "DocumentName",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AuditLog");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLog",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
