using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261004123000_SocialEngagementActions")]
public sealed class SocialEngagementActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "comment_likes",
            columns: table => new
            {
                CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_comment_likes", x => new { x.CommentId, x.UserId }));

        migrationBuilder.CreateTable(
            name: "post_bookmarks",
            columns: table => new
            {
                PostId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_post_bookmarks", x => new { x.PostId, x.UserId }));

        migrationBuilder.CreateIndex(
            name: "IX_comment_likes_CommentId_CreatedAt",
            table: "comment_likes",
            columns: new[] { "CommentId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_post_bookmarks_UserId_CreatedAt",
            table: "post_bookmarks",
            columns: new[] { "UserId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "comment_likes");
        migrationBuilder.DropTable(name: "post_bookmarks");
    }
}
