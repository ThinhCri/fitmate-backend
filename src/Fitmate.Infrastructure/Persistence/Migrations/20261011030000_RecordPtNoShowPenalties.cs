using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261011030000_RecordPtNoShowPenalties")]
public partial class RecordPtNoShowPenalties : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("VerifiedNoShowStrikes", "pt_profiles", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("PenaltyFlags", "pt_profiles", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("SuspendedUntilUtc", "pt_profiles", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>("SecondFlagAtUtc", "pt_profiles", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<bool>("PermanentlyBanned", "pt_profiles", type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM pt_profiles WHERE "VerifiedNoShowStrikes" > 0 OR "PenaltyFlags" > 0 OR "PermanentlyBanned") THEN
                    RAISE EXCEPTION 'Cannot discard verified PT no-show penalties';
                END IF;
            END $$;
            """);
        migrationBuilder.DropColumn("PermanentlyBanned", "pt_profiles");
        migrationBuilder.DropColumn("SecondFlagAtUtc", "pt_profiles");
        migrationBuilder.DropColumn("SuspendedUntilUtc", "pt_profiles");
        migrationBuilder.DropColumn("PenaltyFlags", "pt_profiles");
        migrationBuilder.DropColumn("VerifiedNoShowStrikes", "pt_profiles");
    }
}
