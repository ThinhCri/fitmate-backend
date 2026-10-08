using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddPtServiceDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("deliverable", "services", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<int>("delivery_mode", "services", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("location_address", "services", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>("location_name", "services", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("required_equipment", "services", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<int>("service_kind", "services", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("target_audience", "services", type: "character varying(300)", maxLength: 300, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("deliverable", "services");
        migrationBuilder.DropColumn("delivery_mode", "services");
        migrationBuilder.DropColumn("location_address", "services");
        migrationBuilder.DropColumn("location_name", "services");
        migrationBuilder.DropColumn("required_equipment", "services");
        migrationBuilder.DropColumn("service_kind", "services");
        migrationBuilder.DropColumn("target_audience", "services");
    }
}
