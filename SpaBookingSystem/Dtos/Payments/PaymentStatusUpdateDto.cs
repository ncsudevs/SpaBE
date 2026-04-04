using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Payments;

public class PaymentStatusUpdateDto
{
    [Required]
    public string Status { get; set; } = string.Empty;
}
