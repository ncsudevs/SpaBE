namespace SpaBookingSystem.ApplicationCore.Entities;

public class BookingDetailStaffAssignment
{
    public int Id { get; set; }
    public int BookingDetailId { get; set; }
    public int StaffId { get; set; }
    public int AssignedQuantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BookingDetail? BookingDetail { get; set; }
    public Staff? Staff { get; set; }
}
