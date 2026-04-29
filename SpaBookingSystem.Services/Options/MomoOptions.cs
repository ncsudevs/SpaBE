namespace SpaBookingSystem.Services.Options;

public class MomoOptions
{
    public const string SectionName = "Momo";

    public bool Enabled { get; set; }
    public bool UseSandbox { get; set; } = true;
    public string PartnerCode { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string CreateEndpoint { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/create";
    public string FrontendReturnUrl { get; set; } = "http://localhost:5173/payment/momo/result";
    public string IpnPath { get; set; } = "/api/payments/momo/ipn";
    public string StoreName { get; set; } = "SuSpa Booking";
    public string PartnerName { get; set; } = "SuSpa Booking";
    public string StoreId { get; set; } = "SuSpaStore";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ReturnPath { get; set; } = "/api/payments/momo/return";
}
