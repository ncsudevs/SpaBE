namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingItemDto
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public string AppointmentTime { get; set; } = string.Empty;
    public int? StaffId { get; set; }
    public string? StaffName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
