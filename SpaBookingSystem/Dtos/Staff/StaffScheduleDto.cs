namespace SpaBookingSystem.Api.Dtos.Staff;

public class StaffScheduleDto
{
    public int BookingDetailId { get; set; }
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public string AppointmentTime { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int Duration { get; set; }
    public int Quantity { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
