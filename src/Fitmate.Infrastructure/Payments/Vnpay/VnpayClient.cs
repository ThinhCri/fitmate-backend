using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Vnpay;

public class VnpayClient : IVnpayClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<VnpayOptions> _options;
    private readonly ILogger<VnpayClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VnpayClient(
        HttpClient httpClient,
        IOptions<VnpayOptions> options,
        ILogger<VnpayClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<VnpayQueryDrApiResponse> QueryTransactionAsync(
        VnpayQueryDrApiRequest request,
        CancellationToken ct)
    {
        var url = _options.Value.ApiUrl;
        _logger.LogInformation("Sending VNPay QueryDR request for TxnRef: {TxnRef}", request.TxnRef);

        using var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VnpayQueryDrApiResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response received from VNPay QueryDR API");
    }

    public async Task<VnpayRefundApiResponse> RefundAsync(
        VnpayRefundApiRequest request,
        CancellationToken ct)
    {
        var url = _options.Value.ApiUrl;
        _logger.LogInformation("Sending VNPay Refund request for TxnRef: {TxnRef}, Amount: {Amount}", request.TxnRef, request.Amount);

        using var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VnpayRefundApiResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response received from VNPay Refund API");
    }
}
