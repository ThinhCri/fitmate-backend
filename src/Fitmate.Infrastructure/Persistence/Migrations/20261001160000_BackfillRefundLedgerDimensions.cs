using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261001160000_BackfillRefundLedgerDimensions")]
public sealed class BackfillRefundLedgerDimensions : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            DO $$
            DECLARE
                r RECORD;
                v_appt RECORD;
                v_payment RECORD;
                v_cum_fee_before bigint;
                v_cum_fee_after bigint;
                v_refund_fee bigint;
                v_refund_trainer bigint;
                v_refunded_before bigint;
                v_refunded_after bigint;
                v_platform_fee bigint;
                v_paid bigint;
            BEGIN
                FOR r IN
                    SELECT rfd.id, rfd."AppointmentId", rfd."PaymentId", rfd."AmountVnd", rfd.succeeded_at_utc, rfd."CreatedAt"
                    FROM refunds rfd
                    WHERE rfd."Status" = 1 
                      AND rfd.succeeded_at_utc IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM financial_ledger_entries fle 
                          WHERE fle.refund_id = rfd.id 
                            AND fle.entry_type IN ('RefundPlatformFeeReturn', 'RefundTrainerDeduction')
                      )
                    ORDER BY rfd."PaymentId", rfd."CreatedAt", rfd.id
                LOOP
                    -- Get appointment snapshot
                    SELECT id, "ActualPaidVnd", "PlatformFeeVnd", "TrainerShareVnd"
                    INTO v_appt
                    FROM appointments
                    WHERE id = r."AppointmentId";

                    -- Get payment
                    SELECT id, "AmountVnd", "RefundedSoFarVnd"
                    INTO v_payment
                    FROM payments
                    WHERE id = r."PaymentId";

                    IF v_appt.id IS NOT NULL AND v_appt."ActualPaidVnd" > 0 THEN
                        v_paid := v_appt."ActualPaidVnd";
                        v_platform_fee := v_appt."PlatformFeeVnd";
                    ELSIF v_payment.id IS NOT NULL AND v_payment."AmountVnd" > 0 THEN
                        v_paid := v_payment."AmountVnd";
                        v_platform_fee := ROUND(v_payment."AmountVnd" * 0.10);
                    ELSE
                        CONTINUE; -- Skip if no valid payment/appointment baseline
                    END IF;

                    -- Calculate cumulative refunded so far before this refund
                    SELECT COALESCE(SUM("AmountVnd"), 0)
                    INTO v_refunded_before
                    FROM refunds
                    WHERE "PaymentId" = r."PaymentId"
                      AND "Status" = 1
                      AND ("CreatedAt" < r."CreatedAt" OR ("CreatedAt" = r."CreatedAt" AND id < r.id));

                    v_refunded_after := v_refunded_before + r."AmountVnd";

                    -- Cumulative fee calculation with half-up rounding
                    v_cum_fee_before := ROUND(v_platform_fee::numeric * v_refunded_before::numeric / v_paid::numeric);
                    v_cum_fee_after := ROUND(v_platform_fee::numeric * v_refunded_after::numeric / v_paid::numeric);

                    v_refund_fee := v_cum_fee_after - v_cum_fee_before;
                    v_refund_trainer := r."AmountVnd" - v_refund_fee;

                    -- Insert RefundPlatformFeeReturn if fee > 0
                    IF v_refund_fee != 0 THEN
                        INSERT INTO financial_ledger_entries (
                            id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id, refund_id,
                            source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
                        ) VALUES (
                            gen_random_uuid(), 'RefundPlatformFeeReturn', -v_refund_fee, r.succeeded_at_utc, r."AppointmentId", r."PaymentId", r.id,
                            'migration-backfill:refund-dimensions', r.id::text, 'system-migration', now(), 1, '{}'::jsonb
                        ) ON CONFLICT DO NOTHING;
                    END IF;

                    -- Insert RefundTrainerDeduction if trainer deduction > 0
                    IF v_refund_trainer != 0 THEN
                        INSERT INTO financial_ledger_entries (
                            id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id, refund_id,
                            source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
                        ) VALUES (
                            gen_random_uuid(), 'RefundTrainerDeduction', -v_refund_trainer, r.succeeded_at_utc, r."AppointmentId", r."PaymentId", r.id,
                            'migration-backfill:refund-dimensions', r.id::text, 'system-migration', now(), 1, '{}'::jsonb
                        ) ON CONFLICT DO NOTHING;
                    END IF;
                END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DELETE FROM financial_ledger_entries
            WHERE source_event_id = 'migration-backfill:refund-dimensions';
            """);
    }
}
