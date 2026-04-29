using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SpaBookingSystem.Services.Options;

namespace SpaBookingSystem.Services.Momo;

public class MomoService : IMomoService
{
    private readonly HttpClient _httpClient;
    private readonly MomoOptions _options;

    public MomoService(HttpClient httpClient, IOptions<MomoOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<MomoCreatePaymentResponse> CreatePaymentAsync(MomoCreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        request.Signature = CreateCreatePaymentSignature(request);

        using var response = await _httpClient.PostAsJsonAsync(_options.CreateEndpoint, request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MoMo create payment request failed: {(int)response.StatusCode} {payload}");
        }

        var result = JsonSerializer.Deserialize<MomoCreatePaymentResponse>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (result == null)
        {
            throw new InvalidOperationException("MoMo create payment response could not be parsed.");
        }

        return result;
    }

    public string CreateCreatePaymentSignature(MomoCreatePaymentRequest request)
    {
        var rawData = $"accessKey={_options.AccessKey}&amount={request.Amount}&extraData={request.ExtraData}&ipnUrl={request.IpnUrl}&orderId={request.OrderId}&orderInfo={request.OrderInfo}&partnerCode={request.PartnerCode}&redirectUrl={request.RedirectUrl}&requestId={request.RequestId}&requestType={request.RequestType}";
        return Sign(rawData, _options.SecretKey);
    }

    public bool IsValidIpnSignature(MomoIpnRequest request)
    {
        var rawData = $"accessKey={_options.AccessKey}&amount={request.Amount}&extraData={request.ExtraData}&message={request.Message}&orderId={request.OrderId}&orderInfo={request.OrderInfo}&orderType={request.OrderType}&partnerCode={request.PartnerCode}&payType={request.PayType}&requestId={request.RequestId}&responseTime={request.ResponseTime}&resultCode={request.ResultCode}&transId={request.TransId}";
        var signature = Sign(rawData, _options.SecretKey);
        return string.Equals(signature, request.Signature, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sign(string rawData, string secretKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var dataBytes = Encoding.UTF8.GetBytes(rawData);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
