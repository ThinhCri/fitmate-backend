using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

/// <summary>Only verified, non-deleted phone numbers are unique.</summary>
public partial class UniqueVerifiedPhoneNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_asp_net_users_PhoneNumber",
            table: "asp_net_users",
            column: "PhoneNumber",
            unique: true,
            filter: "\"PhoneNumberConfirmed\" = TRUE AND \"PhoneNumber\" IS NOT NULL AND \"DeletedAt\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_asp_net_users_PhoneNumber",
            table: "asp_net_users");
    }
}
