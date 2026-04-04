namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingDto
{
    public int Id { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly? AppointmentDate { get; set; }
    public string? AppointmentTime { get; set; }
    public string? Note { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public bool IsGroupBooking { get; set; }
    public int GroupSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int PaymentAttempts { get; set; }
    public DateTime? LastPaymentCreatedAt { get; set; }
    public List<BookingItemDto> Items { get; set; } = new();
}
