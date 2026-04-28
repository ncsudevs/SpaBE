namespace SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;
using System.ComponentModel.DataAnnotations;

public class Payment
{
    public int Id { get; set; }
    public int BookingId { get; set; }

    [MaxLength(50)]
    public string PaymentCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Method { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [MaxLength(DataLengths.STATUS)]
    public string Status { get; set; } = "PAID";

    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? TransactionCode { get; set; }

    [MaxLength(DataLengths.DESCRIPTION)]
    public string? RefundReason { get; set; }

    public Booking? Booking { get; set; }
}
