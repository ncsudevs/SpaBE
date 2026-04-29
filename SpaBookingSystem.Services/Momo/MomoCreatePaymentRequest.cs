using System.Text.Json.Serialization;

namespace SpaBookingSystem.Services.Momo;

public class MomoCreatePaymentRequest
{
    public string AccessKey { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = "SuSpa Booking";
    public string StoreId { get; set; } = "SuSpaStore";
    public string RequestId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string OrderInfo { get; set; } = string.Empty;
    public string RedirectUrl { get; set; } = string.Empty;
    public string IpnUrl { get; set; } = string.Empty;
    public string RequestType { get; set; } = "captureWallet";
    public string ExtraData { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public string Signature { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoreName { get; set; }
    public bool AutoCapture { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OrderGroupId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object[]? Items { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? UserInfo { get; set; }
}
