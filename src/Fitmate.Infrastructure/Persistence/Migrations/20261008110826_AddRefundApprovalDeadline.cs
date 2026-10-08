using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddRefundApprovalDeadline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "approved_at_utc", table: "refunds", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "approved_by_admin_id", table: "refunds", type: "uuid", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "approved_at_utc", table: "refunds");
        migrationBuilder.DropColumn(name: "approved_by_admin_id", table: "refunds");
    }
}
