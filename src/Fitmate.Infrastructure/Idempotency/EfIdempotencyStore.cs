using System.Data;
using System.Data.Common;
using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fitmate.Infrastructure.Idempotency;

public class EfIdempotencyStore : IIdempotencyStore
{
    private readonly FitmateDbContext _db;

    public EfIdempotencyStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string payloadHash,
        Guid ownerToken,
        TimeSpan leaseDuration,
        bool isFinancial,
        TimeSpan? ttl,
        string? actorUserId,
        string? operationName,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var leaseExpiresAt = nowUtc.Add(leaseDuration);
        DateTime? expiresAt = isFinancial || ttl == null ? null : nowUtc.Add(ttl.Value);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var currentTx = _db.Database.CurrentTransaction?.GetDbTransaction();

        const string insertSql = """
            INSERT INTO idempotency_records (
                "Key", "PayloadHash", "StatusCode", "ResponseBody", "CreatedAt", "ExpiresAt",
                "Status", "OwnerToken", "LeaseExpiresAt", "IsFinancial", "ActorUserId", "OperationName"
            ) VALUES (
                @Key, @PayloadHash, 0, NULL, @NowUtc, @ExpiresAt,
                'processing', @OwnerToken, @LeaseExpiresAt, @IsFinancial, @ActorUserId, @OperationName
            )
            ON CONFLICT ("Key") DO UPDATE
            SET "Status" = 'processing',
                "OwnerToken" = @OwnerToken,
                "LeaseExpiresAt" = @LeaseExpiresAt
            WHERE idempotency_records."PayloadHash" = @PayloadHash
              AND (idempotency_records."Status" = 'failed'
                OR (idempotency_records."Status" = 'processing' AND idempotency_records."LeaseExpiresAt" < @NowUtc))
            RETURNING "Status", "OwnerToken";
            """;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = insertSql;
            cmd.Transaction = currentTx;

