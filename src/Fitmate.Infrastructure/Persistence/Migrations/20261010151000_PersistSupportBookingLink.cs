using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261010151000_PersistSupportBookingLink")]
public partial class PersistSupportBookingLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The EF snapshot already contained this property, but no prior migration
        // created the physical column for databases upgraded from earlier versions.
        migrationBuilder.Sql("ALTER TABLE support_tickets ADD COLUMN IF NOT EXISTS \"BookingId\" uuid;");
        migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_support_tickets_BookingId" ON support_tickets ("BookingId");""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM support_tickets WHERE "BookingId" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove support booking links without reviewing open cases';
                END IF;
            END $$;
            """);
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_support_tickets_BookingId\"");
        migrationBuilder.Sql("ALTER TABLE support_tickets DROP COLUMN IF EXISTS \"BookingId\"");
    }
}
