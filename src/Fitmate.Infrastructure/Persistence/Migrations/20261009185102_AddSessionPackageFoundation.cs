using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddSessionPackageFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "session_packages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                PtUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                SessionCount = table.Column<int>(type: "integer", nullable: false),
                TotalPriceVnd = table.Column<long>(type: "bigint", nullable: false),
                HoldExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                InitialScheduleDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                MakeUpDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_packages", x => x.Id);
                table.CheckConstraint("chk_session_package_count", "\"SessionCount\" IN (4, 8)");
                table.CheckConstraint("chk_session_package_price", "\"TotalPriceVnd\" > 0");
                table.ForeignKey(
                    name: "FK_session_packages_payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "session_package_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                Sequence = table.Column<int>(type: "integer", nullable: false),
                InitialSlotStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PaidAllocationVnd = table.Column<long>(type: "bigint", nullable: false),
                PlatformFeeAllocationVnd = table.Column<long>(type: "bigint", nullable: false),
                TrainerShareAllocationVnd = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_package_sessions", x => x.Id);
                table.CheckConstraint("chk_package_session_money", "\"PaidAllocationVnd\" > 0 AND \"PlatformFeeAllocationVnd\" >= 0 AND \"TrainerShareAllocationVnd\" >= 0 AND \"PaidAllocationVnd\" = \"PlatformFeeAllocationVnd\" + \"TrainerShareAllocationVnd\"");
                table.CheckConstraint("chk_package_session_sequence", "\"Sequence\" BETWEEN 1 AND 8");
                table.ForeignKey(
                    name: "FK_session_package_sessions_appointments_AppointmentId",
                    column: x => x.AppointmentId,
                    principalTable: "appointments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_session_package_sessions_session_packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "session_packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_session_package_sessions_AppointmentId", "session_package_sessions", "AppointmentId", unique: true);
        migrationBuilder.CreateIndex("IX_session_package_sessions_PackageId_Sequence", "session_package_sessions", new[] { "PackageId", "Sequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_session_packages_MemberId_CreatedAt", "session_packages", new[] { "MemberId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_session_packages_PaymentId", "session_packages", "PaymentId", unique: true, filter: "\"PaymentId\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_session_packages_PtUserId_Status", "session_packages", new[] { "PtUserId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "session_package_sessions");
        migrationBuilder.DropTable(name: "session_packages");
    }
}