            AddParam(cmd, "@Key", key);
            AddParam(cmd, "@PayloadHash", payloadHash);
            AddParam(cmd, "@NowUtc", nowUtc);
            AddParam(cmd, "@ExpiresAt", (object?)expiresAt ?? DBNull.Value);
            AddParam(cmd, "@OwnerToken", ownerToken);
            AddParam(cmd, "@LeaseExpiresAt", leaseExpiresAt);
            AddParam(cmd, "@IsFinancial", isFinancial);
            AddParam(cmd, "@ActorUserId", (object?)actorUserId ?? DBNull.Value);
            AddParam(cmd, "@OperationName", (object?)operationName ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var status = reader.GetString(reader.GetOrdinal("Status"));
                var returnedOwner = reader.GetGuid(reader.GetOrdinal("OwnerToken"));
                if (status == IdempotencyRecord.StatusProcessing && returnedOwner == ownerToken)
                {
                    var record = IdempotencyRecord.CreateAcquired(
                        key, payloadHash, ownerToken, leaseDuration, isFinancial, ttl, actorUserId, operationName);
                    return IdempotencyAcquireResult.Acquired(record);
                }
            }
        }

        // Acquisition failed: inspect existing record to determine Conflict, InProgress, or Replay
        const string selectSql = """
            SELECT "Key", "PayloadHash", "StatusCode", "ResponseBody", "CreatedAt", "ExpiresAt",
                   "Status", "OwnerToken", "LeaseExpiresAt", "IsFinancial", "ActorUserId", "OperationName"
            FROM idempotency_records
            WHERE "Key" = @Key;
            """;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = selectSql;
            cmd.Transaction = currentTx;
            AddParam(cmd, "@Key", key);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var existingHash = reader.GetString(reader.GetOrdinal("PayloadHash"));
                if (!string.Equals(existingHash, payloadHash, StringComparison.Ordinal))
                {
                    return IdempotencyAcquireResult.PayloadConflict();
                }

                var status = reader.GetString(reader.GetOrdinal("Status"));
                var isFin = reader.GetBoolean(reader.GetOrdinal("IsFinancial"));
                var statusCode = reader.GetInt32(reader.GetOrdinal("StatusCode"));
                var responseBody = reader.IsDBNull(reader.GetOrdinal("ResponseBody")) ? null : reader.GetString(reader.GetOrdinal("ResponseBody"));

                if (status == IdempotencyRecord.StatusCompleted)
                {
                    var rec = IdempotencyRecord.CompletedRecord(key, existingHash, statusCode, responseBody, isFin, ttl, actorUserId, operationName);
                    return IdempotencyAcquireResult.Replay(rec);
                }

                if (status == IdempotencyRecord.StatusProcessing)
                {
                    var rec = IdempotencyRecord.CreateAcquired(key, existingHash, ownerToken, leaseDuration, isFin, ttl, actorUserId, operationName);
                    return IdempotencyAcquireResult.InProgress(rec);
                }

                var failedRec = IdempotencyRecord.CreateAcquired(key, existingHash, ownerToken, leaseDuration, isFin, ttl, actorUserId, operationName);
                return IdempotencyAcquireResult.InProgress(failedRec);
            }
        }

        return IdempotencyAcquireResult.PayloadConflict();
    }

    public async Task<bool> CompleteAsync(
        string key,
        Guid ownerToken,
        int statusCode,
        string? responseBody,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        var currentTx = _db.Database.CurrentTransaction?.GetDbTransaction();

        const string updateSql = """
            UPDATE idempotency_records
            SET "Status" = 'completed',
                "StatusCode" = @StatusCode,
                "ResponseBody" = CASE WHEN @ResponseBody IS NULL THEN NULL ELSE @ResponseBody::jsonb END,
                "LeaseExpiresAt" = now()
            WHERE "Key" = @Key
              AND "OwnerToken" = @OwnerToken
              AND "Status" = 'processing';
            """;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = updateSql;
        cmd.Transaction = currentTx;
        AddParam(cmd, "@Key", key);
        AddParam(cmd, "@OwnerToken", ownerToken);
        AddParam(cmd, "@StatusCode", statusCode);
        AddParam(cmd, "@ResponseBody", (object?)responseBody ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 1;
    }

    public async Task<bool> ReleaseAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        var currentTx = _db.Database.CurrentTransaction?.GetDbTransaction();

        const string deleteSql = """
            DELETE FROM idempotency_records
            WHERE "Key" = @Key
              AND "OwnerToken" = @OwnerToken
              AND "Status" = 'processing';
            """;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = deleteSql;
        cmd.Transaction = currentTx;
        AddParam(cmd, "@Key", key);
        AddParam(cmd, "@OwnerToken", ownerToken);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 1;
    }

    public async Task<bool> FailAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        var currentTx = _db.Database.CurrentTransaction?.GetDbTransaction();

        const string updateSql = """
            UPDATE idempotency_records
            SET "Status" = 'failed',
                "LeaseExpiresAt" = now()
            WHERE "Key" = @Key
              AND "OwnerToken" = @OwnerToken
              AND "Status" = 'processing';
            """;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = updateSql;
        cmd.Transaction = currentTx;
        AddParam(cmd, "@Key", key);
        AddParam(cmd, "@OwnerToken", ownerToken);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 1;
    }

    public async Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct)
    {
        var existing = await _db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (existing is null) return new IdempotencyLookup(null, false, false);
        if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            return new IdempotencyLookup(existing, Conflict: true, IsProcessing: false);
        if (existing.Status == IdempotencyRecord.StatusProcessing && existing.LeaseExpiresAt > DateTime.UtcNow)
            return new IdempotencyLookup(existing, Conflict: false, IsProcessing: true);
        return new IdempotencyLookup(existing, Conflict: false, IsProcessing: false);
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken ct)
    {
        var existing = await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == record.Key, ct);
        if (existing != null)
        {
            existing.Complete(record.StatusCode, record.ResponseBody, record.OwnerToken);
        }
        else
        {
            await _db.IdempotencyRecords.AddAsync(record, ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct)
    {
        return _db.IdempotencyRecords
            .Where(r => !r.IsFinancial && r.ExpiresAt != null && r.ExpiresAt <= cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
