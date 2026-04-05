namespace SpaBookingSystem.ApplicationCore.Entities;

public class StaffServiceCategory
{
    public int StaffId { get; set; }
    public int CategoryId { get; set; }

    public Staff? Staff { get; set; }
    public ServiceCategory? Category { get; set; }
}
