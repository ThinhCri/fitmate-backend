using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class CompanionRoutingTests
{
    [Fact]
    public async Task ImageRequest_UsesVisionProviderAndDoesNotSendImageToTextOnlyProvider()
    {
        var request = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), "ảnh", Images: [new([1, 2, 3], "image/jpeg")]);
        var text = new Mock<IAiCompanionAdapter>(); var vision = new Mock<IAiCompanionAdapter>();
        vision.Setup(x => x.GenerateReplyAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(new CompanionChatResponseView(Guid.NewGuid(), "gợi ý", DateTime.UtcNow, "gemini", null));
        var result = await new CompanionRoutingAdapter(text.Object, vision.Object).GenerateReplyAsync(request, default);
        result.Source.Should().Be("gemini");
        text.Verify(x => x.GenerateReplyAsync(It.IsAny<CompanionChatRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingVisionProvider_FailsClearlyWithoutFakeImageUnderstanding()
    {
        var request = new CompanionChatRequest(Guid.NewGuid(), Guid.NewGuid(), "ảnh", Images: [new([1], "image/jpeg")]);
        var act = () => new CompanionRoutingAdapter(Mock.Of<IAiCompanionAdapter>(), null).GenerateReplyAsync(request, default);
        await act.Should().ThrowAsync<AiUnavailableException>();
    }
}
