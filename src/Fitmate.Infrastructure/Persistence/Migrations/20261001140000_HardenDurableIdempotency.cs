using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261001140000_HardenDurableIdempotency")]
public sealed class HardenDurableIdempotency : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            ALTER TABLE idempotency_records
            ADD COLUMN IF NOT EXISTS "OwnerToken" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
            ADD COLUMN IF NOT EXISTS "LeaseExpiresAt" timestamp with time zone NOT NULL DEFAULT now(),
            ADD COLUMN IF NOT EXISTS "IsFinancial" boolean NOT NULL DEFAULT FALSE,
            ADD COLUMN IF NOT EXISTS "ActorUserId" character varying(64) NULL,
            ADD COLUMN IF NOT EXISTS "OperationName" character varying(128) NULL;

            ALTER TABLE idempotency_records
            ALTER COLUMN "ExpiresAt" DROP NOT NULL;

            CREATE INDEX IF NOT EXISTS ix_idempotency_records_is_financial_expires_at
            ON idempotency_records ("IsFinancial", "ExpiresAt");
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP INDEX IF EXISTS ix_idempotency_records_is_financial_expires_at;

            ALTER TABLE idempotency_records
            DROP COLUMN IF EXISTS "OwnerToken",
            DROP COLUMN IF EXISTS "LeaseExpiresAt",
            DROP COLUMN IF EXISTS "IsFinancial",
            DROP COLUMN IF EXISTS "ActorUserId",
            DROP COLUMN IF EXISTS "OperationName";

            ALTER TABLE idempotency_records
            ALTER COLUMN "ExpiresAt" SET NOT NULL;
            """);
    }
}
