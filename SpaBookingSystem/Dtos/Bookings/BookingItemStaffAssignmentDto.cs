namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingItemStaffAssignmentDto
{
    public int Id { get; set; }
    public int StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public int AssignedQuantity { get; set; }
    public int StaffMaxConcurrent { get; set; }
}
