namespace SpaBookingSystem.Api.Dtos.Bookings;

public class AvailabilityDto
{
    public int ServiceId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public string AppointmentTime { get; set; } = string.Empty;
    public int SlotCapacity { get; set; }
    public int BookedQuantity { get; set; }
    public int RemainingSlots { get; set; }
    public bool IsAvailable => RemainingSlots > 0;
}
