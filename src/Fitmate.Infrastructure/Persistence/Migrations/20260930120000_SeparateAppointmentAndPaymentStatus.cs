using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260930120000_SeparateAppointmentAndPaymentStatus")]
public sealed class SeparateAppointmentAndPaymentStatus : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS appointment_status integer NOT NULL DEFAULT 0;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS legacy_status integer NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS legacy_completion_time_unknown boolean NOT NULL DEFAULT false;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS hold_expires_at_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS acceptance_deadline_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS confirmed_at_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS started_at_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS cancelled_at_utc timestamptz NULL;
            ALTER TABLE appointments ADD COLUMN IF NOT EXISTS rescheduled_count integer NOT NULL DEFAULT 0;

            ALTER TABLE refunds ADD COLUMN IF NOT EXISTS succeeded_at_utc timestamptz NULL;

            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'appointments' AND column_name = 'status') THEN
                    UPDATE appointments SET legacy_status = status WHERE legacy_status IS NULL;

                    -- Map legacy status to new appointment_status:
                    -- 0 (Held) -> 0 (Held)
                    -- 1 (AwaitingPayment) -> 0 (Held)
                    -- 2 (Paid) -> 1 (Pending)
                    -- 3 (Completed) -> 4 (Completed)
                    -- 4 (Cancelled) -> 5 (Cancelled)
                    -- 5 (Refunded) -> 5 (Cancelled)
                    -- 6 (Expired) -> 6 (Expired)
                    -- 7 (Confirmed) -> 2 (Confirmed)
                    -- 8 (InProgress) -> 3 (InProgress)
                    UPDATE appointments
                    SET appointment_status = CASE status
                        WHEN 0 THEN 0
                        WHEN 1 THEN 0
                        WHEN 2 THEN 1
                        WHEN 3 THEN 4
                        WHEN 4 THEN 5
                        WHEN 5 THEN 5
                        WHEN 6 THEN 6
                        WHEN 7 THEN 2
                        WHEN 8 THEN 3
                        ELSE 0
                    END;

                    -- For completed legacy appointments without verified completion evidence:
                    -- Set completed_at_utc = NULL and legacy_completion_time_unknown = true
                    UPDATE appointments
                    SET completed_at_utc = NULL,
                        legacy_completion_time_unknown = true
                    WHERE status = 3 AND completed_at_utc IS NULL;
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS "IX_appointments_appointment_status" ON appointments (appointment_status);
            CREATE INDEX IF NOT EXISTS "IX_appointments_hold_expires_at_utc" ON appointments (hold_expires_at_utc);
            CREATE INDEX IF NOT EXISTS "IX_appointments_acceptance_deadline_utc" ON appointments (acceptance_deadline_utc);
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'appointments' AND column_name = 'status') THEN
                    -- If legacy row, restore legacy_status if preserved
                    UPDATE appointments
                    SET status = legacy_status
                    WHERE legacy_status IS NOT NULL;

                    -- For new rows without legacy_status:
                    -- If Cancelled and Payment is Refunded -> 5 (Refunded)
                    -- Other Cancelled -> 4 (Cancelled)
                    -- 0 (Held) -> 0 (Held)
                    -- 1 (Pending) -> 2 (Paid)
                    -- 2 (Confirmed) -> 7 (Confirmed)
                    -- 3 (InProgress) -> 8 (InProgress)
                    -- 4 (Completed) -> 3 (Completed)
                    -- 6 (Expired) -> 6 (Expired)
                    UPDATE appointments a
                    SET status = CASE
                        WHEN a.appointment_status = 5 THEN
                            CASE WHEN EXISTS (
                                SELECT 1 FROM payments p
                                WHERE p.appointment_id = a.id AND p.status = 3
                            ) THEN 5 ELSE 4 END
                        WHEN a.appointment_status = 0 THEN 0
                        WHEN a.appointment_status = 1 THEN 2
                        WHEN a.appointment_status = 2 THEN 7
                        WHEN a.appointment_status = 3 THEN 8
                        WHEN a.appointment_status = 4 THEN 3
                        WHEN a.appointment_status = 6 THEN 6
                        ELSE 0
                    END
                    WHERE a.legacy_status IS NULL;
                END IF;
            END $$;

            DROP INDEX IF EXISTS "IX_appointments_appointment_status";
            DROP INDEX IF EXISTS "IX_appointments_hold_expires_at_utc";
            DROP INDEX IF EXISTS "IX_appointments_acceptance_deadline_utc";

            ALTER TABLE appointments DROP COLUMN IF EXISTS appointment_status;
            ALTER TABLE appointments DROP COLUMN IF EXISTS legacy_status;
            ALTER TABLE appointments DROP COLUMN IF EXISTS completed_at_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS legacy_completion_time_unknown;
            ALTER TABLE appointments DROP COLUMN IF EXISTS hold_expires_at_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS acceptance_deadline_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS confirmed_at_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS started_at_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS cancelled_at_utc;
            ALTER TABLE appointments DROP COLUMN IF EXISTS rescheduled_count;

            ALTER TABLE refunds DROP COLUMN IF EXISTS succeeded_at_utc;
            """);
    }
}
