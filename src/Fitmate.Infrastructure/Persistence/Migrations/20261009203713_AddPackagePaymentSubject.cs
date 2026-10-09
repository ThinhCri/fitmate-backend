using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddPackagePaymentSubject : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "AppointmentId", table: "payments", type: "uuid", nullable: true,
            oldClrType: typeof(Guid), oldType: "uuid");
        migrationBuilder.AddColumn<Guid>(
            name: "PackageId", table: "payments", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_payments_PackageId", table: "payments", column: "PackageId",
            unique: true, filter: "\"PackageId\" IS NOT NULL");
        migrationBuilder.AddCheckConstraint(
            name: "chk_payment_one_subject", table: "payments",
            sql: "(\"AppointmentId\" IS NULL) <> (\"PackageId\" IS NULL)");
        migrationBuilder.AddForeignKey(
            name: "FK_payments_session_packages_PackageId", table: "payments",
            column: "PackageId", principalTable: "session_packages",
            principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never erase the ownership of a collected package payment on rollback.
        migrationBuilder.Sql(
            "DO $$ BEGIN IF EXISTS (SELECT 1 FROM payments WHERE \"PackageId\" IS NOT NULL) THEN " +
            "RAISE EXCEPTION 'Package payments require reconciliation before rollback'; " +
            "END IF; END $$;");
        migrationBuilder.DropForeignKey(
            name: "FK_payments_session_packages_PackageId", table: "payments");
        migrationBuilder.DropIndex(name: "IX_payments_PackageId", table: "payments");
        migrationBuilder.DropCheckConstraint(name: "chk_payment_one_subject", table: "payments");
        migrationBuilder.DropColumn(name: "PackageId", table: "payments");
        migrationBuilder.AlterColumn<Guid>(
            name: "AppointmentId", table: "payments", type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
    }
}
