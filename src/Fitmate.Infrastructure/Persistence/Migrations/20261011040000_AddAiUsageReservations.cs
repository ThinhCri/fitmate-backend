using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261011040000_AddAiUsageReservations")]
public partial class AddAiUsageReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ai_usage_reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ai_usage_reservations", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_ai_usage_reservations_UserId_LocalDate_Kind_State",
            table: "ai_usage_reservations",
            columns: new[] { "UserId", "LocalDate", "Kind", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM ai_usage_reservations WHERE "State" = 'committed') THEN
                    RAISE EXCEPTION 'Cannot discard committed AI usage history';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable("ai_usage_reservations");
    }
}
