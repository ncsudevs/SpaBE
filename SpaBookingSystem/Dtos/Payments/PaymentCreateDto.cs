using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Payments;

public class PaymentCreateDto
{
    [Required]
    public int BookingId { get; set; }

    [Required]
    public string Method { get; set; } = string.Empty;
}
