using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationsPushV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId_Read_CreatedAt",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "notifications",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "notifications",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Read",
                table: "notifications",
                newName: "read");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "notifications",
                newName: "message");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "notifications",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "notifications",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "PayloadJson",
                table: "notifications",
                newName: "payload_json");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "notifications",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "notifications",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "SenderId",
                table: "friend_requests",
                newName: "sender_id");

            migrationBuilder.RenameColumn(
                name: "RecipientId",
                table: "friend_requests",
                newName: "recipient_id");

            migrationBuilder.RenameIndex(
                name: "IX_friend_requests_SenderId_RecipientId",
                table: "friend_requests",
                newName: "IX_friend_requests_sender_id_recipient_id");

            migrationBuilder.RenameIndex(
                name: "IX_friend_requests_RecipientId_Status",
                table: "friend_requests",
                newName: "IX_friend_requests_recipient_id_Status");

            migrationBuilder.RenameColumn(
                name: "Platform",
                table: "devices",
                newName: "platform");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "devices",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "devices",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "devices",
                newName: "token_hash");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "devices",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_devices_UserId_TokenHash",
                table: "devices",
                newName: "ux_devices_user_token");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "notifications",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<Guid>(
                name: "source_event_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "notifications",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "encrypted_token",
                table: "devices",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "key_id",
                table: "devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_used_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "provider",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "revoked_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "push_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sweep_claim_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_provider_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    config_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_provider_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_created",
                table: "notifications",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_read_created",
                table: "notifications",
                columns: new[] { "user_id", "read", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_notifications_dedup",
                table: "notifications",
                columns: new[] { "user_id", "source_event_id", "source_type" },
                unique: true,
                filter: "source_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_devices_active",
                table: "devices",
                column: "user_id",
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_push_deliveries_device",
                table: "push_deliveries",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_deliveries_notification",
                table: "push_deliveries",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_deliveries_sweep",
                table: "push_deliveries",
                columns: new[] { "status", "expires_at" },
                filter: "status IN (0, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_deliveries");

            migrationBuilder.DropTable(
                name: "push_provider_configs");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_created",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_read_created",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ux_notifications_dedup",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_devices_active",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "source_event_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "encrypted_token",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "key_id",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "last_used_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                table: "devices");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "notifications",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "notifications",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "read",
                table: "notifications",
                newName: "Read");

            migrationBuilder.RenameColumn(
                name: "message",
                table: "notifications",
                newName: "Message");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "notifications",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "notifications",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "payload_json",
                table: "notifications",
                newName: "PayloadJson");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "notifications",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "notifications",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "sender_id",
                table: "friend_requests",
                newName: "SenderId");

            migrationBuilder.RenameColumn(
                name: "recipient_id",
                table: "friend_requests",
                newName: "RecipientId");

            migrationBuilder.RenameIndex(
                name: "IX_friend_requests_sender_id_recipient_id",
                table: "friend_requests",
                newName: "IX_friend_requests_SenderId_RecipientId");

            migrationBuilder.RenameIndex(
                name: "IX_friend_requests_recipient_id_Status",
                table: "friend_requests",
                newName: "IX_friend_requests_RecipientId_Status");

            migrationBuilder.RenameColumn(
                name: "platform",
                table: "devices",
                newName: "Platform");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "devices",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "devices",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "token_hash",
                table: "devices",
                newName: "TokenHash");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "devices",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ux_devices_user_token",
                table: "devices",
                newName: "IX_devices_UserId_TokenHash");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_Read_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "Read", "CreatedAt" });
        }
    }
}
