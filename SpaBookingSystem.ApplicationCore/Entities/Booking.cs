namespace SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;
using System.ComponentModel.DataAnnotations;

public class Booking
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string BookingCode { get; set; } = string.Empty;

    [MaxLength(DataLengths.NAME)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(DataLengths.EMAIL)]
    public string Email { get; set; } = string.Empty;

    public DateOnly AppointmentDate { get; set; }

    [MaxLength(20)]
    public string AppointmentTime { get; set; } = string.Empty;

    [MaxLength(DataLengths.DESCRIPTION)]
    public string? Note { get; set; }

    public decimal TotalAmount { get; set; }

    [MaxLength(DataLengths.STATUS)]
    public string Status { get; set; } = "PENDING";

    [MaxLength(DataLengths.STATUS)]
    public string PaymentStatus { get; set; } = "UNPAID";

    public int PaymentAttempts { get; set; } = 0;

    public bool IsGroupBooking { get; set; }
    public int GroupSize { get; set; } = 1;
    public bool IsCheckedIn { get; set; }
    public DateTime? CheckedInAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<BookingDetail> BookingDetails { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}
