using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261003010000_PracticalBookingLifecycle")]
public sealed class PracticalBookingLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE appointments
                ADD COLUMN IF NOT EXISTS completion_submitted_at_utc timestamptz NULL,
                ADD COLUMN IF NOT EXISTS member_confirmation_deadline_utc timestamptz NULL,
                ADD COLUMN IF NOT EXISTS disputed_at_utc timestamptz NULL;

            CREATE INDEX IF NOT EXISTS "IX_appointments_member_confirmation_deadline_utc"
                ON appointments (member_confirmation_deadline_utc)
                WHERE appointment_status = 7;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_appointments_member_confirmation_deadline_utc";
            ALTER TABLE appointments
                DROP COLUMN IF EXISTS disputed_at_utc,
                DROP COLUMN IF EXISTS member_confirmation_deadline_utc,
                DROP COLUMN IF EXISTS completion_submitted_at_utc;
            """);
    }
}
