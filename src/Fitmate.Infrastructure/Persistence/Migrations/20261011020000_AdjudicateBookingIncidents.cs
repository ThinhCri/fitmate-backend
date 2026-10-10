using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261011020000_AdjudicateBookingIncidents")]
public partial class AdjudicateBookingIncidents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("booking_resolution", "support_tickets",
            type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<Guid>("booking_resolved_by_admin_id", "support_tickets",
            type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>("booking_resolved_at_utc", "support_tickets",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("booking_resolution_reason", "support_tickets",
            type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("booking_evidence_reference", "support_tickets",
            type: "character varying(200)", maxLength: 200, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM support_tickets WHERE booking_resolution IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot discard adjudicated booking incidents';
                END IF;
            END $$;
            """);
        migrationBuilder.DropColumn("booking_evidence_reference", "support_tickets");
        migrationBuilder.DropColumn("booking_resolution_reason", "support_tickets");
        migrationBuilder.DropColumn("booking_resolved_at_utc", "support_tickets");
        migrationBuilder.DropColumn("booking_resolved_by_admin_id", "support_tickets");
        migrationBuilder.DropColumn("booking_resolution", "support_tickets");
    }
}
