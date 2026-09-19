using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModerationAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "reports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolveAction",
                table: "reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_moderation_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActorAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_moderation_actions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reports_ReporterId_IdempotencyKey",
                table: "reports",
                columns: new[] { "ReporterId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reports_ReporterId_TargetType_TargetId",
                table: "reports",
                columns: new[] { "ReporterId", "TargetType", "TargetId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_user_moderation_actions_ReportId",
                table: "user_moderation_actions",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_user_moderation_actions_UserId_CreatedAt",
                table: "user_moderation_actions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_moderation_actions");

            migrationBuilder.DropIndex(
                name: "IX_reports_ReporterId_IdempotencyKey",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_reports_ReporterId_TargetType_TargetId",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "ResolveAction",
                table: "reports");
        }
    }
}
