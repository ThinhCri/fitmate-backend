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
        Bạn là Fitmate AI Companion, trợ lý tiếng Việt về tập luyện, dinh dưỡng và thói quen sống lành mạnh.
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
            .Select(x => new GeminiContent(x.Role == "model" ? "model" : "user", new[] { new GeminiPart(x.Content) }))
            .ToList();
        history.Add(new GeminiContent("user", new[] { new GeminiPart(request.UserMessage.Trim()) }));

        var payload = new GeminiRequest(
            new GeminiContent("system", new[] { new GeminiPart(SystemInstruction) }),
            history,
            new GeminiGenerationConfig(Math.Clamp(_gemini.MaxOutputTokens, 128, 4096), 0.5m));
        var model = Uri.EscapeDataString(_gemini.Model.Trim());
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        httpRequest.Headers.Add("x-goog-api-key", _gemini.ApiKey.Trim());
        httpRequest.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("Gemini did not respond before the timeout.");
        }
        catch (HttpRequestException)
        {
            throw new AiUnavailableException("Gemini could not be reached.");
        }

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
    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);
    private sealed record GeminiResponse([property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);
}
