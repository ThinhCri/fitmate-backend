using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Finance;

public sealed class VietQrLookupOptions
{
    public const string SectionName = "BankLookup";
    public string Provider { get; set; } = "Disabled";
    public string BaseUrl { get; set; } = "https://api.vietqr.io";
    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool Configured => Provider.Equals("VietQr", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && ClientId.Length > 0 && ApiKey.Length > 0;
}

internal sealed record VietQrLookupRequest(
    [property: JsonPropertyName("bin")] string Bin,
    [property: JsonPropertyName("accountNumber")] string AccountNumber);
internal sealed record VietQrLookupData(
    [property: JsonPropertyName("accountName")] string? AccountName);
internal sealed record VietQrLookupResponse(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("desc")] string? Description,
    [property: JsonPropertyName("data")] VietQrLookupData? Data);

public sealed class VietQrBankAccountLookup(HttpClient http, IOptions<VietQrLookupOptions> options)
    : IBankAccountLookup
{
    private readonly VietQrLookupOptions config = options.Value;

    public async Task<BankAccountLookupResult> LookupAsync(string bankCode, string accountNumber,
        CancellationToken ct)
    {
        if (!config.Configured)
            throw new DomainException("BANK_LOOKUP_UNAVAILABLE", "Bank account verification is not configured.", 503);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(config.BaseUrl.TrimEnd('/') + "/"), "v2/lookup"));
        request.Headers.TryAddWithoutValidation("x-client-id", config.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Content = JsonContent.Create(new VietQrLookupRequest(bankCode, accountNumber));
        using var response = await http.SendAsync(request, ct);
        var payload = await response.Content.ReadFromJsonAsync<VietQrLookupResponse>(cancellationToken: ct);
        if (!response.IsSuccessStatusCode || payload?.Code != "00" ||
            string.IsNullOrWhiteSpace(payload.Data?.AccountName))
            throw new DomainException("BANK_ACCOUNT_NOT_FOUND",
                payload?.Description ?? "The bank account could not be verified.", 422);
        var reference = $"vietqr:{DateTime.UtcNow:yyyyMMddHHmmss}:{Guid.NewGuid():N}";
        return new BankAccountLookupResult(bankCode, bankCode, accountNumber,
            payload.Data.AccountName.Trim(), "VietQr", reference);
    }
}
