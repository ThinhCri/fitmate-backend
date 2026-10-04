using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261004120000_SocialCommentThreads")]
public sealed class SocialCommentThreads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ParentCommentId",
            table: "comments",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_comments_ParentCommentId",
            table: "comments",
            column: "ParentCommentId");

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_comments_ParentCommentId",
            table: "comments");
        migrationBuilder.DropColumn(
            name: "ParentCommentId",
            table: "comments");
    }
}
