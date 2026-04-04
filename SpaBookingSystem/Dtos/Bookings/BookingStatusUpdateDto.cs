using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingStatusUpdateDto
{
    [Required]
    public string Status { get; set; } = string.Empty;
}
