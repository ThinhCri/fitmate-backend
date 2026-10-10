using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddSessionPackageStopSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "session_package_stop_requests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PtResponseDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RefundQuoteVnd = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_package_stop_requests", x => x.Id);
                table.CheckConstraint("chk_package_stop_quote", "\"RefundQuoteVnd\" >= 0");
                table.CheckConstraint("chk_package_stop_reason", "length(\"Reason\") BETWEEN 10 AND 1000");
                table.ForeignKey(
                    name: "FK_session_package_stop_requests_session_packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "session_packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "session_package_stop_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StopRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                StatusAtRequest = table.Column<int>(type: "integer", nullable: false),
                SlotStartAtRequestUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PaidAllocationVnd = table.Column<long>(type: "bigint", nullable: false),
                EligibleForRefund = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_package_stop_sessions", x => x.Id);
                table.CheckConstraint("chk_package_stop_session_money", "\"PaidAllocationVnd\" > 0");
                table.ForeignKey(
                    name: "FK_session_package_stop_sessions_appointments_AppointmentId",
                    column: x => x.AppointmentId,
                    principalTable: "appointments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_session_package_stop_sessions_session_package_stop_requests~",
                    column: x => x.StopRequestId,
                    principalTable: "session_package_stop_requests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_session_package_stop_requests_PackageId",
            "session_package_stop_requests", "PackageId", unique: true);
        migrationBuilder.CreateIndex("IX_session_package_stop_sessions_AppointmentId",
            "session_package_stop_sessions", "AppointmentId");
        migrationBuilder.CreateIndex("IX_session_package_stop_sessions_StopRequestId_AppointmentId",
            "session_package_stop_sessions", new[] { "StopRequestId", "AppointmentId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve financial decisions; operator must reconcile open requests before rollback.
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM session_package_stop_requests) THEN " +
                             "RAISE EXCEPTION 'Package stop requests require reconciliation before rollback'; " +
                             "END IF; END $$;");
        migrationBuilder.DropTable("session_package_stop_sessions");
        migrationBuilder.DropTable("session_package_stop_requests");
    }
}
