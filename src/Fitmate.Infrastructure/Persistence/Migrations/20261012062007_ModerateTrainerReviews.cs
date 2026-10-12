using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261012062007_ModerateTrainerReviews")]
public sealed class ModerateTrainerReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsHidden", "reviews", type: "boolean",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>("ModeratedAtUtc", "reviews",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>("ModeratedByAdminId", "reviews",
            type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>("ModerationReason", "reviews",
            type: "character varying(500)", maxLength: 500, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ModerationReason", "reviews");
        migrationBuilder.DropColumn("ModeratedByAdminId", "reviews");
        migrationBuilder.DropColumn("ModeratedAtUtc", "reviews");
        migrationBuilder.DropColumn("IsHidden", "reviews");
    }
}
