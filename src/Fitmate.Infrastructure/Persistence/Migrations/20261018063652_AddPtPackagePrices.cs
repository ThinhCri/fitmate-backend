using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

/// <summary>New PT package prices default to unavailable; existing services keep their single-session price.</summary>
public partial class AddPtPackagePrices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Package4PriceVnd", table: "services", type: "bigint",
            nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>(
            name: "Package8PriceVnd", table: "services", type: "bigint",
            nullable: false, defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Package4PriceVnd", table: "services");
        migrationBuilder.DropColumn(name: "Package8PriceVnd", table: "services");
    }
}
