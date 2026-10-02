using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class CompleteMobileAccountProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AvatarUrl", table: "asp_net_users", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Bio", table: "user_profiles", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HealthNotes", table: "user_profiles", type: "character varying(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string[]>(name: "PreferredTimeSlots", table: "user_profiles", type: "text[]", nullable: false, defaultValue: Array.Empty<string>());
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AvatarUrl", table: "asp_net_users");
        migrationBuilder.DropColumn(name: "Bio", table: "user_profiles");
        migrationBuilder.DropColumn(name: "HealthNotes", table: "user_profiles");
        migrationBuilder.DropColumn(name: "PreferredTimeSlots", table: "user_profiles");
    }
}
