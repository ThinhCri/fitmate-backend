using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261121190000_DropLegacyAppointmentStatusNotNull")]
public sealed class DropLegacyAppointmentStatusNotNull : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'appointments' AND column_name = 'Status') THEN
                    ALTER TABLE appointments ALTER COLUMN "Status" DROP NOT NULL;
                    ALTER TABLE appointments ALTER COLUMN "Status" SET DEFAULT 0;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'appointments' AND column_name = 'status') THEN
                    ALTER TABLE appointments ALTER COLUMN status DROP NOT NULL;
                    ALTER TABLE appointments ALTER COLUMN status SET DEFAULT 0;
                END IF;
            END $$;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
