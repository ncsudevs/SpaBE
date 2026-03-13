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
}
