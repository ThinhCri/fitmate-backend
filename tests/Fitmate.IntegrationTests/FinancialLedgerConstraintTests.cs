using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fitmate.IntegrationTests;

public class FinancialLedgerConstraintTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public FinancialLedgerConstraintTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public void Domain_Create_ZeroAmount_ThrowsDomainException()
    {
        var action = () => FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.GrossBookingFee,
            amountVnd: 0,
            occurredAtUtc: DateTime.UtcNow,
            createdByUserId: "admin");

        action.Should().Throw<DomainException>()
            .WithMessage("*amount cannot be zero*");
    }

    [Fact]
    public void Domain_Create_InvalidEntryType_ThrowsDomainException()
    {
        var action = () => FinancialLedgerEntry.Create(
            "UnsupportedRandomType",
            amountVnd: 100_000,
            occurredAtUtc: DateTime.UtcNow,
            createdByUserId: "admin");

        action.Should().Throw<DomainException>()
            .WithMessage("*Invalid financial ledger entry type*");
    }

    [Fact]
    public void Domain_Create_BothReversesAndCorrects_ThrowsDomainException()
    {
        var action = () => FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.GrossBookingFee,
            amountVnd: -100_000,
            occurredAtUtc: DateTime.UtcNow,
            createdByUserId: "admin",
            reversesEntryId: Guid.NewGuid(),
            correctsEntryId: Guid.NewGuid());

        action.Should().Throw<DomainException>()
            .WithMessage("*simultaneously be a reversal and a correction*");
    }

    [Fact]
    public async Task DbConstraint_ZeroAmount_ThrowsDbUpdateException()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var rawSql = """
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'GrossBookingFee', 0, now(), 'test', now(), 1, '{{}}'::jsonb
            );
            """;

        var action = async () => await db.Database.ExecuteSqlRawAsync(rawSql);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*chk_ledger_amount_nonzero*");
    }

    [Fact]
    public async Task DbConstraint_InvalidEntryType_ThrowsDbUpdateException()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var rawSql = """
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'FakeType123', 50000, now(), 'test', now(), 1, '{{}}'::jsonb
            );
            """;

        var action = async () => await db.Database.ExecuteSqlRawAsync(rawSql);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*chk_ledger_entry_type*");
    }

    [Fact]
    public async Task DbConstraint_BothReversesAndCorrects_ThrowsDbUpdateException()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var originId = Guid.NewGuid();
        // Insert legitimate origin entry first
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc, metadata_version, metadata_json
            ) VALUES (
                {0}, 'GrossBookingFee', 50000, now(), 'test', now(), 1, '{{}}'::jsonb
            );
            """, originId);

        var rawSql = """
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                reverses_entry_id, corrects_entry_id, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'GrossBookingFee', -50000, now(), 'test', now(),
                {0}, {0}, 1, '{{}}'::jsonb
            );
            """;

        var action = async () => await db.Database.ExecuteSqlRawAsync(rawSql, originId);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*chk_ledger_reverses_corrects_exclusive*");
    }

    [Fact]
    public async Task DbIndex_DuplicateReversal_ThrowsUniqueViolation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var originId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc, metadata_version, metadata_json
            ) VALUES (
                {0}, 'GrossBookingFee', 70000, now(), 'test', now(), 1, '{{}}'::jsonb
            );
            """, originId);

        // First reversal: succeeds
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                reverses_entry_id, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'GrossBookingFee', -70000, now(), 'test', now(),
                {0}, 1, '{{}}'::jsonb
            );
            """, originId);

        // Second reversal for same entry: must be rejected by uq_ledger_single_reversal
        var secondAction = async () => await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                reverses_entry_id, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'GrossBookingFee', -70000, now(), 'test', now(),
                {0}, 1, '{{}}'::jsonb
            );
            """, originId);

        await secondAction.Should().ThrowAsync<Exception>()
            .WithMessage("*uq_ledger_single_reversal*");
    }

    [Fact]
    public async Task DbIndex_DuplicateCorrection_ThrowsUniqueViolation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var originId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc, metadata_version, metadata_json
            ) VALUES (
                {0}, 'PlatformCommission', 7000, now(), 'test', now(), 1, '{{}}'::jsonb
            );
            """, originId);

        // First correction: succeeds
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                corrects_entry_id, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'PlatformCommission', 6000, now(), 'test', now(),
                {0}, 1, '{{}}'::jsonb
            );
            """, originId);

        // Second correction for same entry: must be rejected by uq_ledger_single_correction
        var secondAction = async () => await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                corrects_entry_id, metadata_version, metadata_json
            ) VALUES (
                gen_random_uuid(), 'PlatformCommission', 5000, now(), 'test', now(),
                {0}, 1, '{{}}'::jsonb
            );
            """, originId);

        await secondAction.Should().ThrowAsync<Exception>()
            .WithMessage("*uq_ledger_single_correction*");
    }

    [Fact]
    public async Task DbIndex_ReversalAndCorrection_DoNotCollideWithInitialEventIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var appt = Fitmate.Domain.Booking.Appointment.CreateHold(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        var apptId = appt.Id;
        var originId = Guid.NewGuid();

        // 1. Initial appointment recognition entry
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                appointment_id, metadata_version, metadata_json
            ) VALUES (
                {0}, 'GrossBookingFee', 100000, now(), 'test', now(),
                {1}, 1, '{{}}'::jsonb
            );
            """, originId, apptId);

        // 2. Reversal referencing same appointment_id and entry_type with reverses_entry_id set
        // Must succeed because uq_ledger_appointment_recognition filters out non-null reverses_entry_id
        var reversalId = Guid.NewGuid();
        var reversalAction = async () => await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO financial_ledger_entries (
                id, entry_type, amount_vnd, occurred_at_utc, created_by_user_id, created_at_utc,
                appointment_id, reverses_entry_id, metadata_version, metadata_json
            ) VALUES (
                {0}, 'GrossBookingFee', -100000, now(), 'test', now(),
                {1}, {2}, 1, '{{}}'::jsonb
            );
            """, reversalId, apptId, originId);

        await reversalAction.Should().NotThrowAsync();
    }
}
