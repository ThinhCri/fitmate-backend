using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261001150000_HardenFinancialLedgerInvariants")]
public sealed class HardenFinancialLedgerInvariants : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        // 1. Preflight validation
        b.Sql("""
            DO $$
            DECLARE
                v_invalid_types_count integer;
                v_zero_amount_count integer;
                v_both_reverses_corrects_count integer;
                v_duplicate_reversals_count integer;
                v_duplicate_corrections_count integer;
            BEGIN
                SELECT COUNT(*) INTO v_invalid_types_count
                FROM financial_ledger_entries
                WHERE entry_type NOT IN (
                    'GrossBookingFee',
                    'PlatformCommission',
                    'TrainerPayable',
                    'RefundGrossReduction',
                    'RefundPlatformFeeReturn',
                    'RefundTrainerDeduction',
                    'TrainerPayout'
                );
                IF v_invalid_types_count > 0 THEN
                    RAISE EXCEPTION 'Preflight failed: found % rows with invalid entry_type in financial_ledger_entries', v_invalid_types_count;
                END IF;

                SELECT COUNT(*) INTO v_zero_amount_count
                FROM financial_ledger_entries
                WHERE amount_vnd = 0;
                IF v_zero_amount_count > 0 THEN
                    RAISE EXCEPTION 'Preflight failed: found % rows with amount_vnd = 0 in financial_ledger_entries', v_zero_amount_count;
                END IF;

                SELECT COUNT(*) INTO v_both_reverses_corrects_count
                FROM financial_ledger_entries
                WHERE reverses_entry_id IS NOT NULL AND corrects_entry_id IS NOT NULL;
                IF v_both_reverses_corrects_count > 0 THEN
                    RAISE EXCEPTION 'Preflight failed: found % rows with both reverses_entry_id and corrects_entry_id set', v_both_reverses_corrects_count;
                END IF;

                SELECT COUNT(*) INTO v_duplicate_reversals_count
                FROM (
                    SELECT reverses_entry_id
                    FROM financial_ledger_entries
                    WHERE reverses_entry_id IS NOT NULL
                    GROUP BY reverses_entry_id
                    HAVING COUNT(*) > 1
                ) sub;
                IF v_duplicate_reversals_count > 0 THEN
                    RAISE EXCEPTION 'Preflight failed: found % duplicate reversals in financial_ledger_entries', v_duplicate_reversals_count;
                END IF;

                SELECT COUNT(*) INTO v_duplicate_corrections_count
                FROM (
                    SELECT corrects_entry_id
                    FROM financial_ledger_entries
                    WHERE corrects_entry_id IS NOT NULL
                    GROUP BY corrects_entry_id
                    HAVING COUNT(*) > 1
                ) sub;
                IF v_duplicate_corrections_count > 0 THEN
                    RAISE EXCEPTION 'Preflight failed: found % duplicate corrections in financial_ledger_entries', v_duplicate_corrections_count;
                END IF;
            END $$;
            """);

        // 2. Add CHECK constraints
        b.Sql("""
            ALTER TABLE financial_ledger_entries
            ADD CONSTRAINT chk_ledger_entry_type CHECK (
                entry_type IN (
                    'GrossBookingFee',
                    'PlatformCommission',
                    'TrainerPayable',
                    'RefundGrossReduction',
                    'RefundPlatformFeeReturn',
                    'RefundTrainerDeduction',
                    'TrainerPayout'
                )
            );

            ALTER TABLE financial_ledger_entries
            ADD CONSTRAINT chk_ledger_amount_nonzero CHECK (amount_vnd != 0);

            ALTER TABLE financial_ledger_entries
            ADD CONSTRAINT chk_ledger_reverses_corrects_exclusive CHECK (
                NOT (reverses_entry_id IS NOT NULL AND corrects_entry_id IS NOT NULL)
            );
            """);

        // 3. Add single-reversal and single-correction unique partial indexes
        b.Sql("""
            CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_single_reversal
            ON financial_ledger_entries (reverses_entry_id)
            WHERE reverses_entry_id IS NOT NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_single_correction
            ON financial_ledger_entries (corrects_entry_id)
            WHERE corrects_entry_id IS NOT NULL;
            """);

        // 4. Recreate initial event unique indexes excluding reversals and corrections
        b.Sql("""
            DROP INDEX IF EXISTS uq_ledger_appointment_recognition;
            DROP INDEX IF EXISTS uq_ledger_refund_adjustment;
            DROP INDEX IF EXISTS uq_ledger_settlement_payout;

            CREATE UNIQUE INDEX uq_ledger_appointment_recognition
            ON financial_ledger_entries (appointment_id, entry_type)
            WHERE appointment_id IS NOT NULL
              AND entry_type IN ('GrossBookingFee', 'PlatformCommission', 'TrainerPayable')
              AND reverses_entry_id IS NULL
              AND corrects_entry_id IS NULL;

            CREATE UNIQUE INDEX uq_ledger_refund_adjustment
            ON financial_ledger_entries (refund_id, entry_type)
            WHERE refund_id IS NOT NULL
              AND reverses_entry_id IS NULL
              AND corrects_entry_id IS NULL;

            CREATE UNIQUE INDEX uq_ledger_settlement_payout
            ON financial_ledger_entries (settlement_id, entry_type)
            WHERE settlement_id IS NOT NULL
              AND reverses_entry_id IS NULL
              AND corrects_entry_id IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        // Revert initial event indexes back to original definitions
        b.Sql("""
            DROP INDEX IF EXISTS uq_ledger_appointment_recognition;
            DROP INDEX IF EXISTS uq_ledger_refund_adjustment;
            DROP INDEX IF EXISTS uq_ledger_settlement_payout;

            CREATE UNIQUE INDEX uq_ledger_appointment_recognition
            ON financial_ledger_entries (appointment_id, entry_type)
            WHERE appointment_id IS NOT NULL
              AND entry_type IN ('GrossBookingFee', 'PlatformCommission', 'TrainerPayable');

            CREATE UNIQUE INDEX uq_ledger_refund_adjustment
            ON financial_ledger_entries (refund_id, entry_type)
            WHERE refund_id IS NOT NULL;

            CREATE UNIQUE INDEX uq_ledger_settlement_payout
            ON financial_ledger_entries (settlement_id, entry_type)
            WHERE settlement_id IS NOT NULL;
            """);

        // Drop unique partial indexes
        b.Sql("""
            DROP INDEX IF EXISTS uq_ledger_single_correction;
            DROP INDEX IF EXISTS uq_ledger_single_reversal;
            """);

        // Drop check constraints
        b.Sql("""
            ALTER TABLE financial_ledger_entries
            DROP CONSTRAINT IF EXISTS chk_ledger_reverses_corrects_exclusive;

            ALTER TABLE financial_ledger_entries
            DROP CONSTRAINT IF EXISTS chk_ledger_amount_nonzero;

            ALTER TABLE financial_ledger_entries
            DROP CONSTRAINT IF EXISTS chk_ledger_entry_type;
            """);
    }
}
