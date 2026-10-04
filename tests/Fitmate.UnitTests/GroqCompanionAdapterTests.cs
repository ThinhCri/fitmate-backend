using System.Net;
using System.Text;
using Fitmate.Application.Ai;
using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Chat;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public class GroqCompanionAdapterTests
{
    [Fact]
    public async Task GenerateReply_TextRequest_ReturnsAssistantContent()
    {
        string? payload = null;
        var handler = new Handler(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            request.RequestUri.Should().Be("https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            return Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Xin chào từ Groq"}}]}""");
        });

        var result = await NewAdapter(handler).GenerateReplyAsync(Request(), default);

        result.ReplyText.Should().Be("Xin chào từ Groq");
        result.Source.Should().Be("groq");
        payload.Should().Contain("qwen/qwen3.8-27b")
            .And.Contain("\"role\":\"user\"")
            .And.Contain("Vietnamese and English are the only supported languages");
    }

    [Fact]
    public async Task GenerateReply_ImageRequest_SendsDataUrl()
    {
        string? payload = null;
        var handler = new Handler(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Image received"}}]}""");
        });

        await NewAdapter(handler).GenerateReplyAsync(Request(new byte[] { 1, 2, 3 }, "image/jpeg"), default);

        payload.Should().Contain("data:image/jpeg;base64,AQID").And.Contain("image_url");
    }

    [Fact]
    public async Task GenerateReply_RateLimited_ThrowsStructuredException()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.TooManyRequests, "{}")));
        var action = () => NewAdapter(handler).GenerateReplyAsync(Request(), default);
        await action.Should().ThrowAsync<AiRateLimitedException>();
    }

    [Fact]
    public async Task GenerateReply_InvalidKey_ThrowsUnavailable()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));
        var action = () => NewAdapter(handler).GenerateReplyAsync(Request(), default);
        await action.Should().ThrowAsync<AiUnavailableException>();
    }

    private static GroqCompanionAdapter NewAdapter(HttpMessageHandler handler)
    {
        var groq = Options.Create(new GroqOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            Model = "qwen/qwen3.8-27b"
        });
        var ai = Options.Create(new AiOptions { Enabled = true, CompanionChatEnabled = true });
        return new GroqCompanionAdapter(new HttpClient(handler), groq, ai);
    }

    private static CompanionChatRequest Request(byte[]? image = null, string? mimeType = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Xin chào", Array.Empty<CompanionChatMessageView>(), image, mimeType);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }
}
