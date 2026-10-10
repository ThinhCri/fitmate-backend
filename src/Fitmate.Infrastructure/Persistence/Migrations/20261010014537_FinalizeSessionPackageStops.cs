using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class FinalizeSessionPackageStops : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "FinalizedAtUtc",
            table: "session_package_stop_requests",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM session_package_stop_requests WHERE "FinalizedAtUtc" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove package stop finalization history without manual reconciliation';
                END IF;
            END $$;
            """);
        migrationBuilder.DropColumn(
            name: "FinalizedAtUtc",
            table: "session_package_stop_requests");
    }
}
