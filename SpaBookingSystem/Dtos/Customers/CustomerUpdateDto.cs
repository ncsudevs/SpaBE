using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Customers;

public class CustomerUpdateDto
{
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    public string? Region { get; set; } = "VN";

    public bool IsActive { get; set; } = true;
}
