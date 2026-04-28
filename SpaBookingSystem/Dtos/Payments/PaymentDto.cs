namespace SpaBookingSystem.Api.Dtos.Payments;

public class PaymentDto
{
    public int Id { get; set; }
    public string PaymentCode { get; set; } = string.Empty;
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }
    public string? TransactionCode { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string PaymentContent { get; set; } = string.Empty;
    public string QrNote { get; set; } = string.Empty;
    public string? PayUrl { get; set; }
    public string? DeepLink { get; set; }
    public string? QrCodeUrl { get; set; }
    public bool IsSandbox { get; set; }
    public bool CustomerCanConfirm { get; set; }
    public bool RequiresManualReview { get; set; }
}
