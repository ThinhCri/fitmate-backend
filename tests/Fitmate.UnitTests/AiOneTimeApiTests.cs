using System.Security.Claims;
using Fitmate.Api.Ai;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace Fitmate.UnitTests;

public sealed class AiOneTimeApiTests
{
    private static (AiSubscriptionController Controller, Mock<IAiOneTimePurchaseService> Purchases) Create(string build)
    {
        var id = Guid.NewGuid();
        var subscriptions = new Mock<IAiSubscriptionService>();
        subscriptions.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AiSubscriptionView("plus", true, true, "fitmate_ai_plus_month", "", "SUBSCRIPTION_STATE_ACTIVE",
                DateTime.UtcNow.AddMonths(1), new string('a', 64), "in-app", "fitmate_ai_plus"));
        var purchases = new Mock<IAiOneTimePurchaseService>();
        var controller = new AiSubscriptionController(subscriptions.Object, Options.Create(new GooglePlayBillingOptions()), purchases.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test"));
        controller.Request.Headers["X-Fitmate-Android-Version-Code"] = build;
        return (controller, purchases);
    }
    [Fact] public async Task New_client_sees_one_time_storefront_and_server_expiry()
    {
        var (controller, _) = Create("8");
        var view = (AiSubscriptionView)((OkObjectResult)await controller.Get(default)).Value!;
        view.PurchaseType.Should().Be("in-app"); view.ProductId.Should().Be("fitmate_ai_plus_month");
        view.BillingAvailable.Should().BeTrue(); view.ExpiresAtUtc.Should().NotBeNull();
    }
    [Fact] public async Task Old_client_keeps_paid_rights_and_legacy_restore_but_cannot_buy_again()
    {
        var (controller, _) = Create("7");
        var view = (AiSubscriptionView)((OkObjectResult)await controller.Get(default)).Value!;
        view.Plan.Should().Be("plus"); view.RestoreAvailable.Should().BeTrue(); view.BillingAvailable.Should().BeFalse();
        view.PurchaseType.Should().Be("subs"); view.ProductId.Should().Be("fitmate_ai_plus");
    }
    [Fact] public async Task Verification_requires_idempotency_before_calling_provider_service()
    {
        var (controller, purchases) = Create("8");
        await FluentActions.Invoking(() => controller.VerifyPurchase(new("unit-token"), null, default)).Should().ThrowAsync<DomainException>().Where(x => x.HttpStatus == 400);
        purchases.VerifyNoOtherCalls();
    }
    [Fact] public async Task Verification_returns_durable_account_status_after_fulfillment()
    {
        var (controller, purchases) = Create("8");
        var view = (AiSubscriptionView)((OkObjectResult)await controller.VerifyPurchase(new("unit-token"), "repeated-request", default)).Value!;
        view.Plan.Should().Be("plus");
        purchases.Verify(x => x.VerifyAsync(It.IsAny<Guid>(), "unit-token", It.IsAny<CancellationToken>()), Times.Once);
    }
}
