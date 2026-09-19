using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FriendshipAndBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "friends");

            migrationBuilder.DropIndex(
                name: "IX_friend_requests_FromUserId_ToUserId",
                table: "friend_requests");

            migrationBuilder.RenameColumn(
                name: "ToUserId",
                table: "friend_requests",
                newName: "SenderId");

            migrationBuilder.RenameColumn(
                name: "FromUserId",
                table: "friend_requests",
                newName: "RecipientId");

            migrationBuilder.CreateTable(
                name: "blocked_users",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_users", x => new { x.blocker_id, x.blocked_id });
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    user_low_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_high_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => new { x.user_low_id, x.user_high_id });
                    table.CheckConstraint("ck_friendships_canonical_order", "\"user_low_id\" < \"user_high_id\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_RecipientId_Status",
                table: "friend_requests",
                columns: new[] { "RecipientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_SenderId_RecipientId",
                table: "friend_requests",
                columns: new[] { "SenderId", "RecipientId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_blocked_users_blocked_id",
                table: "blocked_users",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_user_high_id",
                table: "friendships",
                column: "user_high_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocked_users");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropIndex(
                name: "IX_friend_requests_RecipientId_Status",
                table: "friend_requests");

            migrationBuilder.DropIndex(
                name: "IX_friend_requests_SenderId_RecipientId",
                table: "friend_requests");

            migrationBuilder.RenameColumn(
                name: "SenderId",
                table: "friend_requests",
                newName: "ToUserId");

            migrationBuilder.RenameColumn(
                name: "RecipientId",
                table: "friend_requests",
                newName: "FromUserId");

            migrationBuilder.CreateTable(
                name: "friends",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FriendUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friends", x => new { x.UserId, x.FriendUserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_FromUserId_ToUserId",
                table: "friend_requests",
                columns: new[] { "FromUserId", "ToUserId" },
                unique: true);
        }
    }
}
