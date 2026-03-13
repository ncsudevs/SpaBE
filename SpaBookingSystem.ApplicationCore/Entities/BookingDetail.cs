namespace SpaBookingSystem.ApplicationCore.Entities;

public class BookingDetail
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int ServiceId { get; set; }
    public int Quantity { get; set; }

    // UnitPrice and LineTotal are persisted so booking history remains stable even if service prices change later.
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public Booking? Booking { get; set; }
    public Service? Service { get; set; }
}
