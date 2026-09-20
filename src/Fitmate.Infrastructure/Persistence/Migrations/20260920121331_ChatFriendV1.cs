using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatFriendV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ThreadId",
                table: "chat_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientMessageId",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_thread_reads",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_thread_reads", x => new { x.conversation_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "friend_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserLowId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserHighId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friend_conversations", x => x.id);
                    table.CheckConstraint("ck_friend_conversations_canonical", "\"UserLowId\" < \"UserHighId\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ConversationId_ClientMessageId",
                table: "chat_messages",
                columns: new[] { "ConversationId", "ClientMessageId" },
                unique: true,
                filter: "\"ConversationId\" IS NOT NULL AND \"ClientMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ConversationId_CreatedAt_id",
                table: "chat_messages",
                columns: new[] { "ConversationId", "CreatedAt", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_thread_reads_conversation_id_last_read_message_id",
                table: "chat_thread_reads",
                columns: new[] { "conversation_id", "last_read_message_id" });

            migrationBuilder.CreateIndex(
                name: "IX_friend_conversations_UserHighId",
                table: "friend_conversations",
                column: "UserHighId");

            migrationBuilder.CreateIndex(
                name: "IX_friend_conversations_UserLowId_UserHighId",
                table: "friend_conversations",
                columns: new[] { "UserLowId", "UserHighId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_thread_reads");

            migrationBuilder.DropTable(
                name: "friend_conversations");

            migrationBuilder.DropIndex(
                name: "IX_chat_messages_ConversationId_ClientMessageId",
                table: "chat_messages");

            migrationBuilder.DropIndex(
                name: "IX_chat_messages_ConversationId_CreatedAt_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "chat_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "ThreadId",
                table: "chat_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
