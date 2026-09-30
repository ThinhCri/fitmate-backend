using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260930140000_AddBookingRescheduleRequests")]
public sealed class AddBookingRescheduleRequests : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            CREATE TABLE IF NOT EXISTS booking_reschedule_requests (
                id uuid NOT NULL,
                appointment_id uuid NOT NULL,
                requested_by_user_id uuid NOT NULL,
                old_slot_start_utc timestamptz NOT NULL,
                new_slot_start_utc timestamptz NOT NULL,
                duration_minutes integer NOT NULL,
                status integer NOT NULL,
                reason character varying(500) NOT NULL,
                deadline_utc timestamptz NOT NULL,
                created_at_utc timestamptz NOT NULL,
                responded_at_utc timestamptz NULL,
                rejection_reason character varying(500) NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NULL,
                CONSTRAINT pk_booking_reschedule_requests PRIMARY KEY (id),
                CONSTRAINT fk_booking_reschedule_requests_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uq_booking_reschedule_pending ON booking_reschedule_requests (appointment_id) WHERE status = 0;
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP TABLE IF EXISTS booking_reschedule_requests;
            """);
    }
}
