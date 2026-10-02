using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261002150000_AddPtAccountVerification")]
public partial class AddPtAccountVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Specialty", table: "pt_profiles", type: "character varying(200)", maxLength: 200, nullable: true, oldClrType: typeof(string), oldType: "character varying(120)", oldMaxLength: 120, oldNullable: true);
        migrationBuilder.AddColumn<int>(name: "VerificationStatus", table: "pt_profiles", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "VerificationReason", table: "pt_profiles", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "VerificationReviewedAt", table: "pt_profiles", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "VerificationReviewedByAdminId", table: "pt_profiles", type: "uuid", nullable: true);

        migrationBuilder.Sql("""
            UPDATE pt_profiles p
            SET "VerificationStatus" = CASE
                WHEN EXISTS (SELECT 1 FROM certificates c WHERE c."PtUserId" = p."UserId" AND c."Status" = 1) THEN 3
                WHEN EXISTS (SELECT 1 FROM certificates c WHERE c."PtUserId" = p."UserId" AND c."Status" = 0) THEN 1
                ELSE 0
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Specialty", table: "pt_profiles", type: "character varying(120)", maxLength: 120, nullable: true, oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200, oldNullable: true);
        migrationBuilder.DropColumn(name: "VerificationStatus", table: "pt_profiles");
        migrationBuilder.DropColumn(name: "VerificationReason", table: "pt_profiles");
        migrationBuilder.DropColumn(name: "VerificationReviewedAt", table: "pt_profiles");
        migrationBuilder.DropColumn(name: "VerificationReviewedByAdminId", table: "pt_profiles");
    }
}
