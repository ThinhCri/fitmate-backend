using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AllowRepeatedPackageStopRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_session_package_stop_requests_PackageId",
            "session_package_stop_requests");
        migrationBuilder.AddColumn<int>("ApprovalSource", "session_package_stop_requests",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("ApprovedAtUtc", "session_package_stop_requests",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<int>("Status", "session_package_stop_requests",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("WithdrawnAtUtc", "session_package_stop_requests",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex("IX_session_package_stop_requests_PackageId",
            "session_package_stop_requests", "PackageId", unique: true,
            filter: "\"Status\" IN (0, 1)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old one-request schema cannot preserve withdrawal history or decisions.
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (" +
            "SELECT 1 FROM session_package_stop_requests " +
            "WHERE \"Status\" <> 0 OR \"ApprovalSource\" <> 0 OR \"ApprovedAtUtc\" IS NOT NULL " +
            "OR \"WithdrawnAtUtc\" IS NOT NULL) OR EXISTS (" +
            "SELECT 1 FROM session_package_stop_requests GROUP BY \"PackageId\" HAVING count(*) > 1) " +
            "THEN RAISE EXCEPTION 'Package stop decisions require reconciliation before rollback'; " +
            "END IF; END $$;");
        migrationBuilder.DropIndex("IX_session_package_stop_requests_PackageId",
            "session_package_stop_requests");
        migrationBuilder.DropColumn("ApprovalSource", "session_package_stop_requests");
        migrationBuilder.DropColumn("ApprovedAtUtc", "session_package_stop_requests");
        migrationBuilder.DropColumn("Status", "session_package_stop_requests");
        migrationBuilder.DropColumn("WithdrawnAtUtc", "session_package_stop_requests");
        migrationBuilder.CreateIndex("IX_session_package_stop_requests_PackageId",
            "session_package_stop_requests", "PackageId", unique: true);
    }
}
