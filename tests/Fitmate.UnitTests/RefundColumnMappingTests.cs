using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fitmate.UnitTests;

public sealed class RefundColumnMappingTests
{
    [Fact]
    public void SucceededAtUtc_UsesColumnCreatedByRefundMigration()
    {
        var options = new DbContextOptionsBuilder<FitmateDbContext>()
            .UseNpgsql("Host=localhost;Database=fitmate;Username=fitmate;Password=unused")
            .Options;
        using var db = new FitmateDbContext(options);
        var refund = db.Model.FindEntityType(typeof(Refund))!;
        var column = refund.FindProperty(nameof(Refund.SucceededAtUtc))!
            .GetColumnName(StoreObjectIdentifier.Table("refunds", null));

        Assert.Equal("succeeded_at_utc", column);
    }
}
