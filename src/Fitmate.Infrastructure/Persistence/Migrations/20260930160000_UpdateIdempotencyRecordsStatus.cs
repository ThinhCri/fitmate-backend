using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260930160000_UpdateIdempotencyRecordsStatus")]
public sealed class UpdateIdempotencyRecordsStatus : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            ALTER TABLE idempotency_records 
            ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'completed';

            CREATE INDEX IF NOT EXISTS ix_idempotency_records_key_status 
            ON idempotency_records ("Key", "Status");
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP INDEX IF EXISTS ix_idempotency_records_key_status;

            ALTER TABLE idempotency_records 
            DROP COLUMN IF EXISTS "Status";
            """);
    }
}
