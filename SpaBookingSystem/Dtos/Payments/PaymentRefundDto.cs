using System.ComponentModel.DataAnnotations;
using SpaBookingSystem.ApplicationCore.Constants;

namespace SpaBookingSystem.Api.Dtos.Payments;

public class PaymentRefundDto
{
    [Required]
    [MaxLength(DataLengths.DESCRIPTION)]
    public string Reason { get; set; } = string.Empty;
}
