using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class LinkSessionPackageHolds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No public package checkout existed before this migration. Fail rather than
        // attach an arbitrary hold to any unexpected pre-existing package session.
        migrationBuilder.Sql(
            "DO $$ BEGIN IF EXISTS (SELECT 1 FROM session_package_sessions) THEN " +
            "RAISE EXCEPTION 'Package sessions require an explicit hold backfill'; " +
            "END IF; END $$;");
        migrationBuilder.AddColumn<Guid>(
            name: "HoldId",
            table: "session_package_sessions",
            type: "uuid",
            nullable: false);
        migrationBuilder.CreateIndex(
            name: "IX_session_package_sessions_HoldId",
            table: "session_package_sessions",
            column: "HoldId",
            unique: true);
        migrationBuilder.AddForeignKey(
            name: "FK_session_package_sessions_booking_holds_HoldId",
            table: "session_package_sessions",
            column: "HoldId",
            principalTable: "booking_holds",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_session_package_sessions_booking_holds_HoldId",
            table: "session_package_sessions");
        migrationBuilder.DropIndex(
            name: "IX_session_package_sessions_HoldId",
            table: "session_package_sessions");
        migrationBuilder.DropColumn(name: "HoldId", table: "session_package_sessions");
    }
}
