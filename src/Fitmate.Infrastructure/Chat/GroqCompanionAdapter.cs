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

public sealed class GroqCompanionAdapter : IAiCompanionAdapter
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string SystemInstruction = """
        You are Fitmate AI Companion, a bilingual Vietnamese-English assistant for exercise, nutrition,
        and healthy habits. Vietnamese and English are the only supported languages. If the user's latest
        message is Vietnamese, answer in Vietnamese. If it is English, answer in English. If both are mixed,
        use the dominant language unless the user explicitly requests Vietnamese or English.
        If the latest message is primarily any other language, do not answer its substantive request.
        Reply exactly: "Hiện tại Fitmate AI chỉ hỗ trợ tiếng Việt và tiếng Anh. Please continue in Vietnamese or English."
        Be clear, friendly, concise, and actionable. Do not diagnose illness, prescribe medication, or
        replace a doctor or qualified professional. For dangerous symptoms, advise contacting medical care.
        Never claim to have changed workouts, meals, bookings, payments, roles, or account data.
        Do not invent personal data that is absent from the conversation. State uncertainty honestly.
        """;

    private readonly HttpClient _http;
    private readonly GroqOptions _options;
    private readonly AiOptions _ai;

    public GroqCompanionAdapter(HttpClient http, IOptions<GroqOptions> options, IOptions<AiOptions> ai)
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
            throw new AiUnavailableException("Groq companion chat is not configured.");

        var messages = new List<GroqMessage> { new("system", SystemInstruction) };
        messages.AddRange((request.History ?? Array.Empty<CompanionChatMessageView>())
            .TakeLast(Math.Clamp(_options.MaxHistoryMessages, 0, 50))
            .Select(x => new GroqMessage(x.Role == "model" ? "assistant" : "user", x.Content)));

        object userContent = request.ImageData is { Length: > 0 } && !string.IsNullOrWhiteSpace(request.ImageMimeType)
            ? new GroqContentPart[]
            {
                new("text", request.UserMessage.Trim(), null),
                new("image_url", null, new($"data:{request.ImageMimeType};base64,{Convert.ToBase64String(request.ImageData)}"))
            }
            : request.UserMessage.Trim();
        messages.Add(new GroqMessage("user", userContent));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        httpRequest.Content = JsonContent.Create(new GroqRequest(
            _options.Model.Trim(), messages, Math.Clamp(_options.MaxOutputTokens, 128, 4096), 0.5m));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("Groq did not respond before the timeout.");
        }
        catch (HttpRequestException)
        {
            throw new AiUnavailableException("Groq could not be reached.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitedException("Groq quota or rate limit was reached.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AiUnavailableException("Groq API credentials were rejected.");
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException("Groq returned an unavailable response.");

            GroqResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<GroqResponse>(cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new AiInvalidOutputException();
            }

            var reply = body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(reply) || reply.Length > 8000)
                throw new AiInvalidOutputException("Groq returned no usable text response.");

            return new CompanionChatResponseView(
                Guid.NewGuid(), reply, DateTime.UtcNow, "groq", "AI guidance is for reference only.");
        }
    }

    private static void Validate(CompanionChatRequest request)
    {
        if (request.UserId == Guid.Empty || request.ThreadId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.UserMessage) || request.UserMessage.Length > 2000)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Invalid companion chat request.",
                new Dictionary<string, string[]> { ["text"] = new[] { "must be 1..2000 characters" } });
    }

    private sealed record GroqRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<GroqMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] decimal Temperature);
    private sealed record GroqMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object Content);
    private sealed record GroqContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("image_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GroqImageUrl? ImageUrl);
    private sealed record GroqImageUrl([property: JsonPropertyName("url")] string Url);
    private sealed record GroqResponse([property: JsonPropertyName("choices")] IReadOnlyList<GroqChoice>? Choices);
    private sealed record GroqChoice([property: JsonPropertyName("message")] GroqResponseMessage? Message);
    private sealed record GroqResponseMessage([property: JsonPropertyName("content")] string? Content);
}
