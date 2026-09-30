using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260930150000_AddFinancialLedgerEntries")]
public sealed class AddFinancialLedgerEntries : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            CREATE TABLE IF NOT EXISTS financial_ledger_entries (
                id uuid NOT NULL,
                entry_type character varying(64) NOT NULL,
                amount_vnd bigint NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                appointment_id uuid NULL,
                payment_id uuid NULL,
                refund_id uuid NULL,
                settlement_id uuid NULL,
                source_event_id character varying(128) NULL,
                correlation_id character varying(128) NULL,
                created_by_user_id character varying(128) NOT NULL,
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                reverses_entry_id uuid NULL,
                corrects_entry_id uuid NULL,
                metadata_version integer NOT NULL DEFAULT 1,
                metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                CONSTRAINT pk_financial_ledger_entries PRIMARY KEY (id),
                CONSTRAINT fk_financial_ledger_entries_appointments FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
                CONSTRAINT fk_financial_ledger_entries_payments FOREIGN KEY (payment_id) REFERENCES payments (id) ON DELETE RESTRICT,
                CONSTRAINT fk_financial_ledger_entries_refunds FOREIGN KEY (refund_id) REFERENCES refunds (id) ON DELETE RESTRICT,
                CONSTRAINT fk_financial_ledger_entries_manual_pt_settlements FOREIGN KEY (settlement_id) REFERENCES manual_pt_settlements (appointment_id) ON DELETE RESTRICT,
                CONSTRAINT fk_financial_ledger_entries_reverses FOREIGN KEY (reverses_entry_id) REFERENCES financial_ledger_entries (id) ON DELETE RESTRICT,
                CONSTRAINT fk_financial_ledger_entries_corrects FOREIGN KEY (corrects_entry_id) REFERENCES financial_ledger_entries (id) ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_appointment_recognition 
            ON financial_ledger_entries (appointment_id, entry_type) 
            WHERE appointment_id IS NOT NULL AND entry_type IN ('GrossBookingFee', 'PlatformCommission', 'TrainerPayable');

            CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_refund_adjustment 
            ON financial_ledger_entries (refund_id, entry_type) 
            WHERE refund_id IS NOT NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_settlement_payout 
            ON financial_ledger_entries (settlement_id, entry_type) 
            WHERE settlement_id IS NOT NULL;

            CREATE INDEX IF NOT EXISTS ix_financial_ledger_entries_occurred_at_utc 
            ON financial_ledger_entries (occurred_at_utc);

            -- PostgreSQL trigger to enforce immutable append-only ledger
            CREATE OR REPLACE FUNCTION fn_prevent_financial_ledger_mutation()
            RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'financial_ledger_entries is immutable and append-only. UPDATE and DELETE operations are forbidden.';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_financial_ledger_entries_immutable ON financial_ledger_entries;
            CREATE TRIGGER trg_financial_ledger_entries_immutable
            BEFORE UPDATE OR DELETE ON financial_ledger_entries
            FOR EACH ROW
            EXECUTE FUNCTION fn_prevent_financial_ledger_mutation();

            -- Backfill legacy completed appointments WITH verified completion timestamp only
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id,
                source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
            )
            SELECT
                gen_random_uuid(), 'GrossBookingFee', a.actual_paid_vnd, a.completed_at_utc, a.id, a.payment_id,
                'migration-backfill:completed', a.id::text, 'system-migration', now(), 1, '{}'::jsonb
            FROM appointments a
            WHERE a.appointment_status = 4 
              AND a.completed_at_utc IS NOT NULL 
              AND a.actual_paid_vnd > 0
            ON CONFLICT DO NOTHING;

            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id,
                source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
            )
            SELECT
                gen_random_uuid(), 'PlatformCommission', a.platform_fee_vnd, a.completed_at_utc, a.id, a.payment_id,
                'migration-backfill:completed', a.id::text, 'system-migration', now(), 1, '{}'::jsonb
            FROM appointments a
            WHERE a.appointment_status = 4 
              AND a.completed_at_utc IS NOT NULL 
              AND a.actual_paid_vnd > 0
            ON CONFLICT DO NOTHING;

            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id,
                source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
            )
            SELECT
                gen_random_uuid(), 'TrainerPayable', a.trainer_share_vnd, a.completed_at_utc, a.id, a.payment_id,
                'migration-backfill:completed', a.id::text, 'system-migration', now(), 1, '{}'::jsonb
            FROM appointments a
            WHERE a.appointment_status = 4 
              AND a.completed_at_utc IS NOT NULL 
              AND a.actual_paid_vnd > 0
            ON CONFLICT DO NOTHING;

            -- Backfill legacy succeeded refunds WITH verified succeeded timestamp only
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id, refund_id,
                source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
            )
            SELECT
                gen_random_uuid(), 'RefundGrossReduction', -r.amount_vnd, r.succeeded_at_utc, r.appointment_id, r.payment_id, r.id,
                'migration-backfill:refund', r.id::text, 'system-migration', now(), 1, '{}'::jsonb
            FROM refunds r
            WHERE r.status = 1 
              AND r.succeeded_at_utc IS NOT NULL
            ON CONFLICT DO NOTHING;

            -- Backfill legacy settlements WITH verified transferred timestamp only
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, appointment_id, payment_id, settlement_id,
                source_event_id, correlation_id, created_by_user_id, created_at_utc, metadata_version, metadata_json
            )
            SELECT
                gen_random_uuid(), 'TrainerPayout', -s.amount_vnd, s.transferred_at, s.appointment_id, s.payment_id, s.appointment_id,
                'migration-backfill:payout', s.appointment_id::text, 'system-migration', now(), 1, '{}'::jsonb
            FROM manual_pt_settlements s
            WHERE s.status = 'paid' 
              AND s.transferred_at IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP TRIGGER IF EXISTS trg_financial_ledger_entries_immutable ON financial_ledger_entries;
            DROP FUNCTION IF EXISTS fn_prevent_financial_ledger_mutation;
            DROP TABLE IF EXISTS financial_ledger_entries;
            """);
    }
}
