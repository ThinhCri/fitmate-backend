using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Chat;

public sealed class OpenRouterCompanionAdapter : IAiCompanionAdapter
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const string SystemInstruction = """
        Bạn là Fitmate AI Companion, trợ lý tiếng Việt về tập luyện, dinh dưỡng và thói quen sống lành mạnh.
        Trả lời rõ ràng, thân thiện, ngắn gọn và có thể thực hiện được. Không chẩn đoán bệnh, không kê đơn,
        không thay thế bác sĩ hoặc chuyên gia. Với dấu hiệu nguy hiểm, khuyên người dùng liên hệ cơ sở y tế.
        Không tuyên bố đã thay đổi lịch tập, bữa ăn, booking, thanh toán, vai trò hoặc dữ liệu tài khoản.
        Không bịa dữ liệu cá nhân chưa có trong hội thoại. Nếu không chắc, hãy nói rõ giới hạn.
        """;

    private readonly HttpClient _http;
    private readonly OpenRouterOptions _options;
    private readonly AiOptions _ai;

    public OpenRouterCompanionAdapter(
        HttpClient http,
        IOptions<OpenRouterOptions> options,
        IOptions<AiOptions> ai)
    {
        _http = http;
        _options = options.Value;
        _ai = ai.Value;
    }

    public async Task<CompanionChatResponseView> GenerateReplyAsync(CompanionChatRequest request, CancellationToken ct)
    {
        Validate(request);
        if (!_ai.Enabled || !_ai.CompanionChatEnabled || !_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Model))
            throw new AiUnavailableException("OpenRouter companion chat is not configured.");

        var messages = new List<OpenRouterMessage> { new("system", SystemInstruction) };
        messages.AddRange((request.History ?? Array.Empty<CompanionChatMessageView>())
            .TakeLast(Math.Clamp(_options.MaxHistoryMessages, 0, 50))
            .Select(x => new OpenRouterMessage(x.Role == "model" ? "assistant" : "user", x.Content)));

        object userContent = request.ImageData is { Length: > 0 } && !string.IsNullOrWhiteSpace(request.ImageMimeType)
            ? new OpenRouterContentPart[]
            {
                new("text", request.UserMessage.Trim(), null),
                new("image_url", null, new($"data:{request.ImageMimeType};base64,{Convert.ToBase64String(request.ImageData)}"))
            }
            : request.UserMessage.Trim();
        messages.Add(new OpenRouterMessage("user", userContent));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        if (Uri.TryCreate(_options.AppUrl, UriKind.Absolute, out _))
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", _options.AppUrl);
        if (!string.IsNullOrWhiteSpace(_options.AppName))
            httpRequest.Headers.TryAddWithoutValidation("X-OpenRouter-Title", _options.AppName.Trim());
        httpRequest.Content = JsonContent.Create(new OpenRouterRequest(
            _options.Model.Trim(), messages,
            Math.Clamp(_options.MaxOutputTokens, 128, 4096), 0.5m));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("OpenRouter did not respond before the timeout.");
        }
        catch (HttpRequestException)
        {
            throw new AiUnavailableException("OpenRouter could not be reached.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitedException("OpenRouter quota or rate limit was reached.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AiUnavailableException("OpenRouter API credentials were rejected.");
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException("OpenRouter returned an unavailable response.");

            OpenRouterResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new AiInvalidOutputException();
            }

            var reply = body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(reply) || reply.Length > 8000)
                throw new AiInvalidOutputException("OpenRouter returned no usable text response.");

            return new CompanionChatResponseView(
                Guid.NewGuid(), reply, DateTime.UtcNow, "openrouter", "Thông tin AI chỉ mang tính tham khảo.");
        }
    }

    private static void Validate(CompanionChatRequest request)
    {
        if (request.UserId == Guid.Empty || request.ThreadId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.UserMessage) || request.UserMessage.Length > 2000)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Invalid companion chat request.",
                new Dictionary<string, string[]> { ["text"] = new[] { "must be 1..2000 characters" } });
    }

    private sealed record OpenRouterRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OpenRouterMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] decimal Temperature);
    private sealed record OpenRouterMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object Content);
    private sealed record OpenRouterContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("image_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OpenRouterImageUrl? ImageUrl);
    private sealed record OpenRouterImageUrl([property: JsonPropertyName("url")] string Url);
    private sealed record OpenRouterResponse([property: JsonPropertyName("choices")] IReadOnlyList<OpenRouterChoice>? Choices);
    private sealed record OpenRouterChoice([property: JsonPropertyName("message")] OpenRouterResponseMessage? Message);
    private sealed record OpenRouterResponseMessage([property: JsonPropertyName("content")] string? Content);
}
