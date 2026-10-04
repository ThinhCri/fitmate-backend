using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Chat;

public sealed class GeminiCompanionAdapter : IAiCompanionAdapter
{
    private const string SystemInstruction = """
        You are Fitmate AI Companion, a bilingual Vietnamese-English assistant. Vietnamese and English are
        the only supported languages. Answer Vietnamese messages in Vietnamese and English messages in English.
        For a message primarily in any other language, do not answer its substantive request. Reply exactly:
        "Hiện tại Fitmate AI chỉ hỗ trợ tiếng Việt và tiếng Anh. Please continue in Vietnamese or English."
        Trả lời rõ ràng, thân thiện, ngắn gọn và có thể thực hiện được. Không chẩn đoán bệnh, không kê đơn,
        không thay thế bác sĩ hoặc chuyên gia. Với dấu hiệu nguy hiểm, khuyên người dùng liên hệ cơ sở y tế.
        Không tuyên bố đã thay đổi lịch tập, bữa ăn, booking, thanh toán, vai trò hoặc dữ liệu tài khoản.
        Không bịa dữ liệu cá nhân chưa có trong hội thoại. Nếu không chắc, hãy nói rõ giới hạn.
        """;

    private readonly HttpClient _http;
    private readonly GeminiOptions _gemini;
    private readonly AiOptions _ai;

    public GeminiCompanionAdapter(
        HttpClient http,
        IOptions<GeminiOptions> gemini,
        IOptions<AiOptions> ai)
    {
        _http = http;
        _gemini = gemini.Value;
        _ai = ai.Value;
    }

    public async Task<CompanionChatResponseView> GenerateReplyAsync(
        CompanionChatRequest request, CancellationToken ct)
    {
        Validate(request);
        if (!_ai.Enabled || !_ai.CompanionChatEnabled || !_gemini.Enabled ||
            string.IsNullOrWhiteSpace(_gemini.ApiKey) || string.IsNullOrWhiteSpace(_gemini.Model))
            throw new AiUnavailableException("Gemini companion chat is not configured.");

        var history = (request.History ?? Array.Empty<CompanionChatMessageView>())
            .TakeLast(Math.Clamp(_gemini.MaxHistoryMessages, 0, 50))
            .Select(x => new GeminiContent(x.Role == "model" ? "model" : "user", new[] { GeminiPart.FromText(x.Content) }))
            .ToList();
        var userParts = new List<GeminiPart> { GeminiPart.FromText(request.UserMessage.Trim()) };
        if (request.ImageData is { Length: > 0 } && !string.IsNullOrWhiteSpace(request.ImageMimeType))
            userParts.Add(GeminiPart.FromImage(request.ImageMimeType, Convert.ToBase64String(request.ImageData)));
        history.Add(new GeminiContent("user", userParts));

        var payload = new GeminiRequest(
            new GeminiContent("system", new[] { GeminiPart.FromText(SystemInstruction) }),
            history,
            new GeminiGenerationConfig(Math.Clamp(_gemini.MaxOutputTokens, 128, 4096), 0.5m));
        var model = Uri.EscapeDataString(_gemini.Model.Trim());
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        var response = await SendWithOneTransientRetryAsync(endpoint, payload, ct);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitedException("Gemini quota or rate limit was reached.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AiUnavailableException("Gemini API credentials were rejected.");
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException("Gemini returned an unavailable response.");

            GeminiResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new AiInvalidOutputException();
            }

            var reply = body?.Candidates?.FirstOrDefault()?.Content?.Parts?
                .Select(x => x.Text).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
            if (string.IsNullOrWhiteSpace(reply) || reply.Length > 8000)
                throw new AiInvalidOutputException("Gemini returned no usable text response.");

            return new CompanionChatResponseView(
                Guid.NewGuid(), reply, DateTime.UtcNow, "gemini", "Thông tin AI chỉ mang tính tham khảo.");
        }
    }

    private async Task<HttpResponseMessage> SendWithOneTransientRetryAsync(
        string endpoint, GeminiRequest payload, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", _gemini.ApiKey!.Trim());
                request.Content = JsonContent.Create(payload);
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (attempt == 0 && response.StatusCode is HttpStatusCode.InternalServerError or
                    HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                {
                    response.Dispose();
                    await Task.Delay(400, ct);
                    continue;
                }
                return response;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt == 0) continue;
                throw new AiUnavailableException("Gemini did not respond before the timeout.");
            }
            catch (HttpRequestException)
            {
                if (attempt == 0)
                {
                    await Task.Delay(400, ct);
                    continue;
                }
                throw new AiUnavailableException("Gemini could not be reached.");
            }
        }
        throw new AiUnavailableException("Gemini is temporarily unavailable.");
    }

    private static void Validate(CompanionChatRequest request)
    {
        if (request.UserId == Guid.Empty || request.ThreadId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.UserMessage) || request.UserMessage.Length > 2000)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Invalid companion chat request.",
                new Dictionary<string, string[]> { ["text"] = new[] { "must be 1..2000 characters" } });
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("system_instruction")] GeminiContent SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);
    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
        [property: JsonPropertyName("temperature")] decimal Temperature);
    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("inline_data")] GeminiInlineData? InlineData)
    {
        public static GeminiPart FromText(string text) => new(text, null);
        public static GeminiPart FromImage(string mimeType, string data) => new(null, new(mimeType, data));
    }
    private sealed record GeminiInlineData(
        [property: JsonPropertyName("mime_type")] string MimeType,
        [property: JsonPropertyName("data")] string Data);
    private sealed record GeminiResponse([property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);
}
