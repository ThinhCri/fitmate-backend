using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261003100000_AddAvatarStorageKey")]
public sealed class AddAvatarStorageKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AvatarStorageKey",
            table: "asp_net_users",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AvatarStorageKey", table: "asp_net_users");
    }
}
