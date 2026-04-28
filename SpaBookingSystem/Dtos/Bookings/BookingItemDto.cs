namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingItemDto
{
    public int DetailId { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public string AppointmentTime { get; set; } = string.Empty;
    public int AssignedQuantity { get; set; }
    public int UnassignedQuantity { get; set; }
    public bool IsFullyStaffed { get; set; }
    public string? StaffingWarning { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public List<BookingItemStaffAssignmentDto> StaffAssignments { get; set; } = new();
}
