using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingItemCreateDto
{
    [Required]
    public int ServiceId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; }
}
