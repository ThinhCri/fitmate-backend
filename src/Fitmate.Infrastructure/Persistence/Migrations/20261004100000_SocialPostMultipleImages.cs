using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class SocialPostMultipleImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "ImageStorageKeys",
            table: "posts",
            type: "text[]",
            nullable: false,
            defaultValue: Array.Empty<string>());

        migrationBuilder.Sql("""
            UPDATE posts
            SET "ImageStorageKeys" = ARRAY["ImageStorageKey"]
            WHERE "ImageStorageKey" IS NOT NULL AND cardinality("ImageStorageKeys") = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ImageStorageKeys", table: "posts");
    }
}
