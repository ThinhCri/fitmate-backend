using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Fitmate.Infrastructure.Persistence;

namespace Fitmate.UnitTests;

public sealed class AiSalesReportingTests
{
    [Theory] [InlineData("Filter")] [InlineData("BuyerGroups")]
    public void Buyer_queries_translate_to_PostgreSQL_without_client_aggregation(string method)
    {
        using var db = new FitmateDbContext(new DbContextOptionsBuilder<FitmateDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused").Options);
        var store = new EfAdminAiSalesStore(db);
        var query = (IQueryable)typeof(EfAdminAiSalesStore).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(store, new object?[] { new AiSalesQuery(Search: "buyer@example.test", UserId: Guid.NewGuid()), DateTime.UtcNow.AddDays(-30) })!;
        var sql = query.ToQueryString();
        sql.Should().Contain("LEFT JOIN");
        if (method == "BuyerGroups") sql.Should().Contain("GROUP BY");
    }
    [Fact] public async Task Buyers_requires_admin_before_accessing_store()
    {
        var auth = new Mock<IAuthorizationGuard>();
        auth.Setup(x => x.EnsureRoleAsync(It.IsAny<Guid>(), "Admin", It.IsAny<CancellationToken>())).ThrowsAsync(new DomainException("FORBIDDEN", "Forbidden", 403));
        var store = new Mock<IAdminAiSalesStore>(MockBehavior.Strict);
        await FluentActions.Invoking(() => new AdminAiSalesService(auth.Object, store.Object).BuyersAsync(Guid.NewGuid(), new(), default)).Should().ThrowAsync<DomainException>();
        store.VerifyNoOtherCalls();
    }
    [Fact] public async Task Buyers_rejects_unbounded_search()
    {
        var service = new AdminAiSalesService(new Mock<IAuthorizationGuard>().Object, new Mock<IAdminAiSalesStore>(MockBehavior.Strict).Object);
        (await FluentActions.Invoking(() => service.BuyersAsync(Guid.NewGuid(), new(Search: new string('x', 201)), default)).Should().ThrowAsync<DomainException>()).Which.HttpStatus.Should().Be(400);
    }
    private static string Order(string currency = "VND", string units = "29000", int nanos = 0,
        string token = "token", string state = "PROCESSED", string? revenue = "24650") => JsonSerializer.Serialize(new
    {
        orderId = "GPA.unit", purchaseToken = token, state, createTime = "2026-11-08T03:00:00Z",
        lineItems = new[] { new { productId = "fitmate_ai_plus", subscriptionDetails = new { basePlanId = "monthly" } } },
        total = new { currencyCode = currency, units, nanos },
        tax = new { currencyCode = currency, units = "0" },
        developerRevenueInBuyerCurrency = revenue == null ? null : new { currencyCode = currency, units = revenue }
    });
    private static VerifiedPlayOrder Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return GooglePlayOrderParser.Parse(doc.RootElement, "GPA.unit", "token", "fitmate_ai_plus");
    }
    [Fact] public void Reads_actual_Google_money_not_catalog_price_or_assumed_fee()
    {
        var value = Parse(Order(units: "27000", revenue: "22311"));
        value.TotalVnd.Should().Be(27000);
        value.DeveloperRevenueVnd.Should().Be(22311);
        value.TaxVnd.Should().Be(0);
    }
    [Fact] public void Missing_revenue_is_unknown_not_zero() => Parse(Order(revenue: null)).DeveloperRevenueVnd.Should().BeNull();
    [Theory] [InlineData(499999999, 29000)] [InlineData(500000000, 29001)]
    public void Rounds_Google_Money_half_up_without_float(int nanos, long expected) => Parse(Order(nanos: nanos)).TotalVnd.Should().Be(expected);
    [Theory] [InlineData("USD", "token", "PROCESSED")] [InlineData("VND", "other-token", "PROCESSED")] [InlineData("VND", "token", "UNKNOWN")]
    public void Rejects_foreign_currency_wrong_token_or_unknown_state(string currency, string token, string state)
    {
        var act = () => Parse(Order(currency: currency, token: token, state: state));
        act.Should().Throw<DomainException>();
    }
    [Fact] public async Task Enforces_application_authorization_before_accessing_finance_store()
    {
        var auth = new Mock<IAuthorizationGuard>();
        auth.Setup(x => x.EnsureRoleAsync(It.IsAny<Guid>(), "Admin", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("FORBIDDEN", "Forbidden", 403));
        var store = new Mock<IAdminAiSalesStore>(MockBehavior.Strict);
        var service = new AdminAiSalesService(auth.Object, store.Object);
        await FluentActions.Invoking(() => service.ListAsync(Guid.NewGuid(), new(), default)).Should().ThrowAsync<DomainException>();
        store.VerifyNoOtherCalls();
    }
    [Theory] [InlineData(0, 25, "newest", "30d")] [InlineData(1, 101, "newest", "30d")]
    [InlineData(1, 25, "price", "30d")] [InlineData(1, 25, "newest", "yesterday")]
    public async Task Rejects_unsupported_filters(int page, int size, string sort, string range)
    {
        var service = new AdminAiSalesService(new Mock<IAuthorizationGuard>().Object, new Mock<IAdminAiSalesStore>(MockBehavior.Strict).Object);
        var act = () => service.ListAsync(Guid.NewGuid(), new(Range: range, Sort: sort, Page: page, PageSize: size), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.HttpStatus.Should().Be(400);
    }
}
